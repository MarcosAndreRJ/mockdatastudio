using System.Text.Json.Nodes;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Tests.TestSupport;
using Shouldly;
using Xunit;
using static MockDataStudio.Generation.Tests.TestSupport.GenerationFixtures;

namespace MockDataStudio.Generation.Tests.Generators;

// T-GEN-COMP-001 (consistência de slot composto) e T-GEN-LIB (snapshots de 1000 valores por gerador, docs/26 §5.5).
public sealed class CompositeAndSnapshotTests
{
    private const string SnapshotFolder = "tests/fixtures/generation-snapshots";

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-COMP-001")]
    public async Task T_GEN_COMP_001_ColunasDoMesmoSlotLeemOMesmoRegistro()
    {
        var table = Table("pedido", [
            Plan(Col("cep", "char(8)", ordinal: 1), "catalog.template.field", Obj(("template", "ENDERECO"), ("field", "cep"), ("slot", "entrega"))),
            Plan(Col("cidade", "varchar(60)", ordinal: 2), "catalog.template.field", Obj(("template", "ENDERECO"), ("field", "cidade"), ("slot", "entrega"))),
            Plan(Col("uf", "char(2)", ordinal: 3), "catalog.template.field", Obj(("template", "ENDERECO"), ("field", "uf"), ("slot", "entrega"))),
        ], rows: 2000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        var endereco = PackTemplates()["ENDERECO"];
        var records = Enumerable.Range(0, endereco.Count)
            .Select(i => string.Join("|", endereco.GetField(i, "cep"), endereco.GetField(i, "cidade"), endereco.GetField(i, "uf")))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var row in sink.Rows)
        {
            records.ShouldContain(string.Join("|", row[0], row[1], row[2]));
        }
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-LIB")]
    public async Task T_GEN_LIB_SnapshotsDeMilValoresPorGerador()
    {
        var root = Path.Combine(RepoRoot(), SnapshotFolder);
        Directory.CreateDirectory(root);
        var update = Environment.GetEnvironmentVariable("MDS_UPDATE_SNAPSHOTS") == "1";
        var failures = new List<string>();

        foreach (var (id, parameters, nativeType) in SnapshotCases())
        {
            var table = Table("snap", [Plan(Col("valor", nativeType, ordinal: 1), id, parameters)], rows: 1000);
            var sink = new MemorySink();
            var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);
            if (!result.IsSuccess)
            {
                failures.Add($"{id}: {result.Error.Code} {result.Error.Message}");
                continue;
            }

            var text = string.Join("\n", sink.Rows.Select(r => Text(r[0]))) + "\n";
            var path = Path.Combine(root, id + ".v1.txt");
            if (update || !File.Exists(path))
            {
                File.WriteAllText(path, text);
                continue;
            }

            // Fim de linha normalizado: o checkout Windows (core.autocrlf) grava os snapshots com CRLF.
            if (File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) != text)
            {
                failures.Add($"{id}: saída diverge do snapshot {path}");
            }
        }

        failures.ShouldBeEmpty(string.Join("; ", failures));
    }

    private static IEnumerable<(string Id, JsonObject Parameters, string NativeType)> SnapshotCases()
    {
        yield return ("core.fixed", Obj(("value", "fixo")), "varchar(20)");
        yield return ("core.null", new JsonObject(), "varchar(20)");
        yield return ("core.bool", Obj(("probabilityTrue", 0.3)), "boolean");
        yield return ("core.list.random", Obj(("values", new JsonArray("a", "b", "c"))), "varchar(10)");
        yield return ("core.list.sequential", Obj(("values", new JsonArray("x", "y")), ("offset", 1L)), "varchar(10)");
        yield return ("core.sequence.int", Obj(("start", 100L), ("step", 5L)), "integer");
        yield return ("core.random.int", Obj(("min", 1L), ("max", 1000L)), "integer");
        yield return ("core.random.decimal", Obj(("min", 0m), ("max", 500m), ("scale", 2L)), "numeric(12,2)");
        yield return ("core.random.float", Obj(("min", -1.0), ("max", 1.0)), "double");
        yield return ("core.random.string", Obj(("minLength", 5L), ("maxLength", 9L), ("charset", "hex")), "varchar(9)");
        yield return ("core.mask", Obj(("pattern", "###.###.###-##")), "varchar(14)");
        yield return ("core.uuid", new JsonObject(), "uuid");
        yield return ("core.random.bytes", Obj(("length", 8L)), "blob");
        yield return ("core.date.range", Obj(("start", "2020-01-01"), ("end", "2026-10-12"), ("granularity", "day")), "date");
        yield return ("core.date.relative", Obj(("from", "-P80Y"), ("to", "-P18Y"), ("granularity", "day")), "date");
        yield return ("core.date.sequence", Obj(("start", "2026-01-01 08:00:00"), ("step", "P1DT1H")), "datetime");
        yield return ("core.expr", Obj(("expression", "row_index() * 3 + 1")), "integer");
        yield return ("catalog.template", Obj(("template", "NOME")), "varchar(60)");
        yield return ("catalog.template.field", Obj(("template", "ENDERECO"), ("field", "bairro")), "varchar(60)");
    }
}
