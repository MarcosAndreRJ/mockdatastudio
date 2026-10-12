using System.Text;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Generators;

// Geradores de data (docs/15 §6). Saída em DateTime; o ValueCoercer converte para Date/DateTime da coluna.

// Intervalo [start, end] discretizado na granularidade. Domínio enumerável somente para day (índice = dias).
internal sealed class DateRangeGenerator : IIndexableGenerator
{
    private readonly DateTime _start;
    private readonly long _unitTicks;
    private readonly long _count;
    private readonly bool _dayGranularity;

    public DateRangeGenerator(DateTime start, DateTime end, string granularity)
    {
        _unitTicks = granularity switch
        {
            "day" => TimeSpan.TicksPerDay,
            "hour" => TimeSpan.TicksPerHour,
            "minute" => TimeSpan.TicksPerMinute,
            "second" => TimeSpan.TicksPerSecond,
            _ => TimeSpan.TicksPerMillisecond,
        };
        _dayGranularity = granularity == "day";
        _start = _dayGranularity ? start.Date : start;
        _count = (end - _start).Ticks / _unitTicks + 1;
        Domain = new ValueDomain(_count, _dayGranularity);
    }

    public ValueDomain Domain { get; }

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) =>
        _start.AddTicks(cell.Rng.NextInt64(0, _count) * _unitTicks);

    public object? ValueAt(long index) => _start.AddTicks(index * _unitTicks);
}

// core.date.sequence: start + row·(dias + tempo). Passos com anos/meses não são suportados nesta versão.
internal sealed class DateSequenceGenerator(DateTime start, long days, TimeSpan time) : IIndexableGenerator
{
    public ValueDomain Domain { get; } = new(null, true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => ValueAt(cell.RowIndex);

    public object? ValueAt(long index) => start.AddDays(checked(index * days)).Add(TimeSpan.FromTicks(checked(index * time.Ticks)));
}

// core.random.string: comprimento uniforme em [min, max]; charset alpha|alnum|numeric|hex.
internal sealed class RandomStringGenerator(int minLength, int maxLength, string charset) : IValueGenerator
{
    private const string Alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string Numeric = "0123456789";
    private const string Hex = "0123456789abcdef";

    private readonly string _alphabet = charset switch
    {
        "alpha" => Alpha,
        "numeric" => Numeric,
        "hex" => Hex,
        _ => Alpha + Numeric,
    };

    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        var length = (int)cell.Rng.NextInt64(minLength, maxLength + 1);
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(_alphabet[(int)cell.Rng.NextInt64(0, _alphabet.Length)]);
        }

        return sb.ToString();
    }
}

// core.mask (docs/15 §6): # dígito, A maiúscula, a minúscula, X alfanumérico maiúsculo, ? letra, \ escapa o próximo caractere.
// Domínio = produto das cardinalidades por posição (indexável quando cabe em long).
internal sealed class MaskGenerator : IIndexableGenerator
{
    private readonly List<string> _positions = [];
    private readonly List<string> _literals = [];

    public MaskGenerator(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '\\' && i + 1 < pattern.Length)
            {
                _positions.Add(string.Empty);
                _literals.Add(pattern[++i].ToString());
                continue;
            }

            _positions.Add(Alphabet(ch));
            _literals.Add(string.Empty);
        }

        long? size = 1;
        foreach (var set in _positions)
        {
            if (set.Length == 0)
            {
                continue;
            }

            size = size is { } s && s <= long.MaxValue / set.Length ? s * set.Length : null;
        }

        Domain = new ValueDomain(size, size is not null);
    }

    public ValueDomain Domain { get; }

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        var sb = new StringBuilder(_positions.Count);
        for (var i = 0; i < _positions.Count; i++)
        {
            if (_positions[i].Length == 0)
            {
                sb.Append(_literals[i]);
                continue;
            }

            sb.Append(_positions[i][(int)cell.Rng.NextInt64(0, _positions[i].Length)]);
        }

        return sb.ToString();
    }

    // Decodificação em base mista (ordem da máscara da esquerda para a direita).
    public object? ValueAt(long index)
    {
        var parts = new List<string>(_positions.Count);
        var rest = index;
        for (var i = _positions.Count - 1; i >= 0; i--)
        {
            if (_positions[i].Length == 0)
            {
                parts.Add(_literals[i]);
                continue;
            }

            var radix = _positions[i].Length;
            parts.Add(_positions[i][(int)(rest % radix)].ToString());
            rest /= radix;
        }

        parts.Reverse();
        return string.Concat(parts);
    }

    private static string Alphabet(char ch) => ch switch
    {
        '#' => "0123456789",
        'A' => "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        'a' => "abcdefghijklmnopqrstuvwxyz",
        'X' => "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",
        '?' => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz",
        _ => ch.ToString(),
    };
}
