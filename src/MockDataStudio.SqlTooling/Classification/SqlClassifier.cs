using System.Text.RegularExpressions;
using MockDataStudio.SqlTooling.Tokens;

namespace MockDataStudio.SqlTooling.Classification;

/// <summary>
/// Classifica comandos SQL e avalia periculosidade/destrutividade conforme docs/20 §5.
/// </summary>
public static class SqlClassifier
{
    private static readonly Regex TrivialWhereRegex = new(
        @"^\s*(1\s*=\s*1|0\s*=\s*0|true|1|'a'\s*=\s*'a')\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (StatementKind Kind, bool IsDestructive, bool HasWhereClause) Classify(IReadOnlyList<SqlToken> tokens)
    {
        var significantTokens = tokens
            .Where(t => t.Type != SqlTokenType.Whitespace &&
                        t.Type != SqlTokenType.LineComment &&
                        t.Type != SqlTokenType.BlockComment &&
                        t.Type != SqlTokenType.Terminator)
            .ToList();

        if (significantTokens.Count == 0)
        {
            return (StatementKind.Other, false, false);
        }

        var firstWord = significantTokens[0].Text.ToUpperInvariant();

        // 1. Session specials (PRAGMA foreign_keys=OFF / SET FOREIGN_KEY_CHECKS=0)
        var fullText = string.Join(" ", significantTokens.Select(t => t.Text)).ToUpperInvariant();
        if (fullText.Contains("PRAGMA FOREIGN_KEYS = OFF", StringComparison.Ordinal) ||
            fullText.Contains("PRAGMA FOREIGN_KEYS=OFF", StringComparison.Ordinal) ||
            fullText.Contains("PRAGMA FOREIGN_KEYS = 0", StringComparison.Ordinal) ||
            fullText.Contains("PRAGMA FOREIGN_KEYS=0", StringComparison.Ordinal) ||
            fullText.Contains("FOREIGN_KEY_CHECKS = 0", StringComparison.Ordinal) ||
            fullText.Contains("FOREIGN_KEY_CHECKS=0", StringComparison.Ordinal))
        {
            return (StatementKind.Session, true, false);
        }

        // 2. CTE Handling: WITH ...
        if (firstWord == "WITH")
        {
            return ClassifyCte(significantTokens);
        }

        // 3. Regular statements
        var kind = DetermineKind(firstWord, significantTokens);
        var hasWhere = CheckHasWhereClause(significantTokens);
        var isDestructive = CheckIsDestructive(kind, firstWord, significantTokens, hasWhere);

        return (kind, isDestructive, hasWhere);
    }

    private static StatementKind DetermineKind(string firstWord, IReadOnlyList<SqlToken> tokens)
    {
        switch (firstWord)
        {
            case "SELECT":
                // Check if SELECT ... INTO (DDL)
                if (IsSelectInto(tokens))
                {
                    return StatementKind.Ddl;
                }
                return StatementKind.Select;

            // Consultas de metadados/plano que não alteram nada (MySQL SHOW/DESCRIBE, EXPLAIN de todos os SGBDs,
            // VALUES e TABLE do padrão SQL). Tratadas como leitura: sem isso caíam em Other e exigiam confirmação.
            case "EXPLAIN":
                // EXPLAIN ANALYZE executa o comando de fato (PostgreSQL/MySQL 8): não é leitura garantida.
                return tokens.Any(t => t.Type == SqlTokenType.Word && t.Text.Equals("ANALYZE", StringComparison.OrdinalIgnoreCase))
                    ? StatementKind.Other
                    : StatementKind.Select;

            case "SHOW":
            case "DESCRIBE":
            case "DESC":
            case "VALUES":
            case "TABLE":
                return StatementKind.Select;

            case "INSERT":
                return StatementKind.Insert;

            case "UPDATE":
                return StatementKind.Update;

            case "DELETE":
                return StatementKind.Delete;

            case "MERGE":
                return StatementKind.Merge;

            case "CREATE":
            case "ALTER":
            case "DROP":
            case "TRUNCATE":
            case "RECREATE":
            case "COMMENT":
                return StatementKind.Ddl;

            case "BEGIN":
            case "START":
            case "COMMIT":
            case "ROLLBACK":
            case "SAVEPOINT":
            case "RELEASE":
                return StatementKind.Transaction;

            case "SET":
            case "USE":
            case "PRAGMA":
                return StatementKind.Session;

            case "CALL":
            case "EXECUTE":
            case "DO":
                return StatementKind.Procedure;

            default:
                return StatementKind.Other;
        }
    }

    private static (StatementKind Kind, bool IsDestructive, bool HasWhereClause) ClassifyCte(IReadOnlyList<SqlToken> tokens)
    {
        // Scan for DML within tokens (outside string/comments)
        var words = tokens
            .Where(t => t.Type == SqlTokenType.Word)
            .Select(t => t.Text.ToUpperInvariant())
            .ToList();

        StatementKind finalKind;
        if (words.Contains("DELETE"))
            finalKind = StatementKind.Delete;
        else if (words.Contains("UPDATE"))
            finalKind = StatementKind.Update;
        else if (words.Contains("MERGE"))
            finalKind = StatementKind.Merge;
        else if (words.Contains("INSERT"))
            finalKind = StatementKind.Insert;
        else if (words.Contains("SELECT"))
            finalKind = StatementKind.Select;
        else
            finalKind = StatementKind.Other;

        var hasWhere = CheckHasWhereClause(tokens, isCte: true);
        var isDestructive = CheckIsDestructive(finalKind, "WITH", tokens, hasWhere);

        return (finalKind, isDestructive, hasWhere);
    }

    private static bool IsSelectInto(IReadOnlyList<SqlToken> tokens)
    {
        var parenDepth = 0;
        for (var i = 1; i < tokens.Count; i++)
        {
            var text = tokens[i].Text;
            if (text == "(") parenDepth++;
            else if (text == ")") parenDepth--;
            else if (parenDepth == 0 && string.Equals(text, "INTO", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else if (parenDepth == 0 && string.Equals(text, "FROM", StringComparison.OrdinalIgnoreCase))
            {
                // INTO comes before FROM in SELECT INTO syntax
                return false;
            }
        }
        return false;
    }

    private static bool CheckHasWhereClause(IReadOnlyList<SqlToken> tokens, bool isCte = false)
    {
        if (isCte)
        {
            return tokens.Any(t => string.Equals(t.Text, "WHERE", StringComparison.OrdinalIgnoreCase));
        }

        var parenDepth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var text = tokens[i].Text;
            if (text == "(") parenDepth++;
            else if (text == ")") parenDepth--;
            else if (parenDepth == 0 && string.Equals(text, "WHERE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool CheckIsDestructive(StatementKind kind, string firstWord, IReadOnlyList<SqlToken> tokens, bool hasWhere)
    {
        switch (kind)
        {
            case StatementKind.Update:
            case StatementKind.Delete:
                if (!hasWhere)
                {
                    return true;
                }
                return IsTrivialWhere(tokens);

            case StatementKind.Ddl:
                // CREATE is not destructive unless CREATE OR REPLACE or DROP/TRUNCATE/ALTER/RECREATE
                return firstWord is "DROP" or "TRUNCATE" or "ALTER" or "RECREATE" or "SELECT";

            case StatementKind.Merge:
            case StatementKind.Procedure:
                return true;

            default:
                return false;
        }
    }

    private static bool IsTrivialWhere(IReadOnlyList<SqlToken> tokens)
    {
        var whereIdx = -1;
        var parenDepth = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            var text = tokens[i].Text;
            if (text == "(") parenDepth++;
            else if (text == ")") parenDepth--;
            else if (parenDepth == 0 && string.Equals(text, "WHERE", StringComparison.OrdinalIgnoreCase))
            {
                whereIdx = i;
                break;
            }
        }

        if (whereIdx < 0) return true;

        // Collect condition tokens until end or terminator or group/order
        var conditionTokens = new List<string>();
        parenDepth = 0;
        for (var i = whereIdx + 1; i < tokens.Count; i++)
        {
            var t = tokens[i].Text;
            if (t == "(") parenDepth++;
            else if (t == ")") parenDepth--;
            else if (parenDepth == 0 && (string.Equals(t, "ORDER", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(t, "GROUP", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(t, "LIMIT", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
            conditionTokens.Add(t);
        }

        var conditionText = string.Join(" ", conditionTokens);
        return TrivialWhereRegex.IsMatch(conditionText);
    }
}
