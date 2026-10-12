using System.Text.Json;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Mapping;

namespace MockDataStudio.Mapping.Tests.TestSupport;

// Conhecimento de mapeamento construído a partir do pack CORE_PTBR embutido no repositório (sem depender do Catalog, AR-02).
internal static class MappingFixtures
{
    private static readonly Lazy<string> PackJson = new(() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "MockDataStudio.Catalog", "Seeds", "pack-core-ptbr.json")));

    public static MappingKnowledge PackKnowledge(IEnumerable<KnowledgeRule>? rules = null)
    {
        var normalizer = new NameNormalizer();
        var templates = new List<KnowledgeTemplate>();
        var aliases = new List<KnowledgeAlias>();

        using var document = JsonDocument.Parse(PackJson.Value);
        foreach (var template in document.RootElement.GetProperty("templates").EnumerateArray())
        {
            var code = template.GetProperty("code").GetString()!;
            var composite = template.GetProperty("kind").GetString() == "composite";
            var fields = new List<KnowledgeField>();
            if (template.TryGetProperty("fields", out var fieldArray))
            {
                foreach (var field in fieldArray.EnumerateArray())
                {
                    fields.Add(new KnowledgeField(field.GetProperty("code").GetString()!,
                        ParseLogical(field.GetProperty("logicalType").GetString()!),
                        field.TryGetProperty("maxLength", out var max) ? max.GetInt32() : null,
                        null));
                }
            }

            var values = template.TryGetProperty("values", out var valueArray)
                ? valueArray.EnumerateArray().Select(v => v.GetProperty("value").GetString()!).ToList()
                : new List<string>();
            var lengths = values.Select(v => v.Length).OrderBy(l => l).ToList();

            templates.Add(new KnowledgeTemplate(
                Code: code,
                IsComposite: composite,
                LogicalType: composite || !template.TryGetProperty("logicalType", out var logical) ? null : ParseLogical(logical.GetString()!),
                Fields: fields,
                ValueCount: values.Count,
                MaxValueLength: lengths.Count > 0 ? lengths[^1] : null,
                P95ValueLength: lengths.Count > 0 ? lengths[(int)Math.Ceiling(lengths.Count * 0.95) - 1] : null));

            if (template.TryGetProperty("aliases", out var aliasArray))
            {
                foreach (var alias in aliasArray.EnumerateArray())
                {
                    if (alias.ValueKind == JsonValueKind.String)
                    {
                        aliases.Add(new KnowledgeAlias(code, null, alias.GetString()!, 1.0, null));
                    }
                    else
                    {
                        aliases.Add(new KnowledgeAlias(code, alias.GetProperty("field").GetString(), alias.GetProperty("alias").GetString()!, 1.0, null));
                    }
                }
            }
        }

        return new MappingKnowledge(templates, aliases, rules ?? [], normalizer);
    }

    public static DataTypeInfo Type(string nativeType) => NativeTypeParser.Parse(nativeType);

    public static ColumnModel Column(string name, string nativeType = "varchar(120)", bool nullable = true, int ordinal = 1,
        DefaultValue? defaultValue = null, IdentityInfo? identity = null, GeneratedInfo? generated = null) =>
        ColumnModel.Create(name, ordinal, Type(nativeType), nullable, defaultValue, identity, generated).Value;

    public static SchemaModel Schema(params (string Table, IReadOnlyList<ColumnModel> Columns, PrimaryKeyModel? Pk, IReadOnlyList<ForeignKeyModel>? Fks)[] tables)
    {
        var models = tables.Select(t => TableModel.Create(t.Table, t.Columns, t.Pk, t.Fks).Value).ToList();
        var ns = NamespaceModel.Create("main", isVirtual: false, models).Value;
        return SchemaModel.Create(DatabaseEngine.Sqlite, "3.46.1", "teste.db", [ns], DateTimeOffset.UnixEpoch).Value;
    }

    public static MappingContext Context(MappingKnowledge knowledge, SchemaModel schema,
        IReadOnlyDictionary<string, ColumnBinding>? bindings = null) =>
        new(schema, knowledge, bindings ?? new Dictionary<string, ColumnBinding>(StringComparer.Ordinal), new MappingOptions());

    public static ObjectName Table(string name) => new("main", name);

    public static LogicalType ParseLogical(string text) => text switch
    {
        "boolean" => LogicalType.Boolean,
        "integer" => LogicalType.Integer,
        "decimal" => LogicalType.Decimal,
        "float" => LogicalType.Float,
        "string" => LogicalType.String,
        "text" => LogicalType.Text,
        "date" => LogicalType.Date,
        "datetime" => LogicalType.DateTime,
        "uuid" => LogicalType.Uuid,
        "json" => LogicalType.Json,
        _ => LogicalType.Other,
    };

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "MockDataStudio.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Raiz da solution não encontrada.");
    }
}
