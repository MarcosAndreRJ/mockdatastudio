using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MockDataStudio.Mapping;

// Normalização de nomes de coluna (docs/14 §2, C04). Algoritmo determinístico, sem IA (AR-01).
public sealed partial class NameNormalizer : INameNormalizer
{
    // Dicionário de abreviações embutido no V1 (docs/14 §2 passo 5). Chave: token bruto; valor: forma expandida.
    public static readonly IReadOnlyDictionary<string, string> AbbreviationMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NR"] = "NUM", ["NRO"] = "NUM", ["NUM"] = "NUM", ["NUMERO"] = "NUM",
        ["TEL"] = "TEL", ["FONE"] = "TEL", ["TELEF"] = "TEL",
        ["END"] = "ENDERECO", ["ENDER"] = "ENDERECO",
        ["DT"] = "DATA", ["DAT"] = "DATA",
        ["DS"] = "DESCRICAO", ["DESC"] = "DESCRICAO", ["DESCR"] = "DESCRICAO",
        ["VL"] = "VALOR", ["VLR"] = "VALOR",
        ["QT"] = "QUANTIDADE", ["QTD"] = "QUANTIDADE", ["QTDE"] = "QUANTIDADE",
        ["COD"] = "CODIGO", ["CD"] = "CODIGO",
        ["NASC"] = "NASCIMENTO",
        ["CEL"] = "CELULAR",
        ["LOGR"] = "LOGRADOURO",
        ["COMPL"] = "COMPLEMENTO",
        ["MUN"] = "MUNICIPIO",
        ["RAZ"] = "RAZAO",
        ["FANT"] = "FANTASIA",
    };

    // Prefixos de tipo/húngaros: ruído quando é o primeiro token (docs/14 §2 passo 6).
    private static readonly HashSet<string> PrefixNoise = new(StringComparer.Ordinal)
    {
        "DS", "NM", "VL", "DT", "FL", "ID", "TX", "ST", "IN", "CD", "NR", "NUM", "CODIGO",
    };

    // Sufixos de chave: ruído quando é o último token.
    private static readonly HashSet<string> SuffixNoise = new(StringComparer.Ordinal) { "ID", "FK", "PK" };

    // Tokens expandidos que são ruído (ex.: DATA em DT_NASC, NUM em NRO_CEP).
    private static readonly HashSet<string> ExpandedNoise = new(StringComparer.Ordinal) { "NUM", "DATA" };

    public NormalizedName Normalize(string identifier, string? tableName = null)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var separated = SplitCamelCase(RemoveDiacritics(identifier)).ToUpperInvariant();
        var raw = MergeEmail(TokenRegex().Matches(separated).Select(m => m.Value).ToList());
        var expanded = raw.Select(t => AbbreviationMap.TryGetValue(t, out var e) ? e : t).ToList();

        var tableTokens = tableName is null ? new HashSet<string>(StringComparer.Ordinal) : TableTokens(tableName);
        var noise = new bool[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            if (IsNumeric(raw[i]))
            {
                continue;
            }

            noise[i] = (i == 0 && PrefixNoise.Contains(raw[i]))
                || (i == raw.Count - 1 && i > 0 && SuffixNoise.Contains(raw[i]))
                || ExpandedNoise.Contains(expanded[i])
                || tableTokens.Contains(expanded[i]);
        }

        // Ruído só é removido quando sobra pelo menos um token não ruidoso (docs/14 §2 passo 6: "quando não é o único").
        var meaningful = Enumerable.Range(0, raw.Count).Count(i => !IsNumeric(raw[i]) && !noise[i]);
        if (meaningful == 0)
        {
            Array.Clear(noise);
        }

        var core = new List<string>();
        int? ordinal = null;
        for (var i = 0; i < raw.Count; i++)
        {
            if (IsNumeric(raw[i]))
            {
                ordinal = int.Parse(raw[i], CultureInfo.InvariantCulture);
            }
            else if (!noise[i])
            {
                core.Add(expanded[i]);
            }
        }

        return new NormalizedName(
            Original: identifier,
            Compact: string.Concat(raw),
            ExpandedCompact: string.Concat(expanded),
            Tokens: expanded,
            CoreTokens: core,
            Ordinal: ordinal);
    }

    // Passo 1: NFD, remove marcas diacríticas (Mn), NFC.
    internal static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // Passo 2: separa camelCase/PascalCase, siglas seguidas de palavra e transições letra↔dígito.
    internal static string SplitCamelCase(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var cur = text[i];
            if (i > 0)
            {
                var prev = text[i - 1];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                var lowerToUpper = char.IsLower(prev) && char.IsUpper(cur);
                var acronymEnd = char.IsUpper(prev) && char.IsUpper(cur) && char.IsLower(next);
                var letterDigit = (char.IsLetter(prev) && char.IsDigit(cur)) || (char.IsDigit(prev) && char.IsLetter(cur));
                if (lowerToUpper || acronymEnd || letterDigit)
                {
                    sb.Append('_');
                }
            }

            sb.Append(cur);
        }

        return sb.ToString();
    }

    // Passo 5 (fusão): tokens "E" seguido de "MAIL" viram "EMAIL" (E_MAIL → EMAIL).
    private static List<string> MergeEmail(List<string> tokens)
    {
        var result = new List<string>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "E" && i + 1 < tokens.Count && tokens[i + 1] == "MAIL")
            {
                result.Add("EMAIL");
                i++;
            }
            else
            {
                result.Add(tokens[i]);
            }
        }

        return result;
    }

    // Tokens da tabela (expandidos) e seus singulares: "clientes" também remove "CLIENTE" da coluna.
    private static HashSet<string> TableTokens(string tableName)
    {
        var tokens = TokenRegex().Matches(SplitCamelCase(RemoveDiacritics(tableName)).ToUpperInvariant())
            .Select(m => AbbreviationMap.TryGetValue(m.Value, out var e) ? e : m.Value)
            .ToList();

        var set = new HashSet<string>(tokens, StringComparer.Ordinal);
        foreach (var token in tokens.Where(t => t.Length > 1 && t.EndsWith('S')))
        {
            set.Add(token[..^1]);
        }

        return set;
    }

    private static bool IsNumeric(string token) => token.Length > 0 && token.All(char.IsDigit);

    [GeneratedRegex("[A-Z0-9]+")]
    private static partial Regex TokenRegex();
}
