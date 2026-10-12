using System.Numerics;

namespace MockDataStudio.Generation.Random;

// RNG determinístico (docs/15 §4.1). Estado = 4 saídas de SplitMix64 semeadas por key XOR (row * golden); saída xoshiro256**.
// Struct mutável: cada célula usa a sua instância; não compartilhar entre threads.
public struct DeterministicRandom
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    private ulong _s0, _s1, _s2, _s3;

    private DeterministicRandom(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    public static DeterministicRandom For(StreamKey key, long rowIndex)
    {
        var seed = key.Value ^ ((ulong)rowIndex * Golden);
        var sm = seed;
        Span<ulong> state = stackalloc ulong[4];
        for (var i = 0; i < 4; i++)
        {
            state[i] = SplitMix64(ref sm);
        }

        return new DeterministicRandom(state[0], state[1], state[2], state[3]);
    }

    public ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    // Intervalo [minInclusive, maxExclusive) sem viés, por multiplicação de 128 bits com rejeição (Lemire).
    public long NextInt64(long minInclusive, long maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Intervalo vazio.");
        }

        var range = unchecked((ulong)(maxExclusive - minInclusive));
        var x = NextUInt64();
        var product = Math.BigMul(x, range, out var low);
        if (low < range)
        {
            var threshold = unchecked((0UL - range) % range);
            while (low < threshold)
            {
                x = NextUInt64();
                product = Math.BigMul(x, range, out low);
            }
        }

        return unchecked(minInclusive + (long)product);
    }

    // [0, 1) com 53 bits.
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public bool NextBool(double probabilityTrue) => NextDouble() < probabilityTrue;

    // Decimal uniforme em [min, max] com a escala indicada (unidades inteiras, 15 §4.1).
    public decimal NextDecimal(decimal min, decimal max, int scale)
    {
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "max menor que min.");
        }

        var factor = (decimal)Math.Pow(10, scale);
        var units = (long)((max - min) * factor);
        var pick = NextInt64(0, checked(units + 1));
        return min + pick / factor;
    }

    public int NextWeightedIndex(WeightedIndex table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var column = NextInt64(0, table.Count);
        return NextDouble() < table.Probability(column) ? (int)column : table.Alias(column);
    }

    private static ulong SplitMix64(ref ulong s)
    {
        s += Golden;
        var z = s;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
