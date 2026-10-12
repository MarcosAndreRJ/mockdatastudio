# 10 — Providers de Banco de Dados

| Campo | Valor |
|---|---|
| Objetivo | Especificar a arquitetura de providers, particularidades de cada SGBD e a matriz de capacidades. |
| Escopo | SQLite, MySQL, PostgreSQL, Firebird (obrigatórios); MariaDB em melhor esforço. |
| Relacionados | [C02](contracts/C02-providers.md), [11](11-schema-introspection.md), [18](18-sql-generation.md), [19](19-direct-database-execution.md), ADR-003, ADR-012 |
| Requisitos | FR-001..FR-008, FR-085, FR-095 |

## 1. Arquitetura

Cada provider é um assembly `MockDataStudio.Providers.<X>` com:

| Classe | Implementa | Responsabilidade |
|---|---|---|
| `<X>Provider` | `IDatabaseProvider` | Fábrica/ponto de entrada; capacidades por versão |
| `<X>SchemaExtractor` | `ISchemaExtractor` | Consultas de catálogo → `SchemaModel` ([11](11-schema-introspection.md)) |
| `<X>TypeMapper` | `ITypeMapper` | Tipo nativo → `DataTypeInfo` |
| `<X>Dialect` | `ISqlDialect` | Quoting, literais, INSERT, sequences, sessão |
| `<X>LiteralFormatter` | `ISqlLiteralFormatter` | Formatação de literais |
| `<X>BulkInserter` | `IBulkInserter` | Inserção direta eficiente |
| `<X>TargetInspector` | `ITargetInspector` | MAX(pk), sequences, triggers, verificação de chaves |
| `<X>ErrorTranslator` | `IErrorTranslator` | Exceção do driver → `DbErrorCategory` |
| `<X>Module` | `IMdsModule` | Registro no DI |

Adicionar um SGBD novo = novo assembly implementando o conjunto + passar a suíte `Providers.ContractTests` ([26 §4](26-testing-strategy.md)). Nenhuma alteração em Core/Generation/Mapping é necessária (critério de extensibilidade verificável: T-ARC-007 garante que nenhum `switch` sobre `DatabaseEngine` existe fora de `Providers.*` e de `App/Composition`).

> Exceção controlada: `DatabaseEngine` é enum fechado em Core (persistido em projeto/digest). Um SGBD novo exige adicionar valor ao enum e ao `engine` do digest (MINOR do formato). Alternativa de string aberta foi rejeitada por perder validação forte; reavaliar no V2 com plugins (ADR-009).

## 2. Matriz de capacidades

| Capacidade | SQLite | MySQL 8.4 | PostgreSQL 14+ | Firebird 3 | Firebird 4/5 |
|---|---|---|---|---|---|
| Namespaces | Não (`main`) | database = namespace | Sim (schemas) | Não (virtual) | Não (virtual) |
| `INSERT ... RETURNING` | Sim (≥3.35) | **Não** (MariaDB ≥10.5 sim [K]) | Sim | Sim (1 linha) | Sim (FB5: múltiplas linhas em alguns casos [K—verificar]) |
| INSERT multi-linha `VALUES` | Sim | Sim | Sim | **Não** | **Não** |
| Constraints deferrable | Somente FKs com `DEFERRABLE INITIALLY DEFERRED` ou `PRAGMA defer_foreign_keys` | Não | Sim (se declarada `DEFERRABLE`) | Não | Não |
| Desligar FK por sessão | `PRAGMA foreign_keys=OFF` (fora de transação) | `SET FOREIGN_KEY_CHECKS=0` | Não seguro (`session_replication_role` exige superusuário e desliga triggers) | Não | Não |
| Override de identity | Valor explícito sempre aceito | Valor explícito aceito | `OVERRIDING SYSTEM VALUE` p/ ALWAYS | Identity só BY DEFAULT → explícito aceito | `OVERRIDING SYSTEM VALUE` p/ ALWAYS [K—verificar] |
| Sequences | Não (`sqlite_sequence` p/ AUTOINCREMENT) | Não (AUTO_INCREMENT) | Sim | Sim (generators) | Sim |
| DDL transacional | Sim | Não | Sim | Sim (com ressalvas) | Sim |
| Máx. parâmetros por comando | 32766 (≥3.32) [K] | 65535 | 65535 | ~ limitado por tamanho de mensagem [K] | idem |
| Máx. comprimento de identificador | ilimitado prático | 64 | 63 bytes | 31 (FB3) | 63 caracteres (FB4+) |
| Bulk recomendado | Prepared + transação | Multi-row parametrizado | `COPY ... BINARY` | Prepared por linha | Prepared por linha (batch API — PV-002) |

## 3. SQLite (`Microsoft.Data.Sqlite`)

- **Conexão**: `Data Source=<caminho>;Mode=ReadWrite|ReadOnly;Foreign Keys=True` (Microsoft.Data.Sqlite aceita `Foreign Keys` na connection string [K]). O app **liga** `foreign_keys` por padrão em conexões do editor e inserção — o usuário pode desligar por perfil (`Extra["foreignKeys"]=false`) com aviso. Arquivo inexistente: erro `PRV-SQLITE-NOFILE` (não criar implicitamente; opção explícita "Criar novo banco" no diálogo).
- **Tipos**: afinidade por regras do SQLite (INT→INTEGER; CHAR/CLOB/TEXT→TEXT; BLOB/vazio→BLOB; REAL/FLOA/DOUB→REAL; resto→NUMERIC). O MDS refina o `LogicalType` pelo **tipo declarado**: `bool/boolean`→Boolean, `date`→Date, `datetime/timestamp`→DateTime, `time`→Time, `uuid/guid`→Uuid, `json`→Json, `decimal/numeric(p,s)`→Decimal, `varchar(n)/char(n)`→String com Length=n (informativo: SQLite não aplica limites, exceto tabelas `STRICT`). Tipo vazio → `any` → `Other`.
- **Armazenamento de datas** (configurável por perfil, `Extra["dateStorage"]`): `text` (padrão, ISO-8601 `YYYY-MM-DD HH:MM:SS[.fff]`), `unix` (inteiro), `julian` (real).
- **Chaves**: `INTEGER PRIMARY KEY` é alias de rowid → flag `ai`. Valores explícitos aceitos.
- **Concorrência**: escrita exclusiva por arquivo; inserção direta usa `BEGIN IMMEDIATE`.
- **Particularidades**: `WITHOUT ROWID`, `STRICT` (≥3.37), colunas geradas (≥3.31). Nomes de FK não existem no SQLite → nomes sintéticos `fk_<tabela>_<id>` a partir de `pragma_foreign_key_list.id`.

## 4. MySQL (`MySqlConnector`)

- **Conexão**: `Server;Port;Database;User ID;Password;SslMode;CharacterSet=utf8mb4;AllowUserVariables=false;AllowLoadLocalInfile=false;ConvertZeroDateTime=true;DefaultCommandTimeout`. `AllowLoadLocalInfile=false` sempre (segurança; `MySqlBulkCopy` não é usado no V1).
- **Diferenças de connection string vs MySql.Data**: documentadas na UI por dicas; chaves extras permitidas: `SslMode`, `SslCa`, `ConnectionTimeout`, `DefaultCommandTimeout`, `CharacterSet`, `ServerRedirectionMode` (não) → allow-list em `MySqlProvider.ExtraSettings`.
- **Tipos**: `tinyint(1)` → Boolean (convenção; configurável `Extra["tinyint1IsBool"]`, padrão `true`); `unsigned` preservado; `enum`/`set` com valores; `json`; `datetime(fsp)`/`timestamp(fsp)`; `year` → Integer(16) com faixa 1901–2155; `bit(n)` → Binary/Integer.
- **Faixas**: `TIMESTAMP` 1970-01-01 00:00:01 a 2038-01-19 03:14:07 UTC; `DATETIME` 1000–9999; validador aplica ([17 §3](17-constraint-validation.md)).
- **sql_mode**: lido na introspecção (`@@SESSION.sql_mode`, `@@GLOBAL.sql_mode`). Sem `STRICT_*` o MySQL trunca silenciosamente → validação torna violações de tamanho **erro** independentemente do modo.
- **Chaves**: AUTO_INCREMENT aceita explícito; após inserção explícita, InnoDB ajusta o contador automaticamente (sem resync necessário) [K]. `LAST_INSERT_ID()` só usado na estratégia Captured com INSERT de **uma linha** (ADR-008).
- **Escapes**: barra invertida é caractere de escape salvo `NO_BACKSLASH_ESCAPES` → formatter escapa `\` e `'` conforme `sql_mode` capturado do destino; no script, preâmbulo `SET NAMES utf8mb4;` e comentário avisando o `sql_mode` assumido.
- **Sessão**: `SET FOREIGN_KEY_CHECKS=0` disponível somente como estratégia de ciclo `DisableChecks` com opt-in explícito e reativação garantida em `finally`/teardown.
- **MariaDB**: mesmo provider; detecção por `@@version` contendo `MariaDB`; diferenças tratadas por `ProviderCapabilities` (RETURNING ≥10.5). Oficial apenas no V2.

## 5. PostgreSQL (`Npgsql`)

- **Conexão**: `Host;Port;Database;Username;Password;SSL Mode;Search Path;Timeout;Command Timeout;Application Name=MockDataStudio`.
- **Tipos**: `int2/int4/int8`, `numeric(p,s)` (sem p → Decimal ilimitado, validação usa escala 2 por padrão — PA-011), `varchar(n)`, `bpchar(n)`, `text`, `bool`, `date`, `time`, `timetz`, `timestamp`, `timestamptz`, `interval`, `uuid`, `json/jsonb`, `bytea`, `float4/float8`, enums de usuário (`pg_enum`) → Enum, domains → tipo base + `dom`, arrays (`_int4`) → `Other` com aviso "geração de arrays: V2".
- **Chaves**: identity (`attidentity` `a`/`d`) e serial (default `nextval`). Explicit + `OVERRIDING SYSTEM VALUE` para ALWAYS. Resync: `SELECT setval('<seq>', <max>, true);` usando `pg_get_serial_sequence`.
- **Bulk**: `COPY tabela (cols) FROM STDIN (FORMAT BINARY)` via `BeginBinaryImport`, com escrita tipada por `NpgsqlDbType` derivado do tipo nativo. Fallback: INSERT multi-linha parametrizado (≤ 65535 parâmetros) quando COPY falhar por permissão.
- **Deferrable**: FKs `DEFERRABLE` permitem estratégia `DeferredConstraint` (`SET CONSTRAINTS ALL DEFERRED` dentro da transação).
- **Particionamento/herança**: inserção na tabela pai particionada é suportada pelo PG; partições individuais são excluídas do digest por padrão (`relispartition`).

## 6. Firebird (`FirebirdSql.Data.FirebirdClient`)

- **Conexão**: `DataSource;Port;Database (caminho ou alias);User;Password;Role;Charset;Dialect=3;ServerType (Default|Embedded);ConnectionTimeout;WireCrypt`. Embedded (fbclient local) suportado como opção avançada (requer DLLs do Firebird no PATH; não distribuídas no V1 — PA-008).
- **Versões**: 3.0 (mínimo), 4.0, 5.0. Detecção por `rdb$get_context('SYSTEM','ENGINE_VERSION')`.
- **Tipos**: `SMALLINT/INTEGER/BIGINT/INT128 (4+)`, `NUMERIC/DECIMAL(p,s)` (armazenados como inteiros escalados; `RDB$FIELD_SCALE` negativo), `FLOAT/DOUBLE PRECISION/DECFLOAT (4+)`, `CHAR/VARCHAR(n)` com charset (`OCTETS` → Binary), `BLOB SUB_TYPE TEXT` → Text, `BLOB SUB_TYPE BINARY` → Binary, `DATE/TIME/TIMESTAMP`, `TIME/TIMESTAMP WITH TIME ZONE (4+)`, `BOOLEAN (3+)`. Domínios (`RDB$FIELD_SOURCE` não iniciado por `RDB$`) preservados em `dom`.
- **Limites**: `VARCHAR` máximo 32765 **bytes** → com UTF8, `n` chars podem ocupar até 4n bytes; validador calcula bytes reais (UTF-8) para colunas com charset UTF8 e `lu` desconhecido.
- **Identificadores**: não citados são armazenados em MAIÚSCULAS. O MDS sempre cita (`"CLIENTE"`) usando o nome armazenado → seguro com dialeto 3.
- **Chaves**: identity (FB3+: BY DEFAULT; FB4+: ALWAYS também), generators + trigger legada (`tsq`). Resync (sequences e identity via `ALTER TABLE t ALTER COLUMN c RESTART WITH <n>`): a semântica de `RESTART WITH n` difere entre versões [K — PV-005, T-PRV-FB-KEY-003]: **FB3** — o próximo `NEXT VALUE` retorna `n + increment` ⇒ emitir `n = last`; **FB4+** — o próximo valor retorna `n` ⇒ emitir `n = last + increment`. O `FirebirdDialect` escolhe pela versão do destino; em script sem destino conectado usa a versão do digest e escreve comentário explicativo. Após resync em inserção direta, verificação com `GEN_ID(gen, 0)` (sequence) para confirmar o valor corrente esperado.
- **INSERT**: um registro por comando. Script gerado **não** usa `SET TERM` (desnecessário sem blocos PSQL). `COMMIT;` a cada N comandos (padrão 5.000) para compatibilidade com isql.
- **Transações**: isolamento padrão `ReadCommitted` + `rec_version`, `wait` com timeout de lock (`Extra["lockTimeout"]`, padrão 10 s).
- **Limite de contexto**: muitas linhas numa transação aumentam o tamanho do undo log; `PerBatch` padrão com 5.000 linhas.

## 7. Tratamento de desconexão e timeouts (todos)

| Situação | Detecção | Ação |
|---|---|---|
| Servidor indisponível ao abrir | Exceção de conexão (`DbErrorCategory.Connection`) | 2 tentativas (1 s, 3 s); depois erro `PRV-CONN-FAILED` com mensagem do driver |
| Timeout de comando | `Timeout` | Erro `PRV-TIMEOUT`; no editor, sugestão de aumentar timeout |
| Conexão caída durante operação | `Connection` em comando | Operação abortada; pool limpo (`ClearPool`) para o perfil |
| Cancelamento | `Cancelled` | Tratado como cancelamento normal, não erro |

## 8. Identificação de ambiente e fingerprint

`ServerInfo.FingerprintHash = SHA256(engine | host normalizado (lowercase, sem porta padrão) | porta | database normalizado | identificador de servidor se disponível)`. Identificadores disponíveis: PG `inet_server_addr()`+`current_database()`; MySQL `@@server_uuid`; Firebird `MON$DATABASE.MON$GUID` (4+) [K]; SQLite caminho absoluto canônico do arquivo. Usado para: vincular `ExecutionAuthorization`, detectar origem ≠ destino ([16 §9](16-relational-generation.md)), e alertar quando o perfil aponta para outro servidor que o registrado no projeto.
