# C01 — Contratos comuns e do modelo canônico (`MockDataStudio.Core`)

```csharp
namespace MockDataStudio.Core.Common;

public sealed record MdsError(string Code, string Message, IReadOnlyDictionary<string, string>? Details = null);

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }              // lança InvalidOperationException se !IsSuccess
    public MdsError Error { get; }       // lança InvalidOperationException se IsSuccess
    public static Result<T> Ok(T value);
    public static Result<T> Fail(MdsError error);
}

public interface IClock { DateTimeOffset UtcNow { get; } }

public enum DatabaseEngine { Sqlite, MySql, PostgreSql, Firebird }
```

```csharp
namespace MockDataStudio.Core.Naming;

public sealed record ObjectName(string Namespace, string Name) : IComparable<ObjectName>
{
    public string CanonicalId { get; }   // regras em 08 §2.1
    public string NormalizedKey { get; } // ToUpperInvariant de ambas as partes
    public static ObjectName ParseCanonical(string canonicalId);
}

public sealed record ColumnRef(ObjectName Table, string Column)
{
    public string CanonicalId { get; }   // "<ns>.<table>.<column>" com quoting
    public static ColumnRef ParseCanonical(string canonicalId);
}
```

```csharp
namespace MockDataStudio.Core.Schema;

public enum LogicalType { Boolean, Integer, Decimal, Float, String, Text, Binary, Date, Time,
                          DateTime, DateTimeTz, Uuid, Json, Enum, Interval, Other }
public enum LengthUnit { Chars, Bytes }
public enum IdentityKind { AutoIncrement, IdentityAlways, IdentityByDefault, Serial, TriggerSequence }
public enum ReferentialAction { NoAction, Restrict, Cascade, SetNull, SetDefault }
public enum TriggerTiming { Before, After, InsteadOf }
[Flags] public enum TriggerEvents { None = 0, Insert = 1, Update = 2, Delete = 4 }
public enum RoutineKind { Procedure, Function }
public enum DefaultKind { Literal, Function, Sequence, Other }

public sealed record DataTypeInfo(
    string NativeName, LogicalType LogicalType,
    int? Length = null, int? Precision = null, int? Scale = null,
    bool IsUnsigned = false, int? IntegerBits = null,
    IReadOnlyList<string>? EnumValues = null,
    string? Charset = null, LengthUnit LengthUnit = LengthUnit.Chars, string? Domain = null);

public sealed record IdentityInfo(IdentityKind Kind, string? SequenceName = null, long? Start = null, long? Increment = null);
public sealed record GeneratedInfo(string Expression, bool Stored);
public sealed record DefaultValue(string RawExpression, DefaultKind Kind, string? LiteralValue = null);

public sealed record ColumnModel(
    string Name, int Ordinal, DataTypeInfo Type, bool IsNullable,
    DefaultValue? Default = null, IdentityInfo? Identity = null, GeneratedInfo? Generated = null,
    string? Collation = null, string? Comment = null,
    IReadOnlyDictionary<string, string>? Extras = null)
{
    public bool IsInsertable => Generated is null;
}

public sealed record PrimaryKeyModel(string? Name, IReadOnlyList<string> Columns);
public sealed record ForeignKeyModel(
    string Name, IReadOnlyList<string> Columns, ObjectName Referenced, IReadOnlyList<string> ReferencedColumns,
    ReferentialAction OnDelete = ReferentialAction.NoAction, ReferentialAction OnUpdate = ReferentialAction.NoAction,
    bool IsDeferrable = false, bool InitiallyDeferred = false, bool IsEnforced = true);
public enum UniqueSource { Constraint, UniqueIndex }
public sealed record UniqueConstraintModel(string? Name, IReadOnlyList<string> Columns, UniqueSource Source, bool NullsDistinct = true);
public sealed record IndexColumn(string? Column, bool Descending = false, string? Expression = null);
public sealed record IndexModel(string Name, IReadOnlyList<IndexColumn> Columns, bool IsUnique, string? Predicate = null);
public sealed record CheckConstraintModel(string? Name, string Expression, IReadOnlyList<string> ColumnScope);

public sealed record TableModel(
    string Name, IReadOnlyList<ColumnModel> Columns,
    PrimaryKeyModel? PrimaryKey, IReadOnlyList<ForeignKeyModel> ForeignKeys,
    IReadOnlyList<UniqueConstraintModel> UniqueConstraints, IReadOnlyList<IndexModel> Indexes,
    IReadOnlyList<CheckConstraintModel> Checks, string? Comment = null,
    IReadOnlyDictionary<string, string>? Extras = null)
{
    public ColumnModel? FindColumn(string name);  // ordinal
}

public sealed record ViewModel(string Name, IReadOnlyList<ColumnModel> Columns, bool? IsUpdatable = null);
public sealed record SequenceModel(string Name, long? Start = null, long? Increment = null, long? MinValue = null, long? MaxValue = null, bool IsCycle = false);
public sealed record TriggerModel(string Name, ObjectName? Table, TriggerTiming Timing, TriggerEvents Events, bool IsActive = true, int? Position = null); // DA-030
public sealed record RoutineModel(string Name, RoutineKind Kind, int ParameterCount, string? ReturnType = null);

public sealed record NamespaceModel(
    string Name, bool IsVirtual,
    IReadOnlyList<TableModel> Tables, IReadOnlyList<ViewModel> Views,
    IReadOnlyList<SequenceModel> Sequences, IReadOnlyList<RoutineModel> Routines,
    IReadOnlyList<TriggerModel> Triggers);

public sealed record CaptureOptions(
    bool IncludeViews = true, bool IncludeRoutines = true, bool IncludeTriggers = true,
    bool IncludeComments = false, bool IncludeIndexes = true,
    IReadOnlyList<string>? NamespaceFilter = null,      // null = todos os não-sistema
    IReadOnlyList<string>? TableIncludePatterns = null,  // glob, ex. "CAD_*"
    IReadOnlyList<string>? TableExcludePatterns = null);

public sealed record SchemaModel(
    DatabaseEngine Engine, string ServerVersion, string DatabaseName,
    IReadOnlyList<NamespaceModel> Namespaces, DateTimeOffset CapturedAt, CaptureOptions Options)
{
    public TableModel? FindTable(ObjectName name);
    public IEnumerable<(ObjectName Name, TableModel Table)> AllTables();
}
```

```csharp
namespace MockDataStudio.Core.Digest;

public interface IDigestSerializer
{
    string FormatVersion { get; }                                 // "1.0"
    string Serialize(SchemaModel model, DigestWriteOptions options);
    Result<SchemaModel> Deserialize(string json);                  // valida + migra versões antigas
}
public sealed record DigestWriteOptions(bool Indented = false, bool IncludeComments = false);

public interface IDigestValidator
{
    IReadOnlyList<DigestValidationError> Validate(string json);   // vazio = válido; avisos não invalidam
}
public enum DigestSeverity { Warning, Error }
// DA-030: severidade distingue avisos (ex.: DIG-FK-EXTERNAL, DIG-HASH-MISMATCH) de erros (12 §7).
public sealed record DigestValidationError(string JsonPath, string Code, string Message, DigestSeverity Severity = DigestSeverity.Error);
```

```csharp
namespace MockDataStudio.Core.Hashing;

public interface IStructureHasher
{
    /// SHA-256 (hex minúsculo, prefixo "sha256:") da forma canônica (12 §6).
    string Compute(SchemaModel model);
    string ComputeTable(ObjectName name, TableModel table);
    string ComputeColumnFingerprint(TableModel table, ColumnModel column); // 16 hex
}
```

```csharp
namespace MockDataStudio.Core.Diff;

public enum ChangeKind { Added, Removed, Modified, RenamedCandidate }
public enum ChangeSeverity { Info, Compatible, Breaking }
public sealed record SchemaChange(ChangeKind Kind, string Path, string? Before, string? After, ChangeSeverity Severity, string? Note = null);
public sealed record SchemaDiff(string FromHash, string ToHash, IReadOnlyList<SchemaChange> Changes)
{
    public bool IsEmpty => Changes.Count == 0;
}
public interface ISchemaDiffer { SchemaDiff Compare(SchemaModel from, SchemaModel to); }
```
