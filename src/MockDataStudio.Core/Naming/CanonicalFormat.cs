using System.Text;

namespace MockDataStudio.Core.Naming;

// Formatação e leitura da forma textual canônica (08 §2.1): partes separadas por "." e
// envolvidas em aspas duplas quando contêm ".", """, espaço ou caractere fora de [A-Za-z0-9_$].
internal static class CanonicalFormat
{
    public static string QuotePart(string part)
    {
        ArgumentException.ThrowIfNullOrEmpty(part);

        if (!NeedsQuotes(part))
        {
            return part;
        }

        return "\"" + part.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string Join(params string[] parts) =>
        string.Join('.', parts.Select(QuotePart));

    public static IReadOnlyList<string> Split(string canonicalId)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonicalId);

        var parts = new List<string>();
        var index = 0;

        while (true)
        {
            var (part, next) = ReadPart(canonicalId, index);
            parts.Add(part);

            if (next == canonicalId.Length)
            {
                return parts;
            }

            // next aponta para o separador "."
            index = next + 1;
        }
    }

    private static bool NeedsQuotes(string part)
    {
        foreach (var c in part)
        {
            if (c is '.' or '"' or ' ')
            {
                return true;
            }

            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '$'))
            {
                return true;
            }
        }

        return false;
    }

    // Retorna a parte lida (já sem aspas) e o índice do próximo separador (ou o fim da cadeia).
    private static (string Part, int Next) ReadPart(string text, int start)
    {
        if (start >= text.Length)
        {
            throw new FormatException("Identificador canônico vazio.");
        }

        if (text[start] != '"')
        {
            var end = text.IndexOf('.', start);
            if (end < 0)
            {
                end = text.Length;
            }

            var raw = text[start..end];
            if (raw.Length == 0)
            {
                throw new FormatException("Parte vazia no identificador canônico.");
            }

            return (raw, end);
        }

        var sb = new StringBuilder();
        var i = start + 1;
        while (true)
        {
            if (i >= text.Length)
            {
                throw new FormatException("Aspa de fechamento ausente no identificador canônico.");
            }

            if (text[i] == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }

                // Fim da parte citada: deve ser fim da cadeia ou separador.
                var next = i + 1;
                if (next < text.Length && text[next] != '.')
                {
                    throw new FormatException("Caractere inesperado após parte citada.");
                }

                if (sb.Length == 0)
                {
                    throw new FormatException("Parte vazia no identificador canônico.");
                }

                return (sb.ToString(), next);
            }

            sb.Append(text[i]);
            i++;
        }
    }
}
