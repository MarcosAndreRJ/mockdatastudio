using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Tests.TestSupport;
using Shouldly;
using Xunit;
using static MockDataStudio.Generation.Tests.TestSupport.GenerationFixtures;

namespace MockDataStudio.Generation.Tests.Engine;

// Motor: determinismo entre graus de paralelismo (T-GEN-DET-001) e taxa de nulos (T-GEN-NULL-001).
public sealed class EngineTests
{
    private static readonly char[] Variants = ['8', '9', 'a', 'b'];

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-DET-001")]
    public async Task T_GEN_DET_001_MesmaSaidaComParalelismoUmEOito()
    {
        var columns = new List<ColumnPlan>
        {
            Plan(Col("id", "integer", nullable: false, ordinal: 1), "core.sequence.int", Obj(("start", 1L))),
            Plan(Col("nome", "varchar(60)", ordinal: 2), "catalog.template", Obj(("template", "NOME"))),
            Plan(Col("sobrenome", "varchar(60)", ordinal: 3), "catalog.template", Obj(("template", "SOBRENOME"))),
            Plan(Col("valor", "numeric(12,2)", ordinal: 4), "core.random.decimal", Obj(("min", 0m), ("max", 9999m))),
            Plan(Col("criado", "datetime", ordinal: 5), "core.date.relative", Obj(("from", "-P2Y"), ("to", "P0D"), ("granularity", "second"))),
            Plan(Col("token", "varchar(36)", ordinal: 6), "core.uuid"),
            Plan(Col("email", "varchar(120)", ordinal: 7), "core.expr", Obj(("expression", "lower(@nome) || '.' || @id || '@exemplo.test'"))),
            Plan(Col("cep", "char(8)", ordinal: 8), "catalog.template.field", Obj(("template", "ENDERECO"), ("field", "cep"), ("slot", "entrega"))),
        };

        var serial = Run(columns, rows: 3000, parallelism: 1, batch: 257);
        var parallel = Run(columns, rows: 3000, parallelism: 8, batch: 257);

        serial.ShouldBe(parallel);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-NULL-001")]
    public async Task T_GEN_NULL_001_TaxaDeNulosEhRespeitadaEmColunaNula()
    {
        var table = Table("cliente", [Plan(Col("apelido", "varchar(20)", ordinal: 1), "core.fixed", Obj(("value", "x")), nullRate: 0.2)],
            rows: 100_000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var nulls = sink.Rows.Count(r => r[0] is null);
        var rate = (double)nulls / sink.Rows.Count;
        rate.ShouldBeInRange(0.19, 0.21);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-NULL-001")]
    public async Task T_GEN_NULL_001_NotNullComTaxaPositivaEhErroDeValidacao()
    {
        var table = Table("cliente", [Plan(Col("nome", "varchar(20)", nullable: false, ordinal: 1), "core.fixed", Obj(("value", "x")), nullRate: 0.1)], rows: 10);

        var result = await new GenerationEngine(Registry()).RunAsync(table, new MemorySink(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.ValNullNotNull);
    }

    // Semântica do UUID v4 (versão e variante RFC 4122), independente do snapshot.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-LIB")]
    public async Task T_GEN_LIB_UuidV4TemVersaoEVarianteCorretas()
    {
        var table = Table("t", [Plan(Col("token", "uuid", ordinal: 1), "core.uuid")], rows: 500);
        var sink = new MemorySink();

        await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        foreach (var row in sink.Rows)
        {
            var text = ((Guid)row[0]!).ToString();
            text[14].ShouldBe('4');
            Variants.ShouldContain(text[19]);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-DET-001")]
    public async Task T_GEN_DET_001_LotesSaemEmOrdemCrescenteDeIndice()
    {
        var table = Table("t", [Plan(Col("id", "integer", ordinal: 1), "core.sequence.int", Obj(("start", 1L)))], rows: 2500, batch: 1000);
        var sink = new MemorySink();

        await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        sink.BatchStarts.ShouldBe([0L, 1000L, 2000L]);
        sink.Rows.Count.ShouldBe(2500);
    }

    private static List<string> Run(IReadOnlyList<ColumnPlan> columns, long rows, int parallelism, int batch)
    {
        var table = Table("cliente", columns, rows, batch: batch, parallelism: parallelism);
        var sink = new MemorySink();
        var result = new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        return sink.Rows.Select(r => string.Join("|", r.Select(Text))).ToList();
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-SINK-001")]
    public async Task IRowSink_CicloDeVidaDeTabelaInvocado()
    {
        var table = Table("cliente", [Plan(Col("id", "integer", ordinal: 1), "core.sequence.int", Obj(("start", 1L)))], rows: 100);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        sink.BeganTable.ShouldBeTrue();
        sink.EndedTable.ShouldBeTrue();
        sink.BatchStarts.ShouldBe([0L]);
    }

    [Fact]
    public void PlanSteps_InstanciamComOrdemCorreta()
    {
        var setup = new MockDataStudio.Generation.Planning.SessionSetupStep(1, ["SET foreign_key_checks = 0;"]);
        setup.Order.ShouldBe(1);
        setup.Statements.Count.ShouldBe(1);

        var teardown = new MockDataStudio.Generation.Planning.SessionTeardownStep(2, ["SET foreign_key_checks = 1;"]);
        teardown.Order.ShouldBe(2);
    }
}
