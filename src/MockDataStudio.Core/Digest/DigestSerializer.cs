using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;

namespace MockDataStudio.Core.Digest;

// Serialização e desserialização do digest mds.digest 1.0 (12 §3–4).
// A saída segue a ordem canônica de 12 §4: namespaces, tabelas, views, sequences, rotinas e triggers por nome;
// colunas por posição ordinal; defaults omitidos; listas vazias omitidas.
public sealed class DigestSerializer(IStructureHasher hasher, IDigestValidator? validator = null) : IDigestSerializer
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IDigestValidator _validator = validator ?? new DigestValidator(hasher);

    public string FormatVersion => "1.0";

    public string Serialize(SchemaModel model, DigestWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var root = new JsonObject
        {
            ["format"] = "mds.digest",
            ["version"] = FormatVersion,
            ["engine"] = EngineName(model.Engine),
            ["server"] = model.ServerVersion,
            ["database"] = model.DatabaseName,
            ["capturedAt"] = model.CapturedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["hash"] = hasher.Compute(model),
            ["options"] = OptionsNode(model.Options, options.IncludeComments),
            ["namespaces"] = new JsonArray(model.Namespaces
                .OrderBy(n => n.Name, StringComparer.Ordinal)
                .Select(ns => (JsonNode)NamespaceNode(ns, options.IncludeComments))
                .ToArray()),
        };

        return root.ToJsonString(options.Indented ? IndentedOptions : CompactOptions);
    }

    public Result<SchemaModel> Deserialize(string json)
    {
        var errors = _validator.Validate(json).Where(e => e.Severity == DigestSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            var first = errors[0];
            return Result<SchemaModel>.Fail(first.Code, $"{first.Message} ({first.JsonPath})");
        }

        return ReadModel(json);
    }

    // Leitura sem validação (uso interno: validador e Deserialize). Assume JSON já validado.
    internal static Result<SchemaModel> ReadModel(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();

        var namespaces = new List<NamespaceModel>();
        foreach (var nsNode in root["namespaces"]!.AsArray())
        {
            var result = ReadNamespace(nsNode!.AsObject());
            if (!result.IsSuccess)
            {
                return Result<SchemaModel>.Fail(result.Error);
            }

            namespaces.Add(result.Value);
        }

        var engine = ParseEngine(root["engine"]!.GetValue<string>());
        var capturedAt = DateTimeOffset.Parse(root["capturedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);

        return SchemaModel.Create(
            engine,
            root["server"]!.GetValue<string>(),
            root["database"]!.GetValue<string>(),
            namespaces,
            capturedAt,
            ReadOptions(root["options"]?.AsObject()));
    }

    // ---------- Escrita ----------

    private static JsonObject OptionsNode(CaptureOptions options, bool includeComments)
    {
        var node = new JsonObject
        {
            ["views"] = options.IncludeViews,
            ["routines"] = options.IncludeRoutines,
            ["triggers"] = options.IncludeTriggers,
            ["comments"] = includeComments || options.IncludeComments,
        };

        if (!options.IncludeIndexes)
        {
            node["indexes"] = false;
        }

        var include = options.TableIncludePatterns ?? [];
        if (include.Count > 0)
        {
            node["include"] = StringArray(include);
        }

        var exclude = options.TableExcludePatterns ?? [];
        if (exclude.Count > 0)
        {
            node["exclude"] = StringArray(exclude);
        }

        return node;
    }

    private static JsonObject NamespaceNode(NamespaceModel ns, bool includeComments)
    {
        var node = new JsonObject { ["name"] = ns.Name };
        if (ns.IsVirtual)
        {
            node["virtual"] = true;
        }

        AddList(node, "tables", ns.Tables.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => TableNode(t, includeComments)));
        AddList(node, "views", ns.Views.OrderBy(v => v.Name, StringComparer.Ordinal).Select(ViewNode));
        AddList(node, "sequences", ns.Sequences.OrderBy(s => s.Name, StringComparer.Ordinal).Select(SequenceNode));
        AddList(node, "routines", ns.Routines.OrderBy(r => r.Name, StringComparer.Ordinal).Select(RoutineNode));
        AddList(node, "triggers", ns.Triggers.OrderBy(t => t.Name, StringComparer.Ordinal).Select(TriggerNode));
        return node;
    }

    private static JsonObject TableNode(TableModel table, bool includeComments)
    {
        var node = new JsonObject { ["name"] = table.Name };
        AddList(node, "cols", table.Columns.OrderBy(c => c.Ordinal).Select(c => ColumnTuple(c, includeComments)));

        if (table.PrimaryKey is not null)
        {
            node["pk"] = StringArray(table.PrimaryKey.Columns);
            if (table.PrimaryKey.Name is not null)
            {
                node["pkn"] = table.PrimaryKey.Name;
            }
        }

        AddList(node, "fk", table.ForeignKeys.Select(ForeignKeyNode));

        var uniques = table.UniqueConstraints.Select(UniqueNode)
            .Concat(table.Indexes.Where(i => i.IsUnique).Select(UniqueFromIndex));
        AddList(node, "uq", uniques);

        AddList(node, "ix", table.Indexes.Where(i => !i.IsUnique).Select(IndexNode));
        AddList(node, "ck", table.Checks.Select(CheckNode));

        if (includeComments && table.Comment is not null)
        {
            node["cmt"] = table.Comment;
        }

        if (table.Extras is { Count: > 0 })
        {
            node["x"] = ExtrasNode(table.Extras);
        }

        return node;
    }

    private static JsonArray ColumnTuple(ColumnModel column, bool includeComments)
    {
        var flags = new List<string>();
        if (!column.IsNullable)
        {
            flags.Add("nn");
        }

        if (column.Identity is not null)
        {
            flags.Add(IdentityToken(column.Identity.Kind));
        }

        if (column.Generated is not null)
        {
            flags.Add(column.Generated.Stored ? "gs" : "gv");
        }

        var extras = ColumnExtras(column, includeComments);
        var tuple = new JsonArray(column.Name, NativeTypeParser.Format(column.Type));

        if (flags.Count > 0 || extras is not null)
        {
            tuple.Add(string.Join(' ', flags));
        }

        if (extras is not null)
        {
            tuple.Add(extras);
        }

        return tuple;
    }

    private static JsonObject? ColumnExtras(ColumnModel column, bool includeComments)
    {
        var node = new JsonObject();

        if (column.Default is not null)
        {
            node["def"] = column.Default.RawExpression;
        }

        if (column.Identity?.SequenceName is { } sequence)
        {
            node["seq"] = sequence;
        }

        if (column.Generated is not null && column.Generated.Expression.Length > 0)
        {
            node["gx"] = column.Generated.Expression;
        }

        if (column.Type.EnumValues is { Count: > 0 } values)
        {
            node["enum"] = StringArray(values);
        }

        if (column.Type.Charset is not null)
        {
            node["cs"] = column.Type.Charset;
        }

        if (column.Collation is not null)
        {
            node["coll"] = column.Collation;
        }

        if (column.Type.Domain is not null)
        {
            node["dom"] = column.Type.Domain;
        }

        if (column.Type.LengthUnit == LengthUnit.Bytes)
        {
            node["lu"] = "b";
        }

        if (includeComments && column.Comment is not null)
        {
            node["cmt"] = column.Comment;
        }

        if (column.Extras is { Count: > 0 })
        {
            node["x"] = ExtrasNode(column.Extras);
        }

        return node.Count > 0 ? node : null;
    }

    private static JsonObject ForeignKeyNode(ForeignKeyModel fk)
    {
        var node = new JsonObject
        {
            ["n"] = fk.Name,
            ["c"] = StringArray(fk.Columns),
            ["r"] = fk.Referenced.CanonicalId,
            ["rc"] = StringArray(fk.ReferencedColumns),
        };

        if (fk.OnDelete != ReferentialAction.NoAction)
        {
            node["od"] = ActionToken(fk.OnDelete);
        }

        if (fk.OnUpdate != ReferentialAction.NoAction)
        {
            node["ou"] = ActionToken(fk.OnUpdate);
        }

        if (fk.IsDeferrable)
        {
            node["df"] = fk.InitiallyDeferred ? "d" : "i";
        }

        if (!fk.IsEnforced)
        {
            node["ne"] = true;
        }

        return node;
    }

    private static JsonObject UniqueNode(UniqueConstraintModel uq)
    {
        var node = new JsonObject { ["c"] = StringArray(uq.Columns) };
        if (uq.Name is not null)
        {
            node["n"] = uq.Name;
        }

        if (uq.Source == UniqueSource.UniqueIndex)
        {
            node["ix"] = true;
        }

        if (!uq.NullsDistinct)
        {
            node["nnd"] = true;
        }

        return node;
    }

    // Índice único do modelo é representado como UNIQUE com ix:true (12 §3.3).
    private static JsonObject UniqueFromIndex(IndexModel index) => UniqueNode(new UniqueConstraintModel(
        index.Name, index.Columns.Select(c => c.Column ?? c.Expression ?? string.Empty).ToList(), UniqueSource.UniqueIndex));

    private static JsonObject IndexNode(IndexModel index)
    {
        var node = new JsonObject { ["n"] = index.Name };
        var hasExpressions = index.Columns.Any(c => c.Expression is not null);

        if (hasExpressions)
        {
            node["e"] = new JsonArray(index.Columns.Select(IndexEntry).Select(e => (JsonNode)e).ToArray());
        }
        else
        {
            node["c"] = new JsonArray(index.Columns.Select(IndexEntry).Select(e => (JsonNode)e).ToArray());
        }

        if (index.Predicate is not null)
        {
            node["w"] = index.Predicate;
        }

        return node;
    }

    private static string IndexEntry(IndexColumn column)
    {
        var basis = column.Column ?? column.Expression ?? string.Empty;
        return column.Descending ? basis + " desc" : basis;
    }

    private static JsonObject CheckNode(CheckConstraintModel check)
    {
        var node = new JsonObject { ["e"] = check.Expression };
        if (check.Name is not null)
        {
            node["n"] = check.Name;
        }

        return node;
    }

    private static JsonObject ViewNode(ViewModel view)
    {
        var node = new JsonObject { ["name"] = view.Name };
        AddList(node, "cols", view.Columns.OrderBy(c => c.Ordinal).Select(c => ColumnTuple(c, includeComments: false)));
        if (view.IsUpdatable is { } updatable)
        {
            node["upd"] = updatable;
        }

        return node;
    }

    private static JsonObject SequenceNode(SequenceModel seq)
    {
        var node = new JsonObject { ["n"] = seq.Name };
        if (seq.Start is { } start)
        {
            node["s"] = start;
        }

        if (seq.Increment is { } increment)
        {
            node["i"] = increment;
        }

        if (seq.MinValue is { } min)
        {
            node["min"] = min;
        }

        if (seq.MaxValue is { } max)
        {
            node["max"] = max;
        }

        if (seq.IsCycle)
        {
            node["cy"] = true;
        }

        return node;
    }

    private static JsonObject RoutineNode(RoutineModel routine)
    {
        var node = new JsonObject
        {
            ["n"] = routine.Name,
            ["k"] = routine.Kind == RoutineKind.Procedure ? "p" : "f",
            ["p"] = routine.ParameterCount,
        };

        if (routine.ReturnType is not null)
        {
            node["rt"] = routine.ReturnType;
        }

        return node;
    }

    private static JsonObject TriggerNode(TriggerModel trigger)
    {
        var node = new JsonObject { ["n"] = trigger.Name };
        if (trigger.Table is not null)
        {
            node["t"] = trigger.Table.Name;
        }

        node["tm"] = trigger.Timing switch
        {
            TriggerTiming.Before => "b",
            TriggerTiming.After => "a",
            _ => "io",
        };

        node["ev"] = EventsToken(trigger.Events);

        if (!trigger.IsActive)
        {
            node["a"] = false;
        }

        if (trigger.Position is { } position)
        {
            node["pos"] = position;
        }

        return node;
    }

    // ---------- Leitura ----------

    private static Result<NamespaceModel> ReadNamespace(JsonObject node)
    {
        var name = node["name"]!.GetValue<string>();
        var isVirtual = node["virtual"]?.GetValue<bool>() ?? false;

        var tables = new List<TableModel>();
        foreach (var t in ListOf(node, "tables"))
        {
            var table = ReadTable(t!.AsObject());
            if (!table.IsSuccess)
            {
                return Result<NamespaceModel>.Fail(table.Error);
            }

            tables.Add(table.Value);
        }

        var views = ListOf(node, "views").Select(v => ReadView(v!.AsObject())).ToList();

        return NamespaceModel.Create(
            name,
            isVirtual,
            tables,
            views,
            ListOf(node, "sequences").Select(s => ReadSequence(s!.AsObject())).ToList(),
            ListOf(node, "routines").Select(r => ReadRoutine(r!.AsObject())).ToList(),
            ListOf(node, "triggers").Select(t => ReadTrigger(name, t!.AsObject())).ToList());
    }

    private static Result<TableModel> ReadTable(JsonObject node)
    {
        var name = node["name"]!.GetValue<string>();

        var columns = new List<ColumnModel>();
        var ordinal = 1;
        foreach (var colNode in node["cols"]!.AsArray())
        {
            var column = ReadColumn(colNode!.AsArray(), ordinal++);
            if (!column.IsSuccess)
            {
                return Result<TableModel>.Fail(column.Error);
            }

            columns.Add(column.Value);
        }

        var pk = node["pk"] is { } pkNode
            ? new PrimaryKeyModel(node["pkn"]?.GetValue<string>(), pkNode.AsArray().Select(c => c!.GetValue<string>()).ToList())
            : null;

        var foreignKeys = ListOf(node, "fk").Select(f => ReadForeignKey(f!.AsObject())).ToList();

        var uniques = new List<UniqueConstraintModel>();
        foreach (var uqNode in ListOf(node, "uq"))
        {
            uniques.Add(ReadUnique(uqNode!.AsObject()));
        }

        var indexes = ListOf(node, "ix").Select(ix => ReadIndex(ix!.AsObject(), isUnique: false)).ToList();
        var checks = ListOf(node, "ck").Select(ck => new CheckConstraintModel(
            ck!.AsObject()["n"]?.GetValue<string>(),
            ck.AsObject()["e"]!.GetValue<string>(),
            ColumnScope(ck.AsObject()["e"]!.GetValue<string>(), columns))).ToList();

        return TableModel.Create(
            name,
            columns,
            pk,
            foreignKeys,
            uniques,
            indexes,
            checks,
            node["cmt"]?.GetValue<string>(),
            ReadExtras(node["x"]));
    }

    private static Result<ColumnModel> ReadColumn(JsonArray tuple, int ordinal)
    {
        var name = tuple[0]!.GetValue<string>();
        var typeText = tuple[1]!.GetValue<string>();
        var flags = tuple.Count > 2 ? tuple[2]!.GetValue<string>() : string.Empty;
        var extras = tuple.Count > 3 ? tuple[3]!.AsObject() : null;

        var enumValues = extras?["enum"] is { } enumNode
            ? enumNode.AsArray().Select(v => v!.GetValue<string>()).ToList()
            : null;

        var bytes = extras?["lu"]?.GetValue<string>() == "b";
        var type = NativeTypeParser.Parse(
            typeText,
            extras?["cs"]?.GetValue<string>(),
            enumValues,
            extras?["dom"]?.GetValue<string>(),
            bytes);

        var tokens = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nullable = !tokens.Contains("nn");

        IdentityInfo? identity = null;
        var identityToken = tokens.FirstOrDefault(t => t is "ai" or "idd" or "ida" or "ser" or "tsq");
        if (identityToken is not null)
        {
            identity = new IdentityInfo(IdentityFromToken(identityToken), extras?["seq"]?.GetValue<string>());
        }

        GeneratedInfo? generated = null;
        if (tokens.Contains("gs") || tokens.Contains("gv"))
        {
            generated = new GeneratedInfo(extras?["gx"]?.GetValue<string>() ?? string.Empty, tokens.Contains("gs"));
        }

        var defaultRaw = extras?["def"]?.GetValue<string>();

        return ColumnModel.Create(
            name,
            ordinal,
            type,
            nullable,
            defaultRaw is null ? null : ReadDefault(defaultRaw),
            identity,
            generated,
            extras?["coll"]?.GetValue<string>(),
            extras?["cmt"]?.GetValue<string>(),
            ReadExtras(extras?["x"]));
    }

    private static ForeignKeyModel ReadForeignKey(JsonObject node)
    {
        var df = node["df"]?.GetValue<string>();
        return new ForeignKeyModel(
            node["n"]!.GetValue<string>(),
            node["c"]!.AsArray().Select(c => c!.GetValue<string>()).ToList(),
            ObjectName.ParseCanonical(node["r"]!.GetValue<string>()),
            node["rc"]!.AsArray().Select(c => c!.GetValue<string>()).ToList(),
            ActionFromToken(node["od"]?.GetValue<string>()),
            ActionFromToken(node["ou"]?.GetValue<string>()),
            IsDeferrable: df is not null,
            InitiallyDeferred: df == "d",
            IsEnforced: node["ne"] is null);
    }

    private static UniqueConstraintModel ReadUnique(JsonObject node) => new(
        node["n"]?.GetValue<string>(),
        node["c"]!.AsArray().Select(c => c!.GetValue<string>()).ToList(),
        node["ix"]?.GetValue<bool>() == true ? UniqueSource.UniqueIndex : UniqueSource.Constraint,
        NullsDistinct: node["nnd"]?.GetValue<bool>() != true);

    private static IndexModel ReadIndex(JsonObject node, bool isUnique)
    {
        var columns = new List<IndexColumn>();
        if (node["c"] is { } cols)
        {
            columns.AddRange(cols.AsArray().Select(c => ParseIndexEntry(c!.GetValue<string>(), isExpression: false)));
        }

        if (node["e"] is { } exprs)
        {
            columns.AddRange(exprs.AsArray().Select(e => ParseIndexEntry(e!.GetValue<string>(), isExpression: true)));
        }

        return new IndexModel(node["n"]!.GetValue<string>(), columns, isUnique, node["w"]?.GetValue<string>());
    }

    private static IndexColumn ParseIndexEntry(string entry, bool isExpression)
    {
        var descending = entry.EndsWith(" desc", StringComparison.Ordinal);
        var basis = descending ? entry[..^" desc".Length] : entry;
        return isExpression ? new IndexColumn(null, descending, basis) : new IndexColumn(basis, descending);
    }

    private static ViewModel ReadView(JsonObject node)
    {
        var columns = new List<ColumnModel>();
        var ordinal = 1;
        foreach (var colNode in node["cols"]!.AsArray())
        {
            var column = ReadColumn(colNode!.AsArray(), ordinal++);
            if (column.IsSuccess)
            {
                columns.Add(column.Value);
            }
        }

        return new ViewModel(
            node["name"]!.GetValue<string>(),
            columns,
            node["upd"] is { } upd ? upd.GetValue<bool>() : null);
    }

    private static SequenceModel ReadSequence(JsonObject node) => new(
        node["n"]!.GetValue<string>(),
        LongValue(node["s"]),
        LongValue(node["i"]),
        LongValue(node["min"]),
        LongValue(node["max"]),
        node["cy"]?.GetValue<bool>() == true);

    private static RoutineModel ReadRoutine(JsonObject node) => new(
        node["n"]!.GetValue<string>(),
        node["k"]!.GetValue<string>() == "p" ? RoutineKind.Procedure : RoutineKind.Function,
        node["p"]!.GetValue<int>(),
        node["rt"]?.GetValue<string>());

    private static TriggerModel ReadTrigger(string namespaceName, JsonObject node)
    {
        var tableName = node["t"]?.GetValue<string>();
        return new TriggerModel(
            node["n"]!.GetValue<string>(),
            tableName is null ? null : new ObjectName(namespaceName, tableName),
            node["tm"]!.GetValue<string>() switch
            {
                "b" => TriggerTiming.Before,
                "a" => TriggerTiming.After,
                _ => TriggerTiming.InsteadOf,
            },
            ParseEvents(node["ev"]!.GetValue<string>()),
            IsActive: node["a"]?.GetValue<bool>() != false,
            Position: node["pos"]?.GetValue<int>());
    }

    private static CaptureOptions ReadOptions(JsonObject? node)
    {
        if (node is null)
        {
            return new CaptureOptions();
        }

        return new CaptureOptions(
            IncludeViews: node["views"]?.GetValue<bool>() ?? true,
            IncludeRoutines: node["routines"]?.GetValue<bool>() ?? true,
            IncludeTriggers: node["triggers"]?.GetValue<bool>() ?? true,
            IncludeComments: node["comments"]?.GetValue<bool>() ?? false,
            IncludeIndexes: node["indexes"]?.GetValue<bool>() ?? true,
            NamespaceFilter: null,
            TableIncludePatterns: node["include"] is { } inc ? inc.AsArray().Select(i => i!.GetValue<string>()).ToList() : null,
            TableExcludePatterns: node["exclude"] is { } exc ? exc.AsArray().Select(e => e!.GetValue<string>()).ToList() : null);
    }

    // Colunas referenciadas por um CHECK: identificadores do escopo da tabela que aparecem na expressão.
    private static List<string> ColumnScope(string expression, IReadOnlyList<ColumnModel> columns)
    {
        var scope = new List<string>();
        foreach (var column in columns)
        {
            if (ContainsWord(expression, column.Name))
            {
                scope.Add(column.Name);
            }
        }

        return scope;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !IsIdentifierChar(text[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= text.Length || !IsIdentifierChar(text[afterIndex]);
            if (before && after)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static DefaultValue ReadDefault(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length >= 2)
        {
            return new DefaultValue(raw, DefaultKind.Literal, trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal));
        }

        if (decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
        {
            return new DefaultValue(raw, DefaultKind.Literal, trimmed);
        }

        if (trimmed.Contains("nextval", StringComparison.OrdinalIgnoreCase))
        {
            return new DefaultValue(raw, DefaultKind.Sequence);
        }

        if (trimmed.Contains('(', StringComparison.Ordinal) || trimmed.StartsWith("CURRENT_", StringComparison.OrdinalIgnoreCase))
        {
            return new DefaultValue(raw, DefaultKind.Function);
        }

        return new DefaultValue(raw, DefaultKind.Other);
    }

    private static Dictionary<string, string>? ReadExtras(JsonNode? node)
    {
        if (node is not JsonObject obj || obj.Count == 0)
        {
            return null;
        }

        return obj.ToDictionary(p => p.Key, p => p.Value!.GetValue<string>(), StringComparer.Ordinal);
    }

    // ---------- Auxiliares ----------

    private static JsonNode?[] ListOf(JsonObject node, string key) =>
        node[key] is JsonArray array ? array.ToArray() : [];

    private static void AddList(JsonObject node, string key, IEnumerable<JsonNode> items)
    {
        var array = items.ToArray();
        if (array.Length > 0)
        {
            node[key] = new JsonArray(array.Select(item => (JsonNode)item).ToArray());
        }
    }

    private static JsonArray StringArray(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

    private static JsonObject ExtrasNode(IReadOnlyDictionary<string, string> extras)
    {
        var node = new JsonObject();
        foreach (var (key, value) in extras.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            node[key] = value;
        }

        return node;
    }

    private static long? LongValue(JsonNode? node) => node is null ? null : node.GetValue<long>();

    // Nome do motor no digest = nome do enum em minúsculas ("PostgreSql" -> "postgresql"). Sem ramificação por motor (T-ARC-007).
    private static string EngineName(DatabaseEngine engine) => engine.ToString().ToLowerInvariant();

    private static DatabaseEngine ParseEngine(string name) => Enum.Parse<DatabaseEngine>(name, ignoreCase: true);

    private static string IdentityToken(IdentityKind kind) => kind switch
    {
        IdentityKind.AutoIncrement => "ai",
        IdentityKind.IdentityByDefault => "idd",
        IdentityKind.IdentityAlways => "ida",
        IdentityKind.Serial => "ser",
        _ => "tsq",
    };

    private static IdentityKind IdentityFromToken(string token) => token switch
    {
        "ai" => IdentityKind.AutoIncrement,
        "idd" => IdentityKind.IdentityByDefault,
        "ida" => IdentityKind.IdentityAlways,
        "ser" => IdentityKind.Serial,
        _ => IdentityKind.TriggerSequence,
    };

    private static string ActionToken(ReferentialAction action) => action switch
    {
        ReferentialAction.Restrict => "restrict",
        ReferentialAction.Cascade => "cascade",
        ReferentialAction.SetNull => "setnull",
        _ => "setdefault",
    };

    private static ReferentialAction ActionFromToken(string? token) => token switch
    {
        "restrict" => ReferentialAction.Restrict,
        "cascade" => ReferentialAction.Cascade,
        "setnull" => ReferentialAction.SetNull,
        "setdefault" => ReferentialAction.SetDefault,
        _ => ReferentialAction.NoAction,
    };

    private static string EventsToken(TriggerEvents events)
    {
        var sb = new System.Text.StringBuilder();
        if (events.HasFlag(TriggerEvents.Insert))
        {
            sb.Append('i');
        }

        if (events.HasFlag(TriggerEvents.Update))
        {
            sb.Append('u');
        }

        if (events.HasFlag(TriggerEvents.Delete))
        {
            sb.Append('d');
        }

        return sb.ToString();
    }

    private static TriggerEvents ParseEvents(string token)
    {
        var events = TriggerEvents.None;
        foreach (var c in token)
        {
            events |= c switch
            {
                'i' => TriggerEvents.Insert,
                'u' => TriggerEvents.Update,
                'd' => TriggerEvents.Delete,
                _ => TriggerEvents.None,
            };
        }

        return events;
    }
}
