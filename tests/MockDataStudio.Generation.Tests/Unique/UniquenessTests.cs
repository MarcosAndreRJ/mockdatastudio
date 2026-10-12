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

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-UQ-003")]
    public async Task T_GEN_UQ_003_SufixoOptInValidaComprimento()
    {
        // Caso 1: Comprimento insuficiente (varchar(5) com base de 4 chars + sufixo de 3 chars para 100 linhas)
        // 4 + 3 = 7 > 5 -> deve falhar com VAL-LEN.
        var colCurta = Col("codigo", "varchar(5)", nullable: false, ordinal: 1);
        var tableInvalida = Table("t_curta", [Plan(colCurta, "core.random.string",
            Obj(("minLength", 4L), ("maxLength", 4L), ("charset", "alpha")), unique: true, uniqueStrategy: "suffix")], rows: 100);

        var resultInvalido = await new GenerationEngine(Registry()).RunAsync(tableInvalida, new MemorySink(), TestContext.Current.CancellationToken);
        resultInvalido.IsSuccess.ShouldBeFalse();
        resultInvalido.Error.Code.ShouldBe(ErrorCodes.ValLen);

        // Caso 2: Comprimento suficiente (varchar(20) com base de 4 chars + sufixo)
        // Deve gerar com sucesso e todos os 100 valores devem ser únicos e terminar com "-base36(row)".
        var colLonga = Col("codigo", "varchar(20)", nullable: false, ordinal: 1);
        var tableValida = Table("t_longa", [Plan(colLonga, "core.random.string",
            Obj(("minLength", 4L), ("maxLength", 4L), ("charset", "alpha")), unique: true, uniqueStrategy: "suffix")], rows: 100);
        var sink = new MemorySink();

        var resultValido = await new GenerationEngine(Registry()).RunAsync(tableValida, sink, TestContext.Current.CancellationToken);
        resultValido.IsSuccess.ShouldBeTrue();
        sink.Rows.Count.ShouldBe(100);
        var values = sink.Rows.Select(r => (string)r[0]!).ToList();
        values.Distinct().Count().ShouldBe(100);
        for (var i = 0; i < 100; i++)
        {
            values[i].ShouldEndWith("-" + SuffixDecorator.ToBase36(i));
        }
    }

    [Fact]
    public void MixedRadixDecomposer_DecomposicaoERecomposicaoSaoBijetivas()
    {
        var radices = new long[] { 3, 5, 4 }; // P = 60
        var seen = new HashSet<string>();

        for (ulong i = 0; i < 60; i++)
        {
            var indices = MixedRadixDecomposer.Decompose(i, radices);
            indices.Length.ShouldBe(3);
            indices[0].ShouldBeInRange(0, 2);
            indices[1].ShouldBeInRange(0, 4);
            indices[2].ShouldBeInRange(0, 3);

            var composed = MixedRadixDecomposer.Compose(indices, radices);
            composed.ShouldBe(i);

            var tupleKey = $"{indices[0]},{indices[1]},{indices[2]}";
            seen.Add(tupleKey).ShouldBeTrue();
        }

        seen.Count.ShouldBe(60);
    }

    [Fact]
    public async Task UniqueComposto_GeraTuplasDistintasComBaseMista()
    {
        // colA tem 5 opções, colB tem 10 opções -> P = 50 tuplas possíveis
        var arrA = new System.Text.Json.Nodes.JsonArray("A1", "A2", "A3", "A4", "A5");
        var arrB = new System.Text.Json.Nodes.JsonArray("B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10");

        var colA = Col("tipo_a", "varchar(10)", nullable: false, ordinal: 1);
        var colB = Col("tipo_b", "varchar(10)", nullable: false, ordinal: 2);

        var table = Table("item", [
            Plan(colA, "core.list.sequential", Obj(("values", arrA))),
            Plan(colB, "core.list.sequential", Obj(("values", arrB)))
        ], rows: 50, compositeUniques: [["tipo_a", "tipo_b"]]);

        var sink = new MemorySink();
        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var tuples = sink.Rows.Select(r => $"{r[0]}|{r[1]}").ToList();
        tuples.Distinct().Count().ShouldBe(50);
    }

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
