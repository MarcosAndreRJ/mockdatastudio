namespace MockDataStudio.Generation.Unique;

// Decomposição em base mista para UNIQUE composto (docs/15 §8.4).
// Transforma um índice univariado p em [0, P) em uma tupla de índices (i_0, ..., i_{k-1}),
// onde P = \prod r_j e 0 <= i_j < r_j.
public static class MixedRadixDecomposer
{
    public static long[] Decompose(ulong index, IReadOnlyList<long> radices)
    {
        ArgumentNullException.ThrowIfNull(radices);
        var result = new long[radices.Count];
        var rem = index;
        for (var i = 0; i < radices.Count; i++)
        {
            var r = (ulong)radices[i];
            if (r == 0)
            {
                throw new ArgumentException("Radix não pode ser zero.", nameof(radices));
            }

            result[i] = (long)(rem % r);
            rem /= r;
        }

        return result;
    }

    public static ulong Compose(IReadOnlyList<long> indices, IReadOnlyList<long> radices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(radices);
        if (indices.Count != radices.Count)
        {
            throw new ArgumentException("Dimensões incompatíveis.");
        }

        ulong value = 0;
        ulong multiplier = 1;
        for (var i = 0; i < indices.Count; i++)
        {
            value += (ulong)indices[i] * multiplier;
            multiplier *= (ulong)radices[i];
        }

        return value;
    }
}
