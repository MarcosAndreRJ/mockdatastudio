using System.Text.Json.Nodes;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Generation.Tests.TestSupport;
using Shouldly;
using Xunit;
using static MockDataStudio.Generation.Tests.TestSupport.GenerationFixtures;

namespace MockDataStudio.Generation.Tests.Generators;

// T-GEN-BR-001 (docs/02 FR-076, AC-076.1): 100% dos CPF/CNPJ com DVs válidos; unicidade O(1) (AC-075.1).
public sealed class BrazilDocTests
{
    private static GeneratorRegistry Registry() => GeneratorRegistry.Standard();

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-BR-001")]
    public async Task T_GEN_BR_001_CpfCnpjGeramDigitosVerificadoresValidos()
    {
        var table = Table("cliente_br", [
            Plan(Col("cpf_raw", "char(11)", ordinal: 1), "br.cpf", Obj(("formatted", false))),
            Plan(Col("cpf_fmt", "varchar(14)", ordinal: 2), "br.cpf", Obj(("formatted", true))),
            Plan(Col("cnpj_raw", "char(14)", ordinal: 3), "br.cnpj", Obj(("formatted", false))),
            Plan(Col("cnpj_fmt", "varchar(18)", ordinal: 4), "br.cnpj", Obj(("formatted", true)))
        ], rows: 2000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        sink.Rows.Count.ShouldBe(2000);

        foreach (var row in sink.Rows)
        {
            var cpfRaw = (string)row[0]!;
            var cpfFmt = (string)row[1]!;
            var cnpjRaw = (string)row[2]!;
            var cnpjFmt = (string)row[3]!;

            cpfRaw.Length.ShouldBe(11);
            BrazilDocUtils.IsValidCpf(cpfRaw).ShouldBeTrue($"CPF raw inválido: {cpfRaw}");

            cpfFmt.Length.ShouldBe(14);
            BrazilDocUtils.IsValidCpf(cpfFmt).ShouldBeTrue($"CPF formatado inválido: {cpfFmt}");

            cnpjRaw.Length.ShouldBe(14);
            BrazilDocUtils.IsValidCnpj(cnpjRaw).ShouldBeTrue($"CNPJ raw inválido: {cnpjRaw}");

            cnpjFmt.Length.ShouldBe(18);
            BrazilDocUtils.IsValidCnpj(cnpjFmt).ShouldBeTrue($"CNPJ formatado inválido: {cnpjFmt}");
        }
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-BR-001")]
    public async Task T_GEN_BR_001_CpfUnicoSemColisaoPorPermutacao()
    {
        // Garante que a estratégia 2 (permutação bijetiva sobre 10^9 bases) gera CPFs distintos
        var table = Table("pessoa", [
            Plan(Col("cpf", "char(11)", ordinal: 1), "br.cpf", Obj(("formatted", false)), unique: true)
        ], rows: 5000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        sink.Rows.Count.ShouldBe(5000);
        var cpfs = sink.Rows.Select(r => (string)r[0]!).ToList();
        cpfs.Distinct().Count().ShouldBe(5000);
        cpfs.All(BrazilDocUtils.IsValidCpf).ShouldBeTrue();
    }

    [Fact]
    public async Task MdsExpr_FuncoesCpfECnpjGeramValoresValidos()
    {
        var table = Table("docs_expr", [
            Plan(Col("c1", "char(11)", ordinal: 1), "core.expr", Obj(("expression", "cpf()"))),
            Plan(Col("c2", "varchar(14)", ordinal: 2), "core.expr", Obj(("expression", "cpf(true)"))),
            Plan(Col("c3", "char(14)", ordinal: 3), "core.expr", Obj(("expression", "cnpj()"))),
            Plan(Col("c4", "varchar(18)", ordinal: 4), "core.expr", Obj(("expression", "cnpj(true, 2)")))
        ], rows: 500);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        foreach (var row in sink.Rows)
        {
            BrazilDocUtils.IsValidCpf((string)row[0]!).ShouldBeTrue();
            BrazilDocUtils.IsValidCpf((string)row[1]!).ShouldBeTrue();
            BrazilDocUtils.IsValidCnpj((string)row[2]!).ShouldBeTrue();
            var c4 = (string)row[3]!;
            BrazilDocUtils.IsValidCnpj(c4).ShouldBeTrue();
            c4.ShouldContain("/0002-");
        }
    }
}
