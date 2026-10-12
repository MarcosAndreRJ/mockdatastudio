using System.Text;

namespace MockDataStudio.Core.Hashing;

// Normalização de expressões SQL (defaults, CHECKs, colunas geradas, predicados) para o hash estrutural (12 §6).
// Varredura simples ciente de literais ('...'), identificadores citados ("...") e crases (`...`).
// Não há parser SQL aqui: Core não referencia SqlTooling (AR-01).
public static class ExpressionNormalizer
{
    public static string Normalize(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        var pieces = Tokenize(expression);
        var text = Collapse(pieces);
        text = StripOuterParentheses(text);
        return text;
    }

    // Cada caractere marcado com se está dentro de literal/identificador citado.
    private static List<(char C, bool Quoted)> Tokenize(string expression)
    {
        var pieces = new List<(char C, bool Quoted)>(expression.Length);
        var i = 0;

        while (i < expression.Length)
        {
            var c = expression[i];

            if (c is '\'' or '"' or '`')
            {
                var end = FindClosingQuote(expression, i);
                for (var k = i; k < end; k++)
                {
                    pieces.Add((expression[k], true));
                }

                i = end;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pieces.Add((' ', false));
            }
            else
            {
                pieces.Add((char.ToLowerInvariant(c), false));
            }

            i++;
        }

        return pieces;
    }

    // Retorna o índice logo após a aspa de fechamento (duplicação de aspa é escape).
    private static int FindClosingQuote(string text, int start)
    {
        var quote = text[start];
        var i = start + 1;

        while (i < text.Length)
        {
            if (text[i] == quote)
            {
                if (i + 1 < text.Length && text[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        // Literal não fechado: trata o restante como citado.
        return text.Length;
    }

    private static string Collapse(List<(char C, bool Quoted)> pieces)
    {
        var sb = new StringBuilder(pieces.Count);
        var previousUnquoted = '\0';
        var pendingSpace = false;

        for (var i = 0; i < pieces.Count; i++)
        {
            var (c, quoted) = pieces[i];

            if (!quoted && c == ' ')
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                pendingSpace = false;
                // Espaço é removido ao lado de "(", ")", "," (fora de aspas), ou se já houver espaço anterior.
                var nextUnquoted = NextUnquoted(pieces, i);
                var dropped = !quoted && c is '(' or ')' or ','
                    || previousUnquoted is '(' or ')' or ',' || nextUnquoted is '(' or ')' or ','
                    || sb.Length == 0;
                if (!dropped)
                {
                    sb.Append(' ');
                }
            }

            sb.Append(c);
            if (!quoted)
            {
                previousUnquoted = c;
            }
            else
            {
                previousUnquoted = '\0';
            }
        }

        return sb.ToString().Trim();
    }

    // Caractere após um espaço pendente, se estiver fora de aspas; senão, '\0'.
    private static char NextUnquoted(List<(char C, bool Quoted)> pieces, int index) =>
        pieces[index].Quoted ? '\0' : pieces[index].C;

    // Remove um único par de parênteses externos que envolve a expressão inteira.
    private static string StripOuterParentheses(string text)
    {
        if (text.Length < 2 || text[0] != '(' || text[^1] != ')')
        {
            return text;
        }

        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0 && i != text.Length - 1)
                {
                    return text;
                }
            }
        }

        return text[1..^1].Trim();
    }
}
