using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.MySql;

// Extrator de schema do MySQL (docs/11 §2 e §4.2) sobre information_schema, uma consulta por tipo de objeto.
// Namespace = database. Com database na conexão, lê só ele; sem database, lê todos os databases do usuário
// (exceto os de sistema). @db e @tbl restringem as consultas (refresh por tabela do Explorer).
// Compatível com MySQL 5.7+: CHECK constraints (8.0.16+) são lidas à parte e ignoradas se a tabela não existir.
public sealed partial class MySqlSchemaExtractor(MySqlTypeMapper types) : ISchemaExtractor
{
    private const string SystemSchemas = "'information_schema','mysql','performance_schema','sys'";

    // Filtro comum: exclui databases de sistema e restringe por @db/@tbl quando informados.
    private static string Filter(string schemaColumn, string tableColumn) =>
        " " + schemaColumn + " NOT IN (" + SystemSchemas + ") AND (@db IS NULL OR " + schemaColumn + " = @db)"
        + " AND (@tbl IS NULL OR " + tableColumn + " = @tbl)";

    private static readonly string RelationsSql = @"
SELECT table_schema, table_name, table_type, table_comment
FROM information_schema.tables
WHERE " + Filter("table_schema", "table_name") + @"
ORDER BY table_schema, table_name";

    private static readonly string ColumnsSql = @"
SELECT table_schema, table_name, ordinal_position, column_name, column_type, is_nullable, column_default, extra,
       column_comment, character_set_name, generation_expression
FROM information_schema.columns
WHERE " + Filter("table_schema", "table_name") + @"
ORDER BY table_schema, table_name, ordinal_position";

    private static readonly string KeysSql = @"
SELECT tc.table_schema, tc.table_name, tc.constraint_name, tc.constraint_type, k.column_name, k.ordinal_position,
       k.referenced_table_schema, k.referenced_table_name, k.referenced_column_name, rc.update_rule, rc.delete_rule
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage k
  ON k.constraint_schema = tc.constraint_schema AND k.constraint_name = tc.constraint_name AND k.table_name = tc.table_name
LEFT JOIN information_schema.referential_constraints rc
  ON rc.constraint_schema = tc.constraint_schema AND rc.constraint_name = tc.constraint_name AND rc.table_name = tc.table_name
WHERE tc.constraint_type IN ('PRIMARY KEY','UNIQUE','FOREIGN KEY') AND " +
        Filter("tc.table_schema", "tc.table_name") + @"
ORDER BY tc.table_schema, tc.table_name, tc.constraint_name, k.ordinal_position";

    private static readonly string IndexesSql = @"
SELECT table_schema, table_name, index_name, non_unique, seq_in_index, column_name, collation
FROM information_schema.statistics
WHERE index_name <> 'PRIMARY' AND " + Filter("table_schema", "table_name") + @"
ORDER BY table_schema, table_name, index_name, seq_in_index";

    private static readonly string ChecksSql = @"
SELECT tc.table_schema, tc.table_name, cc.constraint_name, cc.check_clause
FROM information_schema.table_constraints tc
JOIN information_schema.check_constraints cc
  ON cc.constraint_schema = tc.constraint_schema AND cc.constraint_name = tc.constraint_name
WHERE tc.constraint_type = 'CHECK' AND " + Filter("tc.table_schema", "tc.table_name") + @"
ORDER BY tc.table_schema, tc.table_name, cc.constraint_name";

    private static readonly string TriggersSql = @"
SELECT trigger_schema, event_object_table, trigger_name, action_timing, event_manipulation
FROM information_schema.triggers
WHERE " + Filter("trigger_schema", "event_object_table") + @"
ORDER BY trigger_schema, event_object_table, trigger_name";

    private static readonly string RoutinesSql = @"
SELECT routine_schema, routine_name, routine_type, data_type,
       (SELECT count(*) FROM information_schema.parameters p
         WHERE p.specific_schema = r.routine_schema AND p.specific_name = r.specific_name AND p.ordinal_position > 0)
FROM information_schema.routines r
WHERE " + Filter("routine_schema", "routine_name") + @"
ORDER BY routine_schema, routine_name";

    private sealed record RelationRow(string Schema, string Name, string Type, string? Comment);
    private sealed record ColumnRow(string Schema, string Table, int Ordinal, string Name, string ColumnType, bool Nullable,
        string? Default, string Extra, string? Comment, string? Charset, string? GenerationExpression);
    private sealed record KeyRow(string Schema, string Table, string Name, string Type, string Column, int Position,
        string? RefSchema, string? RefTable, string? RefColumn, string? UpdateRule, string? DeleteRule);
    private sealed record IndexRow(string Schema, string Table, string Name, bool NonUnique, int Seq, string? Column, string? Collation);
    private sealed record CheckRow(string Schema, string Table, string Name, string Clause);
    private sealed record TriggerRow(string Schema, string Table, string Name, string Timing, string Event);

    private sealed record Catalog(
        IReadOnlyList<RelationRow> Relations, IReadOnlyList<ColumnRow> Columns, IReadOnlyList<KeyRow> Keys,
        IReadOnlyList<IndexRow> Indexes, IReadOnlyList<CheckRow> Checks, IReadOnlyList<TriggerRow> Triggers);

    public async Task<Result<SchemaModel>> ExtractAsync(DbConnection connection, CaptureOptions options,
        IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        var db = CurrentDatabase(connection);
        var catalog = await ReadCatalogAsync(connection, db, null, ct);
        var relations = catalog.Relations
            .Where(r => NamespaceIncluded(r.Schema, options) && TableNameIncluded(r.Name, options))
            .ToList();

        var namespaces = new List<NamespaceModel>();
        var done = 0;
        foreach (var schema in relations.Select(r => r.Schema).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal))
        {
            var tables = new List<TableModel>();
            var views = new List<ViewModel>();
            foreach (var rel in relations.Where(r => r.Schema == schema))
            {
                ct.ThrowIfCancellationRequested();
                if (IsView(rel))
                {
                    if (options.IncludeViews)
                    {
                        views.Add(BuildView(rel, catalog));
                    }
                }
                else
                {
                    var table = BuildTable(rel, catalog, options);
                    if (!table.IsSuccess)
                    {
                        return Result<SchemaModel>.Fail(table.Error);
                    }

                    tables.Add(table.Value);
                }

                progress?.Report(new ExtractionProgress("Tables", ++done, relations.Count));
            }

            var triggers = options.IncludeTriggers
                ? catalog.Triggers.Where(t => t.Schema == schema).Select(BuildTrigger).ToList()
                : [];
            var routines = options.IncludeRoutines
                ? (await ListRoutinesAsync(connection, ct)).Where(r => r.Namespace == schema).Select(r => r.Routine).ToList()
                : [];

            var ns = NamespaceModel.Create(schema, isVirtual: false, tables, views, routines: routines, triggers: triggers);
            if (!ns.IsSuccess)
            {
                return Result<SchemaModel>.Fail(ns.Error);
            }

            namespaces.Add(ns.Value);
        }

        var version = await ScalarAsync(connection, "SELECT @@version", ct) ?? string.Empty;
        return SchemaModel.Create(DatabaseEngine.MySql, version, db ?? string.Empty, namespaces, DateTimeOffset.UtcNow, options);
    }

    public async Task<Result<TableModel>> ExtractTableAsync(DbConnection connection, ObjectName table, CancellationToken ct)
    {
        var catalog = await ReadCatalogAsync(connection, table.Namespace, table.Name, ct);
        var rel = catalog.Relations.FirstOrDefault(r => r.Schema == table.Namespace && r.Name == table.Name);
        if (rel is null)
        {
            return Result<TableModel>.Fail(ErrorCodes.DigSchema, $"Tabela '{table.CanonicalId}' não existe no banco.");
        }

        // Views também são descritas (colunas) para o inspetor do Explorer; sem chaves.
        return BuildTable(rel, catalog, new CaptureOptions());
    }

    public async Task<IReadOnlyList<ObjectName>> ListTablesAsync(DbConnection connection, CancellationToken ct) =>
        (await ReadRelationsAsync(connection, ct)).Where(r => !IsView(r)).Select(r => new ObjectName(r.Schema, r.Name)).ToList();

    public async Task<IReadOnlyList<ObjectName>> ListViewsAsync(DbConnection connection, CancellationToken ct) =>
        (await ReadRelationsAsync(connection, ct)).Where(IsView).Select(r => new ObjectName(r.Schema, r.Name)).ToList();

    public async Task<IReadOnlyList<TriggerModel>> ListTriggersAsync(DbConnection connection, CancellationToken ct)
    {
        var rows = await QueryAsync(connection, TriggersSql, CurrentDatabase(connection), null, ReadTrigger, ct);
        return rows.Select(BuildTrigger).ToList();
    }

    public async Task<IReadOnlyList<(string Namespace, RoutineModel Routine)>> ListRoutinesAsync(DbConnection connection, CancellationToken ct)
    {
        var rows = await QueryAsync(connection, RoutinesSql, CurrentDatabase(connection), null,
            r => (Schema: Str(r, 0)!, Name: Str(r, 1)!, Type: Str(r, 2) ?? "PROCEDURE", Returns: Str(r, 3), Params: Int(r, 4)), ct);
        return rows.Select(r => (r.Schema, new RoutineModel(r.Name,
                r.Type.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase) ? RoutineKind.Function : RoutineKind.Procedure,
                r.Params, string.IsNullOrEmpty(r.Returns) ? null : r.Returns)))
            .ToList();
    }

    private static bool IsView(RelationRow r) => r.Type.Contains("VIEW", StringComparison.OrdinalIgnoreCase);

    // Database da conexão (null = todos os databases do usuário).
    private static string? CurrentDatabase(DbConnection connection) =>
        string.IsNullOrWhiteSpace(connection.Database) ? null : connection.Database;

    private static Task<List<RelationRow>> ReadRelationsAsync(DbConnection connection, CancellationToken ct) =>
        QueryAsync(connection, RelationsSql, CurrentDatabase(connection), null,
            r => new RelationRow(Str(r, 0)!, Str(r, 1)!, Str(r, 2) ?? "BASE TABLE", Str(r, 3)), ct);

    private static async Task<Catalog> ReadCatalogAsync(DbConnection connection, string? schema, string? table, CancellationToken ct)
    {
        var relations = await QueryAsync(connection, RelationsSql, schema, table,
            r => new RelationRow(Str(r, 0)!, Str(r, 1)!, Str(r, 2) ?? "BASE TABLE", Str(r, 3)), ct);
        var columns = await QueryAsync(connection, ColumnsSql, schema, table,
            r => new ColumnRow(Str(r, 0)!, Str(r, 1)!, Int(r, 2), Str(r, 3)!, Str(r, 4) ?? "text",
                string.Equals(Str(r, 5), "YES", StringComparison.OrdinalIgnoreCase), Str(r, 6), Str(r, 7) ?? string.Empty,
                Str(r, 8), Str(r, 9), Str(r, 10)), ct);
        var keys = await QueryAsync(connection, KeysSql, schema, table,
            r => new KeyRow(Str(r, 0)!, Str(r, 1)!, Str(r, 2)!, Str(r, 3)!, Str(r, 4)!, Int(r, 5),
                Str(r, 6), Str(r, 7), Str(r, 8), Str(r, 9), Str(r, 10)), ct);
        var indexes = await QueryAsync(connection, IndexesSql, schema, table,
            r => new IndexRow(Str(r, 0)!, Str(r, 1)!, Str(r, 2)!, Int(r, 3) != 0, Int(r, 4), Str(r, 5), Str(r, 6)), ct);

        List<CheckRow> checks;
        try
        {
            checks = await QueryAsync(connection, ChecksSql, schema, table,
                r => new CheckRow(Str(r, 0)!, Str(r, 1)!, Str(r, 2)!, Str(r, 3) ?? string.Empty), ct);
        }
        catch (DbException)
        {
            checks = []; // information_schema.check_constraints só existe a partir do MySQL 8.0.16
        }

        var triggers = await QueryAsync(connection, TriggersSql, schema, table, ReadTrigger, ct);
        return new Catalog(relations, columns, keys, indexes, checks, triggers);
    }

    private static TriggerRow ReadTrigger(DbDataReader r) =>
        new(Str(r, 0)!, Str(r, 1)!, Str(r, 2)!, Str(r, 3) ?? "AFTER", Str(r, 4) ?? "INSERT");

    private Result<TableModel> BuildTable(RelationRow rel, Catalog catalog, CaptureOptions options)
    {
        var columns = new List<ColumnModel>();
        foreach (var row in catalog.Columns.Where(c => c.Schema == rel.Schema && c.Table == rel.Name))
        {
            var column = BuildColumn(row, options);
            if (!column.IsSuccess)
            {
                return Result<TableModel>.Fail(column.Error);
            }

            columns.Add(column.Value);
        }

        var keys = catalog.Keys.Where(k => k.Schema == rel.Schema && k.Table == rel.Name).ToList();
        var groups = keys.GroupBy(k => (k.Name, k.Type)).ToList();

        var pkGroup = groups.FirstOrDefault(g => g.Key.Type == "PRIMARY KEY");
        var pk = pkGroup is null ? null : new PrimaryKeyModel(pkGroup.Key.Name, pkGroup.OrderBy(k => k.Position).Select(k => k.Column).ToList());

        var uniques = groups.Where(g => g.Key.Type == "UNIQUE")
            .Select(g => new UniqueConstraintModel(g.Key.Name, g.OrderBy(k => k.Position).Select(k => k.Column).ToList(), UniqueSource.UniqueIndex))
            .ToList();

        var fks = groups.Where(g => g.Key.Type == "FOREIGN KEY")
            .Select(g =>
            {
                var ordered = g.OrderBy(k => k.Position).ToList();
                var first = ordered[0];
                return new ForeignKeyModel(g.Key.Name, ordered.Select(k => k.Column).ToList(),
                    new ObjectName(first.RefSchema ?? rel.Schema, first.RefTable ?? string.Empty),
                    ordered.Select(k => k.RefColumn ?? string.Empty).ToList(),
                    OnDelete: ActionOf(first.DeleteRule), OnUpdate: ActionOf(first.UpdateRule));
            })
            .ToList();

        // Índices que não implementam PK/UNIQUE (MySQL cria índice com o nome da constraint UNIQUE).
        var uniqueNames = new HashSet<string>(uniques.Select(u => u.Name!), StringComparer.Ordinal);
        var indexes = catalog.Indexes
            .Where(i => i.Schema == rel.Schema && i.Table == rel.Name && !uniqueNames.Contains(i.Name))
            .GroupBy(i => i.Name)
            .Select(g => new IndexModel(g.Key,
                g.OrderBy(i => i.Seq).Select(i => i.Column is null
                    ? new IndexColumn(null, i.Collation == "D", "(expressão)")
                    : new IndexColumn(i.Column, i.Collation == "D")).ToList(),
                IsUnique: !g.First().NonUnique))
            .ToList();

        var checks = catalog.Checks.Where(c => c.Schema == rel.Schema && c.Table == rel.Name)
            .Select(c => new CheckConstraintModel(c.Name, c.Clause, []))
            .ToList();

        return TableModel.Create(
            rel.Name,
            columns,
            primaryKey: pk,
            foreignKeys: IsView(rel) ? [] : fks,
            uniqueConstraints: uniques,
            indexes: options.IncludeIndexes ? indexes : [],
            checks: checks,
            comment: options.IncludeComments && !string.IsNullOrEmpty(rel.Comment) ? rel.Comment : null);
    }

    private Result<ColumnModel> BuildColumn(ColumnRow row, CaptureOptions options)
    {
        var type = types.Map(new NativeTypeDescriptor(row.ColumnType, null, null, null, null, row.Charset, null));
        var extra = row.Extra.ToLowerInvariant();

        IdentityInfo? identity = extra.Contains("auto_increment", StringComparison.Ordinal)
            ? new IdentityInfo(IdentityKind.AutoIncrement)
            : null;
        GeneratedInfo? generated = extra.Contains("generated", StringComparison.Ordinal)
                                   && !extra.Contains("default_generated", StringComparison.Ordinal)
                                   && !string.IsNullOrEmpty(row.GenerationExpression)
            ? new GeneratedInfo(row.GenerationExpression!, Stored: extra.Contains("stored", StringComparison.Ordinal))
            : null;

        DefaultValue? defaultValue = null;
        if (generated is null && row.Default is not null && !row.Default.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            // MySQL 8 devolve literais sem aspas e expressões marcadas com DEFAULT_GENERATED (docs/11 §4.2).
            // MariaDB 10.2+ devolve literais de texto entre aspas ('abc') e funções com parênteses (current_timestamp()).
            var quoted = row.Default.Length >= 2 && row.Default[0] == '\'' && row.Default[^1] == '\'';
            var isExpression = !quoted && (extra.Contains("default_generated", StringComparison.Ordinal)
                                           || CurrentTimestampRegex().IsMatch(row.Default)
                                           || row.Default.EndsWith(')'));
            var literal = quoted ? row.Default[1..^1].Replace("''", "'", StringComparison.Ordinal) : row.Default;
            defaultValue = new DefaultValue(row.Default, isExpression ? DefaultKind.Function : DefaultKind.Literal,
                isExpression ? null : literal);
        }

        return ColumnModel.Create(row.Name, row.Ordinal, type, row.Nullable,
            defaultValue: defaultValue, identity: identity, generated: generated,
            comment: options.IncludeComments && !string.IsNullOrEmpty(row.Comment) ? row.Comment : null);
    }

    private ViewModel BuildView(RelationRow rel, Catalog catalog)
    {
        var columns = new List<ColumnModel>();
        foreach (var c in catalog.Columns.Where(c => c.Schema == rel.Schema && c.Table == rel.Name))
        {
            var column = ColumnModel.Create(c.Name, c.Ordinal,
                types.Map(new NativeTypeDescriptor(c.ColumnType, null, null, null, null, c.Charset, null)), c.Nullable);
            if (column.IsSuccess)
            {
                columns.Add(column.Value);
            }
        }

        return new ViewModel(rel.Name, columns, IsUpdatable: null);
    }

    private static TriggerModel BuildTrigger(TriggerRow t)
    {
        var timing = t.Timing.Equals("BEFORE", StringComparison.OrdinalIgnoreCase) ? TriggerTiming.Before : TriggerTiming.After;
        var events = t.Event.ToUpperInvariant() switch
        {
            "INSERT" => TriggerEvents.Insert,
            "UPDATE" => TriggerEvents.Update,
            "DELETE" => TriggerEvents.Delete,
            _ => TriggerEvents.None,
        };
        return new TriggerModel(t.Name, new ObjectName(t.Schema, t.Table), timing, events);
    }

    private static ReferentialAction ActionOf(string? rule) => (rule ?? string.Empty).ToUpperInvariant() switch
    {
        "CASCADE" => ReferentialAction.Cascade,
        "SET NULL" => ReferentialAction.SetNull,
        "SET DEFAULT" => ReferentialAction.SetDefault,
        "RESTRICT" => ReferentialAction.Restrict,
        _ => ReferentialAction.NoAction,
    };

    private static bool NamespaceIncluded(string schema, CaptureOptions options) =>
        options.NamespaceFilter is not { Count: > 0 } filter || filter.Contains(schema, StringComparer.OrdinalIgnoreCase);

    private static bool TableNameIncluded(string name, CaptureOptions options)
    {
        if (options.TableExcludePatterns is { Count: > 0 } exclude && exclude.Any(p => GlobMatch(p, name)))
        {
            return false;
        }

        return options.TableIncludePatterns is not { Count: > 0 } include || include.Any(p => GlobMatch(p, name));
    }

    private static bool GlobMatch(string pattern, string name)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(name, regex, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static async Task<List<T>> QueryAsync<T>(DbConnection connection, string sql, string? schema, string? table,
        Func<DbDataReader, T> map, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@db", schema);
        AddParameter(command, "@tbl", table);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private static void AddParameter(DbCommand command, string name, string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = (object?)value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    // information_schema pode devolver texto como string ou byte[] conforme versão/charset: normaliza para string.
    private static string? Str(DbDataReader r, int i)
    {
        if (r.IsDBNull(i))
        {
            return null;
        }

        var value = r.GetValue(i);
        return value is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static int Int(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(current_timestamp|now)(\(\d*\))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CurrentTimestampRegex();
}
