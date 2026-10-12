using System.Globalization;
using System.Resources;

namespace MockDataStudio.Core.Common;

/// <summary>
/// Acesso às mensagens de recursos localizadas do núcleo.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager ResourceMan =
        new("MockDataStudio.Core.Resources.Strings", typeof(Strings).Assembly);

    public static ResourceManager ResourceManager => ResourceMan;

    public static string? GetString(string name, CultureInfo? culture = null)
        => ResourceMan.GetString(name, culture ?? CultureInfo.CurrentUICulture);

    public static string GetErrorMessage(string code, CultureInfo? culture = null)
        => GetString(code, culture) ?? $"Erro não catalogado ({code}).";
}
