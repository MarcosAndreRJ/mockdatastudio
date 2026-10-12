using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MockDataStudio.Core.Common;

/// <summary>
/// Utilitários para validação de argumentos e pré-condições.
/// </summary>
public static class Guard
{
    public static T NotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        return value ?? throw new ArgumentNullException(paramName);
    }

    public static string NotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        return value;
    }

    public static int NotNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
        }

        return value;
    }

    public static long NotNegative(long value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
        }

        return value;
    }
}
