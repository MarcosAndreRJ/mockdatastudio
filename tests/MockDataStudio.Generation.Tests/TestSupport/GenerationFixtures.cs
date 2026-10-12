using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Generators;

namespace MockDataStudio.Generation.Tests.TestSupport;

// Suporte de teste: templates reais do pack CORE_PTBR, sink em memória e construtores de plano.
internal static class GenerationFixtures
{
    public static readonly DateTimeOffset Reference = new(2026, 10, 12, 12, 0, 0, TimeSpan.FromHours(-3));

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static readonly Lazy<JsonElement> Pack = new(() =>
    {
        var path = Path.Combine(RepoRoot(), "src", "MockDataStudio.Catalog", "Seeds", "pack-core-ptbr.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    });

    public static IReadOnlyDictionary<string, TemplateDataSet> PackTemplates()
    {
        var result = new Dictionary<string, TemplateDataSet>(StringComparer.Ordinal);
        foreach (var template in Pack.Value.GetProperty("templates").EnumerateArray())
        {
            var code = template.GetProperty("code").GetString()!;
            if (template.GetProperty("kind").GetString() == "composite")
            {
                var fields = template.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("code").GetString()!).ToList();
                var items = template.GetProperty("records").EnumerateArray().Select(r =>
                {
                    var data = r.GetProperty("data");
                    return (Key: r.GetProperty("key").GetString()!,
                        Fields: (string[]?)fields.Select(f => data.GetProperty(f).GetString()!).ToArray(),
                        Scalar: (string?)null,
                        Weight: 1.0);
                }).ToList();
                result[code] = TemplateDataSet.Create(code, TemplateKind.Composite, fields, items).Value;
            }
            else
            {
                var items = template.GetProperty("values").EnumerateArray().Select(v =>
                    (Key: v.GetProperty("value").GetString()!,
                     Fields: (string[]?)null,
                     Scalar: v.GetProperty("value").GetString(),
                     Weight: v.TryGetProperty("weight", out var w) ? w.GetDouble() : 1.0)).ToList();
                result[code] = TemplateDataSet.Create(code, TemplateKind.Scalar, [], items).Value;
            }
        }

        return result;
    }

    public static ColumnModel Col(string name, string nativeType = "varchar(60)", bool nullable = true, int ordinal = 1,
        IdentityInfo? identity = null) =>
        ColumnModel.Create(name, ordinal, NativeTypeParser.Parse(nativeType), nullable, identity: identity).Value;

    public static ColumnPlan Plan(ColumnModel column, string generatorId, JsonObject? parameters = null, double nullRate = 0, bool unique = false, string? uniqueStrategy = null) =>
        new(column, new GeneratorSpec(generatorId, 1, parameters ?? new JsonObject()), nullRate, unique, uniqueStrategy);

    public static TablePlan Table(string name, IReadOnlyList<ColumnPlan> columns, long rows, ulong seed = 20261012UL,
        int batch = 1000, int parallelism = 1, IReadOnlyList<IReadOnlyList<string>>? compositeUniques = null) =>
        new(new ObjectName("main", name), columns, rows, seed, Reference, Zone, PackTemplates(), batch, parallelism, compositeUniques);

    public static GeneratorRegistry Registry() => GeneratorRegistry.Standard();

    // Parâmetros de gerador: valores primitivos viram JsonValue; JsonNode é mantido como está.
    public static JsonObject Obj(params (string Key, object? Value)[] pairs)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in pairs)
        {
            obj[key] = value switch
            {
                null => null,
                JsonNode node => node,
                _ => JsonValue.Create(value),
            };
        }

        return obj;
    }

    public static string RepoRoot()
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

    public static string Text(object? value) => value switch
    {
        null => "<null>",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}

// Sink em memória: guarda as linhas na ordem recebida e verifica a ordem dos lotes.
internal sealed class MemorySink : IRowSink
{
    public List<object?[]> Rows { get; } = [];

    public List<long> BatchStarts { get; } = [];

    public bool BeganTable { get; private set; }

    public bool EndedTable { get; private set; }

    public Task BeginTableAsync(TablePlan table, CancellationToken ct)
    {
        BeganTable = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<object?[]>?> WriteBatchAsync(RowBatch batch, CancellationToken ct)
    {
        BatchStarts.Add(batch.FirstRowIndex);
        Rows.AddRange(batch.Rows);
        return Task.FromResult<IReadOnlyList<object?[]>?>((IReadOnlyList<object?[]>?)null);
    }

    public Task EndTableAsync(TableRunResult result, CancellationToken ct)
    {
        EndedTable = true;
        return Task.CompletedTask;
    }
}
