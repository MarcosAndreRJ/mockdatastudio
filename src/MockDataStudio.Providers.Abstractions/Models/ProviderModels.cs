namespace MockDataStudio.Providers.Models;

public sealed record ConnectionSettingDefinition(
    string Key,
    string Label,
    string Type,
    string? Default,
    IReadOnlyList<string>? Options);

public sealed record ServerInfo(
    string Version,
    string Product,
    string DatabaseName,
    string? Host,
    string FingerprintHash);

public sealed record ExtractionProgress(string Phase, int Done, int Total);

public sealed record NativeTypeDescriptor(
    string RawName,
    int? Length,
    int? Precision,
    int? Scale,
    int? SubType,
    string? Charset,
    string? Domain);

public sealed record ScriptOptions(
    string Encoding = "utf-8",
    bool QuoteAllIdentifiers = true,
    bool IncludeTransaction = true);

public enum BulkInsertMethod { Copy, MultiRowInsert, PreparedStatement, Batch }

public sealed record InsertOptions(bool OverrideIdentity, IReadOnlyList<string>? CaptureColumns);

public sealed record BulkInsertResult(long RowsInserted, IReadOnlyList<object?[]>? CapturedKeys);

public enum DbErrorCategory
{
    Connection,
    Timeout,
    Cancelled,
    Syntax,
    Permission,
    UniqueViolation,
    ForeignKeyViolation,
    NotNullViolation,
    CheckViolation,
    DataTooLong,
    Deadlock,
    Other
}

public sealed record QueryOptions(int PageSize = 500, int CommandTimeoutSec = 0);

public sealed record ResultColumn(string Name, string NativeType, Type ClrType);

public sealed record QueryResultPage(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    bool HasMore,
    int? RecordsAffected,
    TimeSpan Elapsed);
