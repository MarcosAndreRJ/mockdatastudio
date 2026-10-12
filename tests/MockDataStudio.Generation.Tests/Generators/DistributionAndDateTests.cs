using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Generation.Tests.TestSupport;
using Shouldly;
using Xunit;
using static MockDataStudio.Generation.Tests.TestSupport.GenerationFixtures;

namespace MockDataStudio.Generation.Tests.Generators;

public sealed class DistributionAndDateTests
{
    [Fact]
    public async Task RandomInt_Normal_GeraDentroDaFaixaEConcentraNaMedia()
    {
        var spec = Obj(("min", 100L), ("max", 200L), ("distribution", "normal"), ("mean", 150.0), ("stddev", 10.0));
        var table = Table("t", [Plan(Col("val", "integer", ordinal: 1), "core.random.int", spec)], rows: 5000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        sink.Rows.Count.ShouldBe(5000);

        var values = sink.Rows.Select(r => (long)r[0]!).ToList();
        values.All(v => v is >= 100 and <= 200).ShouldBeTrue();

        var avg = values.Average();
        avg.ShouldBeInRange(145.0, 155.0);
    }

    [Fact]
    public async Task RandomInt_Zipf_GeraMaisProximoDoMinimo()
    {
        var spec = Obj(("min", 1L), ("max", 100L), ("distribution", "zipf"), ("zipfS", 1.5));
        var table = Table("t", [Plan(Col("val", "integer", ordinal: 1), "core.random.int", spec)], rows: 5000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var values = sink.Rows.Select(r => (long)r[0]!).ToList();
        values.All(v => v is >= 1 and <= 100).ShouldBeTrue();

        // Em Zipf, valores baixos devem ser maioria expressiva:
        var inFirstDecile = values.Count(v => v <= 10);
        ((double)inFirstDecile / values.Count).ShouldBeGreaterThan(0.5);
    }

    [Fact]
    public async Task RandomDecimal_Normal_RespeitaFaixaEEscala()
    {
        var spec = Obj(("min", 10.0m), ("max", 50.0m), ("scale", 2L), ("distribution", "normal"), ("mean", 30.0), ("stddev", 5.0));
        var table = Table("t", [Plan(Col("val", "numeric(10,2)", ordinal: 1), "core.random.decimal", spec)], rows: 1000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var values = sink.Rows.Select(r => (decimal)r[0]!).ToList();
        values.All(v => v >= 10m && v <= 50m).ShouldBeTrue();
        var avg = (double)values.Average();
        avg.ShouldBeInRange(28.0, 32.0);
    }

    [Fact]
    public async Task DateRange_Weekdays_GeraApenasDiasDaSemanaPermitidos()
    {
        // 1 a 5 (segunda a sexta)
        var weekdaysArr = new JsonArray(1, 2, 3, 4, 5);
        var spec = new JsonObject
        {
            ["start"] = "2026-01-01 00:00:00",
            ["end"] = "2026-06-30 00:00:00",
            ["granularity"] = "day",
            ["weekdays"] = weekdaysArr
        };

        var table = Table("t", [Plan(Col("data", "date", ordinal: 1), "core.date.range", spec)], rows: 1000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        foreach (var row in sink.Rows)
        {
            var dow = row[0] is DateOnly d ? d.DayOfWeek : ((DateTime)row[0]!).DayOfWeek;
            var iso = dow == DayOfWeek.Sunday ? 7 : (int)dow;
            iso.ShouldBeInRange(1, 5);
        }
    }

    [Fact]
    public async Task DateRange_Hours_GeraApenasDentroDoHorario()
    {
        var spec = Obj(("start", "2026-01-01 00:00:00"), ("end", "2026-01-10 23:59:59"), ("granularity", "hour"), ("hours", "08-18"));
        var table = Table("t", [Plan(Col("data", "datetime", ordinal: 1), "core.date.range", spec)], rows: 1000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        foreach (var row in sink.Rows)
        {
            var dt = (DateTime)row[0]!;
            dt.Hour.ShouldBeInRange(8, 18);
        }
    }

    [Fact]
    public async Task DateSequence_PassoComAnosEMeses_AvancaCorretamente()
    {
        var specYear = Obj(("start", "2020-01-15 10:00:00"), ("step", "P1Y"));
        var tableYear = Table("t", [Plan(Col("data", "datetime", ordinal: 1), "core.date.sequence", specYear)], rows: 5);
        var sinkYear = new MemorySink();

        var resYear = await new GenerationEngine(Registry()).RunAsync(tableYear, sinkYear, TestContext.Current.CancellationToken);
        resYear.IsSuccess.ShouldBeTrue();
        var years = sinkYear.Rows.Select(r => ((DateTime)r[0]!).Year).ToList();
        years.ShouldBe([2020, 2021, 2022, 2023, 2024]);

        var specMonth = Obj(("start", "2026-01-01 00:00:00"), ("step", "P2M"));
        var tableMonth = Table("t", [Plan(Col("data", "datetime", ordinal: 1), "core.date.sequence", specMonth)], rows: 4);
        var sinkMonth = new MemorySink();

        var resMonth = await new GenerationEngine(Registry()).RunAsync(tableMonth, sinkMonth, TestContext.Current.CancellationToken);
        resMonth.IsSuccess.ShouldBeTrue();
        var months = sinkMonth.Rows.Select(r => ((DateTime)r[0]!).Month).ToList();
        months.ShouldBe([1, 3, 5, 7]);
    }
}
