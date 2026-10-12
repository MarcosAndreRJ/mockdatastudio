using MockDataStudio.Generation.Random;
using Shouldly;
using Xunit;

namespace MockDataStudio.Generation.Tests.Random;

// T-GEN-RNG-002: uniformidade (χ², 10^6 amostras, p > 0,001; docs/15 §4 e 26 §5.5). Determinístico: sem variação entre execuções.
public sealed class RngStatisticsTests
{
    // Valor crítico χ² com 9 graus de liberdade para p = 0,001.
    private const double ChiSquareCritical = 27.877;

    [Fact]
    [Trait("Category", "Statistical")]
    [Trait("Id", "T-GEN-RNG-002")]
    public void T_GEN_RNG_002_InteiroUniformeEmDezBinsSemVies()
    {
        var key = new StreamKey(0xC0FFEE0123456789UL);
        var counts = new long[10];
        const int samples = 1_000_000;
        for (var row = 0; row < samples; row++)
        {
            var rng = DeterministicRandom.For(key, row);
            counts[rng.NextInt64(0, 10)]++;
        }

        ChiSquare(counts, samples).ShouldBeLessThan(ChiSquareCritical);
    }

    [Fact]
    [Trait("Category", "Statistical")]
    [Trait("Id", "T-GEN-RNG-002")]
    public void T_GEN_RNG_002_RealUniformeEmDezBins()
    {
        var key = new StreamKey(0x0BADF00DCAFEBABEUL);
        var counts = new long[10];
        const int samples = 1_000_000;
        for (var row = 0; row < samples; row++)
        {
            var rng = DeterministicRandom.For(key, row);
            counts[(int)(rng.NextDouble() * 10)]++;
        }

        ChiSquare(counts, samples).ShouldBeLessThan(ChiSquareCritical);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-RNG-002")]
    public void T_GEN_RNG_002_PesosPonderadosRespeitamProporcao()
    {
        var table = new WeightedIndex([1.0, 3.0]);
        var key = new StreamKey(0x5EEDUL);
        var hits = 0;
        const int samples = 200_000;
        for (var row = 0; row < samples; row++)
        {
            var rng = DeterministicRandom.For(key, row);
            if (rng.NextWeightedIndex(table) == 1)
            {
                hits++;
            }
        }

        var share = (double)hits / samples;
        share.ShouldBeInRange(0.745, 0.755);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-RNG-002")]
    public void T_GEN_RNG_002_ChildDerivaFluxoDiferenteDoPai()
    {
        var parent = StreamKey.Derive(7, "tabela", "value");
        parent.Child("null").ShouldNotBe(parent.Child("value"));
        StreamKey.Derive(7, "tabela", "value").ShouldBe(parent);
        StreamKey.Derive(8, "tabela", "value").ShouldNotBe(parent);
    }

    private static double ChiSquare(long[] observed, int samples)
    {
        var expected = (double)samples / observed.Length;
        return observed.Sum(o => (o - expected) * (o - expected) / expected);
    }
}
