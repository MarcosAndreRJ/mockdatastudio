using System.Numerics;

namespace MockDataStudio.Generation.Unique;

// Permutação bijetiva determinística de [0, N) (docs/15 §8.3): Feistel de 4 rodadas com cycle-walking.
// O(1) de memória; a bijeção é verificada exaustivamente para N ≤ 65.536 (T-GEN-UQ-001).
public sealed class FeistelPermutation
{
    private const int Rounds = 4;

    private readonly ulong _size;
    private readonly ulong _key;
    private readonly int _half;
    private readonly ulong _mask;

    public FeistelPermutation(ulong size, ulong key)
    {
        if (size == 0 || size > (1UL << 62))
        {
            throw new ArgumentOutOfRangeException(nameof(size), "N deve estar em [1, 2^62].");
        }

        _size = size;
        _key = key;
        var bits = Math.Max(2, (int)BitOperations.Log2(size - 1) + 1);
        if (size == 1)
        {
            bits = 2;
        }

        if (bits % 2 == 1)
        {
            bits++;
        }

        _half = bits / 2;
        _mask = (1UL << _half) - 1;
    }

    // Perm(i): cifra e repete enquanto o resultado cair fora de [0, N).
    public ulong Map(ulong index)
    {
        if (index >= _size)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Índice fora do domínio.");
        }

        var y = Encrypt(index);
        while (y >= _size)
        {
            y = Encrypt(y);
        }

        return y;
    }

    private ulong Encrypt(ulong x)
    {
        var left = x >> _half;
        var right = x & _mask;
        for (var round = 0; round < Rounds; round++)
        {
            (left, right) = (right, left ^ Round(round, right));
        }

        return (left << _half) | right;
    }

    private ulong Round(int round, ulong x) => Mix(_key ^ ((ulong)round << 56) ^ x) & _mask;

    // Finalizador de SplitMix64 (sem o incremento), usado como função de rodada.
    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
