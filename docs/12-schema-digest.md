# 12 — Schema Digest

| Campo | Valor |
|---|---|
| Objetivo | Especificar o formato compacto exportável (digest), sua relação com o modelo canônico e os snapshots, versionamento, validação, hash estrutural, comparação e reaproveitamento de mapeamentos. |
| Escopo | Formato `mds.digest` versão **1.0**. |
| Relacionados | [08 §2](08-domain-model.md), [11](11-schema-introspection.md), [C01](contracts/C01-common-and-schema.md), [schema JSON](contracts/schemas/digest-1.0.schema.json), exemplos em [examples/](examples/) |
| Requisitos | FR-040 a FR-047 |

## 1. Três representações distintas

| Representação | Onde vive | Finalidade | Mutável? |
|---|---|---|---|
| **SchemaModel** (modelo canônico) | Memória (`Core.Schema`) | Trabalho: mapeamento, planejamento, validação | Imutável (records) |
| **Digest** | Arquivo `.mdsdigest.json` ou string JSON | Transporte, exportação, contexto para IA externa, inspeção humana | Arquivo |
| **Snapshot** | Dentro do projeto (`SnapshotEntry`) | Histórico versionado: digest + hash + fingerprint da origem + rótulo | Imutável após criado |

Regra: **o digest é uma projeção sem perdas do SchemaModel** — `Deserialize(Serialize(m)) == m` (igualdade estrutural, exceto `CapturedAt` com precisão de segundos). Garantido por T-DIG-002 para os quatro SGBDs.

## 2. Técnicas de redução de tamanho avaliadas

| Técnica | Redução estimada | Custo | Adotada? |
|---|---|---|---|
| Excluir objetos de sistema, estatísticas, privilégios, corpos de procedures/triggers | 80–95% vs. metadata bruto | Nenhum | **Sim** |
| Omitir valores padrão (nullable, ações `NO ACTION`, `virtual:false`, ativo) | 20–30% | Regras de default documentadas | **Sim** |
| Colunas como tuplas posicionais `[nome, tipo, flags, extras]` | ~40% sobre objetos | Menor legibilidade (mitigada por documentação) | **Sim** |
| Flags como string de tokens (`"nn ida"`) | ~10% | Parsing simples | **Sim** |
| Chaves curtas em objetos folha (`n`, `c`, `r`, `rc`) | ~10% | Dicionário de chaves | **Sim (somente folhas)** |
| Índices que implementam PK/UNIQUE omitidos | 5–15% | Nenhum | **Sim** |
| Interning de tipos (tabela de tipos + índice) | 5–10% | Ilegível para humanos/IA | Não |
| Compressão gzip (`.mdsdigest.json.gz`) | 85–90% adicional | Arquivo binário | **Opcional na exportação** |
| Formato binário (MessagePack/CBOR) | Alta | Não legível, não útil para IA | Não |

Estimativa: ~45–70 bytes por coluna e ~1–2 KB por tabela típica com constraints (não minificado ~2x). Um ERP de 400 tabelas/6.000 colunas ≈ 500–700 KB minificado, ~60 KB gzip. Teste de verificação: `B-DIG-SIZE` ([26](26-testing-strategy.md)) mede o corpus de referência e falha se a média por coluna exceder 90 bytes.

## 3. Estrutura do documento (versão 1.0)

```json
{
  "format": "mds.digest",
  "version": "1.0",
  "engine": "postgresql",
  "server": "17.2",
  "database": "loja",
  "capturedAt": "2026-10-11T14:03:12Z",
  "hash": "sha256:<64 hex>",
  "options": { "views": true, "routines": true, "triggers": true, "comments": false },
  "namespaces": [
    {
      "name": "public",
      "tables": [ { "name": "cliente", "cols": [["id","int8","nn idd"], ["nome","varchar(120)","nn"]], "pk": ["id"] } ],
      "views": [],
      "sequences": [],
      "routines": [],
      "triggers": []
    }
  ]
}
```

### 3.1 Dicionário de chaves

| Contexto | Chave | Significado | Obrigatória | Default se omitida |
|---|---|---|---|---|
| raiz | `format` | Sempre `"mds.digest"` | Sim | — |
| raiz | `version` | `"<major>.<minor>"` | Sim | — |
| raiz | `engine` | `sqlite` \| `mysql` \| `postgresql` \| `firebird` | Sim | — |
| raiz | `server` | Versão do servidor (texto) | Sim | — |
| raiz | `database` | Nome do banco / arquivo (sem caminho) | Sim | — |
| raiz | `capturedAt` | ISO-8601 UTC, precisão de segundos | Sim | — |
| raiz | `hash` | Hash estrutural (§6) | Sim | — |
| raiz | `options` | Opções de captura | Não | todos `true`, `comments:false` |
| namespace | `name` | Nome | Sim | — |
| namespace | `virtual` | Namespace virtual (Firebird 3–5) | Não | `false` |
| namespace | `tables`,`views`,`sequences`,`routines`,`triggers` | Listas | Não | `[]` |
| tabela/view | `name` | Nome | Sim | — |
| tabela/view | `cols` | Tuplas de coluna (§3.2), ordem = posição ordinal | Sim | — |
| tabela | `pk` | Colunas da PK em ordem | Não | sem PK |
| tabela | `pkn` | Nome da constraint PK | Não | — |
| tabela | `fk` | Lista de FKs (§3.3) | Não | `[]` |
| tabela | `uq` | Lista de UNIQUE (§3.3) | Não | `[]` |
| tabela | `ix` | Índices não-únicos e não-PK (§3.3) | Não | `[]` |
| tabela | `ck` | CHECKs | Não | `[]` |
| tabela | `cmt` | Comentário | Não | — |
| tabela | `x` | Extras específicos (mapa string→string), ex. `{"engine":"InnoDB","strict":"1"}` | Não | `{}` |
| view | `upd` | View atualizável | Não | desconhecido |

### 3.2 Tupla de coluna

`[nome, tipo, flags?, extras?]` — 2 a 4 elementos. Se `extras` estiver presente e não houver flags, `flags` é `""`.

- **tipo**: tipo nativo normalizado em minúsculas conforme tabela por SGBD em [11 §5](11-schema-introspection.md) (ex. `varchar(120)`, `numeric(12,2)`, `int unsigned`, `timestamptz`, `blob sub_type text`).
- **flags** (tokens separados por espaço, ordem canônica abaixo):

| Token | Significado |
|---|---|
| `nn` | NOT NULL |
| `ai` | Auto incremento (MySQL `AUTO_INCREMENT`; SQLite `INTEGER PRIMARY KEY`, com ou sem `AUTOINCREMENT`) |
| `idd` | Identity `GENERATED BY DEFAULT` (PG, Firebird 3+) |
| `ida` | Identity `GENERATED ALWAYS` (PG, Firebird 4+) |
| `ser` | Default de sequence (`serial`/`nextval`) no PostgreSQL |
| `tsq` | Firebird legado: valor atribuído por trigger `BEFORE INSERT` a partir de generator (detecção heurística, [11 §4.4](11-schema-introspection.md)) |
| `gs` | Coluna gerada armazenada |
| `gv` | Coluna gerada virtual / `COMPUTED BY` |

Ordem canônica de serialização: `nn`, depois o token de identidade (no máximo um), depois o de geração (no máximo um).

- **extras** (objeto, todas as chaves opcionais):

| Chave | Tipo | Significado |
|---|---|---|
| `def` | string | Expressão default bruta (ex. `now()`, `'A'`, `0`) |
| `seq` | string | Sequence/generator associado (`ser`, `idd`/`ida` PG, `tsq` FB) |
| `gx` | string | Expressão de coluna gerada |
| `enum` | string[] | Valores de ENUM (MySQL) / tipo enum PG |
| `cs` | string | Charset (MySQL/Firebird) se diferente do default do banco |
| `coll` | string | Collation se diferente do default |
| `dom` | string | Domínio Firebird / domain PG |
| `lu` | `"b"` | Comprimento em bytes (padrão: caracteres) |
| `cmt` | string | Comentário (somente se `options.comments`) |
| `x` | objeto string→string | Outros extras específicos |

### 3.3 Objetos de constraint (chaves curtas)

| Objeto | Chaves |
|---|---|
| FK | `n` nome, `c` colunas, `r` tabela referenciada (CanonicalId), `rc` colunas referenciadas, `od`/`ou` ação (`cascade`,`restrict`,`setnull`,`setdefault`; omitido = `noaction`), `df` (`"i"` deferrable initially immediate, `"d"` initially deferred; omitido = não deferrable), `ne`:true (não aplicada — MySQL `FOREIGN_KEY_CHECKS`/SQLite FKs desligadas no arquivo) |
| UNIQUE | `n` nome (opcional), `c` colunas, `ix`:true se originado de índice único, `nnd`:true se `NULLS NOT DISTINCT` (PG 15+) |
| Índice | `n` nome, `c` colunas (sufixo ` desc` para descendente), `e` expressões (lista), `w` predicado parcial |
| CHECK | `n` nome (opcional), `e` expressão |
| Sequence | `n`, `s` start, `i` increment, `min`, `max`, `cy`:true (cycle) |
| Rotina | `n`, `k` (`p` procedure, `f` function), `p` nº de parâmetros, `rt` tipo de retorno |
| Trigger | `n`, `t` tabela (nome simples dentro do namespace; omitido = trigger de banco/DDL), `tm` (`b`,`a`,`io`), `ev` (combinação de `i`,`u`,`d`), `a`:false se inativa, `pos` posição |

## 4. Regras de serialização

1. UTF-8 sem BOM; minificado por padrão; indentação opcional (`DigestWriteOptions.Indented`).
2. Ordem: namespaces por nome (ordinal); tabelas, views, sequences, rotinas, triggers por nome; colunas por posição ordinal; FKs/UQs/índices/CHECKs por nome (sem nome → por lista de colunas).
3. Strings em NFC; nenhum escape desnecessário (System.Text.Json com `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` — seguro pois o arquivo não é embutido em HTML).
4. Números de sequence como inteiros JSON (até 2^53; valores maiores serializados como string — PA-010).
5. Nenhuma chave com valor `null`; listas vazias omitidas, exceto `namespaces` e `cols`.
6. `capturedAt` truncado para segundos, sufixo `Z`.

### 4.6 Extras específicos de SGBD (registrados)

| SGBD | Escopo | Chaves `x` |
|---|---|---|
| MySQL | tabela | `engine` (InnoDB, MyISAM...), `charset`, `collation`, `autoinc` **não** (volátil) |
| SQLite | tabela | `strict` (`"1"`), `withoutRowid` (`"1"`) |
| PostgreSQL | tabela | `partitioned` (`"1"`), `inherits` (CanonicalId) |
| Firebird | tabela | `gtt` (`"preserve"`/`"delete"` para Global Temporary Tables) |
| Firebird | namespace virtual `PUBLIC` | `dialect` (`"3"`), `defaultCharset` (ex. `"UTF8"`, `"WIN1252"`) |
| MySQL | namespace | `defaultCharset`, `defaultCollation`, `sqlMode` (relevante para validação de datas zero e truncamento) |

O schema JSON permite `x` em namespace, tabela e coluna (dentro do objeto de extras da tupla).

## 5. Versionamento e compatibilidade

- `version` = `MAJOR.MINOR`. **MINOR** adiciona chaves opcionais (leitores 1.x ignoram chaves desconhecidas **somente dentro de `x`** e chaves de nível superior listadas como "reservadas para extensão": `stats`, `meta`). **MAJOR** muda semântica ou remove chaves.
- Leitor da versão 1.N aceita 1.0..1.N; para 1.M com M > N: aceita com aviso `DIG-MINOR-NEWER`, ignorando chaves desconhecidas.
- MAJOR diferente: rejeita com `DIG-MAJOR-UNSUPPORTED`, a menos que exista migrador registrado (`IDigestMigrator`, cadeia 1→2).
- Compatibilidade retroativa garantida por testes de fixture: todo digest de versão anterior em `tests/fixtures/digests/v1.0/` deve continuar sendo lido (T-DIG-020).

## 6. Hash estrutural

**Objetivo:** identificar se duas capturas representam a mesma estrutura **relevante para geração**, independentemente de nome do banco, servidor, horário ou nomes auto-gerados.

Algoritmo `StructureHasher` v1:
1. Construir a **forma canônica de hash** a partir do SchemaModel:
   - Inclui: namespaces (nome), tabelas (nome), colunas (nome, tipo normalizado, nn, identidade, geração, `def` normalizado), PK (colunas), FKs (colunas, `r`, `rc`, ações, deferrable), UNIQUEs (conjunto de colunas, `nnd`), CHECKs (expressão normalizada), índices (colunas/expressões, predicado), sequences (nome, start, increment), triggers (nome, tabela, timing, eventos, ativo), rotinas (nome, kind, nº parâmetros), views (nome, colunas).
   - **Exclui**: `server`, `database`, `capturedAt`, `options`, comentários, ordem ordinal das colunas (colunas ordenadas por nome), **nomes de constraints e índices** (nomes auto-gerados variam por banco: Firebird `INTEG_45`, `RDB$FOREIGN12`; SQLite `sqlite_autoindex_*`).
   - Normalização de expressões (`def`, CHECK, `gx`, predicados) por `Core.Hashing.ExpressionNormalizer` (varredor simples ciente de `'...'`, `"..."` e `` `...` ``; Core não referencia SqlTooling — AR-01): colapsa espaços fora de literais, remove espaços adjacentes a `(`, `)` e `,`, remove **um** par de parênteses externos que envolva a expressão inteira, converte para minúsculas tudo fora de literais e identificadores citados.
2. Serializar em JSON canônico: chaves ordenadas lexicograficamente (ordinal), sem espaços, números na forma mínima, strings NFC (inspirado em RFC 8785/JCS).
3. `hash = "sha256:" + hex(SHA256(UTF8(json)))`.

Também calculados: `ComputeTable` (mesmo algoritmo restrito à tabela) e `ColumnFingerprint` (16 hex: primeiros 8 bytes de SHA-256 de `logicalType|length|precision|scale|nn|inPk|inFk`).

O hash é **versionado implicitamente** pelo formato; mudança do algoritmo exige `StructureHasher` v2 e o campo `hash` passa a ser `sha256v2:` (prefixo distingue).

## 7. Validação

`DigestValidator` executa em ordem e reporta todos os erros (não para no primeiro):

| Código | Regra |
|---|---|
| DIG-JSON | JSON sintaticamente válido |
| DIG-FORMAT | `format == "mds.digest"` |
| DIG-VERSION | `version` suportada (§5) |
| DIG-SCHEMA | Conformidade com `digest-1.0.schema.json` (tipos, chaves, tuplas, padrões de flags) |
| DIG-DUP | Nomes únicos por escopo |
| DIG-PK-COL | Colunas de PK/UQ/índice/FK existem na tabela |
| DIG-FK-TARGET | `r` existe no digest **ou** está fora do filtro de captura (aviso `DIG-FK-EXTERNAL`, não erro) |
| DIG-FK-ARITY | `c.length == rc.length` |
| DIG-FLAGS | No máximo 1 token de identidade e 1 de geração; `gs`/`gv` incompatível com identidade |
| DIG-HASH | Hash recalculado == `hash` (divergência = aviso `DIG-HASH-MISMATCH`: arquivo editado manualmente; o app recalcula e informa) |

## 8. Comparação entre versões (diff)

`SchemaDiffer.Compare(from, to)`:
1. Casa namespaces e tabelas por nome exato; colunas por nome exato.
2. Para tabelas/colunas sem par na mesma tabela/namespace: gera `RenamedCandidate` se houver par com similaridade ≥ 0,85 (Jaro-Winkler sobre `NormalizedName.Compact`) **e** mesmo `LogicalType` (ou mesma família numérica), ou mesma posição ordinal e mesmo tipo. Candidatos **nunca** são aplicados automaticamente.
3. Severidade:

| Mudança | Severidade |
|---|---|
| Tabela/coluna removida | Breaking |
| Coluna adicionada NOT NULL sem default | Breaking (exige binding) |
| Mudança de família de tipo lógico | Breaking |
| Nullable → NOT NULL | Breaking |
| Redução de comprimento/precisão | Breaking |
| Nova FK / nova UNIQUE / novo CHECK | Breaking (pode invalidar plano) |
| Coluna adicionada nullable ou com default | Compatible |
| Aumento de comprimento/precisão | Compatible |
| Trigger/rotina adicionada/removida | Info (trigger de INSERT: Compatible com aviso) |
| Comentário alterado | Info |

## 9. Reutilização de mapeamentos após mudanças estruturais

Executado por `IMappingService.MigrateBindingsAsync` ao ativar um novo snapshot:

| Situação | Ação | Estado resultante |
|---|---|---|
| Coluna existe, fingerprint igual | Mantém | `Active` |
| Coluna existe, fingerprint diferente, gerador ainda suporta o novo `LogicalType` e restrições | Mantém, registra aviso | `NeedsReview` |
| Coluna existe, gerador incompatível | Mantém spec original para referência, marca | `NeedsReview` (bloqueia geração da tabela até revisão) |
| Coluna removida | Binding preservado | `Orphaned` (exibido em "Mapeamentos órfãos"; pode ser reatribuído a uma `RenameProposal` com um clique ou excluído) |
| Coluna nova | `AutoBind` apenas para ela | `Active`/sem binding |
| Tabela removida | Configurações de tabela/relacionamento preservadas como órfãs | `Orphaned` |
| FK nova | Binding estrutural `rel.fk` criado automaticamente com `ReferenceSource.Generated` | `Active` |

Bindings `Orphaned` são retidos indefinidamente até o usuário excluí-los (princípio "nada perdido").

## 10. Exemplos

| Arquivo | SGBD | Destaques |
|---|---|---|
| [examples/digest-sqlite.json](examples/digest-sqlite.json) | SQLite 3.46 | `ai` rowid, CHECK, FK composta, sem sequences |
| [examples/digest-mysql.json](examples/digest-mysql.json) | MySQL 8.4 | `enum`, `unsigned`, `x.engine`, trigger |
| [examples/digest-postgresql.json](examples/digest-postgresql.json) | PostgreSQL 17 | `idd`/`ida`, `ser`, deferrable, função/procedure, índice parcial |
| [examples/digest-firebird.json](examples/digest-firebird.json) | Firebird 5 | namespace virtual `PUBLIC`, generator + `tsq`, domínio, `blob sub_type text`, procedure |

Os quatro descrevem o mesmo schema de referência "loja" (categoria autorreferenciada, cliente, produto, pedido, item_pedido com PK composta, entrega com FK composta), permitindo comparar as diferenças de representação. Os valores de `hash` nos exemplos são ilustrativos (o algoritmo real os recalculará; o validador emitirá `DIG-HASH-MISMATCH` como aviso).
