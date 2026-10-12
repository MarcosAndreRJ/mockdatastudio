using System.Globalization;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Generation.Coercion;

// Coerção do valor do gerador para o tipo canônico da coluna (docs/15 §5, C05 §4.1). Nunca trunca em silêncio.
public static partial class ValueCoercer
{
    private static readonly HashSet<string> TrueWords = new(StringComparer.OrdinalIgnoreCase) { "true", "1", "s", "sim", "y" };
    private static readonly HashSet<string> FalseWords = new(StringComparer.OrdinalIgnoreCase) { "false", "0", "n", "nao", "não" };

    public static Result<object?> Coerce(object? value, DataTypeInfo type, CoerceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        options ??= CoerceOptions.Default;
        if (value is null)
        {
            return Result<object?>.Ok(null);
        }

        return type.LogicalType switch
        {
            LogicalType.Boolean => CoerceBoolean(value),
            LogicalType.Integer => CoerceInteger(value),
            LogicalType.Decimal => CoerceDecimal(value),
            LogicalType.Float => CoerceFloat(value),
            LogicalType.String or LogicalType.Text or LogicalType.Enum or LogicalType.Json => CoerceText(value, type, options),
            LogicalType.Date => CoerceDate(value),
            LogicalType.DateTime => CoerceDateTime(value),
            LogicalType.DateTimeTz => CoerceDateTimeTz(value),
            LogicalType.Time => CoerceTime(value),
            LogicalType.Uuid => CoerceUuid(value),
            _ => Result<object?>.Ok(value),
        };
    }

    private static Result<object?> CoerceBoolean(object value)
    {
        switch (value)
        {
            case bool b:
                return Result<object?>.Ok(b);
            case long n when n is 0 or 1:
                return Result<object?>.Ok(n == 1);
            case string s when TrueWords.Contains(s.Trim()):
                return Result<object?>.Ok(true);
            case string s when FalseWords.Contains(s.Trim()):
                return Result<object?>.Ok(false);
            default:
                return Fail($"Valor '{value}' não é booleano.");
        }
    }

    private static Result<object?> CoerceInteger(object value)
    {
        switch (value)
        {
            case long n:
                return Result<object?>.Ok(n);
            case int i:
                return Result<object?>.Ok((long)i);
            case bool b:
                return Result<object?>.Ok(b ? 1L : 0L);
            case decimal m:
                return RoundToLong(decimal.Round(m, 0, MidpointRounding.ToEven));
            case double d when double.IsFinite(d):
                return RoundToLong((decimal)Math.Round(d, 0, MidpointRounding.ToEven));
            case string s:
                var digits = KeepNumeric(s, allowDecimal: false);
                return long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                    ? Result<object?>.Ok(parsed)
                    : Fail($"Texto '{s}' não converte para inteiro (vazio ou fora da faixa).");
            default:
                return Fail($"Tipo {value.GetType().Name} não converte para inteiro.");
        }
    }

    private static Result<object?> CoerceDecimal(object value)
    {
        switch (value)
        {
            case decimal m:
                return Result<object?>.Ok(m);
            case long n:
                return Result<object?>.Ok((decimal)n);
            case double d when double.IsFinite(d):
                return Result<object?>.Ok((decimal)d);
            case string s:
                var text = KeepNumeric(s, allowDecimal: true);
                return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                    ? Result<object?>.Ok(parsed)
                    : Fail($"Texto '{s}' não converte para decimal.");
            default:
                return Fail($"Tipo {value.GetType().Name} não converte para decimal.");
        }
    }

    private static Result<object?> CoerceFloat(object value)
    {
        switch (value)
        {
            case double d:
                return Result<object?>.Ok(d);
            case long or decimal:
                return Result<object?>.Ok(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            case string s:
                var text = KeepNumeric(s, allowDecimal: true);
                return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                    ? Result<object?>.Ok(parsed)
                    : Fail($"Texto '{s}' não converte para real.");
            default:
                return Fail($"Tipo {value.GetType().Name} não converte para real.");
        }
    }

    // Texto: formata números em invariante; aplica zeros à esquerda (padLeft) e recusa exceder o comprimento.
    private static Result<object?> CoerceText(object value, DataTypeInfo type, CoerceOptions options)
    {
        var text = value switch
        {
            string s => s,
            long n when options.PadLeft is { } width => n.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'),
            long n => n.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Guid g => g.ToString(),
            _ => value.ToString() ?? string.Empty,
        };

        if (type.Length is { } max && text.Length > max)
        {
            if (!options.Truncate)
            {
                return Fail($"Valor de {text.Length} caracteres excede o comprimento {max} da coluna.");
            }

            text = text[..max];
        }

        return Result<object?>.Ok(text);
    }

    private static Result<object?> CoerceDate(object value) => value switch
    {
        DateOnly date => Result<object?>.Ok(date),
        DateTime dt => Result<object?>.Ok(DateOnly.FromDateTime(dt)),
        string s when DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            => Result<object?>.Ok(d),
        _ => Fail($"Valor '{value}' não é data ISO (yyyy-MM-dd)."),
    };

    private static Result<object?> CoerceDateTime(object value)
    {
        switch (value)
        {
            case DateTime dt:
                return Result<object?>.Ok(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified));
            case DateTimeOffset dto:
                return Result<object?>.Ok(dto.DateTime);
            case DateOnly date:
                return Result<object?>.Ok(date.ToDateTime(TimeOnly.MinValue));
            case string s:
                var trimmed = s.Trim();
                foreach (var format in new[] { "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" })
                {
                    if (DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        return Result<object?>.Ok(parsed);
                    }
                }

                return Fail($"Valor '{s}' não é data/hora ISO.");
            default:
                return Fail($"Tipo {value.GetType().Name} não converte para data/hora.");
        }
    }

    private static Result<object?> CoerceDateTimeTz(object value) => value switch
    {
        DateTimeOffset dto => Result<object?>.Ok(dto),
        string s when DateTimeOffset.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            => Result<object?>.Ok(parsed),
        _ => Fail($"Valor '{value}' não é data/hora com fuso."),
    };

    private static Result<object?> CoerceTime(object value) => value switch
    {
        TimeOnly time => Result<object?>.Ok(time),
        string s when TimeOnly.TryParseExact(s.Trim(), "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            => Result<object?>.Ok(t),
        _ => Fail($"Valor '{value}' não é hora (HH:mm:ss)."),
    };

    private static Result<object?> CoerceUuid(object value) => value switch
    {
        Guid g => Result<object?>.Ok(g),
        string s when Guid.TryParse(s.Trim(), out var parsed) => Result<object?>.Ok(parsed),
        _ => Fail($"Valor '{value}' não é GUID."),
    };

    // Mantém dígitos, sinal e separador decimal (docs/15 §5: "01310-100" → "1310100").
    // Só o sinal inicial é mantido; qualquer outro caractere não numérico (inclusive '-' no meio) é removido.
    private static string KeepNumeric(string text, bool allowDecimal)
    {
        var trimmed = text.Trim();
        var sign = trimmed.Length > 0 && (trimmed[0] == '+' || trimmed[0] == '-') ? trimmed[0].ToString() : string.Empty;
        var pattern = allowDecimal ? "[^0-9.]" : "[^0-9]";
        return sign + Regex.Replace(trimmed, pattern, string.Empty);
    }

    private static Result<object?> RoundToLong(decimal value) =>
        value is >= long.MinValue and <= long.MaxValue
            ? Result<object?>.Ok((long)value)
            : Fail($"Valor {value} fora da faixa do inteiro.");

    private static Result<object?> Fail(string message) =>
        Result<object?>.Fail(ErrorCodes.GenCoerce, message);
}

// Opções de coerção por binding (15 §5): padLeft para char(n) e truncate explícito.
public sealed record CoerceOptions(int? PadLeft = null, bool Truncate = false)
{
    public static CoerceOptions Default { get; } = new();
}
