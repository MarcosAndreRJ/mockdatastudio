using System.Text;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Generators;

// Geradores de data (docs/15 §6). Saída em DateTime; o ValueCoercer converte para Date/DateTime da coluna.

// Intervalo [start, end] discretizado na granularidade. Domínio enumerável somente para day (índice = dias).
internal sealed class DateRangeGenerator : IIndexableGenerator
{
    private readonly DateTime _start;
    private readonly DateTime _end;
    private readonly long _unitTicks;
    private readonly long _count;
    private readonly bool _dayGranularity;
    private readonly HashSet<int>? _weekdays;
    private readonly (int Start, int End)? _hoursRange;

    public DateRangeGenerator(DateTime start, DateTime end, string granularity, IReadOnlyList<int>? weekdays = null, string? hours = null)
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
        _end = end;
        _count = (end - _start).Ticks / _unitTicks + 1;

        if (weekdays is { Count: > 0 })
        {
            _weekdays = [.. weekdays];
        }

        if (!string.IsNullOrWhiteSpace(hours))
        {
            var parts = hours.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var sh) && int.TryParse(parts[1], out var eh))
            {
                _hoursRange = (Math.Clamp(sh, 0, 23), Math.Clamp(eh, 0, 23));
            }
        }

        Domain = new ValueDomain(_count, _dayGranularity && _weekdays is null && _hoursRange is null);
    }

    public ValueDomain Domain { get; }

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        if (_weekdays is null && _hoursRange is null)
        {
            return _start.AddTicks(cell.Rng.NextInt64(0, _count) * _unitTicks);
        }

        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidate = _start.AddTicks(cell.Rng.NextInt64(0, _count) * _unitTicks);
            if (Matches(candidate))
            {
                return candidate;
            }
        }

        var fallback = _start.AddTicks(cell.Rng.NextInt64(0, _count) * _unitTicks);
        return Adjust(fallback);
    }

    public object? ValueAt(long index) => _start.AddTicks(index * _unitTicks);

    private bool Matches(DateTime dt)
    {
        if (_weekdays is not null)
        {
            var isoWeekday = dt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dt.DayOfWeek;
            if (!_weekdays.Contains(isoWeekday))
            {
                return false;
            }
        }

        if (_hoursRange is { } range)
        {
            if (dt.Hour < range.Start || dt.Hour > range.End)
            {
                return false;
            }
        }

        return true;
    }

    private DateTime Adjust(DateTime dt)
    {
        var result = dt;
        if (_weekdays is not null)
        {
            for (var i = 0; i < 7; i++)
            {
                var iso = result.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)result.DayOfWeek;
                if (_weekdays.Contains(iso))
                {
                    break;
                }

                result = result.AddDays(1);
            }
        }

        if (_hoursRange is { } range)
        {
            var h = Math.Clamp(result.Hour, range.Start, range.End);
            result = result.Date.AddHours(h).AddMinutes(result.Minute).AddSeconds(result.Second);
        }

        if (result > _end)
        {
            result = _end;
        }

        return result;
    }
}

// core.date.sequence: start + row·step (anos, meses, dias e tempo via IsoDuration).
internal sealed class DateSequenceGenerator(DateTime start, IsoDuration step) : IIndexableGenerator
{
    public ValueDomain Domain { get; } = new(null, true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell) => ValueAt(cell.RowIndex);

    public object? ValueAt(long index)
    {
        var dt = start;
        if (step.Years != 0)
        {
            dt = dt.AddYears(checked((int)(index * step.Sign * step.Years)));
        }

        if (step.Months != 0)
        {
            dt = dt.AddMonths(checked((int)(index * step.Sign * step.Months)));
        }

        if (step.Days != 0)
        {
            dt = dt.AddDays(checked(index * step.Sign * step.Days));
        }

        if (step.Time != TimeSpan.Zero)
        {
            var ticks = checked(index * (step.Sign < 0 ? -step.Time.Ticks : step.Time.Ticks));
            dt = dt.AddTicks(ticks);
        }

        return dt;
    }
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

    public int MinLength => minLength;

    public int MaxLength => maxLength;

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
