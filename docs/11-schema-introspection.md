# 11 — Introspecção de Schema

| Campo | Valor |
|---|---|
| Objetivo | Definir como cada provider extrai o modelo canônico, de forma eficiente e consistente. |
| Escopo | `ISchemaExtractor` dos 4 SGBDs. |
| Relacionados | [08 §2](08-domain-model.md), [10](10-database-providers.md), [12](12-schema-digest.md), [C02](contracts/C02-providers.md) |
| Requisitos | FR-010, FR-011, FR-014, FR-040, FR-047 |

## 1. Princípios

1. **Consultas em lote por tipo de objeto**, nunca por tabela (evita N+1): uma consulta para todas as colunas, uma para todas as constraints etc. Meta: schema de 1.000 tabelas extraído em ≤ 30 s em rede local (NFR-PERF-003).
2. **Somente leitura**; nenhuma consulta altera estado. Transação de leitura com isolamento snapshot quando disponível (PG `REPEATABLE READ READ ONLY`, Firebird `snapshot read only`, MySQL `START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY`) para capturar estado coerente.
3. Objetos de sistema excluídos: PG (`pg_catalog`, `information_schema`, `pg_toast*`), MySQL (`mysql`, `sys`, `performance_schema`, `information_schema`), Firebird (`RDB$SYSTEM_FLAG <> 0`, prefixos `RDB$`, `MON$`, `SEC$`), SQLite (`sqlite_%`).
4. Filtros de captura (`CaptureOptions`) aplicados no SQL quando possível (namespace) e em memória para glob de tabela.
5. Progresso reportado por fase: `Tables`, `Columns`, `Keys`, `Indexes`, `Checks`, `Sequences`, `Triggers`, `Routines`.
6. Falha em fase opcional (ex.: sem permissão para ler rotinas) → aviso `PRV-INTROSPECT-PARTIAL` e fase omitida; falha em fase obrigatória (tabelas/colunas/chaves) → erro.

## 2. Fontes por SGBD

| Fase | SQLite | MySQL | PostgreSQL | Firebird |
|---|---|---|---|---|
| Tabelas/views | `sqlite_schema` (`type IN ('table','view')`) + `pragma_table_list` (≥3.37 para `strict`/`withoutRowid`; fallback: parse do `sql`) | `information_schema.TABLES` | `pg_class` + `pg_namespace` (`relkind IN ('r','p','v','m','f')`) | `RDB$RELATIONS` (`RDB$VIEW_BLR IS NULL` → tabela) |
| Colunas | `pragma_table_xinfo(t)` (por tabela — exceção ao princípio 1, pois pragmas são locais e rápidos) | `information_schema.COLUMNS` | `pg_attribute` + `format_type` + `pg_attrdef` + `attidentity`/`attgenerated` | `RDB$RELATION_FIELDS` ⨝ `RDB$FIELDS` (+ `RDB$IDENTITY_TYPE`, `RDB$COMPUTED_SOURCE`, `RDB$DEFAULT_SOURCE`, `RDB$CHARACTER_SETS`) |
| PK/UNIQUE | `pragma_index_list` (`origin` `pk`/`u`) + `pragma_index_info` | `TABLE_CONSTRAINTS` + `KEY_COLUMN_USAGE`; `STATISTICS` para índices únicos | `pg_constraint` (`p`,`u`) + `pg_index` (`indisunique`) | `RDB$RELATION_CONSTRAINTS` + `RDB$INDEX_SEGMENTS` |
| FKs | `pragma_foreign_key_list` | `REFERENTIAL_CONSTRAINTS` + `KEY_COLUMN_USAGE` | `pg_constraint` (`f`, `confkey`, `condeferrable`, `condeferred`, `confdeltype`) | `RDB$REF_CONSTRAINTS` + segmentos de índice |
| Índices | `pragma_index_list` (`origin='c'`) + `pragma_index_xinfo` | `information_schema.STATISTICS` | `pg_index` + `pg_get_indexdef` (expressões/predicado) | `RDB$INDICES` (`RDB$EXPRESSION_SOURCE`, `RDB$CONDITION_SOURCE` FB5) |
| CHECK | Parse do `sql` de `CREATE TABLE` (lexer) | `information_schema.CHECK_CONSTRAINTS` (8.0.16+) | `pg_constraint` (`c`) + `pg_get_constraintdef` | `RDB$CHECK_CONSTRAINTS` → `RDB$TRIGGERS.RDB$TRIGGER_SOURCE` (CHECK vira trigger de sistema; extrair texto após `CHECK`) |
| Sequences | — (`sqlite_sequence` é estado, não estrutura) | — | `pg_sequence` | `RDB$GENERATORS` (`RDB$SYSTEM_FLAG=0`) |
| Triggers | `sqlite_schema` (`type='trigger'`), timing/evento por lexer sobre `sql` | `information_schema.TRIGGERS` | `pg_trigger` (`NOT tgisinternal`) + `tgtype` bits | `RDB$TRIGGERS` (`RDB$TRIGGER_TYPE` decodificado, `RDB$TRIGGER_INACTIVE`, `RDB$TRIGGER_SEQUENCE`) |
| Rotinas | — | `information_schema.ROUTINES` + `PARAMETERS` | `pg_proc` (`prokind` `f`/`p`), exclui funções de extensões (`pg_depend` deptype `e`) | `RDB$PROCEDURES`, `RDB$FUNCTIONS` (FB3+; exclui `RDB$SYSTEM_FLAG<>0`; pacotes: `RDB$PACKAGE_NAME` → nome `PACOTE.ROTINA`) |
| Comentários | — | `TABLE_COMMENT`/`COLUMN_COMMENT` | `obj_description`/`col_description` | `RDB$DESCRIPTION` |

As consultas SQL concretas ficam em arquivos `.sql` embutidos em cada provider (`Providers.<X>/Introspection/*.sql`) e são cobertas por testes de contrato. Não são reproduzidas aqui para evitar divergência entre documento e código: **a fonte de verdade das consultas é o código + testes**; este documento fixa as fontes, regras e saídas esperadas.

## 3. Regras de normalização comuns

| Regra | Descrição |
|---|---|
| N-01 | Nomes preservados como armazenados (Firebird: `TRIM` de `CHAR(63)` com padding; MySQL: case conforme `lower_case_table_names`). |
| N-02 | Tipo nativo normalizado para minúsculas, sem espaços duplicados, parâmetros sem espaços: `numeric(12,2)`. |
| N-03 | `LogicalType` sempre derivado pelo `ITypeMapper` do provider a partir do tipo nativo normalizado + domínio. |
| N-04 | Índices que implementam PK/UNIQUE constraint são omitidos da lista de índices (marcados internamente `BacksConstraint`). MySQL: índice criado automaticamente para FK é mantido como índice (é visível ao usuário). |
| N-05 | Índices únicos sem constraint → `UniqueConstraintModel(Source=UniqueIndex)` (com `ix:true`) se não forem parciais nem de expressão; parciais/expressão → permanecem em `ix` com `w`/`e` e geram **aviso de validação** (unicidade condicional não garantida construtivamente: [17 §5](17-constraint-validation.md)). |
| N-06 | Default bruto preservado em `def`; `DefaultValue.Kind` classificado: literal (número, string citada, TRUE/FALSE), função (`now()`, `CURRENT_TIMESTAMP`, `gen_random_uuid()`, `uuid()`), sequence (`nextval(...)`, `GEN_ID(...)`). |
| N-07 | Colunas `ROWID`/`oid` implícitas não são incluídas. |
| N-08 | FKs referenciando tabelas fora do filtro de captura são mantidas, e o planner as trata como pai "não incluído" (referência obrigatoriamente `Existing`/`Fixed`). |

## 4. Particularidades

### 4.1 SQLite
- `pragma_table_xinfo.hidden`: 2/3 = coluna gerada (virtual/stored).
- AUTOINCREMENT detectado pela presença do token no `sql` (lexer), apenas informativo (`x.autoincrement="1"`).
- CHECK por coluna e de tabela extraídos do `sql` com o lexer de [20 §4](20-sql-editor.md): sequência `CHECK ( ... )` com balanceamento de parênteses.

### 4.2 MySQL
- `COLUMN_DEFAULT` retorna literais **sem aspas** e expressões entre parênteses (8.0.13+) [K]; `EXTRA` contém `auto_increment`, `DEFAULT_GENERATED`, `VIRTUAL GENERATED`, `STORED GENERATED`.
- `enum('A','B')` → tipo `enum` + `enum:["A","B"]` (parse com o lexer para respeitar aspas escapadas).

### 4.3 PostgreSQL
- `format_type()` produz nomes longos; o extractor usa `pg_type.typname` + modificadores (`atttypmod`) para o formato curto (`varchar(120)`, `numeric(12,2)`, `timestamptz`), conforme tabela §5.
- Serial: default `nextval('x_seq'::regclass)` + dependência `pg_depend` (`deptype='a'`) → flag `ser`, `seq`.

### 4.4 Firebird — detecção de `tsq` (trigger + generator)
Heurística determinística (todas as condições):
1. Coluna é PK de coluna única, tipo inteiro, sem identity.
2. Existe trigger ativo `BEFORE INSERT` na tabela cujo `RDB$TRIGGER_SOURCE` contém, após remoção de comentários e normalização de espaços, o padrão `NEW.<COL> = GEN_ID(<GEN>, <n>)` ou `NEW.<COL> = NEXT VALUE FOR <GEN>`.
3. Se o padrão estiver envolvido por `IF (NEW.<COL> IS NULL)` → `x.tsqConditional="1"` (valor explícito é preservado; estratégia Explicit funciona). Caso contrário → `x.tsqConditional="0"` (trigger **sempre** sobrescreve; planner força `KeyStrategy.Captured` na inserção direta e **bloqueia** script com FK dependente — diagnóstico `PLN-FB-TRIGGER-KEY`).

> O código-fonte da trigger é lido apenas para esta análise e **não** é armazenado no digest.

## 5. Tabela de normalização de tipos nativos (resumo)

| LogicalType | SQLite (declarado) | MySQL | PostgreSQL | Firebird |
|---|---|---|---|---|
| Boolean | `bool`, `boolean` | `tinyint(1)` (config.), `bit(1)` | `bool` | `boolean` |
| Integer 16 | `smallint` | `smallint[ unsigned]`, `tinyint` (8) | `int2` | `smallint` |
| Integer 32 | `int` (afinidade) | `int`, `mediumint` | `int4` | `integer` |
| Integer 64 | `integer`, `bigint` | `bigint[ unsigned]` | `int8` | `bigint` |
| Decimal | `numeric(p,s)`, `decimal(p,s)` | `decimal(p,s)` | `numeric(p,s)`, `money` (Decimal(19,2)) | `numeric(p,s)`, `decimal(p,s)`, `int128` (Integer 128 → Decimal(38,0)) |
| Float | `real`, `double`, `float` | `float`, `double` | `float4`, `float8` | `float`, `double precision`, `decfloat(16|34)` (Decimal) |
| String | `varchar(n)`, `char(n)`, `text` sem limite→Text | `varchar(n)`, `char(n)` | `varchar(n)`, `bpchar(n)` | `varchar(n)`, `char(n)` |
| Text | `text`, `clob` | `text`, `mediumtext`, `longtext` | `text` | `blob sub_type text` |
| Binary | `blob` | `blob`, `varbinary(n)`, `binary(n)` | `bytea` | `blob`, `char(n) character set octets` |
| Date | `date` | `date` | `date` | `date` |
| Time | `time` | `time` | `time` | `time` |
| DateTime | `datetime`, `timestamp` | `datetime`, `timestamp` (UTC conv.) | `timestamp` | `timestamp` |
| DateTimeTz | — | — | `timestamptz` | `timestamp with time zone` (4+) |
| Uuid | `uuid`, `guid` | `char(36)`/`binary(16)` somente via binding (sem tipo nativo) | `uuid` | `char(16) character set octets` somente via binding |
| Json | `json` | `json` | `json`, `jsonb` | — |
| Enum | — | `enum` | enum de usuário | — |
| Interval | — | — | `interval` | — |

## 6. Refresh incremental (explorer)

`ExtractTableAsync` reextrai uma tabela (colunas, chaves, índices, checks, triggers da tabela) com as mesmas consultas filtradas por nome. O explorer usa refresh por nó; o digest do projeto **só** muda por captura explícita (FR-044), nunca por refresh do explorer.
