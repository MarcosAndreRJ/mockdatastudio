using System.Globalization;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Generation.Generators;

// Algoritmos de cálculo e validação de documentos brasileiros (docs/15 §6, FR-076).
public static class BrazilDocUtils
{
    private static readonly int[] CnpjWeights1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjWeights2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static (int D1, int D2) ComputeCpfDigits(long base9)
    {
        Span<int> digits = stackalloc int[9];
        var rem = base9;
        for (var i = 8; i >= 0; i--)
        {
            digits[i] = (int)(rem % 10);
            rem /= 10;
        }

        var sum1 = 0;
        for (var i = 0; i < 9; i++)
        {
            sum1 += digits[i] * (10 - i);
        }

        var mod1 = sum1 % 11;
        var d1 = mod1 < 2 ? 0 : 11 - mod1;

        var sum2 = 0;
        for (var i = 0; i < 9; i++)
        {
            sum2 += digits[i] * (11 - i);
        }

        sum2 += d1 * 2;
        var mod2 = sum2 % 11;
        var d2 = mod2 < 2 ? 0 : 11 - mod2;

        return (d1, d2);
    }

    public static string FormatCpf(long base9, int d1, int d2, bool formatted)
    {
        var raw = string.Create(CultureInfo.InvariantCulture, $"{base9:D9}{d1}{d2}");
        if (!formatted)
        {
            return raw;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{raw.AsSpan(0, 3)}.{raw.AsSpan(3, 3)}.{raw.AsSpan(6, 3)}-{raw.AsSpan(9, 2)}");
    }

    public static (int D1, int D2) ComputeCnpjDigits(long base8, int branch4)
    {
        Span<int> digits = stackalloc int[12];
        var rem = branch4;
        for (var i = 11; i >= 8; i--)
        {
            digits[i] = rem % 10;
            rem /= 10;
        }

        rem = (int)base8;
        for (var i = 7; i >= 0; i--)
        {
            digits[i] = rem % 10;
            rem /= 10;
        }

        var sum1 = 0;
        for (var i = 0; i < 12; i++)
        {
            sum1 += digits[i] * CnpjWeights1[i];
        }

        var mod1 = sum1 % 11;
        var d1 = mod1 < 2 ? 0 : 11 - mod1;

        var sum2 = 0;
        for (var i = 0; i < 12; i++)
        {
            sum2 += digits[i] * CnpjWeights2[i];
        }

        sum2 += d1 * 2;
        var mod2 = sum2 % 11;
        var d2 = mod2 < 2 ? 0 : 11 - mod2;

        return (d1, d2);
    }

    public static string FormatCnpj(long base8, int branch4, int d1, int d2, bool formatted)
    {
        var raw = string.Create(CultureInfo.InvariantCulture, $"{base8:D8}{branch4:D4}{d1}{d2}");
        if (!formatted)
        {
            return raw;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{raw.AsSpan(0, 2)}.{raw.AsSpan(2, 3)}.{raw.AsSpan(5, 3)}/{raw.AsSpan(8, 4)}-{raw.AsSpan(12, 2)}");
    }

    public static bool IsValidCpf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Span<char> digits = stackalloc char[11];
        var count = 0;
        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
            {
                if (count >= 11)
                {
                    return false;
                }

                digits[count++] = ch;
            }
        }

        if (count != 11)
        {
            return false;
        }

        // Não aceita repetição de mesmo dígito
        var allEqual = true;
        for (var i = 1; i < 11; i++)
        {
            if (digits[i] != digits[0])
            {
                allEqual = false;
                break;
            }
        }

        if (allEqual)
        {
            return false;
        }

        long base9 = 0;
        for (var i = 0; i < 9; i++)
        {
            base9 = base9 * 10 + (digits[i] - '0');
        }

        var (d1, d2) = ComputeCpfDigits(base9);
        return digits[9] - '0' == d1 && digits[10] - '0' == d2;
    }

    public static bool IsValidCnpj(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Span<char> digits = stackalloc char[14];
        var count = 0;
        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
            {
                if (count >= 14)
                {
                    return false;
                }

                digits[count++] = ch;
            }
        }

        if (count != 14)
        {
            return false;
        }

        long base8 = 0;
        for (var i = 0; i < 8; i++)
        {
            base8 = base8 * 10 + (digits[i] - '0');
        }

        var branch4 = 0;
        for (var i = 8; i < 12; i++)
        {
            branch4 = branch4 * 10 + (digits[i] - '0');
        }

        var (d1, d2) = ComputeCnpjDigits(base8, branch4);
        return digits[12] - '0' == d1 && digits[13] - '0' == d2;
    }
}

// br.cpf (docs/15 §6): ~10^9 bases únicas bijetivas, DV válido e sem repetição de dígitos (AC-076.1).
internal sealed class CpfGenerator(bool formatted) : IIndexableGenerator
{
    // Exclui as 10 bases com todos os dígitos iguais (000000000, 111111111, ..., 999999999)
    public const long Cardinality = 1_000_000_000L - 10L; // 999.999.990
    private const long InvalidStep = 111_111_111L;

    public ValueDomain Domain { get; } = new(Cardinality, true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? ValueAt(long index)
    {
        if (index < 0 || index >= Cardinality)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Índice de CPF fora do domínio [0, 999999990).");
        }

        // Pula deterministicamente as 10 bases inválidas
        var skip = (index / (InvalidStep - 1)) + 1;
        var base9 = index + skip;

        var (d1, d2) = BrazilDocUtils.ComputeCpfDigits(base9);
        return BrazilDocUtils.FormatCpf(base9, d1, d2, formatted);
    }

    public object? Generate(ref GenerationCell cell)
    {
        var idx = cell.Rng.NextInt64(0, Cardinality);
        return ValueAt(idx);
    }
}

// br.cnpj (docs/15 §6): 10^8 bases por filial, DV válido.
internal sealed class CnpjGenerator(bool formatted, int branch) : IIndexableGenerator
{
    public const long Cardinality = 100_000_000L;

    public ValueDomain Domain { get; } = new(Cardinality, true);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? ValueAt(long index)
    {
        if (index < 0 || index >= Cardinality)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Índice de CNPJ fora do domínio [0, 10^8).");
        }

        var (d1, d2) = BrazilDocUtils.ComputeCnpjDigits(index, branch);
        return BrazilDocUtils.FormatCnpj(index, branch, d1, d2, formatted);
    }

    public object? Generate(ref GenerationCell cell)
    {
        var base8 = cell.Rng.NextInt64(0, Cardinality);
        return ValueAt(base8);
    }
}
