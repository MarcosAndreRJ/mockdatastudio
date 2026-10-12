# 16 — Geração Relacional

| Campo | Valor |
|---|---|
| Objetivo | Especificar o planejamento relacional: grafo de dependências, ciclos, autorreferência, estratégias de chave, cardinalidade, FKs compostas, fontes de referência e integridade entre origem/destino. |
| Escopo | `GenerationPlanner`, `DependencyGraph`, `CycleResolver`, `KeyPlanner`, `KeyOracle`. |
| Relacionados | ADR-008, [15](15-generation-engine.md), [17](17-constraint-validation.md), [C06](contracts/C06-planning.md), [30 §B–C, §J](30-risks-and-tradeoffs.md) |
| Requisitos | FR-080..FR-087 |

## 1. Grafo de dependências

- Nós: tabelas **incluídas** no perfil (`TableSettings.Include`).
- Arestas `filho → pai` para cada FK aplicada (`IsEnforced`) cuja origem de referência é `Generated` e cujo pai está incluído. FKs com fonte `Existing/Fixed/Dataset/Query` **não** criam aresta (o pai não é gerado nesta execução).
- FK para tabela **não incluída** com fonte `Generated` → diagnóstico `PLN-FK-PARENT-EXCLUDED` (Error) com sugestões "incluir tabela pai" ou "usar registros existentes".
- Atributos da aresta: `IsNullable` (todas as colunas da FK anuláveis), `IsDeferrable`, `IsSelfReference`.

## 2. Por que não apenas ordenação topológica

Ordenação topológica falha com ciclos (A→B→A) e autorreferências, comuns em schemas reais (funcionário→gerente; pedido↔nota fiscal; categoria→categoria pai). O planner:

1. Calcula **componentes fortemente conexos** (Tarjan, iterativo para evitar stack overflow; ordem de visita por `CanonicalId` → determinístico).
2. Ordena o **DAG de componentes** topologicamente (pais antes), com desempate por `CanonicalId`.
3. Componente trivial (1 tabela, sem auto-aresta) → `InsertTableStep`.
4. Componente com auto-aresta ou ≥ 2 tabelas → `CycleResolver` (§3/§4).

## 3. Resolução de ciclos (componentes com ≥ 2 tabelas)

Objetivo: escolher um conjunto mínimo de arestas a "adiar" (*feedback edge set*) de forma que o restante seja acíclico, usando somente arestas adiáveis.

**Arestas adiáveis e estratégia** (ordem de preferência por aresta):

| Estratégia | Condição | Efeito |
|---|---|---|
| `NullThenUpdate` | FK anulável | INSERT com NULL nas colunas da FK; após inserir todas as tabelas do componente, `DeferredUpdateStep` emite `UPDATE filho SET fk = valor WHERE pk = ...` em lotes |
| `DeferredConstraint` | FK `DEFERRABLE` (PG) ou SQLite com `PRAGMA defer_foreign_keys=ON` na transação | INSERTs na ordem livre dentro de uma transação; verificação no COMMIT. Exige transação única para o componente (script: bloco `BEGIN ... COMMIT` envolvendo o componente; direta: `TransactionMode` forçado a `Single` para o componente) |
| `DisableChecks` | Destino suporta desligar FKs por sessão (MySQL, SQLite) **e** usuário habilitou explicitamente "Permitir desativar verificação de FKs" no perfil | `SET FOREIGN_KEY_CHECKS=0` / `PRAGMA foreign_keys=OFF` antes do componente e reativação depois. Integridade garantida pelo próprio motor (valores sempre válidos). Aviso `PLN-FKCHECKS-OFF` |

**Algoritmo** (determinístico, componentes são pequenos — tipicamente ≤ 10 tabelas):
```text
// PSEUDOCÓDIGO
candidates = arestas do componente com ao menos uma estratégia aplicável
if componente tem ≤ 12 arestas:
    busca exaustiva pelo menor subconjunto S ⊆ candidates tal que (arestas − S) é acíclico,
    desempate: menor nº de colunas NOT NULL afetadas → preferir NullThenUpdate → ordem por nome da FK
else:
    heurística gulosa: remover repetidamente a aresta candidata que participa de mais ciclos (contagem via DFS), desempate por nome
if nenhum S existe: diagnóstico PLN-CYCLE-UNBREAKABLE (Error)
```

**Condições de bloqueio** (`PLN-CYCLE-UNBREAKABLE`): ciclo em que todas as FKs são NOT NULL, não-deferrable, e o destino não permite (ou o usuário não autorizou) desativar verificações. Mensagem lista o ciclo (`A.fk_x → B, B.fk_y → A`) e sugestões: (1) tornar uma FK deferrable/anulável no banco; (2) usar fonte `Existing` para uma das FKs; (3) habilitar DisableChecks (se suportado); (4) excluir uma tabela do ciclo.

**Firebird**: não há deferral nem desativação por sessão → somente `NullThenUpdate`.

## 4. Autorreferência

| Caso | Estratégia |
|---|---|
| FK anulável (categoria.pai_id) | Hierarquia por **índice anterior**: linha `i` referencia pai `j < i` escolhido pela distribuição; uma fração `rootRatio` (padrão 0,1, mínimo linha 0) recebe NULL. Sem UPDATE posterior. Profundidade máxima configurável (`maxDepth`, padrão 6): o gerador mantém em memória apenas a profundidade de cada linha como `byte[]` (1 byte/linha) |
| FK NOT NULL, chave Explicit | Linha 0 referencia **a si mesma**; demais `j < i`. Válido no PG (FK checada ao fim do comando) [K]; MySQL/InnoDB e Firebird: verificação por linha — autorreferência na **mesma linha** é aceita? **[A verificar: T-REL-SELF-NN-MYSQL, T-REL-SELF-NN-FB]**. Se o teste falhar no SGBD, o planner bloqueia com `PLN-SELF-NOTNULL` sugerindo `DisableChecks` (MySQL) ou fonte `Existing` |
| FK NOT NULL, chave Captured | Impossível sem conhecer a própria chave antes do INSERT → `PLN-SELF-CAPTURED` (Error) |
| FK anulável, chave Captured | As chaves dos pais `j < i` do mesmo batch ainda não foram capturadas quando a linha `i` é gerada → a FK é inserida como NULL e preenchida por `DeferredUpdateStep` (NullThenUpdate) após a tabela inteira |

## 5. Estratégias de chave (ADR-008)

### 5.1 Seleção por coluna-chave

| Situação | Estratégia padrão |
|---|---|
| PK inteira (identity/auto/serial/tsq condicional/sem identity) | `Explicit` |
| PK UUID | `Explicit` (gerador `core.uuid`) |
| PK texto/natural sem FK | `Explicit` com gerador semântico + unicidade (§15.8) |
| PK `ida` (ALWAYS) e destino sem override | `Captured` (somente inserção direta); em script → `PLN-KEY-ALWAYS-SCRIPT` (Error) se a tabela é referenciada por outra gerada; se não for referenciada → `DatabaseDefault` (coluna omitida) |
| Firebird `tsq` não-condicional | `Captured` (direta) / bloqueio em script se referenciada (`PLN-FB-TRIGGER-KEY`) |
| Tabela não referenciada por nenhuma outra gerada e PK auto | Preferência do perfil: `Explicit` (padrão, permite limpeza por faixa) ou `DatabaseDefault` |
| Usuário escolheu `NaturalKeyLookup` | Válido só se o pai tem UNIQUE não-chave gerado pelo motor com unicidade construtiva; filhos usam subconsulta |

### 5.2 Reserva de faixa (Explicit inteiro)

```text
start = max( MAX(col) no destino, valor corrente da sequence/identity (se legível sem consumir), 0 ) + 1 + KeyOffset
       (ou KeyStartOverride se definido)
step  = 1 (ou increment da sequence quando ≠ 1, preservando a progressão)
faixa = [start, start + step*(RowCount-1)]
```
- Overflow do tipo (ex.: `smallint` 32767) → `PLN-KEY-OVERFLOW` (Error).
- **Modo offline** (sem conexão ao destino, só digest): `start = KeyStartOverride ?? 1`, diagnóstico `PLN-KEY-OFFLINE` (Warning) "faixa não verificada contra o destino".
- **Concorrência**: imediatamente antes da execução direta, `TargetInspector` relê `MAX(col)`; se ≥ `start` → aborta com `RUN-KEY-RANGE-TAKEN` e oferece replanejar. Em script, o cabeçalho registra a faixa e uma instrução opcional de verificação (`-- VERIFICAÇÃO: SELECT MAX(id) FROM ... deve ser < start`), e, se `OutputSettings.EmitGuards=true`, um comando que falha se a faixa estiver ocupada (PG: bloco `DO $$ ... RAISE EXCEPTION`; MySQL/SQLite/FB: comentário apenas — limitação documentada).
- **Resync** (após inserir): PG `setval(seq, last, true)`; Firebird `ALTER SEQUENCE g RESTART WITH last` / identity `ALTER TABLE ... ALTER COLUMN ... RESTART WITH last`; MySQL automático; SQLite: se `AUTOINCREMENT`, `UPDATE sqlite_sequence SET seq = max(seq, last) WHERE name = 't'`, senão automático.

### 5.3 KeyOracle
- Explicit inteiro: `key(i) = start + i*step` (O(1)).
- Explicit UUID/texto: recomputa o valor do gerador para a linha `i` (função pura).
- Captured: `KeyRegistry[table][i]`.
- Chave composta: tupla dos componentes, cada um por sua regra (ex.: `item_pedido` = (`key(pedido, parentIndex)`, `child_seq`)).

## 6. Cardinalidade e distribuição

### 6.1 ChildDriven (padrão)
O filho tem `RowCount` fixo; cada linha escolhe um pai: `j = Distribution.Sample(rngFk(row), parentCount)`.

| Distribuição | Amostragem |
|---|---|
| Uniform | `NextInt64(0, n)` |
| Zipf(s) | Ranks `1..n` com probabilidade ∝ 1/rank^s; rank → pai por permutação fixa (evita que os primeiros IDs sejam sempre os "populares") |
| Weighted | Pesos por pai vindos de coluna do pai (ex.: `cliente.categoria = 'VIP'` peso 5) — V1.1; V1: pesos por faixa de índice |

### 6.2 ParentDriven
Cada pai `p` recebe `k_p` filhos com `k_p ~ Range(min,max)` ou `Poisson(λ)` truncada, usando `rng(card, p)`. `TableSettings.RowCount` do filho é **ignorado** (a UI o exibe como "≈ derivado"); uma tabela só pode ter **uma** FK ParentDriven (as demais FKs da tabela são ChildDriven) — violação → `PLN-PARENTDRIVEN-MULTI` (Error). `RowCount` do filho = Σ k_p (calculado no planejamento; checkpoints de soma prefixada a cada 4.096 pais permitem localizar o pai do filho `i` em O(log n)). Garante cobertura (`min ≥ 1` → todo pai tem filho). Permite `rel.child_seq`.

### 6.3 1:1 (`OneToOne`)
FK com UNIQUE nas colunas da FK (ex.: `entrega(pedido_id,item_seq)` UNIQUE) → cada pai no máximo uma vez: `j = Perm(i)` sobre `[0, parentCount)`; `RowCount ≤ parentCount` senão `PLN-ONE-TO-ONE-CAPACITY` (Error).

### 6.4 FK opcional
`RelationSettings.NullRate` define fração de NULL (todas as colunas da FK composta juntas — nunca NULL parcial, que em MATCH SIMPLE desabilitaria a verificação de forma inesperada).

## 7. FKs compostas

- O binding é **da FK**, não de cada coluna: as colunas recebem os componentes da **mesma tupla** do pai (`KeyOracle.GetKey(parent, refCols, j)`).
- Se `refCols` não for a PK do pai (FK para UNIQUE alternativa), o KeyOracle recomputa os valores das colunas únicas do pai (requer que sejam geradas por geradores puros; senão `PLN-FK-NONPURE-PARENT`).
- Colunas que participam de **duas** FKs (sobreposição, ex.: `entrega.pedido_id` em FK para `item_pedido` e para `pedido`): o planner exige consistência — escolhe a FK mais específica (maior aridade) como "primária" e deriva as demais colunas; a FK menor é satisfeita automaticamente se for implicada (ex.: item_pedido.pedido_id já referencia pedido). Se não implicada → `PLN-FK-OVERLAP` (Error) com explicação.

## 8. Fontes de referência

| Fonte | Descrição | Determinismo | Validação |
|---|---|---|---|
| `Generated` | Pais gerados nesta execução | Exact | Integridade garantida por construção |
| `Existing` | Chaves lidas do **banco de referência** (`ReadKeysAsync`, ORDER BY chave, até `ExistingLimit`) | SourceBound (fingerprint do pool) | Se referência ≠ destino → §9 |
| `Fixed` | Lista de tuplas informada | Exact | Opcionalmente verificada no destino (`AllKeysExistAsync`) |
| `Dataset` | CSV local (cabeçalho = colunas da FK) | Exact (hash do arquivo) | Idem Fixed |
| `Query` | SELECT configurado (validado: 1 comando, classificação Select) | SourceBound | Executado na conexão de referência; colunas devem bater com a aridade |

Pool vazio (0 chaves) com FK NOT NULL → `PLN-REF-EMPTY` (Error).

## 9. Origem ≠ destino (integridade falsa)

Cenário: chaves de `cliente` lidas do banco **A** (referência) e pedidos inseridos no banco **B** (destino). Se os clientes não existem em B, a FK falha (se aplicada) ou — pior — com FKs não aplicadas (`ne:true`, MySQL MyISAM, SQLite com FKs off) gera **integridade falsa silenciosa**.

Regras:
1. Projeto registra `Connections.Reference` e `Connections.Target` com `FingerprintHash`.
2. Se `ReferenceFingerprint ≠ TargetFingerprint` e alguma FK usa `Existing`/`Query`:
   - `ValidationSettings.VerifyReferencesInTarget = true` (padrão) → antes da execução, `AllKeysExistAsync` no destino para **todas** as chaves do pool (em lotes de 1.000 com `IN`/`VALUES` join). Faltantes → `VAL-REF-MISSING-IN-TARGET` (Error) com contagem e amostra.
   - Desativar a verificação exige confirmação explícita e registra no relatório "integridade não verificada".
3. Saída em script (destino desconhecido em tempo de execução): cabeçalho inclui aviso e, se `EmitGuards`, comandos de verificação comentados por FK.
4. Fixed/Dataset seguem a mesma regra quando o destino está conectado.

## 10. Catálogo de diagnósticos do planner

| Código | Severidade | Situação |
|---|---|---|
| PLN-FK-PARENT-EXCLUDED | Error | Pai excluído com fonte Generated |
| PLN-CYCLE-UNBREAKABLE | Error | §3 |
| PLN-CYCLE-NULLUPDATE | Info | Ciclo resolvido por NullThenUpdate (lista arestas) |
| PLN-FKCHECKS-OFF | Warning | DisableChecks em uso |
| PLN-SELF-NOTNULL | Error | §4 (quando não suportado) |
| PLN-SELF-CAPTURED | Error | §4 |
| PLN-KEY-ALWAYS-SCRIPT | Error | §5.1 |
| PLN-FB-TRIGGER-KEY | Error/Warning | Trigger FB sobrescreve chave |
| PLN-KEY-OVERFLOW | Error | Faixa excede o tipo |
| PLN-KEY-OFFLINE | Warning | Faixa não verificada |
| PLN-UNIQUE-CAPACITY | Error | §15.8 |
| PLN-UNIQUE-EXISTING | Warning | §15.8.5 |
| PLN-ONE-TO-ONE-CAPACITY | Error | §6.3 |
| PLN-PARENTDRIVEN-MULTI | Error | Mais de uma FK ParentDriven na mesma tabela (§6.2) |
| PLN-FK-NONPURE-PARENT | Error | §7 |
| PLN-FK-OVERLAP | Error | §7 |
| PLN-REF-EMPTY | Error | §8 |
| PLN-EXPR-CYCLE | Error | Dependência circular entre colunas |
| PLN-EXPR-PARENT | Error | `parent()` sem dados do pai |
| PLN-BINDING-REVIEW | Error | Binding `NeedsReview` em tabela incluída |
| PLN-NO-ROWS | Info | Tabela incluída com 0 linhas |

## 11. Exemplo de plano — schema "loja" (PostgreSQL)

Perfil: categoria 20 (autorreferência, raízes 20%), cliente 1.000, produto 300, pedido ChildDriven 5.000 (Zipf s=1,1 sobre clientes), item_pedido ParentDriven Range(1..5) sobre pedido, entrega 1:1 sobre item_pedido 60% (RowCount fixo = 0,6 × itens estimados).

```text
1. SessionSetup        SET client_encoding = 'UTF8'
2. Insert categoria    20 linhas  (pai_id → j<i, 20% NULL)           keys id: Explicit [1..20]  (override: idd → não exige)
3. Insert cliente      1000       id: Explicit [1..1000] + OVERRIDING SYSTEM VALUE (ida)
4. Insert produto      300        id: Explicit [1..300]
5. Insert pedido       5000       cliente_id ← Zipf; id Explicit [1..5000]
6. Insert item_pedido  Σk≈15000   (pedido_id, item_seq=rel.child_seq)
7. Insert entrega      ≈9000      1:1 Perm sobre item_pedido; id uuid v4
8. Resync categoria_id_seq→20, cliente_id_seq→1000, produto_id_seq→300, pedido_id_seq→5000
9. SessionTeardown     COMMIT
```
Snapshot desse plano: `tests/fixtures/plans/loja-postgresql.plan.txt` (T-REL-PLAN-001).
