using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.PostgreSql;

// Extrator de schema do PostgreSQL (docs/11 §2 e §4.1). Lê pg_catalog em lote (uma consulta por tipo de objeto).
// Os parâmetros @ns/@tbl são opcionais: ExtractTableAsync reusa as mesmas consultas restritas a uma tabela.
// NÃO VERIFICADO contra servidor real nesta etapa: Docker indisponível (ver B1/B2 em docs/32).
public sealed partial class PostgreSqlSchemaExtractor(PostgreSqlTypeMapper types) : ISchemaExtractor
{
    private const string RelationsSql = @"
SELECT n.nspname, c.relname, c.relkind::text, obj_description(c.oid, 'pg_class')
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p','v','m')
  AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
  AND (@ns::text IS NULL OR n.nspname = @ns::text)
  AND (@tbl::text IS NULL OR c.relname = @tbl::text)
ORDER BY 1, 2";

    private const string ColumnsSql = @"
SELECT n.nspname, c.relname, a.attnum::int, a.attname, format_type(a.atttypid, a.atttypmod),
       NOT a.attnotnull, pg_get_expr(d.adbin, d.adrelid), a.attidentity::text, a.attgenerated::text,
       col_description(c.oid, a.attnum)
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
WHERE a.attnum > 0 AND NOT a.attisdropped AND c.relkind IN ('r','p','v','m')
  AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
  AND (@ns::text IS NULL OR n.nspname = @ns::text)
  AND (@tbl::text IS NULL OR c.relname = @tbl::text)
ORDER BY n.nspname, c.relname, a.attnum";

    private const string ConstraintsSql = @"
SELECT n.nspname, c.relname, con.conname, con.contype::text, pg_get_constraintdef(con.oid),
       ARRAY(SELECT a.attname FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
             JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum ORDER BY k.ord)::text[],
       rn.nspname, rc.relname,
       ARRAY(SELECT a.attname FROM unnest(con.confkey) WITH ORDINALITY AS k(attnum, ord)
             JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.attnum ORDER BY k.ord)::text[],
       con.confupdtype::text, con.confdeltype::text, con.condeferrable, con.condeferred, con.convalidated
FROM pg_constraint con
JOIN pg_class c ON c.oid = con.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
LEFT JOIN pg_class rc ON rc.oid = con.confrelid
LEFT JOIN pg_namespace rn ON rn.oid = rc.relnamespace
WHERE con.contype IN ('p','f','u','c')
  AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
  AND (@ns::text IS NULL OR n.nspname = @ns::text)
  AND (@tbl::text IS NULL OR c.relname = @tbl::text)
ORDER BY n.nspname, c.relname, con.conname";

    // Índices que não pertencem a constraint (PK/UNIQUE/EXCLUDE já vêm de pg_constraint).
    private const string IndexesSql = @"
SELECT n.nspname, t.relname, i.relname, ix.indisunique, pg_get_expr(ix.indpred, ix.indrelid),
       ARRAY(SELECT a.attname FROM generate_subscripts(ix.indkey::int2[], 1) AS k(ord)
             LEFT JOIN pg_attribute a ON a.attrelid = ix.indrelid AND a.attnum = (ix.indkey::int2[])[k.ord]
             ORDER BY k.ord)::text[],
       ARRAY(SELECT ((ix.indoption::int2[])[k.ord] & 1) = 1 FROM generate_subscripts(ix.indkey::int2[], 1) AS k(ord)
             ORDER BY k.ord)::bool[],
       pg_get_indexdef(ix.indexrelid)
FROM pg_index ix
JOIN pg_class i ON i.oid = ix.indexrelid
JOIN pg_class t ON t.oid = ix.indrelid
JOIN pg_namespace n ON n.oid = t.relnamespace
WHERE t.relkind IN ('r','p','m') AND NOT ix.indisprimary
  AND NOT EXISTS (SELECT 1 FROM pg_constraint con WHERE con.conindid = ix.indexrelid)
  AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
  AND (@ns::text IS NULL OR n.nspname = @ns::text)
  AND (@tbl::text IS NULL OR t.relname = @tbl::text)
ORDER BY n.nspname, t.relname, i.relname";

    // tgtype: bit 2 = BEFORE, bit 4 = INSERT, bit 8 = DELETE, bit 16 = UPDATE, bit 64 = INSTEAD OF.
    private const string TriggersSql = @"
SELECT n.nspname, c.relname, t.tgname, t.tgenabled::text, t.tgtype::int
FROM pg_trigger t
JOIN pg_class c ON c.oid = t.tgrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE NOT t.tgisinternal
  AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
  AND (@ns::text IS NULL OR n.nspname = @ns::text)
  AND (@tbl::text IS NULL OR c.relname = @tbl::text)
ORDER BY n.nspname, c.relname, t.tgname";

    private sealed record RelationRow(string Schema, string Name, string Kind, string? Comment);
    private sealed record ColumnRow(string Schema, string Table, int Ordinal, string Name, string NativeType, bool Nullable,
        string? DefaultExpr, string Identity, string Generated, string? Comment);
    private sealed record ConstraintRow(string Schema, string Table, string Name, string Type, string Definition,
        string[] Columns, string? RefSchema, string? RefTable, string[] RefColumns, string OnUpdate, string OnDelete,
        bool Deferrable, bool Deferred, bool Validated);
    private sealed record IndexRow(string Schema, string Table, string Name, bool IsUnique, string? Predicate,
        string[] Columns, bool[] Descending, string Definition);
    private sealed record TriggerRow(string Schema, string Table, string Name, string Enabled, int Type);

    private sealed record Catalog(
        IReadOnlyList<RelationRow> Relations,
        IReadOnlyList<ColumnRow> Columns,
        IReadOnlyList<ConstraintRow> Constraints,
        IReadOnlyList<IndexRow> Indexes,
        IReadOnlyList<TriggerRow> Triggers);

    public async Task<Result<SchemaModel>> ExtractAsync(DbConnection connection, CaptureOptions options,
        IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        var catalog = await ReadCatalogAsync(connection, null, null, ct);
        var relations = catalog.Relations
            .Where(r => NamespaceIncluded(r.Schema, options) && TableNameIncluded(r.Name, options))
            .ToList();

        var namespaces = new List<NamespaceModel>();
        var schemas = relations.Select(r => r.Schema).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal);
        var done = 0;
        foreach (var schema in schemas)
        {
            var tables = new List<TableModel>();
            var views = new List<ViewModel>();
            foreach (var rel in relations.Where(r => r.Schema == schema))
            {
                ct.ThrowIfCancellationRequested();
                if (IsView(rel.Kind))
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

            var ns = NamespaceModel.Create(schema, isVirtual: false, tables, views, triggers: triggers);
            if (!ns.IsSuccess)
            {
                return Result<SchemaModel>.Fail(ns.Error);
            }

            namespaces.Add(ns.Value);
        }

        var version = await ScalarAsync(connection, "SELECT current_setting('server_version')", ct) ?? string.Empty;
        var database = await ScalarAsync(connection, "SELECT current_database()", ct) ?? string.Empty;
        return SchemaModel.Create(DatabaseEngine.PostgreSql, version, database, namespaces, DateTimeOffset.UtcNow, options);
    }

    public async Task<Result<TableModel>> ExtractTableAsync(DbConnection connection, ObjectName table, CancellationToken ct)
    {
        var catalog = await ReadCatalogAsync(connection, table.Namespace, table.Name, ct);
        var rel = catalog.Relations.FirstOrDefault(r => r.Schema == table.Namespace && r.Name == table.Name && !IsView(r.Kind));
        if (rel is null)
        {
            return Result<TableModel>.Fail(ErrorCodes.DigSchema, $"Tabela '{table.CanonicalId}' não existe no banco.");
        }

        return BuildTable(rel, catalog, new CaptureOptions());
    }

    public async Task<IReadOnlyList<ObjectName>> ListTablesAsync(DbConnection connection, CancellationToken ct)
    {
        var catalog = await ReadCatalogAsync(connection, null, null, ct, relationsOnly: true);
        return catalog.Relations.Where(r => !IsView(r.Kind)).Select(r => new ObjectName(r.Schema, r.Name)).ToList();
    }

    public async Task<IReadOnlyList<ObjectName>> ListViewsAsync(DbConnection connection, CancellationToken ct)
    {
        var catalog = await ReadCatalogAsync(connection, null, null, ct, relationsOnly: true);
        return catalog.Relations.Where(r => IsView(r.Kind)).Select(r => new ObjectName(r.Schema, r.Name)).ToList();
    }

    public async Task<IReadOnlyList<TriggerModel>> ListTriggersAsync(DbConnection connection, CancellationToken ct)
    {
        var rows = await QueryAsync(connection, TriggersSql, null, null,
            r => new TriggerRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4)), ct);
        return rows.Select(BuildTrigger).ToList();
    }

    private static bool IsView(string kind) => kind is "v" or "m";

    // Lê os catálogos em lote. relationsOnly evita as consultas de colunas, constraints, índices e triggers.
    private static async Task<Catalog> ReadCatalogAsync(DbConnection connection, string? schema, string? table,
        CancellationToken ct, bool relationsOnly = false)
    {
        var relations = await QueryAsync(connection, RelationsSql, schema, table,
            r => new RelationRow(r.GetString(0), r.GetString(1), r.GetString(2), Str(r, 3)), ct);
        if (relationsOnly)
        {
            return new Catalog(relations, [], [], [], []);
        }

        var columns = await QueryAsync(connection, ColumnsSql, schema, table,
            r => new ColumnRow(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4),
                r.GetBoolean(5), Str(r, 6), r.GetString(7), r.GetString(8), Str(r, 9)), ct);

        var constraints = await QueryAsync(connection, ConstraintsSql, schema, table,
            r => new ConstraintRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetFieldValue<string[]>(5), Str(r, 6), Str(r, 7), r.GetFieldValue<string[]>(8),
                r.GetString(9), r.GetString(10), r.GetBoolean(11), r.GetBoolean(12), r.GetBoolean(13)), ct);

        var indexes = await QueryAsync(connection, IndexesSql, schema, table,
            r => new IndexRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), Str(r, 4),
                r.GetFieldValue<string[]>(5), r.GetFieldValue<bool[]>(6), r.GetString(7)), ct);

        var triggers = await QueryAsync(connection, TriggersSql, schema, table,
            r => new TriggerRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4)), ct);

        return new Catalog(relations, columns, constraints, indexes, triggers);
    }

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

        var constraints = catalog.Constraints.Where(c => c.Schema == rel.Schema && c.Table == rel.Name).ToList();
        var pk = constraints.FirstOrDefault(c => c.Type == "p");
        var fks = constraints.Where(c => c.Type == "f").Select(BuildForeignKey).ToList();
        var uniques = constraints.Where(c => c.Type == "u")
            .Select(c => new UniqueConstraintModel(c.Name, c.Columns, UniqueSource.Constraint))
            .ToList();
        var checks = constraints.Where(c => c.Type == "c")
            .Select(c => new CheckConstraintModel(c.Name, CheckExpression(c.Definition), c.Columns))
            .ToList();

        var indexes = new List<IndexModel>();
        foreach (var ix in catalog.Indexes.Where(i => i.Schema == rel.Schema && i.Table == rel.Name))
        {
            // UNIQUE sem constraint, em colunas simples, vira UNIQUE de índice (digest, docs/11 §2).
            if (ix.IsUnique && ix.Predicate is null && ix.Columns.All(c => c is not null))
            {
                uniques.Add(new UniqueConstraintModel(ix.Name, ix.Columns, UniqueSource.UniqueIndex));
                continue;
            }

            indexes.Add(new IndexModel(ix.Name, IndexColumns(ix), ix.IsUnique, ix.Predicate));
        }

        return TableModel.Create(
            rel.Name,
            columns,
            primaryKey: pk is null ? null : new PrimaryKeyModel(pk.Name, pk.Columns),
            foreignKeys: fks,
            uniqueConstraints: uniques,
            indexes: options.IncludeIndexes ? indexes : [],
            checks: checks,
            comment: options.IncludeComments ? rel.Comment : null);
    }

    private Result<ColumnModel> BuildColumn(ColumnRow row, CaptureOptions options)
    {
        var type = types.Map(new NativeTypeDescriptor(row.NativeType, null, null, null, null, null, null));

        GeneratedInfo? generated = null;
        IdentityInfo? identity = null;
        DefaultValue? defaultValue = null;

        if (row.Generated == "s" && row.DefaultExpr is not null)
        {
            generated = new GeneratedInfo(row.DefaultExpr, Stored: true);
        }
        else if (row.Identity is "a" or "d")
        {
            identity = new IdentityInfo(row.Identity == "a" ? IdentityKind.IdentityAlways : IdentityKind.IdentityByDefault);
        }
        else if (row.DefaultExpr is not null)
        {
            var seq = NextvalRegex().Match(row.DefaultExpr);
            if (seq.Success)
            {
                identity = new IdentityInfo(IdentityKind.Serial, SequenceName: seq.Groups["seq"].Value);
                defaultValue = new DefaultValue(row.DefaultExpr, DefaultKind.Sequence);
            }
            else
            {
                defaultValue = new DefaultValue(row.DefaultExpr, ClassifyDefault(row.DefaultExpr));
            }
        }

        return ColumnModel.Create(
            row.Name, row.Ordinal, type, row.Nullable,
            defaultValue: defaultValue,
            identity: identity,
            generated: generated,
            comment: options.IncludeComments ? row.Comment : null);
    }

    private static DefaultKind ClassifyDefault(string expr)
    {
        if (LiteralDefaultRegex().IsMatch(expr))
        {
            return DefaultKind.Literal;
        }

        return expr.Contains('(', StringComparison.Ordinal) ? DefaultKind.Function : DefaultKind.Other;
    }

    private static ForeignKeyModel BuildForeignKey(ConstraintRow c) =>
        new(c.Name, c.Columns, new ObjectName(c.RefSchema ?? string.Empty, c.RefTable ?? string.Empty), c.RefColumns,
            OnDelete: ReferentialActionOf(c.OnDelete), OnUpdate: ReferentialActionOf(c.OnUpdate),
            IsDeferrable: c.Deferrable, InitiallyDeferred: c.Deferred, IsEnforced: c.Validated);

    private static ReferentialAction ReferentialActionOf(string code) => code switch
    {
        "r" => ReferentialAction.Restrict,
        "c" => ReferentialAction.Cascade,
        "n" => ReferentialAction.SetNull,
        "d" => ReferentialAction.SetDefault,
        _ => ReferentialAction.NoAction,
    };

    private static string CheckExpression(string definition)
    {
        var match = CheckBodyRegex().Match(definition);
        return match.Success ? match.Groups["e"].Value : definition;
    }

    private static List<IndexColumn> IndexColumns(IndexRow ix)
    {
        // Coluna nula = expressão: guarda a definição completa do índice para não perder a informação.
        var list = new List<IndexColumn>(ix.Columns.Length);
        for (var i = 0; i < ix.Columns.Length; i++)
        {
            var desc = i < ix.Descending.Length && ix.Descending[i];
            list.Add(ix.Columns[i] is null
                ? new IndexColumn(null, desc, ix.Definition)
                : new IndexColumn(ix.Columns[i], desc));
        }

        return list;
    }

    private ViewModel BuildView(RelationRow rel, Catalog catalog)
    {
        var columns = new List<ColumnModel>();
        foreach (var c in catalog.Columns.Where(c => c.Schema == rel.Schema && c.Table == rel.Name))
        {
            var column = ColumnModel.Create(c.Name, c.Ordinal,
                types.Map(new NativeTypeDescriptor(c.NativeType, null, null, null, null, null, null)), c.Nullable);
            if (column.IsSuccess)
            {
                columns.Add(column.Value);
            }
        }

        return new ViewModel(rel.Name, columns, IsUpdatable: null);
    }

    private static TriggerModel BuildTrigger(TriggerRow t)
    {
        const int rowBefore = 2, rowInsert = 4, rowDelete = 8, rowUpdate = 16, rowInsteadOf = 64;
        var timing = (t.Type & rowInsteadOf) != 0 ? TriggerTiming.InsteadOf
            : (t.Type & rowBefore) != 0 ? TriggerTiming.Before
            : TriggerTiming.After;
        var events = TriggerEvents.None
            | ((t.Type & rowInsert) != 0 ? TriggerEvents.Insert : TriggerEvents.None)
            | ((t.Type & rowUpdate) != 0 ? TriggerEvents.Update : TriggerEvents.None)
            | ((t.Type & rowDelete) != 0 ? TriggerEvents.Delete : TriggerEvents.None);
        return new TriggerModel(t.Name, new ObjectName(t.Schema, t.Table), timing, events, IsActive: t.Enabled != "D");
    }

    private static bool NamespaceIncluded(string schema, CaptureOptions options) =>
        options.NamespaceFilter is not { Count: > 0 } filter
        || filter.Contains(schema, StringComparer.OrdinalIgnoreCase);

    // Padrões de tabela usam * e ? (glob). Include vazio = todas; exclude tem precedência.
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
        AddParameter(command, "@ns", schema);
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
        return (await command.ExecuteScalarAsync(ct))?.ToString();
    }

    private static string? Str(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    [GeneratedRegex(@"^nextval\('(?<seq>[^']+)'::regclass\)$", RegexOptions.CultureInvariant)]
    private static partial Regex NextvalRegex();

    [GeneratedRegex(@"^(-?\d+(\.\d+)?|'.*'(::[a-z ]+)?|true|false|NULL)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex LiteralDefaultRegex();

    [GeneratedRegex(@"^CHECK \((?<e>.*)\)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex CheckBodyRegex();
}
