using System.Text;

namespace MockDataStudio.Application.Explorer;

// Formatador heurístico (C6, TASK-13): palavras-chave em maiúsculas, quebra antes das cláusulas principais e de JOIN,
// AND/OR indentados e uma coluna por linha na lista do SELECT. Literais, identificadores e comentários não mudam.
// Não é um parser: não reescreve expressões. Aplicá-lo duas vezes dá o mesmo resultado.
public static class SqlFormatter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP", "ORDER", "BY", "HAVING", "LIMIT", "OFFSET", "UNION", "ALL", "INTERSECT", "EXCEPT",
        "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON", "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE",
        "BETWEEN", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END", "AS", "DISTINCT", "INSERT", "INTO", "VALUES", "UPDATE",
        "SET", "DELETE", "WITH", "RETURNING", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "PRIMARY", "KEY",
        "FOREIGN", "REFERENCES", "UNIQUE", "DEFAULT", "CHECK", "CONSTRAINT", "TRUE", "FALSE", "ASC", "DESC", "USING",
    };

    // Cláusulas que começam em nova linha, sem recuo. As de duas palavras são casadas antes das de uma.
    private static readonly HashSet<string> TwoWordClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "GROUP BY", "ORDER BY", "INSERT INTO", "DELETE FROM", "UNION ALL",
    };

    private static readonly HashSet<string> OneWordClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "HAVING", "LIMIT", "OFFSET", "UNION", "INTERSECT", "EXCEPT", "VALUES", "SET",
        "RETURNING", "UPDATE", "WITH",
    };

    private static readonly HashSet<string> JoinWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "JOIN",
    };

    public static string Format(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var tokens = Tokenize(sql);
        var output = new StringBuilder();
        var clause = string.Empty;
        var depth = 0;
        var lineStart = true;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var upper = token.Text.ToUpperInvariant();

            if (token.Kind == TokenKind.Comment)
            {
                BreakIfNeeded(output, ref lineStart);
                output.Append(token.Text);
                if (token.Text.StartsWith("--", StringComparison.Ordinal))
                {
                    output.Append('\n');
                    lineStart = true;
                }
                else
                {
                    output.Append(' ');
                    lineStart = false;
                }

                continue;
            }

            if (token.Kind == TokenKind.Word && depth == 0)
            {
                // Cláusula de duas palavras (GROUP BY, ...).
                if (i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Word)
                {
                    var pair = upper + " " + tokens[i + 1].Text.ToUpperInvariant();
                    if (TwoWordClauses.Contains(pair))
                    {
                        clause = pair;
                        NewClause(output, ref lineStart);
                        output.Append(pair);
                        i++;
                        lineStart = false;
                        continue;
                    }
                }

                if (OneWordClauses.Contains(upper))
                {
                    clause = upper;
                    NewClause(output, ref lineStart);
                    output.Append(upper);
                    lineStart = false;
                    continue;
                }

                // JOIN e variações (LEFT OUTER JOIN, INNER JOIN, ...): só se a sequência termina em JOIN.
                if (JoinWords.Contains(upper))
                {
                    var end = i;
                    while (end < tokens.Count && tokens[end].Kind == TokenKind.Word && JoinWords.Contains(tokens[end].Text))
                    {
                        if (tokens[end].Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        end++;
                    }

                    if (end < tokens.Count && tokens[end].Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase))
                    {
                        clause = "JOIN";
                        NewClause(output, ref lineStart);
                        output.Append(string.Join(" ", tokens.Skip(i).Take(end - i + 1).Select(t => t.Text.ToUpperInvariant())));
                        i = end;
                        lineStart = false;
                        continue;
                    }
                }

                if (upper is "AND" or "OR")
                {
                    BreakIfNeeded(output, ref lineStart);
                    output.Append("  ").Append(upper);
                    lineStart = false;
                    continue;
                }

                if (upper == "ON")
                {
                    BreakIfNeeded(output, ref lineStart);
                    output.Append("  ON");
                    lineStart = false;
                    continue;
                }
            }

            if (token.Kind == TokenKind.Punct)
            {
                switch (token.Text)
                {
                    case "(":
                        Space(output, tokens, i, ref lineStart);
                        output.Append('(');
                        depth++;
                        continue;
                    case ")":
                        depth = Math.Max(0, depth - 1);
                        output.Append(')');
                        lineStart = false;
                        continue;
                    case ",":
                        output.Append(',');
                        if (clause == "SELECT" && depth == 0)
                        {
                            output.Append("\n  ");
                            lineStart = false;
                        }
                        else
                        {
                            output.Append(' ');
                            lineStart = false;
                        }

                        continue;
                    case ";":
                        output.Append(';').Append('\n');
                        clause = string.Empty;
                        depth = 0;
                        lineStart = true;
                        continue;
                }
            }

            Space(output, tokens, i, ref lineStart);
            output.Append(token.Kind == TokenKind.Word && Keywords.Contains(upper) ? upper : token.Text);
            lineStart = false;
        }

        return output.ToString().TrimEnd();
    }

    private static void NewClause(StringBuilder output, ref bool lineStart)
    {
        if (output.Length > 0 && !lineStart)
        {
            output.Append('\n');
        }

        lineStart = true;
    }

    private static void BreakIfNeeded(StringBuilder output, ref bool lineStart)
    {
        if (output.Length > 0 && !lineStart)
        {
            output.Append('\n');
        }

        lineStart = true;
    }

    // Espaço entre tokens: nenhum depois de '(' ou '\n' e antes de ')', ',', '.' ou ';'; nenhum entre função e '('.
    private static void Space(StringBuilder output, List<Token> tokens, int index, ref bool lineStart)
    {
        if (lineStart)
        {
            lineStart = false;
            return;
        }

        if (output.Length == 0)
        {
            return;
        }

        var previous = output[^1];
        if (previous is '(' or '\n' or ' ')
        {
            return;
        }

        var token = tokens[index];
        if (token.Kind == TokenKind.Punct && token.Text is ")" or "," or ";")
        {
            return;
        }

        if (token.Kind == TokenKind.Punct && token.Text == "(" && index > 0)
        {
            var before = tokens[index - 1];
            if (before.Kind == TokenKind.Word && !Keywords.Contains(before.Text))
            {
                return;
            }
        }

        output.Append(' ');
    }

    private enum TokenKind { Word, Literal, Punct, Operator, Comment }

    private sealed record Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var end = sql.IndexOf('\n', i);
                end = end < 0 ? sql.Length : end;
                tokens.Add(new Token(TokenKind.Comment, sql[i..end].TrimEnd()));
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + 2;
                tokens.Add(new Token(TokenKind.Comment, sql[i..end]));
                i = end;
                continue;
            }

            if (c == '\'')
            {
                var end = ReadQuoted(sql, i, '\'');
                tokens.Add(new Token(TokenKind.Literal, sql[i..end]));
                i = end;
                continue;
            }

            if (c is '"' or '`' or '[')
            {
                var end = ReadQuoted(sql, i, c == '[' ? ']' : c);
                tokens.Add(new Token(TokenKind.Word, sql[i..end]));
                i = end;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c is '_' or '@' or '$')
            {
                var start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '@' or '$' or '.'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Word, sql[start..i]));
                continue;
            }

            if (c is '(' or ')' or ',' or ';')
            {
                tokens.Add(new Token(TokenKind.Punct, c.ToString()));
                i++;
                continue;
            }

            var opStart = i;
            while (i < sql.Length && "<>=!|+-*/%&^~:.".Contains(sql[i], StringComparison.Ordinal))
            {
                i++;
            }

            if (i == opStart)
            {
                i++;
            }

            tokens.Add(new Token(TokenKind.Operator, sql[opStart..i]));
        }

        return tokens;
    }

    // Literal ou identificador entre aspas. A aspa dobrada ('') é escape e não fecha o token. Colchetes não têm escape.
    private static int ReadQuoted(string sql, int start, char close)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == close)
            {
                if (close != ']' && i + 1 < sql.Length && sql[i + 1] == close)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return sql.Length;
    }
}
