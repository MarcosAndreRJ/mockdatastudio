using System.Text.RegularExpressions;

namespace MockDataStudio.Infrastructure.Logging;

/// <summary>
/// Remove segredos de texto livre (mensagens, exceções de drivers) antes de gravar logs (docs/23 §6, T-SEC-004).
/// Cobre pares chave=valor de connection string (password, pwd, senha), formas entre aspas e URIs com credenciais.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "***";

    [GeneratedRegex("""(?<key>\b(?:password|pwd|passwd|senha)\b)(?<sep>\s*[=:]\s*)(?:"[^"]*"|'[^']*'|[^;\s,]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValue();

    // Formas de mensagem de driver: pwd 'segredo', password "segredo".
    [GeneratedRegex("""(?<key>\b(?:password|pwd|passwd|senha)\b)(?<sep>\s+)(?:"[^"]*"|'[^']*')""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedAfterSpace();

    [GeneratedRegex(@"(?<scheme>\b[a-z][a-z0-9+.\-]*://)(?<user>[^:/@\s]+):(?<secret>[^@\s]+)@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriCredentials();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = KeyValue().Replace(text, m => m.Groups["key"].Value + m.Groups["sep"].Value + Mask);
        result = QuotedAfterSpace().Replace(result, m => m.Groups["key"].Value + m.Groups["sep"].Value + Mask);
        return UriCredentials().Replace(result, m => m.Groups["scheme"].Value + m.Groups["user"].Value + ":" + Mask + "@");
    }
}
