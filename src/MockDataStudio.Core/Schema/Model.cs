using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;

namespace MockDataStudio.Core.Schema;

// Modelo canônico do schema (08 §2.2, C01). Todos os tipos são imutáveis (AR-07).
// Os construtores estáticos Create validam invariantes e retornam Result<T> (TASK-01.02).

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

    public static Result<ColumnModel> Create(
        string name, int ordinal, DataTypeInfo type, bool isNullable,
        DefaultValue? defaultValue = null, IdentityInfo? identity = null, GeneratedInfo? generated = null,
        string? collation = null, string? comment = null, IReadOnlyDictionary<string, string>? extras = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            return Result<ColumnModel>.Fail(ErrorCodes.DigSchema, "Coluna sem nome.");
        }

        if (ordinal < 1)
        {
            return Result<ColumnModel>.Fail(ErrorCodes.DigSchema, $"Posição ordinal inválida para a coluna '{name}'.");
        }

        if (identity is not null && generated is not null)
        {
            return Result<ColumnModel>.Fail(ErrorCodes.DigFlags, $"A coluna '{name}' não pode ser identidade e gerada ao mesmo tempo.");
        }

        return Result<ColumnModel>.Ok(new ColumnModel(name, ordinal, type, isNullable, defaultValue, identity, generated, collation, comment, extras));
    }
}

public sealed record PrimaryKeyModel(string? Name, IReadOnlyList<string> Columns);

public sealed record ForeignKeyModel(
    string Name, IReadOnlyList<string> Columns, ObjectName Referenced, IReadOnlyList<string> ReferencedColumns,
    ReferentialAction OnDelete = ReferentialAction.NoAction, ReferentialAction OnUpdate = ReferentialAction.NoAction,
    bool IsDeferrable = false, bool InitiallyDeferred = false, bool IsEnforced = true);

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
    public ColumnModel? FindColumn(string name) => Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    public static Result<TableModel> Create(
        string name, IReadOnlyList<ColumnModel> columns,
        PrimaryKeyModel? primaryKey = null, IReadOnlyList<ForeignKeyModel>? foreignKeys = null,
        IReadOnlyList<UniqueConstraintModel>? uniqueConstraints = null, IReadOnlyList<IndexModel>? indexes = null,
        IReadOnlyList<CheckConstraintModel>? checks = null, string? comment = null,
        IReadOnlyDictionary<string, string>? extras = null)
    {
        var fks = foreignKeys ?? [];
        var uqs = uniqueConstraints ?? [];
        var ixs = indexes ?? [];
        var cks = checks ?? [];

        if (string.IsNullOrEmpty(name))
        {
            return Result<TableModel>.Fail(ErrorCodes.DigSchema, "Tabela sem nome.");
        }

        if (columns.Count == 0)
        {
            return Result<TableModel>.Fail(ErrorCodes.DigSchema, $"A tabela '{name}' não possui colunas.");
        }

        var names = columns.Select(c => c.Name).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            return Result<TableModel>.Fail(ErrorCodes.DigDup, $"A tabela '{name}' possui colunas com nome duplicado.");
        }

        var known = names.ToHashSet(StringComparer.Ordinal);

        if (primaryKey is not null && primaryKey.Columns.Any(c => !known.Contains(c)))
        {
            return Result<TableModel>.Fail(ErrorCodes.DigPkCol, $"A PK de '{name}' referencia coluna inexistente.");
        }

        foreach (var fk in fks)
        {
            if (fk.Columns.Count != fk.ReferencedColumns.Count)
            {
                return Result<TableModel>.Fail(ErrorCodes.DigFkArity, $"A FK '{fk.Name}' de '{name}' tem quantidade de colunas diferente da referenciada.");
            }

            if (fk.Columns.Any(c => !known.Contains(c)))
            {
                return Result<TableModel>.Fail(ErrorCodes.DigPkCol, $"A FK '{fk.Name}' de '{name}' referencia coluna inexistente.");
            }
        }

        if (uqs.Any(u => u.Columns.Any(c => !known.Contains(c))))
        {
            return Result<TableModel>.Fail(ErrorCodes.DigPkCol, $"Uma UNIQUE de '{name}' referencia coluna inexistente.");
        }

        if (ixs.Any(i => i.Columns.Any(c => c.Column is not null && !known.Contains(c.Column))))
        {
            return Result<TableModel>.Fail(ErrorCodes.DigPkCol, $"Um índice de '{name}' referencia coluna inexistente.");
        }

        return Result<TableModel>.Ok(new TableModel(name, columns, primaryKey, fks, uqs, ixs, cks, comment, extras));
    }
}

public sealed record ViewModel(string Name, IReadOnlyList<ColumnModel> Columns, bool? IsUpdatable = null);

public sealed record SequenceModel(string Name, long? Start = null, long? Increment = null, long? MinValue = null, long? MaxValue = null, bool IsCycle = false);

public sealed record TriggerModel(string Name, ObjectName? Table, TriggerTiming Timing, TriggerEvents Events, bool IsActive = true, int? Position = null);

public sealed record RoutineModel(string Name, RoutineKind Kind, int ParameterCount, string? ReturnType = null);

public sealed record NamespaceModel(
    string Name, bool IsVirtual,
    IReadOnlyList<TableModel> Tables, IReadOnlyList<ViewModel> Views,
    IReadOnlyList<SequenceModel> Sequences, IReadOnlyList<RoutineModel> Routines,
    IReadOnlyList<TriggerModel> Triggers)
{
    public static Result<NamespaceModel> Create(
        string name, bool isVirtual,
        IReadOnlyList<TableModel> tables, IReadOnlyList<ViewModel>? views = null,
        IReadOnlyList<SequenceModel>? sequences = null, IReadOnlyList<RoutineModel>? routines = null,
        IReadOnlyList<TriggerModel>? triggers = null)
    {
        var vs = views ?? [];
        var ss = sequences ?? [];
        var rs = routines ?? [];
        var ts = triggers ?? [];

        if (string.IsNullOrEmpty(name))
        {
            return Result<NamespaceModel>.Fail(ErrorCodes.DigSchema, "Namespace sem nome.");
        }

        if (!DistinctNames(tables.Select(t => t.Name)) || !DistinctNames(vs.Select(v => v.Name)))
        {
            return Result<NamespaceModel>.Fail(ErrorCodes.DigDup, $"O namespace '{name}' possui tabelas ou views com nome duplicado.");
        }

        if (!DistinctNames(ss.Select(s => s.Name)) || !DistinctNames(rs.Select(r => r.Name)) || !DistinctNames(ts.Select(t => t.Name)))
        {
            return Result<NamespaceModel>.Fail(ErrorCodes.DigDup, $"O namespace '{name}' possui sequences, rotinas ou triggers com nome duplicado.");
        }

        return Result<NamespaceModel>.Ok(new NamespaceModel(name, isVirtual, tables, vs, ss, rs, ts));
    }

    private static bool DistinctNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Distinct(StringComparer.Ordinal).Count() == list.Count;
    }
}

public sealed record CaptureOptions(
    bool IncludeViews = true, bool IncludeRoutines = true, bool IncludeTriggers = true,
    bool IncludeComments = false, bool IncludeIndexes = true,
    IReadOnlyList<string>? NamespaceFilter = null,
    IReadOnlyList<string>? TableIncludePatterns = null,
    IReadOnlyList<string>? TableExcludePatterns = null);

public sealed record SchemaModel(
    DatabaseEngine Engine, string ServerVersion, string DatabaseName,
    IReadOnlyList<NamespaceModel> Namespaces, DateTimeOffset CapturedAt, CaptureOptions Options)
{
    public static Result<SchemaModel> Create(
        DatabaseEngine engine, string serverVersion, string databaseName,
        IReadOnlyList<NamespaceModel> namespaces, DateTimeOffset capturedAt, CaptureOptions? options = null)
    {
        if (string.IsNullOrEmpty(databaseName))
        {
            return Result<SchemaModel>.Fail(ErrorCodes.DigSchema, "Banco sem nome.");
        }

        var names = namespaces.Select(n => n.Name).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            return Result<SchemaModel>.Fail(ErrorCodes.DigDup, "Há namespaces com nome duplicado.");
        }

        return Result<SchemaModel>.Ok(new SchemaModel(engine, serverVersion, databaseName, namespaces, capturedAt, options ?? new CaptureOptions()));
    }

    public TableModel? FindTable(ObjectName name)
    {
        var ns = Namespaces.FirstOrDefault(n => string.Equals(n.Name, name.Namespace, StringComparison.Ordinal));
        return ns?.Tables.FirstOrDefault(t => string.Equals(t.Name, name.Name, StringComparison.Ordinal));
    }

    public IEnumerable<(ObjectName Name, TableModel Table)> AllTables() =>
        Namespaces.SelectMany(ns => ns.Tables.Select(t => (new ObjectName(ns.Name, t.Name), t)));
}
