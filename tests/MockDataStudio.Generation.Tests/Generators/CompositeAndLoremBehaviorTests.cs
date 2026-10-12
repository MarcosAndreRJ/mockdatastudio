using System.Text.Json.Nodes;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Generation.Tests.TestSupport;
using Shouldly;
using Xunit;
using static MockDataStudio.Generation.Tests.TestSupport.GenerationFixtures;

namespace MockDataStudio.Generation.Tests.Generators;

public sealed class CompositeAndLoremBehaviorTests
{
    private static GeneratorRegistry Registry() => GeneratorRegistry.Standard();

    [Fact]
    public async Task Weighted_GeraProporcionalAosPesos()
    {
        var spec = new JsonObject
        {
            ["weights"] = new JsonArray(20.0, 80.0),
            ["children"] = new JsonArray(
                new JsonObject { ["generatorId"] = "core.fixed", ["parameters"] = new JsonObject { ["value"] = "A" } },
                new JsonObject { ["generatorId"] = "core.fixed", ["parameters"] = new JsonObject { ["value"] = "B" } }
            )
        };

        var table = Table("t", [Plan(Col("tipo", "varchar(10)", ordinal: 1), "core.weighted", spec)], rows: 1000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var countA = sink.Rows.Count(r => (string)r[0]! == "A");
        var countB = sink.Rows.Count(r => (string)r[0]! == "B");

        // Esperado ~20% A e ~80% B (com margem estatística para 1000 amostras)
        countA.ShouldBeInRange(150, 260);
        countB.ShouldBeInRange(740, 850);
    }

    [Fact]
    public async Task Switch_AvaliaColunaAnteriorNaMesmaLinha()
    {
        var switchSpec = new JsonObject
        {
            ["cases"] = new JsonArray(
                new JsonObject
                {
                    ["when"] = "@categoria = 'VIP'",
                    ["generator"] = new JsonObject { ["generatorId"] = "core.fixed", ["parameters"] = new JsonObject { ["value"] = "Desconto 50%" } }
                }
            ),
            ["else"] = new JsonObject { ["generatorId"] = "core.fixed", ["parameters"] = new JsonObject { ["value"] = "Sem desconto" } }
        };

        var catList = new JsonArray("REGULAR", "VIP");
        var table = Table("t", [
            Plan(Col("categoria", "varchar(10)", ordinal: 1), "core.list.sequential", Obj(("values", catList))),
            Plan(Col("beneficio", "varchar(20)", ordinal: 2), "core.switch", switchSpec)
        ], rows: 100);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        for (var i = 0; i < 100; i++)
        {
            var cat = (string)sink.Rows[i][0]!;
            var ben = (string)sink.Rows[i][1]!;
            if (cat == "VIP")
            {
                ben.ShouldBe("Desconto 50%");
            }
            else
            {
                ben.ShouldBe("Sem desconto");
            }
        }
    }

    [Fact]
    public async Task LoremWords_RespeitaFaixaDePalavras()
    {
        var spec = Obj(("min", 3L), ("max", 6L));
        var table = Table("t", [Plan(Col("texto", "varchar(200)", ordinal: 1), "core.lorem.words", spec)], rows: 200);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        foreach (var row in sink.Rows)
        {
            var text = (string)row[0]!;
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            words.Length.ShouldBeInRange(3, 6);
        }
    }

    [Fact]
    public async Task LoremParagraph_GeraFrasesPontuadasComCapitalizacao()
    {
        var spec = Obj(("minSentences", 2L), ("maxSentences", 4L));
        var table = Table("t", [Plan(Col("paragrafo", "text", ordinal: 1), "core.lorem.paragraph", spec)], rows: 100);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        foreach (var row in sink.Rows)
        {
            var paragraph = (string)row[0]!;
            paragraph.ShouldEndWith(".");
            char.IsUpper(paragraph[0]).ShouldBeTrue();

            var sentences = paragraph.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            sentences.Length.ShouldBeInRange(2, 4);
        }
    }
}
