using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Firebird;

// Extrator de schema do Firebird (docs/11 §2 e §4.4) sobre as tabelas RDB$, uma consulta por tipo de objeto.
// Compatível com Firebird 2.5 (servidor de desenvolvimento do usuário, DA-311) e 3+: colunas e tabelas que só
// existem a partir do 3.0 (RDB$IDENTITY_TYPE, RDB$FUNCTIONS) são lidas apenas quando a versão do servidor permite.
// Namespace virtual PUBLIC (docs/08 §2.1). Nomes chegam em CHAR com espaços à direita: TRIM no SQL.
public sealed partial class FirebirdSchemaExtractor(FirebirdTypeMapper types) : ISchemaExtractor
{
    private const string Ns = FirebirdDialect.VirtualNamespace;


    private static readonly string RelationsSql = @"
SELECT TRIM(r.RDB$RELATION_NAME), CASE WHEN r.RDB$VIEW_BLR IS NULL THEN 0 ELSE 1 END, r.RDB$DESCRIPTION
FROM RDB$RELATIONS r
WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND " + Filter("r.RDB$RELATION_NAME") + @"
ORDER BY 1";

    private static string ColumnsSql(string identityColumn, string filter) => @"
SELECT TRIM(rf.RDB$RELATION_NAME), rf.RDB$FIELD_POSITION, TRIM(rf.RDB$FIELD_NAME), f.RDB$FIELD_TYPE,
       COALESCE(f.RDB$FIELD_SUB_TYPE, 0), COALESCE(f.RDB$FIELD_LENGTH, 0), f.RDB$CHARACTER_LENGTH,
       COALESCE(f.RDB$FIELD_PRECISION, 0), COALESCE(f.RDB$FIELD_SCALE, 0),
       COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0), COALESCE(rf.RDB$DEFAULT_SOURCE, f.RDB$DEFAULT_SOURCE),
       TRIM(rf.RDB$FIELD_SOURCE), f.RDB$COMPUTED_SOURCE, TRIM(cs.RDB$CHARACTER_SET_NAME), " + identityColumn + @"
FROM RDB$RELATION_FIELDS rf
JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID
WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND " + filter + @"
ORDER BY 1, 2";

    private static readonly string KeysSql = @"
SELECT TRIM(rc.RDB$RELATION_NAME), TRIM(rc.RDB$CONSTRAINT_NAME), TRIM(rc.RDB$CONSTRAINT_TYPE), TRIM(s.RDB$FIELD_NAME),
       s.RDB$FIELD_POSITION, TRIM(rc2.RDB$RELATION_NAME), TRIM(s2.RDB$FIELD_NAME), TRIM(refc.RDB$UPDATE_RULE),
       TRIM(refc.RDB$DELETE_RULE)
FROM RDB$RELATION_CONSTRAINTS rc
JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rc.RDB$RELATION_NAME
JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
LEFT JOIN RDB$REF_CONSTRAINTS refc ON refc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
LEFT JOIN RDB$RELATION_CONSTRAINTS rc2 ON rc2.RDB$CONSTRAINT_NAME = refc.RDB$CONST_NAME_UQ
LEFT JOIN RDB$INDEX_SEGMENTS s2 ON s2.RDB$INDEX_NAME = rc2.RDB$INDEX_NAME AND s2.RDB$FIELD_POSITION = s.RDB$FIELD_POSITION
WHERE rc.RDB$CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY') AND COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
  AND " + Filter("rc.RDB$RELATION_NAME") + @"
ORDER BY 1, 2, 5";

    // Índices que não implementam constraint (PK/UNIQUE/FK já vêm de RDB$RELATION_CONSTRAINTS).
    private static readonly string IndexesSql = @"
SELECT TRIM(i.RDB$RELATION_NAME), TRIM(i.RDB$INDEX_NAME), COALESCE(i.RDB$UNIQUE_FLAG, 0), COALESCE(i.RDB$INDEX_TYPE, 0),
       TRIM(s.RDB$FIELD_NAME), COALESCE(s.RDB$FIELD_POSITION, 0), i.RDB$EXPRESSION_SOURCE
FROM RDB$INDICES i
JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = i.RDB$RELATION_NAME
LEFT JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = i.RDB$INDEX_NAME
WHERE COALESCE(i.RDB$SYSTEM_FLAG, 0) = 0 AND COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
  AND NOT EXISTS (SELECT 1 FROM RDB$RELATION_CONSTRAINTS rc WHERE rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME)
  AND " + Filter("i.RDB$RELATION_NAME") + @"
ORDER BY 1, 2, 6";

    // CHECK vira trigger de sistema BEFORE INSERT (tipo 1); o texto da constraint está no RDB$TRIGGER_SOURCE.
    private static readonly string ChecksSql = @"
SELECT TRIM(rc.RDB$RELATION_NAME), TRIM(rc.RDB$CONSTRAINT_NAME), t.RDB$TRIGGER_SOURCE
FROM RDB$RELATION_CONSTRAINTS rc
JOIN RDB$CHECK_CONSTRAINTS cc ON cc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
JOIN RDB$TRIGGERS t ON t.RDB$TRIGGER_NAME = cc.RDB$TRIGGER_NAME AND t.RDB$TRIGGER_TYPE = 1
WHERE rc.RDB$CONSTRAINT_TYPE = 'CHECK' AND " + Filter("rc.RDB$RELATION_NAME") + @"
ORDER BY 1, 2";

    // Triggers de tabela do usuário, sem os triggers internos de CHECK.
    private static readonly string TriggersSql = @"
SELECT TRIM(t.RDB$RELATION_NAME), TRIM(t.RDB$TRIGGER_NAME), t.RDB$TRIGGER_TYPE, COALESCE(t.RDB$TRIGGER_INACTIVE, 0),
       t.RDB$TRIGGER_SEQUENCE
FROM RDB$TRIGGERS t
WHERE COALESCE(t.RDB$SYSTEM_FLAG, 0) = 0 AND t.RDB$RELATION_NAME IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM RDB$CHECK_CONSTRAINTS cc WHERE cc.RDB$TRIGGER_NAME = t.RDB$TRIGGER_NAME)
  AND " + Filter("t.RDB$RELATION_NAME") + @"
ORDER BY 1, 2";

    private const string GeneratorsSql = @"
SELECT TRIM(g.RDB$GENERATOR_NAME) FROM RDB$GENERATORS g
WHERE COALESCE(g.RDB$SYSTEM_FLAG, 0) = 0
ORDER BY 1";

    private const string ProceduresSql = @"
SELECT TRIM(p.RDB$PROCEDURE_NAME),
       (SELECT COUNT(*) FROM RDB$PROCEDURE_PARAMETERS pp
         WHERE pp.RDB$PROCEDURE_NAME = p.RDB$PROCEDURE_NAME AND pp.RDB$PARAMETER_TYPE = 0)
FROM RDB$PROCEDURES p
WHERE COALESCE(p.RDB$SYSTEM_FLAG, 0) = 0
ORDER BY 1";

    // Funções PSQL (Firebird 3+). Funções externas legadas (UDF) ficam de fora.
    private const string FunctionsSql = @"
SELECT TRIM(f.RDB$FUNCTION_NAME),
       (SELECT COUNT(*) FROM RDB$FUNCTION_ARGUMENTS a
         WHERE a.RDB$FUNCTION_NAME = f.RDB$FUNCTION_NAME AND a.RDB$ARGUMENT_POSITION > 0)
FROM RDB$FUNCTIONS f
WHERE COALESCE(f.RDB$SYSTEM_FLAG, 0) = 0 AND COALESCE(f.RDB$LEGACY_FLAG, 0) = 0 AND f.RDB$PACKAGE_NAME IS NULL
ORDER BY 1";

    private sealed record RelationRow(string Name, bool IsView, string? Comment);
    private sealed record ColumnRow(string Table, int Ordinal, string Name, int Type, int SubType, int Length, int? CharLength,
        int Precision, int Scale, bool NotNull, string? Default, string? Domain, string? Computed, string? Charset, int? Identity);
    private sealed record KeyRow(string Table, string Name, string Type, string Column, int Position, string? RefTable,
        string? RefColumn, string? UpdateRule, string? DeleteRule);
    private sealed record IndexRow(string Table, string Name, bool Unique, bool Descending, string? Column, int Position, string? Expression);
    private sealed record CheckRow(string Table, string Name, string Source);
    private sealed record TriggerRow(string Table, string Name, int Type, bool Inactive, int? Position);

    private sealed record Catalog(
        IReadOnlyList<RelationRow> Relations, IReadOnlyList<ColumnRow> Columns, IReadOnlyList<KeyRow> Keys,
        IReadOnlyList<IndexRow> Indexes, IReadOnlyList<CheckRow> Checks, IReadOnlyList<TriggerRow> Triggers);

    // Filtro opcional por tabela. O parâmetro precisa de CAST: o Firebird não infere o tipo de "? IS NULL".
    private static string Filter(string column) =>
        "(CAST(@tbl AS VARCHAR(63)) IS NULL OR TRIM(" + column + ") = CAST(@tbl AS VARCHAR(63)))";

    public async Task<Result<SchemaModel>> ExtractAsync(DbConnection connection, CaptureOptions options,
        IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        var major = await MajorVersionAsync(connection, ct);
        var catalog = await ReadCatalogAsync(connection, null, major, ct);
        var relations = catalog.Relations.Where(r => TableNameIncluded(r.Name, options)).ToList();

        var tables = new List<TableModel>();
        var views = new List<ViewModel>();
        var done = 0;
        foreach (var rel in relations)
        {
            ct.ThrowIfCancellationRequested();
            if (rel.IsView)
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

        var sequences = (await ListSequencesAsync(connection, ct)).Select(s => s.Sequence).ToList();
        var routines = options.IncludeRoutines ? (await ListRoutinesAsync(connection, ct)).Select(r => r.Routine).ToList() : [];
        var triggers = options.IncludeTriggers ? catalog.Triggers.Select(BuildTrigger).ToList() : [];

        var ns = NamespaceModel.Create(Ns, isVirtual: true, tables, views, sequences, routines, triggers);
        if (!ns.IsSuccess)
        {
            return Result<SchemaModel>.Fail(ns.Error);
        }

        var version = await ScalarAsync(connection, "SELECT rdb$get_context('SYSTEM','ENGINE_VERSION') FROM rdb$database", ct) ?? string.Empty;
        return SchemaModel.Create(DatabaseEngine.Firebird, version, connection.Database ?? string.Empty, [ns.Value],
            DateTimeOffset.UtcNow, options);
    }

    public async Task<Result<TableModel>> ExtractTableAsync(DbConnection connection, ObjectName table, CancellationToken ct)
    {
        var major = await MajorVersionAsync(connection, ct);
        var catalog = await ReadCatalogAsync(connection, table.Name, major, ct);
        var rel = catalog.Relations.FirstOrDefault(r => r.Name == table.Name);
        if (rel is null)
        {
            return Result<TableModel>.Fail(ErrorCodes.DigSchema, $"Tabela '{table.Name}' não existe no banco.");
        }

        return BuildTable(rel, catalog, new CaptureOptions());
    }

    public async Task<IReadOnlyList<ObjectName>> ListTablesAsync(DbConnection connection, CancellationToken ct) =>
        (await ReadRelationsAsync(connection, null, ct)).Where(r => !r.IsView).Select(r => new ObjectName(Ns, r.Name)).ToList();

    public async Task<IReadOnlyList<ObjectName>> ListViewsAsync(DbConnection connection, CancellationToken ct) =>
        (await ReadRelationsAsync(connection, null, ct)).Where(r => r.IsView).Select(r => new ObjectName(Ns, r.Name)).ToList();

    public async Task<IReadOnlyList<TriggerModel>> ListTriggersAsync(DbConnection connection, CancellationToken ct) =>
        (await QueryAsync(connection, TriggersSql, null, ReadTrigger, ct)).Select(BuildTrigger).ToList();

    public async Task<IReadOnlyList<(string Namespace, RoutineModel Routine)>> ListRoutinesAsync(DbConnection connection, CancellationToken ct)
    {
        var routines = new List<(string, RoutineModel)>();
        var procedures = await QueryAsync(connection, ProceduresSql, null, r => (Name: Str(r, 0)!, Params: Int(r, 1)), ct);
        routines.AddRange(procedures.Select(p => (Ns, new RoutineModel(p.Name, RoutineKind.Procedure, p.Params))));

        if (await MajorVersionAsync(connection, ct) >= 3)
        {
            var functions = await QueryAsync(connection, FunctionsSql, null, r => (Name: Str(r, 0)!, Params: Int(r, 1)), ct);
            routines.AddRange(functions.Select(f => (Ns, new RoutineModel(f.Name, RoutineKind.Function, f.Params))));
        }

        return routines.OrderBy(r => r.Item2.Name, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<(string Namespace, SequenceModel Sequence)>> ListSequencesAsync(DbConnection connection, CancellationToken ct) =>
        (await QueryAsync(connection, GeneratorsSql, null, r => Str(r, 0)!, ct))
        .Select(name => (Ns, new SequenceModel(name)))
        .ToList();

    // Versão principal do servidor (2 = Firebird 2.5, 3, 4, 5). Decide quais colunas do catálogo podem ser lidas.
    public static async Task<int> MajorVersionAsync(DbConnection connection, CancellationToken ct)
    {
        var text = await ScalarAsync(connection, "SELECT rdb$get_context('SYSTEM','ENGINE_VERSION') FROM rdb$database", ct) ?? "3.0";
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        return int.TryParse(dot > 0 ? text[..dot] : text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ? major : 3;
    }

    private static Task<List<RelationRow>> ReadRelationsAsync(DbConnection connection, string? table, CancellationToken ct) =>
        QueryAsync(connection, RelationsSql, table, r => new RelationRow(Str(r, 0)!, Int(r, 1) == 1, Str(r, 2)), ct);

    private static async Task<Catalog> ReadCatalogAsync(DbConnection connection, string? table, int major, CancellationToken ct)
    {
        var relations = await ReadRelationsAsync(connection, table, ct);
        var columnsSql = ColumnsSql(major >= 3 ? "rf.RDB$IDENTITY_TYPE" : "CAST(NULL AS SMALLINT)", Filter("rf.RDB$RELATION_NAME"));
        var columns = await QueryAsync(connection, columnsSql, table,
            r => new ColumnRow(Str(r, 0)!, Int(r, 1), Str(r, 2)!, Int(r, 3), Int(r, 4), Int(r, 5), NullableInt(r, 6), Int(r, 7),
                Int(r, 8), Int(r, 9) != 0, Str(r, 10), Str(r, 11), Str(r, 12), Str(r, 13), NullableInt(r, 14)), ct);
        var keys = await QueryAsync(connection, KeysSql, table,
            r => new KeyRow(Str(r, 0)!, Str(r, 1)!, Str(r, 2)!, Str(r, 3)!, Int(r, 4), Str(r, 5), Str(r, 6), Str(r, 7), Str(r, 8)), ct);
        var indexes = await QueryAsync(connection, IndexesSql, table,
            r => new IndexRow(Str(r, 0)!, Str(r, 1)!, Int(r, 2) != 0, Int(r, 3) == 1, Str(r, 4), Int(r, 5), Str(r, 6)), ct);
        var checks = await QueryAsync(connection, ChecksSql, table, r => new CheckRow(Str(r, 0)!, Str(r, 1)!, Str(r, 2) ?? string.Empty), ct);
        var triggers = await QueryAsync(connection, TriggersSql, table, ReadTrigger, ct);
        return new Catalog(relations, columns, keys, indexes, checks, triggers);
    }

    private static TriggerRow ReadTrigger(DbDataReader r) => new(Str(r, 0)!, Str(r, 1)!, Int(r, 2), Int(r, 3) != 0, NullableInt(r, 4));

    private Result<TableModel> BuildTable(RelationRow rel, Catalog catalog, CaptureOptions options)
    {
        var columns = new List<ColumnModel>();
        foreach (var row in catalog.Columns.Where(c => c.Table == rel.Name))
        {
            var column = BuildColumn(row);
            if (!column.IsSuccess)
            {
                return Result<TableModel>.Fail(column.Error);
            }

            columns.Add(column.Value);
        }

        var groups = catalog.Keys.Where(k => k.Table == rel.Name).GroupBy(k => (k.Name, k.Type)).ToList();
        var pkGroup = groups.FirstOrDefault(g => g.Key.Type == "PRIMARY KEY");
        var pk = pkGroup is null ? null : new PrimaryKeyModel(pkGroup.Key.Name, pkGroup.OrderBy(k => k.Position).Select(k => k.Column).ToList());
        var uniques = groups.Where(g => g.Key.Type == "UNIQUE")
            .Select(g => new UniqueConstraintModel(g.Key.Name, g.OrderBy(k => k.Position).Select(k => k.Column).ToList(), UniqueSource.Constraint))
            .ToList();
        var fks = groups.Where(g => g.Key.Type == "FOREIGN KEY")
            .Select(g =>
            {
                var ordered = g.OrderBy(k => k.Position).ToList();
                return new ForeignKeyModel(g.Key.Name, ordered.Select(k => k.Column).ToList(),
                    new ObjectName(Ns, ordered[0].RefTable ?? string.Empty), ordered.Select(k => k.RefColumn ?? string.Empty).ToList(),
                    OnDelete: ActionOf(ordered[0].DeleteRule), OnUpdate: ActionOf(ordered[0].UpdateRule));
            })
            .ToList();

        var indexes = catalog.Indexes.Where(i => i.Table == rel.Name).GroupBy(i => i.Name)
            .Select(g =>
            {
                var first = g.First();
                var cols = first.Expression is not null
                    ? [new IndexColumn(null, first.Descending, first.Expression)]
                    : g.OrderBy(i => i.Position).Select(i => new IndexColumn(i.Column, first.Descending)).ToList();
                return new IndexModel(g.Key, cols, first.Unique);
            })
            .ToList();

        var checks = catalog.Checks.Where(c => c.Table == rel.Name)
            .Select(c => new CheckConstraintModel(c.Name, CheckExpression(c.Source), []))
            .ToList();

        return TableModel.Create(rel.Name, columns, primaryKey: pk, foreignKeys: fks, uniqueConstraints: uniques,
            indexes: options.IncludeIndexes ? indexes : [], checks: checks,
            comment: options.IncludeComments && !string.IsNullOrWhiteSpace(rel.Comment) ? rel.Comment : null);
    }

    private Result<ColumnModel> BuildColumn(ColumnRow row)
    {
        var typeName = FirebirdTypeMapper.TypeName(row.Type, row.SubType, row.Length, row.CharLength, row.Precision, row.Scale);
        // Domínio do usuário (RDB$FIELD_SOURCE sem prefixo RDB$) é preservado; os automáticos não.
        var domain = row.Domain is not null && !row.Domain.StartsWith("RDB$", StringComparison.Ordinal) ? row.Domain : null;
        var type = types.Map(new NativeTypeDescriptor(typeName, row.Length, row.Precision, row.Scale, row.SubType, row.Charset, domain));

        GeneratedInfo? generated = string.IsNullOrWhiteSpace(row.Computed) ? null : new GeneratedInfo(row.Computed.Trim(), Stored: false);
        IdentityInfo? identity = row.Identity switch
        {
            0 => new IdentityInfo(IdentityKind.IdentityAlways),
            1 => new IdentityInfo(IdentityKind.IdentityByDefault),
            _ => null,
        };

        DefaultValue? defaultValue = null;
        if (generated is null && !string.IsNullOrWhiteSpace(row.Default))
        {
            var expr = DefaultPrefixRegex().Replace(row.Default.Trim(), string.Empty);
            var isLiteral = LiteralRegex().IsMatch(expr);
            defaultValue = new DefaultValue(expr, isLiteral ? DefaultKind.Literal : DefaultKind.Function, isLiteral ? expr.Trim('\'') : null);
        }

        return ColumnModel.Create(row.Name, row.Ordinal + 1, type, !row.NotNull,
            defaultValue: defaultValue, identity: identity, generated: generated);
    }

    private ViewModel BuildView(RelationRow rel, Catalog catalog)
    {
        var columns = new List<ColumnModel>();
        foreach (var c in catalog.Columns.Where(c => c.Table == rel.Name))
        {
            var column = BuildColumn(c);
            if (column.IsSuccess)
            {
                columns.Add(column.Value);
            }
        }

        return new ViewModel(rel.Name, columns, IsUpdatable: null);
    }

    // RDB$TRIGGER_TYPE (DML): prefixo = (tipo + 1) & 1 (0 = BEFORE); cada par de bits de ((tipo + 1) >> 1) é uma ação
    // (1 INSERT, 2 UPDATE, 3 DELETE), até três ações por trigger.
    public static TriggerModel BuildTrigger(string table, string name, int type, bool inactive, int? position)
    {
        var timing = ((type + 1) & 1) == 0 ? TriggerTiming.Before : TriggerTiming.After;
        var slots = (type + 1) >> 1;
        var events = TriggerEvents.None;
        for (var i = 0; i < 3; i++)
        {
            events |= ((slots >> (2 * i)) & 3) switch
            {
                1 => TriggerEvents.Insert,
                2 => TriggerEvents.Update,
                3 => TriggerEvents.Delete,
                _ => TriggerEvents.None,
            };
        }

        return new TriggerModel(name, new ObjectName(Ns, table), timing, events, IsActive: !inactive, Position: position);
    }

    private static TriggerModel BuildTrigger(TriggerRow t) => BuildTrigger(t.Table, t.Name, t.Type, t.Inactive, t.Position);

    private static ReferentialAction ActionOf(string? rule) => (rule ?? string.Empty).ToUpperInvariant() switch
    {
        "CASCADE" => ReferentialAction.Cascade,
        "SET NULL" => ReferentialAction.SetNull,
        "SET DEFAULT" => ReferentialAction.SetDefault,
        "RESTRICT" => ReferentialAction.Restrict,
        _ => ReferentialAction.NoAction,
    };

    private static string CheckExpression(string source)
    {
        var match = CheckBodyRegex().Match(source.Trim());
        return match.Success ? match.Groups["e"].Value.Trim() : source.Trim();
    }

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

    private static async Task<List<T>> QueryAsync<T>(DbConnection connection, string sql, string? table, Func<DbDataReader, T> map, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (sql.Contains("@tbl", StringComparison.Ordinal))
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tbl";
            parameter.DbType = DbType.String;
            parameter.Value = (object?)table ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture)?.Trim();
    }

    private static string? Str(DbDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.TrimEnd();

    private static int Int(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    private static int? NullableInt(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^DEFAULT\s+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DefaultPrefixRegex();

    [GeneratedRegex(@"^(-?\d+(\.\d+)?|'.*'|NULL|TRUE|FALSE)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LiteralRegex();

    [GeneratedRegex(@"^CHECK\s*\((?<e>.*)\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CheckBodyRegex();
}
