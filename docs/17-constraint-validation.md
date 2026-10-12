# 17 — Validação de Restrições

| Campo | Valor |
|---|---|
| Objetivo | Identificar **antes** da execução situações que violariam tipos, tamanhos, faixas, NOT NULL, UNIQUE, FKs, CHECKs, triggers e regras específicas de SGBD. |
| Escopo | `IPreflightValidator` (estático), `ICheckConstraintInterpreter`, `ITrialInsertRunner` (dinâmico, opcional). |
| Relacionados | [C07](contracts/C07-validation.md), [15](15-generation-engine.md), [16](16-relational-generation.md), [30 §E](30-risks-and-tradeoffs.md) |
| Requisitos | FR-092, FR-075, FR-087 |

## 1. Camadas de validação

| Camada | Quando | Custo | Detecta |
|---|---|---|---|
| L1 — Planejamento | `IGenerationPlanner.Plan` | ms | Ciclos, capacidade de unicidade, chaves, fontes de referência (diagnósticos `PLN-*`) |
| L2 — Estática | `IPreflightValidator` | ms–s | Tipos, tamanhos, precisão, faixas de data, NOT NULL, CHECK interpretável, enum, coerção (`VAL-*`) |
| L3 — Amostral | Preview de N linhas por tabela (padrão 200) com verificação real de cada valor contra o tipo da coluna | s | Valores de template não conversíveis, comprimento real, expressões com erro |
| L4 — Dinâmica (opt-in) | Inserção de teste em transação revertida no destino | s–min | Triggers, CHECKs não interpretáveis, regras do SGBD, permissões |

Execução só é liberada sem `Error` em L1–L3 (e L4 se habilitada). Warnings exigem reconhecimento (checkbox "Estou ciente") na tela de execução.

## 2. Regras estáticas (L2)

| Código | Categoria | Regra |
|---|---|---|
| VAL-NULL-NOTNULL | Nullability | NullRate > 0 em coluna NOT NULL |
| VAL-UNBOUND | Mapping | Coluna NOT NULL inserível sem binding nem default |
| VAL-TYPE | Type | `OutputTypes` do gerador incompatíveis com a coluna (matriz 14 §6, ✘) |
| VAL-LEN | Length | Comprimento máximo possível do gerador > Length da coluna (strings: máscara, lista, template com `MaxValueLength`, `random.string.maxLength`); para expressões/format: verificação amostral L3 |
| VAL-LEN-BYTES | Length | Firebird/MySQL com limites em bytes: calcula UTF-8 bytes do maior valor conhecido vs. `Length × bytesPerChar` do charset (UTF8=4, WIN1252/latin1=1) e limite absoluto (FB VARCHAR 32765 bytes; MySQL linha 65535 bytes — aviso) |
| VAL-PRECISION | Precision | Faixa numérica excede `precision-scale` dígitos inteiros; escala do gerador > escala da coluna (aviso de arredondamento) |
| VAL-INT-RANGE | Range | Faixa excede o tipo inteiro (incl. unsigned) |
| VAL-DATE-RANGE | Range | Faixa de datas fora do suportado: MySQL TIMESTAMP 1970-01-01..2038-01-19; MySQL DATETIME/DATE 1000..9999; PG date 4713 AC..5874897 DC (clamp prático 0001..9999); Firebird 0001-01-01..9999-12-31; SQLite sem limite (texto ISO 0000..9999) |
| VAL-ENUM | Check | Valores do gerador ⊄ enum (MySQL enum / PG enum) |
| VAL-CHECK | Check | CHECK interpretado (§4) violado pelo domínio do gerador |
| VAL-CHECK-UNVERIFIED | Check | CHECK não interpretável (Info; sugere L4) |
| VAL-UQ-PARTIAL | Uniqueness | Índice único parcial/expressão não garantido |
| VAL-TRIGGER-INSERT | Trigger | Tabela com trigger `BEFORE/AFTER INSERT` ativa no destino (Warning: "pode alterar/rejeitar linhas; considere a inserção de teste") |
| VAL-REF-MISSING-IN-TARGET | ForeignKey | 16 §9 |
| VAL-GENERATED-BOUND | Mapping | Binding ≠ skip em coluna gerada |
| VAL-DEFAULT-NULLRATE | Mapping | `core.default` com NullRate > 0 |
| VAL-SQLITE-DATESTORAGE | Type | Coluna SQLite com tipo declarado de data sem `dateStorage` definido (Info: usando texto ISO) |
| VAL-MYSQL-NONSTRICT | Target | `sql_mode` sem STRICT: truncamentos seriam silenciosos (Warning; regras de comprimento já são Error) |
| VAL-REPRO | Reproducibility | Fontes SourceBound/TimeBound em uso (Info) |
| VAL-PROD-TARGET | Security | Destino Production/Unknown (Error se Output=DirectInsert e perfil `IsReadOnly`; ver 23) |

## 3. Limites por SGBD usados na validação

| Limite | SQLite | MySQL | PostgreSQL | Firebird |
|---|---|---|---|---|
| String máx. | Sem limite (STRICT respeita tipo, não tamanho) | VARCHAR ≤ 65535 bytes por linha | 1 GB (`text`) | VARCHAR 32765 bytes; CHAR 32767 bytes |
| Decimal máx. | Precisão de REAL/texto | DECIMAL(65,30) | numeric ≤ 1000 dígitos (declarado) | NUMERIC(18) (FB3); NUMERIC(38) (FB4+) |
| Inteiros | 64 bits | 8/16/24/32/64 ± unsigned | 16/32/64 | 16/32/64; INT128 (FB4+) |
| Data/hora | Texto | ver VAL-DATE-RANGE | ver | ver |
| Boolean | 0/1 | tinyint(1) aceita −128..127 (MDS gera 0/1) | true/false | FB3+ |

## 4. Interpretação de CHECK (L2)

`ICheckConstraintInterpreter` usa o lexer SQL e reconhece **somente** os padrões abaixo (qualquer outro → `Unsupported`), com coluna única referenciada:

| Padrão (normalizado) | Interpretação | Uso |
|---|---|---|
| `col >= n`, `col > n`, `col <= n`, `col < n`, `n <= col` (e combinações com `AND`) | Range | Ajuste de domínio: o mapeamento sugere faixas compatíveis; validação verifica domínio do gerador ⊆ faixa |
| `col BETWEEN a AND b` | Range | idem |
| `col IN ('a','b',...)` / `col IN (1,2)` | InList | Sugestão automática `core.list.random(valores)` quando não há binding semântico melhor; validação verifica ⊆ |
| `col <> ''` / `trim(col) <> ''` / `length(col) > 0` | NotEmpty | Verifica que gerador não produz vazio |
| `length(col) >= n` / `char_length(col) = n` / `length(col) = n` | MinLength/MaxLength | Verifica comprimentos |
| `col ~ '^...$'` (PG), `regexp_like(col,'...')` (MySQL), `col SIMILAR TO '...'` (FB), `col GLOB '...'` (SQLite) | Regex | Verificação **amostral** L3 com o regex convertido para .NET (SIMILAR TO e GLOB convertidos por regras fixas; falha de conversão → Unsupported) |
| `colA <= colB` | Comparison (2 colunas) | Info: sugere expressão (`colB = date_add(@colA, ...)`); verificação amostral L3 |

Interpretação é **conservadora**: nunca altera bindings automaticamente; apenas sugere e valida.

## 5. Unicidade (L1/L2)
Ver [15 §8](15-generation-engine.md). Validação adicional: duas colunas únicas com o mesmo gerador indexável e mesma permutação seriam correlacionadas — não é erro (permutações usam `scope` distinto).

## 6. Inserção de teste (L4 — `ITrialInsertRunner`)

| Aspecto | Especificação |
|---|---|
| Habilitação | `ValidationSettings.TrialInsert=true`; **proibido** em `Production`/`Unknown` (`SEC-TRIAL-PROD`) |
| Procedimento | Abre conexão de destino → transação → para cada tabela na ordem do plano, insere as **primeiras `TrialRows`** linhas (padrão 50) do plano real (mesmas chaves/valores) → captura erros por linha (sem parar na primeira: usa savepoint por linha onde suportado — PG/FB/SQLite/MySQL suportam `SAVEPOINT` [K]) → **ROLLBACK** sempre (inclusive em exceção, via `finally`) |
| Efeitos colaterais conhecidos | Sequences/generators/AUTO_INCREMENT **avançam** e não retrocedem com rollback (PG, FB, MySQL) [K]; triggers com efeitos externos (FB `POST_EVENT`, PG `NOTIFY` — entregues só no commit, logo não ocorrem; `dblink`/transações autônomas FB `IN AUTONOMOUS TRANSACTION` **persistem**) → aviso exibido antes de executar listando triggers de INSERT das tabelas envolvidas |
| Resultado | `TrialInsertReport` com issues categorizadas pelo `IErrorTranslator` (UniqueViolation, CheckViolation, DataTooLong, FK...) e a linha/valores (valores exibidos apenas no app, não logados) |
| Detecção de trigger que sobrescreve chave | Após INSERT de teste de linha com chave Explicit, relê a linha pela chave; se não encontrada → trigger sobrescreveu → `VAL-TRIGGER-KEY-OVERRIDE` (Error) e sugestão de Captured |

## 7. Relatório de validação (UI)

Agrupado por severidade e tabela; cada item com ação rápida ("Ir para coluna", "Aplicar sugestão", "Reduzir linhas para N"). Exportável como JSON (`ValidationReport`).

## 8. Critérios de aceite (resumo; detalhes em [02](02-requirements.md) FR-092)

- Schema de teste `fixtures/schemas/constraints-*.sql` com 40 casos de violação conhecidos: L1–L3 detectam ≥ 36 sem L4; L4 detecta os 4 restantes (triggers/CHECK complexos) — T-VAL-001..040.
- Nenhum falso positivo `Error` no schema "loja" com bindings sugeridos (T-VAL-LOJA).
