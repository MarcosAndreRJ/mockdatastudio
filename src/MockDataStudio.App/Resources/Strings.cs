using System.Globalization;
using System.Resources;

namespace MockDataStudio.App.Resources;

// Acesso tipado aos textos de Strings.resx (pt-BR padrão, ver DA-026).
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("MockDataStudio.App.Resources.Strings", typeof(Strings).Assembly);

    public static string NavHome => Get(nameof(NavHome));
    public static string NavConnections => Get(nameof(NavConnections));
    public static string NavExplorer => Get(nameof(NavExplorer));
    public static string NavSqlEditor => Get(nameof(NavSqlEditor));
    public static string NavSchemaDigest => Get(nameof(NavSchemaDigest));
    public static string NavTemplates => Get(nameof(NavTemplates));
    public static string NavMapping => Get(nameof(NavMapping));
    public static string NavGeneration => Get(nameof(NavGeneration));
    public static string NavHistory => Get(nameof(NavHistory));
    public static string NavSettings => Get(nameof(NavSettings));

    public static string ErrorUnexpected(string details) =>
        string.Format(CultureInfo.CurrentCulture, Get(nameof(ErrorUnexpected)), details);

    private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
