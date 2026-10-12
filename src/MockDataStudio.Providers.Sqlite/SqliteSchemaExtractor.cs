using System.Data.Common;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Sqlite;

// Extrator de schema do SQLite (docs/11 §2 e §4.1). Namespace único "main" (virtual).
// Consultas em lote por objeto via pragmas table-valued; DDL analisado com SqliteDdl.
public sealed class SqliteSchemaExtractor(SqliteTypeMapper types) : ISchemaExtractor
{
    private const string MainNamespace = "main";

    private sealed record ObjectRow(string Name, string Type, int WithoutRowId, int Strict, string Sql);
    private sealed record ColumnRow(int Cid, string Name, string DeclaredType, bool NotNull, string? Default, int PkPosition, int Hidden);

    public async Task<Result<SchemaModel>> ExtractAsync(DbConnection connection, CaptureOptions options,
        IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        if (options.NamespaceFilter is { Count: > 0 } filter && !filter.Contains(MainNamespace, StringComparer.OrdinalIgnoreCase))
        {
            return Result<SchemaModel>.Fail(ErrorCodes.DigSchema, "Nenhum namespace capturado: o SQLite só expõe 'main'.");
        }

        var objects = await ReadObjectsAsync(connection, ct);
        var selected = objects.Where(o => Included(o.Name, options)).ToList();
        var tables = new List<TableModel>();
        var views = new List<ViewModel>();

        for (var i = 0; i < selected.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var obj = selected[i];
            if (obj.Type == "view")
            {
                if (options.IncludeViews)
                {
                    views.Add(await ReadViewAsync(connection, obj, ct));
                }
            }
            else
            {
                var table = await ReadTableAsync(connection, obj, options, ct);
                if (!table.IsSuccess)
                {
                    return Result<SchemaModel>.Fail(table.Error);
                }

                tables.Add(table.Value);
            }

            progress?.Report(new ExtractionProgress("Tables", i + 1, selected.Count));
        }

        var includedTables = tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var triggers = options.IncludeTriggers
            ? await ReadTriggersAsync(connection, includedTables, ct)
            : [];

        var ns = NamespaceModel.Create(MainNamespace, isVirtual: false, tables, views, triggers: triggers);
        if (!ns.IsSuccess)
        {
            return Result<SchemaModel>.Fail(ns.Error);
        }

        var version = await ScalarAsync(connection, "SELECT sqlite_version()", ct) ?? string.Empty;
        var file = await DatabaseFileAsync(connection, ct);
        return SchemaModel.Create(DatabaseEngine.Sqlite, version, Path.GetFileName(file), [ns.Value], DateTimeOffset.UtcNow, options);
    }

    public async Task<Result<TableModel>> ExtractTableAsync(DbConnection connection, ObjectName table, CancellationToken ct)
    {
        var objects = await ReadObjectsAsync(connection, ct);
        var obj = objects.FirstOrDefault(o => o.Type == "table" && string.Equals(o.Name, table.Name, StringComparison.Ordinal));
        if (obj is null)
        {
            return Result<TableModel>.Fail(ErrorCodes.DigSchema, $"Tabela '{table.Name}' não existe no banco.");
        }

        return await ReadTableAsync(connection, obj, new CaptureOptions(), ct);
    }

    public async Task<IReadOnlyList<ObjectName>> ListTablesAsync(DbConnection connection, CancellationToken ct)
    {
        var objects = await ReadObjectsAsync(connection, ct);
        return objects.Where(o => o.Type == "table").Select(o => new ObjectName(MainNamespace, o.Name)).ToList();
    }

    public async Task<IReadOnlyList<ObjectName>> ListViewsAsync(DbConnection connection, CancellationToken ct)
    {
        var objects = await ReadObjectsAsync(connection, ct);
        return objects.Where(o => o.Type == "view").Select(o => new ObjectName(MainNamespace, o.Name)).ToList();
    }

    public async Task<IReadOnlyList<TriggerModel>> ListTriggersAsync(DbConnection connection, CancellationToken ct)
    {
        var tables = (await ReadObjectsAsync(connection, ct)).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        return await ReadTriggersAsync(connection, tables, ct);
    }

    private static async Task<List<ObjectRow>> ReadObjectsAsync(DbConnection connection, CancellationToken ct)
    {
        var meta = await QueryAsync(connection,
            "SELECT name, type, wr, strict FROM pragma_table_list WHERE schema = 'main' AND type IN ('table','view')",
            [], r => (Name: r.GetString(0), Type: r.GetString(1), Wr: r.GetInt32(2), Strict: r.GetInt32(3)), ct);

        var sqlByName = await QueryAsync(connection,
            "SELECT name, sql FROM sqlite_schema WHERE type IN ('table','view')",
            [], r => (Name: r.GetString(0), Sql: r.IsDBNull(1) ? string.Empty : r.GetString(1)), ct);

        return meta
            .Where(m => !m.Name.StartsWith("sqlite_", StringComparison.Ordinal))
            .Select(m => new ObjectRow(m.Name, m.Type, m.Wr, m.Strict,
                sqlByName.FirstOrDefault(s => s.Name == m.Name).Sql ?? string.Empty))
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<Result<TableModel>> ReadTableAsync(DbConnection connection, ObjectRow obj, CaptureOptions options, CancellationToken ct)
    {
        var rows = await ReadColumnsAsync(connection, obj.Name, ct);
        var pkColumns = rows.Where(r => r.PkPosition > 0).OrderBy(r => r.PkPosition).ToList();
        var singleIntegerPk = pkColumns.Count == 1 && obj.WithoutRowId == 0
            && string.Equals(pkColumns[0].DeclaredType.Trim(), "INTEGER", StringComparison.OrdinalIgnoreCase);
        var hasAutoIncrement = SqliteDdl.HasAutoIncrement(obj.Sql);

        var columns = new List<ColumnModel>();
        foreach (var row in rows.Where(r => r.Hidden != 1))
        {
            var isIdentity = singleIntegerPk && row.PkPosition == 1;
            var generated = row.Hidden is 2 or 3 && SqliteDdl.TryGeneratedColumn(obj.Sql, row.Name, out var expr, out var stored)
                ? new GeneratedInfo(expr, stored)
                : null;

            var column = ColumnModel.Create(
                row.Name, row.Cid + 1, types.Map(new NativeTypeDescriptor(row.DeclaredType, null, null, null, null, null, null)),
                isNullable: !row.NotNull && row.PkPosition == 0,
                defaultValue: DefaultOf(row.Default),
                identity: isIdentity ? new IdentityInfo(IdentityKind.AutoIncrement) : null,
                generated: generated,
                extras: isIdentity && hasAutoIncrement ? new Dictionary<string, string> { ["autoincrement"] = "1" } : null);
            if (!column.IsSuccess)
            {
                return Result<TableModel>.Fail(column.Error);
            }

            columns.Add(column.Value);
        }

        if (obj.Type == "table" && columns.Count == 0)
        {
            return Result<TableModel>.Fail(ErrorCodes.DigSchema, $"A tabela '{obj.Name}' não possui colunas visíveis.");
        }

        var columnNames = columns.Select(c => c.Name).ToList();
        var primaryKey = pkColumns.Count > 0 ? new PrimaryKeyModel(null, pkColumns.Select(c => c.Name).ToList()) : null;

        var foreignKeys = await ReadForeignKeysAsync(connection, obj.Name, ct);
        var (uniques, indexes) = await ReadIndexesAsync(connection, obj.Name, options.IncludeIndexes, ct);
        var checks = SqliteDdl.Checks(obj.Sql, columnNames).ToList();

        var extras = new Dictionary<string, string>();
        if (obj.WithoutRowId == 1)
        {
            extras["withoutRowid"] = "1";
        }

        if (obj.Strict == 1)
        {
            extras["strict"] = "1";
        }

        var table = TableModel.Create(obj.Name, columns, primaryKey, foreignKeys, uniques, indexes, checks,
            extras: extras.Count > 0 ? extras : null);
        return table;
    }

    private async Task<ViewModel> ReadViewAsync(DbConnection connection, ObjectRow obj, CancellationToken ct)
    {
        var rows = await ReadColumnsAsync(connection, obj.Name, ct);
        var columns = rows.Where(r => r.Hidden != 1).Select(row =>
        {
            var type = types.Map(new NativeTypeDescriptor(row.DeclaredType, null, null, null, null, null, null));
            return ColumnModel.Create(row.Name, row.Cid + 1, type, isNullable: !row.NotNull).Value;
        }).ToList();
        return new ViewModel(obj.Name, columns);
    }

    private static async Task<List<ColumnRow>> ReadColumnsAsync(DbConnection connection, string table, CancellationToken ct) =>
        await QueryAsync(connection,
            "SELECT cid, name, type, \"notnull\", dflt_value, pk, hidden FROM pragma_table_xinfo($t) ORDER BY cid",
            [("$t", table)],
            r => new ColumnRow(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? string.Empty : r.GetString(2),
                r.GetInt32(3) == 1, r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5), r.GetInt32(6)),
            ct);

    private static async Task<List<ForeignKeyModel>> ReadForeignKeysAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var rows = await QueryAsync(connection,
            "SELECT id, seq, \"table\", \"from\", \"to\", on_update, on_delete FROM pragma_foreign_key_list($t) ORDER BY id, seq",
            [("$t", table)],
            r => (Id: r.GetInt32(0), Seq: r.GetInt32(1), RefTable: r.GetString(2), From: r.GetString(3),
                To: r.IsDBNull(4) ? null : r.GetString(4), OnUpdate: r.GetString(5), OnDelete: r.GetString(6)),
            ct);

        var result = new List<ForeignKeyModel>();
        foreach (var group in rows.GroupBy(r => r.Id).OrderBy(g => g.Key))
        {
            var first = group.First();
            var refColumns = group.All(g => g.To is not null)
                ? group.Select(g => g.To!).ToList()
                : (await PrimaryKeyOfAsync(connection, first.RefTable, ct)).ToList();

            result.Add(new ForeignKeyModel(
                Name: $"fk_{table}_{first.Id}",
                Columns: group.Select(g => g.From).ToList(),
                Referenced: new ObjectName(MainNamespace, first.RefTable),
                ReferencedColumns: refColumns,
                OnDelete: ActionOf(first.OnDelete),
                OnUpdate: ActionOf(first.OnUpdate)));
        }

        return result;
    }

    private static async Task<(List<UniqueConstraintModel> Uniques, List<IndexModel> Indexes)> ReadIndexesAsync(
        DbConnection connection, string table, bool includeIndexes, CancellationToken ct)
    {
        var uniques = new List<UniqueConstraintModel>();
        var indexes = new List<IndexModel>();

        var list = await QueryAsync(connection,
            "SELECT name, \"unique\", origin, partial FROM pragma_index_list($t)",
            [("$t", table)],
            r => (Name: r.GetString(0), Unique: r.GetInt32(1) == 1, Origin: r.GetString(2), Partial: r.GetInt32(3) == 1),
            ct);

        foreach (var index in list.Where(i => i.Origin != "pk"))
        {
            var keys = await QueryAsync(connection,
                "SELECT seqno, cid, name, \"desc\" FROM pragma_index_xinfo($i) WHERE key = 1 ORDER BY seqno",
                [("$i", index.Name)],
                r => (SeqNo: r.GetInt32(0), Cid: r.GetInt32(1), Name: r.IsDBNull(2) ? null : r.GetString(2), Desc: r.GetInt32(3) == 1),
                ct);

            var indexSql = await ScalarAsync(connection, "SELECT sql FROM sqlite_schema WHERE name = $i", ct, ("$i", index.Name));
            var expressions = indexSql is null ? [] : SqliteDdl.IndexExpressions(indexSql);
            var columns = keys.Select(k => k.Cid == -1
                    ? new IndexColumn(null, k.Desc, k.SeqNo < expressions.Count ? expressions[k.SeqNo] : string.Empty)
                    : new IndexColumn(k.Name, k.Desc))
                .ToList();
            var names = columns.Where(c => c.Column is not null).Select(c => c.Column!).ToList();
            var hasExpression = columns.Any(c => c.Column is null);

            if (index.Origin == "u" && index.Unique)
            {
                uniques.Add(new UniqueConstraintModel(null, names, UniqueSource.Constraint));
            }
            else if (index.Origin == "c" && index.Unique && !index.Partial && !hasExpression)
            {
                uniques.Add(new UniqueConstraintModel(index.Name, names, UniqueSource.UniqueIndex));
            }
            else if (includeIndexes)
            {
                var predicate = indexSql is null ? null : SqliteDdl.IndexPredicate(indexSql);
                indexes.Add(new IndexModel(index.Name, columns, index.Unique, predicate));
            }
        }

        uniques = uniques.OrderBy(u => u.Name is null ? 0 : 1).ThenBy(u => u.Name, StringComparer.Ordinal).ToList();
        return (uniques, indexes);
    }

    private static async Task<IReadOnlyList<string>> PrimaryKeyOfAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var rows = await ReadColumnsAsync(connection, table, ct);
        return rows.Where(r => r.PkPosition > 0).OrderBy(r => r.PkPosition).Select(r => r.Name).ToList();
    }

    private static async Task<List<TriggerModel>> ReadTriggersAsync(DbConnection connection, HashSet<string> includedTables, CancellationToken ct)
    {
        var rows = await QueryAsync(connection,
            "SELECT name, sql FROM sqlite_schema WHERE type = 'trigger' ORDER BY name",
            [],
            r => (Name: r.GetString(0), Sql: r.IsDBNull(1) ? string.Empty : r.GetString(1)),
            ct);

        var triggers = new List<TriggerModel>();
        foreach (var row in rows)
        {
            var parsed = SqliteDdl.ParseTrigger(row.Name, row.Sql, MainNamespace);
            if (parsed is not null && parsed.Table is { } table && includedTables.Contains(table.Name))
            {
                triggers.Add(parsed);
            }
        }

        return triggers;
    }

    private static async Task<string> DatabaseFileAsync(DbConnection connection, CancellationToken ct)
    {
        var rows = await QueryAsync(connection, "PRAGMA database_list", [],
            r => (Name: r.GetString(1), File: r.IsDBNull(2) ? string.Empty : r.GetString(2)), ct);
        return rows.FirstOrDefault(r => r.Name == MainNamespace).File ?? string.Empty;
    }

    private static DefaultValue? DefaultOf(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('\''))
        {
            var literal = trimmed.Length >= 2 ? trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal) : trimmed;
            return new DefaultValue(raw, DefaultKind.Literal, literal);
        }

        if (Regex.IsMatch(trimmed, @"^[+-]?\d+(\.\d+)?$") || string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return new DefaultValue(raw, DefaultKind.Literal, trimmed);
        }

        if (trimmed.Contains('(', StringComparison.Ordinal) || Regex.IsMatch(trimmed, "^CURRENT_(DATE|TIME|TIMESTAMP)$", RegexOptions.IgnoreCase))
        {
            return new DefaultValue(raw, DefaultKind.Function);
        }

        return new DefaultValue(raw, DefaultKind.Other);
    }

    private static ReferentialAction ActionOf(string text) => text.ToUpperInvariant() switch
    {
        "CASCADE" => ReferentialAction.Cascade,
        "SET NULL" => ReferentialAction.SetNull,
        "SET DEFAULT" => ReferentialAction.SetDefault,
        "RESTRICT" => ReferentialAction.Restrict,
        _ => ReferentialAction.NoAction,
    };

    private static bool Included(string name, CaptureOptions options)
    {
        if (options.TableIncludePatterns is { Count: > 0 } include && !include.Any(p => Glob(name, p)))
        {
            return false;
        }

        return options.TableExcludePatterns is not { Count: > 0 } exclude || !exclude.Any(p => Glob(name, p));
    }

    // Glob simples (* e ?) sem diferenciar maiúsculas, como nos filtros de captura (docs/11 §1.4).
    private static bool Glob(string name, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
    }

    private static async Task<List<T>> QueryAsync<T>(DbConnection connection, string sql, (string Name, object? Value)[] parameters,
        Func<DbDataReader, T> map, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            command.Parameters.Add(p);
        }

        var list = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(map(reader));
        }

        return list;
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            command.Parameters.Add(p);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
