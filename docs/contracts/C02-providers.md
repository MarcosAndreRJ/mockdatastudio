# C02 — Contratos de providers (`MockDataStudio.Providers.Abstractions`)

> **Localização de tipos de dados puros:** `ConnectionSettings`, `ConnectionProfile`, `ConnectionEnvironment` e `ProviderCapabilities` são definidos em **`MockDataStudio.Core.Connections`** (consumidos por Catalog, Generation e Application, que não podem referenciar Providers.Abstractions — AR-02). Estão reproduzidos abaixo por conveniência.
>
> **Nome do tipo de ambiente (DA-034):** o código usa `ConnectionEnvironment`. "EnvironmentTag", que aparece nos documentos de domínio (08, 23), é o nome conceitual do mesmo tipo.
>
> **Organização em arquivos (DA-033):** um tipo por arquivo no namespace `MockDataStudio.Providers`; modelos auxiliares em `MockDataStudio.Providers.Models`. `IMdsModule` segue [31 §2](../31-extension-strategy.md).

```csharp
namespace MockDataStudio.Core.Connections;

public enum ConnectionEnvironment { Development, Test, Staging, Production, Unknown }

public sealed record ConnectionProfile(Guid Id, string Name, DatabaseEngine Engine, ConnectionSettings Settings,
    ConnectionEnvironment Environment, string? ColorHex, bool IsReadOnly, bool PromptPassword, string? Notes);

public sealed record ConnectionSettings(
    string? Host, int? Port, string Database, string? User,
    bool UseSsl = false, string? Charset = null,
    int ConnectTimeoutSec = 15, int CommandTimeoutSec = 0,
    IReadOnlyDictionary<string, string>? Extra = null);   // somente chaves da allow-list do provider

public sealed record ProviderCapabilities(
    bool SupportsNamespaces,
    bool SupportsReturning,              // INSERT ... RETURNING
    bool SupportsMultiRowValues,         // INSERT ... VALUES (...),(...)
    bool SupportsDeferrableConstraints,
    bool SupportsDisableFkChecksPerSession,
    bool SupportsIdentityOverride,       // OVERRIDING SYSTEM VALUE ou equivalente
    bool SupportsSequences,
    bool SupportsTransactionalDdl,
    int  MaxParametersPerCommand,
    int  MaxIdentifierLength);
```

```csharp
namespace MockDataStudio.Providers;

public interface IDatabaseProvider
{
    DatabaseEngine Engine { get; }
    string DisplayName { get; }
    IReadOnlyList<ConnectionSettingDefinition> ExtraSettings { get; } // allow-list p/ UI
    ProviderCapabilities GetCapabilities(string serverVersion);
    string BuildConnectionString(ConnectionSettings settings, string? password);
    Task<DbConnection> OpenConnectionAsync(ConnectionSettings settings, string? password, CancellationToken ct);
    Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct);
    ISchemaExtractor CreateSchemaExtractor();
    ISqlDialect GetDialect(string serverVersion);
    ITypeMapper TypeMapper { get; }
    IBulkInserter CreateBulkInserter(DbConnection connection, string serverVersion);
    ITargetInspector CreateTargetInspector(DbConnection connection, string serverVersion);
    IErrorTranslator ErrorTranslator { get; }
    /// Coloca a sessão em modo somente leitura no servidor (20 §8, 23 §5): PG/MySQL `SET SESSION ... READ ONLY`,
    /// Firebird transação `read`. SQLite: retorna false (exige reabrir com Mode=ReadOnly). Acrescentado em 2026-10-12 (Sprint B5).
    Task<bool> ApplySessionReadOnlyAsync(DbConnection connection, CancellationToken ct);
}

public sealed record ConnectionSettingDefinition(string Key, string Label, string Type /*string|int|bool|enum*/, string? Default, IReadOnlyList<string>? Options);
public sealed record ServerInfo(string Version, string Product, string DatabaseName, string? Host, string FingerprintHash);

public interface IProviderRegistry
{
    IReadOnlyList<IDatabaseProvider> All { get; }
    IDatabaseProvider Get(DatabaseEngine engine);
}

public sealed record ExtractionProgress(string Phase, int Done, int Total);

public interface ISchemaExtractor
{
    Task<Result<SchemaModel>> ExtractAsync(DbConnection connection, CaptureOptions options,
        IProgress<ExtractionProgress>? progress, CancellationToken ct);
    /// Recarrega um único objeto (refresh incremental do explorer).
    Task<Result<TableModel>> ExtractTableAsync(DbConnection connection, ObjectName table, CancellationToken ct);
    Task<IReadOnlyList<ObjectName>> ListTablesAsync(DbConnection connection, CancellationToken ct);
}

public interface ITypeMapper
{
    DataTypeInfo Map(NativeTypeDescriptor native);        // nativo → canônico (inclui LogicalType)
}
public sealed record NativeTypeDescriptor(string RawName, int? Length, int? Precision, int? Scale, int? SubType, string? Charset, string? Domain);

public interface ISqlDialect
{
    DatabaseEngine Engine { get; }
    string QuoteIdentifier(string identifier);
    string QualifiedName(ObjectName name);                 // respeita namespace virtual
    ISqlLiteralFormatter Literals { get; }
    string StatementTerminator { get; }                    // ";"
    string? BeginTransaction { get; }                      // ex. "BEGIN;" / null p/ Firebird script (isql já transacional)
    string Commit { get; }
    IReadOnlyList<string> SessionPreamble(ScriptOptions options); // ex. SET NAMES utf8mb4
    string? DisableFkChecks();                             // null se não suportado
    string? EnableFkChecks();
    string? IdentityOverrideClause(ColumnModel identityColumn); // "OVERRIDING SYSTEM VALUE" | null
    IReadOnlyList<string> ResyncSequenceStatements(ObjectName table, ColumnModel column, string? sequenceName, long nextValue);
    string BuildInsert(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalRows, string? overrideClause);
    int RecommendedRowsPerInsert { get; }                  // 1 para Firebird
    /// Comando que falha se a faixa reservada de chaves estiver ocupada (EmitGuards, 16 §5.2, 18 §1).
    /// null = dialeto sem suporte (SQLite, MySQL, Firebird → o writer emite só comentário). PG: bloco DO $$ … RAISE EXCEPTION.
    /// Acrescentado em 2026-10-12: implementado pela trilha B (B1 SQLite, B2 PostgreSQL…), consumido pela trilha A (A3).
    string? BuildKeyRangeGuard(ObjectName table, string column, long start, long end);
}

/// Subconjunto de OutputSettings (C06) relevante para o dialeto; construído pelo SqlScriptWriter a partir do perfil — não persistido.
public sealed record ScriptOptions(string Encoding = "utf-8", bool QuoteAllIdentifiers = true, bool IncludeTransaction = true);

public interface ISqlLiteralFormatter
{
    string Null { get; }
    string Format(object? value, DataTypeInfo type);       // InvariantCulture; escapes por dialeto
}

public interface ITargetInspector
{
    Task<long?> GetMaxIntegerAsync(ObjectName table, string column, CancellationToken ct);
    Task<long?> GetSequenceNextValueAsync(string sequenceName, CancellationToken ct);  // sem consumir valor quando possível
    Task<long> CountRowsAsync(ObjectName table, CancellationToken ct);
    Task<IReadOnlyList<TriggerModel>> GetActiveInsertTriggersAsync(ObjectName table, CancellationToken ct);
    Task<bool> AllKeysExistAsync(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<object?[]> keys, CancellationToken ct);
    IAsyncEnumerable<object?[]> ReadKeysAsync(ObjectName table, IReadOnlyList<string> columns, long limit, CancellationToken ct); // ORDER BY colunas
}

public sealed record BulkInsertResult(long RowsInserted, IReadOnlyList<object?[]>? CapturedKeys);

public interface IBulkInserter : IAsyncDisposable
{
    BulkInsertMethod Method { get; }                        // Copy, MultiRowInsert, PreparedStatement, Batch
    Task PrepareAsync(ObjectName table, IReadOnlyList<ColumnModel> columns, InsertOptions options, CancellationToken ct);
    Task<BulkInsertResult> InsertAsync(IReadOnlyList<object?[]> rows, DbTransaction transaction, CancellationToken ct);
}
public enum BulkInsertMethod { Copy, MultiRowInsert, PreparedStatement, Batch }
public sealed record InsertOptions(bool OverrideIdentity, IReadOnlyList<string>? CaptureColumns);

public interface IErrorTranslator
{
    /// Converte exceção do driver em erro classificado (constraint, timeout, conexão, sintaxe...).
    MdsError Translate(Exception ex);
    DbErrorCategory Categorize(Exception ex);
}
public enum DbErrorCategory { Connection, Timeout, Cancelled, Syntax, Permission, UniqueViolation, ForeignKeyViolation, NotNullViolation, CheckViolation, DataTooLong, Deadlock, Other }

public interface IQueryExecutor   // usado pelo editor SQL (Application) — implementação genérica sobre DbConnection
{
    IAsyncEnumerable<QueryResultPage> ExecuteAsync(DbConnection connection, DbTransaction? tx, string sql, QueryOptions options, CancellationToken ct);
}
public sealed record QueryOptions(int PageSize = 500, int CommandTimeoutSec = 0);
public sealed record QueryResultPage(IReadOnlyList<ResultColumn> Columns, IReadOnlyList<object?[]> Rows, bool HasMore, int? RecordsAffected, TimeSpan Elapsed);
public sealed record ResultColumn(string Name, string NativeType, Type ClrType);
```
