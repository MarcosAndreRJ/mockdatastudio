using System.Globalization;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Application.Explorer;

// Modelo de apresentação do Object Inspector (prompt do módulo, seção 5). Todos os valores vêm de TableModel real.
public sealed record ColumnInfo(
    string Name, int Ordinal, string NativeType, int? Length, int? Precision, int? Scale,
    bool Nullable, string? DefaultValue, bool IsPrimaryKey, bool IsForeignKey, bool IsUnique, bool IsIdentity, bool IsGenerated);

public sealed record IndexInfo(string Name, bool IsUnique, IReadOnlyList<string> Columns, string? Predicate, string Origin);

public sealed record ForeignKeyInfo(
    string Name, IReadOnlyList<string> Columns, ObjectName Referenced, IReadOnlyList<string> ReferencedColumns,
    string OnUpdate, string OnDelete);

public sealed record CheckInfo(string? Name, string Expression);

public sealed record TableInspection(
    ObjectName Table, IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys, IReadOnlyList<CheckInfo> Checks, string? Comment);

public static class ObjectInspector
{
    public static TableInspection Inspect(ObjectName table, TableModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var pk = model.PrimaryKey?.Columns ?? [];
        var fkColumns = model.ForeignKeys.SelectMany(f => f.Columns).ToHashSet(StringComparer.Ordinal);
        var uniqueColumns = model.UniqueConstraints.Where(u => u.Columns.Count == 1).Select(u => u.Columns[0])
            .ToHashSet(StringComparer.Ordinal);

        var columns = model.Columns
            .OrderBy(c => c.Ordinal)
            .Select(c => new ColumnInfo(
                Name: c.Name,
                Ordinal: c.Ordinal,
                NativeType: c.Type.NativeName,
                Length: c.Type.Length,
                Precision: c.Type.Precision,
                Scale: c.Type.Scale,
                Nullable: c.IsNullable,
                DefaultValue: c.Default?.RawExpression,
                IsPrimaryKey: pk.Contains(c.Name, StringComparer.Ordinal),
                IsForeignKey: fkColumns.Contains(c.Name),
                IsUnique: uniqueColumns.Contains(c.Name),
                IsIdentity: c.Identity is not null,
                IsGenerated: c.Generated is not null))
            .ToList();

        // Índices: índices explícitos e UNIQUE constraints (estas aparecem como índice único).
        var indexes = model.Indexes
            .Select(i => new IndexInfo(i.Name, i.IsUnique, i.Columns.Select(ColumnLabel).ToList(), i.Predicate, "index"))
            .Concat(model.UniqueConstraints.Select(u => new IndexInfo(
                u.Name ?? "(sem nome)", true, u.Columns, null, u.Source == UniqueSource.Constraint ? "constraint" : "unique index")))
            .ToList();

        var foreignKeys = model.ForeignKeys
            .Select(f => new ForeignKeyInfo(f.Name, f.Columns, f.Referenced, f.ReferencedColumns,
                ActionText(f.OnUpdate), ActionText(f.OnDelete)))
            .ToList();

        var checks = model.Checks.Select(c => new CheckInfo(c.Name, c.Expression)).ToList();
        return new TableInspection(table, columns, indexes, foreignKeys, checks, model.Comment);
    }

    private static string ColumnLabel(IndexColumn column) =>
        column.Column ?? column.Expression ?? string.Empty;

    private static string ActionText(ReferentialAction action) => action switch
    {
        ReferentialAction.NoAction => "NO ACTION",
        ReferentialAction.Restrict => "RESTRICT",
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.SetDefault => "SET DEFAULT",
        _ => action.ToString().ToUpperInvariant(),
    };
}

// Preview: SELECT com colunas explícitas e LIMIT obrigatório (prompt, seção 5: nunca SELECT * sem limite).
public static class TablePreview
{
    public const int DefaultRows = 100;

    public const int MaxRows = 1000;

    public static int ClampLimit(int requested) => Math.Clamp(requested, 1, MaxRows);

    // quote: função de citação de identificador do dialeto (ISqlDialect.QuoteIdentifier).
    public static string BuildSql(ObjectName table, TableModel model, Func<string, string> quote, int limit)
    {
        ArgumentNullException.ThrowIfNull(quote);
        var columns = string.Join(", ", model.Columns.OrderBy(c => c.Ordinal).Select(c => quote(c.Name)));
        var target = quote(table.Name);
        return $"SELECT {columns} FROM {target} LIMIT {ClampLimit(limit).ToString(CultureInfo.InvariantCulture)}";
    }

    // Versão por dialeto: nome qualificado (ex.: `db`.`tabela` no MySQL) e sintaxe de limite do SGBD
    // (FIRST n no Firebird). Usada pelo Explorer; a versão acima continua para quem só tem a função de citação.
    public static string BuildSql(ObjectName table, TableModel model, MockDataStudio.Providers.ISqlDialect dialect, int limit)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        var columns = string.Join(", ", model.Columns.OrderBy(c => c.Ordinal).Select(c => dialect.QuoteIdentifier(c.Name)));
        return dialect.BuildLimitedSelect(columns, dialect.QualifiedName(table), ClampLimit(limit));
    }
}
