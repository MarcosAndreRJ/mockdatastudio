# 18 — Geração de Scripts SQL

| Campo | Valor |
|---|---|
| Objetivo | Especificar a saída em script SQL por dialeto: estrutura, literais, quoting, lotes, segmentação, manifesto, limpeza e codificação. |
| Escopo | `SqlScriptWriter`, `ISqlDialect`, `ISqlLiteralFormatter`, `ISegmentedTextSink`, `ICleanupScriptBuilder`. Também CSV/JSON Lines (§9). |
| Relacionados | [C02](contracts/C02-providers.md), [C08](contracts/C08-output.md), [10](10-database-providers.md), exemplos [examples/output-postgresql.sql](examples/output-postgresql.sql), [examples/output-firebird.sql](examples/output-firebird.sql) |
| Requisitos | FR-093, FR-094, FR-099, FR-100, FR-101 |

## 1. Estrutura do script

```text
-- ============================================================
-- Mock Data Studio <versão> — script gerado
-- Projeto: <nome>  Perfil: <perfil>  RunId: <guid>
-- Destino: <engine> <versão alvo>   Gerado em: <ISO UTC>
-- Seed: <seed>  ReferenceInstant: <ISO>  PlanHash: <hash>  StructureHash: <hash>
-- Reprodutibilidade: <Exact|SourceBound|...>
-- Segmento <k> de <n> — executar em ordem (ver manifesto)
-- Faixas de chave reservadas: <tabela>.<col> [a..b] ...
-- ============================================================
<preâmbulo de sessão>                 (somente segmento 1)
<BEGIN/START TRANSACTION>             (se IncludeTransaction; ver §5)
-- Tabela: <nome> (<n> linhas)
INSERT ...;
...
<COMMIT periódico por dialeto>
<UPDATEs adiados (ciclos)>
<resync de sequences>
<teardown + COMMIT>                   (último segmento)
```

Nenhum dado sensível (senhas, connection strings, host) no cabeçalho; o nome do destino aparece apenas como engine/versão.

## 2. Quoting de identificadores

| Dialeto | Quote | Escape interno | Observação |
|---|---|---|---|
| SQLite | `"x"` | `""` | |
| MySQL | `` `x` `` | ` `` ` | |
| PostgreSQL | `"x"` | `""` | Case-sensitive quando citado → usa nome armazenado |
| Firebird | `"X"` | `""` | Dialeto 3; nome armazenado (maiúsculo se criado sem aspas) |

Padrão `QuoteAllIdentifiers=true` (correção acima de estética). Opção "citar só quando necessário": cita se não casar `^[a-z_][a-z0-9_]*$` (PG), `^[A-Z][A-Z0-9_$]*$` (FB), ou for palavra reservada (listas por dialeto embutidas).

Nome qualificado: `ns.tabela` exceto namespace virtual (FB) e SQLite `main` (omitido). MySQL: qualifica com database apenas se `OutputSettings.QualifyNamespace=true` (padrão `false`, script executado com `USE`).

## 3. Literais por tipo

| Tipo | SQLite | MySQL | PostgreSQL | Firebird |
|---|---|---|---|---|
| NULL | `NULL` | `NULL` | `NULL` | `NULL` |
| String | `'..'` (`'`→`''`) | `'..'` (`'`→`''`, `\`→`\\` salvo NO_BACKSLASH_ESCAPES; também `\0`, `\n`, `\r`, `\Z` escapados) | `'..'` (`'`→`''`; `standard_conforming_strings=on` assumido e declarado no preâmbulo) | `'..'` (`'`→`''`) |
| Unicode/controle | Literal UTF-8 | idem | idem | idem (charset de conexão UTF8 no isql: `SET NAMES UTF8;` no preâmbulo) |
| Inteiro | `123` | `123` | `123` | `123` |
| Decimal | `123.45` (invariante, sem expoente) | idem | idem | idem |
| Float | `R` round-trip invariante; `NaN/∞` → erro de validação | idem | idem (`'NaN'::float8` permitido só se coluna float) | idem |
| Boolean | `1`/`0` | `1`/`0` | `TRUE`/`FALSE` | `TRUE`/`FALSE` |
| Date | `'2026-01-31'` | `'2026-01-31'` | `DATE '2026-01-31'` | `DATE '2026-01-31'` |
| Time | `'13:45:00'` | `'13:45:00'` | `TIME '13:45:00'` | `TIME '13:45:00'` |
| DateTime | `'2026-01-31 13:45:00.123'` (ou epoch/julian conforme `dateStorage`) | `'2026-01-31 13:45:00.123'` (fsp da coluna) | `TIMESTAMP '2026-01-31 13:45:00.123'` | `TIMESTAMP '2026-01-31 13:45:00.1230'` (FB: 4 casas decimais máx.) |
| DateTimeTz | — | — | `TIMESTAMPTZ '2026-01-31 13:45:00-03:00'` | `TIMESTAMP '2026-01-31 13:45:00 -03:00'` (FB4+) |
| Uuid | `'..'` texto (ou blob 16 se coluna BLOB) | `'..'` (char(36)) / `UUID_TO_BIN('..')` se binary(16) | `'..'::uuid` | `CHAR_TO_UUID('..')` se `char(16) octets` |
| Binary | `X'0A1B'` | `X'0A1B'` | `'\x0a1b'::bytea` | `x'0A1B'` |
| Json | `'{"a":1}'` | `'{"a":1}'` | `'{"a":1}'::jsonb` (conforme tipo) | — |
| Interval | — | — | `INTERVAL 'P1DT2H'` | — |

Testes de literais: T-OUT-LIT-* com valores adversariais (aspas, barras, `\0`, emojis, quebras de linha, `--`, `/*`, `;`, `$$`), executados contra os 4 SGBDs reais em integração (T-OUT-ROUNDTRIP-*: gera, executa, relê e compara).

## 4. Formato dos INSERTs

| Dialeto | Forma | Linhas por comando (padrão) |
|---|---|---|
| SQLite | `INSERT INTO "t" ("a","b") VALUES (..),(..);` | 500 |
| MySQL | `INSERT INTO `t` (`a`,`b`) VALUES (..),(..);` | 500 e ≤ `max_allowed_packet` estimado (assume 4 MB; comando quebrado antes de 1 MB) |
| PostgreSQL | `INSERT INTO "t" ("a","b") [OVERRIDING SYSTEM VALUE] VALUES (..),(..);` | 500 |
| Firebird | `INSERT INTO "T" ("A","B") [OVERRIDING SYSTEM VALUE] VALUES (..);` | 1 |

`RowsPerStatement` configurável (1–5.000). Cada comando termina com `;` e quebra de linha; uma linha por tupla em multi-row para facilitar diff.

## 5. Transações e commits no script

| Dialeto | Abertura | Commit periódico | Observação |
|---|---|---|---|
| SQLite | `BEGIN;` | `COMMIT; BEGIN;` a cada 50.000 linhas | Ciclos `DeferredConstraint`: `PRAGMA defer_foreign_keys=ON;` após `BEGIN` |
| MySQL | `START TRANSACTION;` | a cada 10.000 linhas | `DisableChecks`: `SET FOREIGN_KEY_CHECKS=0;` … `=1;` |
| PostgreSQL | `BEGIN;` | a cada 50.000 linhas (fora de componentes com `SET CONSTRAINTS ALL DEFERRED`, que ficam numa única transação) | |
| Firebird | (isql inicia transação implicitamente) | `COMMIT;` a cada 5.000 linhas | Sem `SET TERM` |

`IncludeTransaction=false` → sem comandos de transação (usuário controla).

## 6. Segmentação e manifesto

- `ISegmentedTextSink` escreve comandos completos; antes de escrever um comando que faria o segmento exceder `SegmentMaxBytes` (padrão 50 MB), fecha o segmento (com `COMMIT` se dentro de transação, reabrindo no próximo) e abre `<nome>_part002.sql`.
- Um componente de ciclo com `DeferredConstraint` não é dividido entre segmentos (se exceder, o segmento cresce além do limite e emite aviso `OUT-SEGMENT-OVERSIZE`).
- Manifesto `<nome>.manifest.json` (`RunManifest`, formato `mds.run-manifest` 1.0): lista de segmentos com SHA-256, bytes, ordem; tabelas e faixas de chave; status.
- Escrita atômica por segmento: grava `*.sql.partial` e renomeia ao fechar.
- Codificação: UTF-8 **sem BOM** (padrão; opção BOM para ferramentas Windows legadas); quebras de linha `\n` (opção `\r\n`).

## 7. Script de limpeza

`<nome>_cleanup.sql` gerado quando `IncludeCleanupScript=true`:
- Ordem inversa de dependência (filhos antes dos pais), inclusive considerando FKs adiadas (zera FKs de ciclo com `UPDATE ... SET fk = NULL` antes dos DELETEs, quando NullThenUpdate foi usado).
- Chaves inteiras Explicit: `DELETE FROM t WHERE id BETWEEN a AND b;`. Chaves compostas/UUID: `DELETE ... WHERE (c1,c2) IN (...)` em lotes de 1.000 (Firebird: `WHERE (c1 = x AND c2 = y) OR ...` em lotes de 200).
- Captured: lista de chaves capturadas (inserção direta).
- Cabeçalho com aviso: "Remove somente as linhas desta execução (RunId). Linhas alteradas por triggers ou cascades em outras tabelas não são revertidas."

## 8. Ressincronização de sequences
Conforme [16 §5.2](16-relational-generation.md); emitido no último segmento, após todos os INSERTs/UPDATEs. Firebird usa `ALTER SEQUENCE "GEN" RESTART WITH n;` precedido de comentário sobre semântica por versão (PV a verificar: T-PRV-FB-KEY-003).

## 9. CSV e JSON Lines

| Formato | Regras |
|---|---|
| CSV | RFC 4180; separador configurável (`,` padrão; `;` sugerido para Excel pt-BR); cabeçalho com nomes das colunas; datas ISO; decimais com `.`; booleanos `true/false`; binário base64; um arquivo por tabela `<tabela>.csv`; UTF-8 com BOM opcional |
| JSON Lines | Um objeto por linha `{"col": valor}`; tipos JSON nativos; datas ISO; decimais como **string** para preservar precisão (opção número); binário base64 |

Ambos incluem `<nome>.manifest.json` com ordem de carga recomendada (pais antes) — útil para ferramentas externas (COPY, LOAD DATA).

## 10. Desempenho do writer
Formatação com `StringBuilder` reaproveitado por batch e `ArrayPool<char>`; escrita via `StreamWriter` com buffer 1 MB; meta ≥ 200 k linhas/s em script PG (B-OUT-001).
