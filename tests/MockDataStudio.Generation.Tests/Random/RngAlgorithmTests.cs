using MockDataStudio.Generation.Random;
using Shouldly;
using Xunit;

namespace MockDataStudio.Generation.Tests.Random;

// T-GEN-RNG-001: vetores congelados de docs/15 §4.1 (key = 0x0123456789ABCDEF, três primeiras saídas).
public sealed class RngAlgorithmTests
{
    private static readonly StreamKey Key = new(0x0123456789ABCDEFUL);

    [Theory]
    [InlineData(0L, 0xA2C2A42038D4EC3DUL, 0x05FC25D0738E7B0FUL, 0x625E7BFF938E701EUL)]
    [InlineData(1L, 0xC8BAB3CA1E5B1E1FUL, 0x188BE30AA610EB7DUL, 0xCF06E1E57C974F9EUL)]
    [InlineData(1000000L, 0x07A5F5E3AC10A13AUL, 0x067D67E0A09AAB25UL, 0x09ABD2AAE1C56789UL)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-RNG-001")]
    public void T_GEN_RNG_001_VetoresCongeladosPorLinha(long row, ulong first, ulong second, ulong third)
    {
        var rng = DeterministicRandom.For(Key, row);

        rng.NextUInt64().ShouldBe(first);
        rng.NextUInt64().ShouldBe(second);
        rng.NextUInt64().ShouldBe(third);
    }
}
