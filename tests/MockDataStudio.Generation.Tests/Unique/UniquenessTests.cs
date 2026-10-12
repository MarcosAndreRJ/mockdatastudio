using MockDataStudio.Core.Common;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Unique;
using MockDataStudio.Generation.Tests.TestSupport;
using Shouldly;
using Xunit;
using static MockDataStudio.Generation.Tests.TestSupport.GenerationFixtures;

namespace MockDataStudio.Generation.Tests.Unique;

// Unicidade (docs/15 §8): permutação bijetiva (UQ-001), capacidade (UQ-002), sufixo (UQ-003) e esgotamento (UQ-004).
public sealed class UniquenessTests
{
    // T-GEN-UQ-001: bijeção exaustiva para N ≤ 4096 e para N = 65.536 (domínio do teste; a varredura completa até 2^16 custa minutos).
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-UQ-001")]
    public void T_GEN_UQ_001_PermutacaoEhBijetivaParaTodosOsNAteQuatroMil()
    {
        for (ulong n = 1; n <= 4096; n++)
        {
            AssertBijection(n, key: 0x9E3779B9UL + n);
        }
    }

    [Theory]
    [InlineData(65536UL)]
    [InlineData(65535UL)]
    [InlineData(1000003UL)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-UQ-001")]
    public void T_GEN_UQ_001_PermutacaoEhBijetivaEmTamanhosGrandes(ulong n) => AssertBijection(n, key: 42UL);

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-UQ-002")]
    public async Task T_GEN_UQ_002_CapacidadeInsuficienteBloqueiaComPlnUniqueCapacity()
    {
        var table = Table("produto", [Plan(Col("cor", "varchar(20)", nullable: false, ordinal: 1), "core.list.random",
            Obj(("values", new System.Text.Json.Nodes.JsonArray("azul", "verde", "vermelho"))), unique: true)], rows: 10);

        var result = await new GenerationEngine(Registry()).RunAsync(table, new MemorySink(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.PlnUniqueCapacity);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-UQ-002")]
    public async Task T_GEN_UQ_002_CapacidadeSuficienteGeraValoresDistintos()
    {
        var table = Table("produto", [Plan(Col("cor", "varchar(20)", nullable: false, ordinal: 1), "core.list.random",
            Obj(("values", new System.Text.Json.Nodes.JsonArray("azul", "verde", "vermelho", "preto"))), unique: true)], rows: 4);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        sink.Rows.Select(r => (string)r[0]!).Distinct().Count().ShouldBe(4);
    }

    // T-GEN-UQ-003: estratégia 4 (sufixo opt-in) não foi implementada nesta versão (docs/15 §8.2). Registrado como pendência.
    [Fact(Skip = "Pendente: estratégia de sufixo (15 §8.2, estratégia 4) ainda não implementada; ver PLANO Sprint 6.")]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-UQ-003")]
    public Task T_GEN_UQ_003_SufixoOptInValidaComprimento() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-UQ-004")]
    public async Task T_GEN_UQ_004_TentativasEsgotadasFalhamComGenUniqueExhausted()
    {
        // core.fixed não é indexável: a única unicidade possível é por tentativas; sempre repete o mesmo valor.
        var table = Table("cliente", [Plan(Col("codigo", "varchar(10)", nullable: false, ordinal: 1), "core.fixed",
            Obj(("value", "X")), unique: true)], rows: 2);

        var result = await new GenerationEngine(Registry()).RunAsync(table, new MemorySink(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.GenUniqueExhausted);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-UQ-004")]
    public async Task T_GEN_UQ_004_TentativasComGeradorAleatorioGeramValoresUnicos()
    {
        var table = Table("cliente", [Plan(Col("codigo", "varchar(12)", nullable: false, ordinal: 1), "core.random.string",
            Obj(("minLength", 10L), ("maxLength", 12L), ("charset", "alpha")), unique: true)], rows: 3000);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        sink.Rows.Select(r => (string)r[0]!).Distinct().Count().ShouldBe(3000);
    }

    private static void AssertBijection(ulong n, ulong key)
    {
        var permutation = new FeistelPermutation(n, key);
        var seen = new bool[n];
        for (ulong i = 0; i < n; i++)
        {
            var value = permutation.Map(i);
            value.ShouldBeLessThan(n);
            seen[value].ShouldBeFalse($"Valor repetido para N={n}, i={i}.");
            seen[value] = true;
        }
    }
}
