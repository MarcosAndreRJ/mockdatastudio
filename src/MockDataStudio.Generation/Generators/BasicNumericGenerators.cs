using System.Globalization;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Generators;

// Geradores básicos e numéricos (docs/15 §6). Funções puras do RNG da célula; sem estado mutável compartilhado.

internal sealed class FixedGenerator(string value) : IValueGenerator
{
    public ValueDomain Domain { get; } = new(1, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => value;
}

internal sealed class NullGenerator : IValueGenerator
{
    public ValueDomain Domain { get; } = new(1, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => null;
}

// core.skip: a coluna não entra no INSERT (o motor a omite; ver GenerationEngine).
internal sealed class SkipGenerator : IValueGenerator
{
    public ValueDomain Domain { get; } = new(0, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => null;
}

internal sealed class BoolGenerator(double probabilityTrue) : IValueGenerator
{
    public ValueDomain Domain { get; } = new(2, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => cell.Rng.NextBool(probabilityTrue);
}

// core.list.random / core.list.sequential (docs/15 §6): valores enumeráveis por índice.
internal sealed class ListGenerator(IReadOnlyList<string> values, IReadOnlyList<double>? weights, bool sequential, int offset)
    : IIndexableGenerator
{
    private readonly WeightedIndex? _weighted = weights is null ? null : new WeightedIndex(weights);

    public ValueDomain Domain { get; } = new(values.Count, true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        if (sequential)
        {
            var index = (cell.RowIndex + offset) % values.Count;
            return values[(int)index];
        }

        var pick = _weighted is null
            ? (int)cell.Rng.NextInt64(0, values.Count)
            : cell.Rng.NextWeightedIndex(_weighted);
        return values[pick];
    }

    public object? ValueAt(long index) => values[(int)index];
}

// core.uuid v4 (docs/15 §6): 122 bits aleatórios do RNG e bits de versão/variante. Tratado como único por construção.
internal sealed class UuidV4Generator : IValueGenerator
{
    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], cell.Rng.NextUInt64());
        BitConverter.TryWriteBytes(bytes[8..], cell.Rng.NextUInt64());
        // Guid grava o terceiro grupo em ordem little-endian: o nibble de versão está no byte 7, não no 6.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

internal sealed class BytesGenerator(int length) : IValueGenerator
{
    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i += 8)
        {
            var word = BitConverter.GetBytes(cell.Rng.NextUInt64());
            Array.Copy(word, 0, bytes, i, Math.Min(8, length - i));
        }

        return bytes;
    }
}

// core.sequence.int: start + row*step (docs/15 §6). Único por construção.
internal sealed class SequenceIntGenerator(long start, long step) : IIndexableGenerator
{
    public ValueDomain Domain { get; } = new(null, true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => unchecked(start + cell.RowIndex * step);

    public object? ValueAt(long index) => unchecked(start + index * step);
}

// core.random.int (distribuição uniforme). Domínio [min, max] enumerável.
internal sealed class RandomIntGenerator(long min, long max) : IIndexableGenerator
{
    public ValueDomain Domain { get; } = new(max - min + 1, true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => cell.Rng.NextInt64(min, max + 1);

    public object? ValueAt(long index) => min + index;
}

// core.random.decimal (uniforme, escala fixa). Domínio = unidades inteiras em [0, (max-min)·10^scale].
internal sealed class RandomDecimalGenerator(decimal min, decimal max, int scale) : IIndexableGenerator
{
    private readonly decimal _factor = (decimal)Math.Pow(10, scale);

    public ValueDomain Domain { get; } = new(DomainSize(min, max, scale), true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => cell.Rng.NextDecimal(min, max, scale);

    public object? ValueAt(long index) => min + index / _factor;

    private static long? DomainSize(decimal min, decimal max, int scale)
    {
        var units = (max - min) * (decimal)Math.Pow(10, scale);
        return units < long.MaxValue ? (long)units + 1 : null;
    }
}

// core.random.float (uniforme em [min, max)).
internal sealed class RandomFloatGenerator(double min, double max) : IValueGenerator
{
    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => min + cell.Rng.NextDouble() * (max - min);
}
