using System.Globalization;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Lexer;
using MockDataStudio.SqlTooling.Tokens;

namespace MockDataStudio.Providers.Sqlite;

// Análise do SQL armazenado em sqlite_schema (CHECK, colunas geradas, triggers, colunas de índice).
// SQLite não expõe essas informações pelos pragmas; usa-se o lexer do SqlTooling (docs/11 §4.1).
internal static class SqliteDdl
{
    // Um item de lista de nível superior: texto original e tokens significativos.
    internal sealed record Item(string Text, IReadOnlyList<SqlToken> Tokens);

    public static IReadOnlyList<SqlToken> Significant(string sql) =>
        SqlLexer.Tokenize(sql, SqlDialectFeatures.Sqlite)
            .Where(t => t.Type is not (SqlTokenType.Whitespace or SqlTokenType.LineComment or SqlTokenType.BlockComment))
            .ToList();

    // Itens separados por vírgula dentro do primeiro parêntese de nível superior a partir de `startIndex`.
    public static IReadOnlyList<Item> ParenthesizedItems(string sql, IReadOnlyList<SqlToken> tokens, int startIndex, out int closeIndex)
    {
        var items = new List<Item>();
        var open = FindOperator(tokens, "(", startIndex);
        closeIndex = -1;
        if (open < 0)
        {
            return items;
        }

        var depth = 0;
        var itemStart = open + 1;
        for (var i = open; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == SqlTokenType.Operator && t.Text == "(")
            {
                depth++;
            }
            else if (t.Type == SqlTokenType.Operator && t.Text == ")")
            {
                depth--;
                if (depth == 0)
                {
                    closeIndex = i;
                    AddItem(sql, tokens, items, itemStart, i);
                    break;
                }
            }
            else if (t.Type == SqlTokenType.Operator && t.Text == "," && depth == 1)
            {
                AddItem(sql, tokens, items, itemStart, i);
                itemStart = i + 1;
            }
        }

        return items;
    }

    // Expressão entre parênteses que segue a palavra-chave (ex.: CHECK (expr), AS (expr)).
    public static bool TryParenExpression(string sql, IReadOnlyList<SqlToken> tokens, int keywordIndex, out string expression, out int closeIndex)
    {
        expression = string.Empty;
        closeIndex = -1;
        var open = keywordIndex + 1;
        if (open >= tokens.Count || tokens[open].Type != SqlTokenType.Operator || tokens[open].Text != "(")
        {
            return false;
        }

        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Type != SqlTokenType.Operator)
            {
                continue;
            }

            if (tokens[i].Text == "(")
            {
                depth++;
            }
            else if (tokens[i].Text == ")")
            {
                depth--;
                if (depth == 0)
                {
                    closeIndex = i;
                    var start = tokens[open].StartOffset + 1;
                    var end = tokens[i].StartOffset;
                    expression = NormalizeWhitespace(sql[start..end]);
                    return true;
                }
            }
        }

        return false;
    }

    // Lista de CHECK de uma tabela: de colunas (escopo = a coluna) e de tabela (escopo = colunas citadas).
    public static IEnumerable<CheckConstraintModel> Checks(string sql, IReadOnlyList<string> columnNames)
    {
        var tokens = Significant(sql);
        foreach (var item in ParenthesizedItems(sql, tokens, 0, out _))
        {
            var (name, first) = ConstraintNameOf(item.Tokens);
            var columnName = IsTableConstraint(item.Tokens) ? null : UnquoteIdentifier(item.Tokens[0].Text, item.Tokens[0].Type);

            for (var i = first; i < item.Tokens.Count; i++)
            {
                if (!IsWord(item.Tokens[i], "CHECK"))
                {
                    continue;
                }

                var keywordIndex = IndexOf(sql, tokens, item, i);
                if (keywordIndex < 0 || !TryParenExpression(sql, tokens, keywordIndex, out var expression, out _))
                {
                    continue;
                }

                IReadOnlyList<string> scope = columnName is not null
                    ? [columnName]
                    : columnNames.Where(c => ReferencesColumn(expression, c)).ToList();
                yield return new CheckConstraintModel(name, expression, scope);
            }
        }
    }

    // Expressão de coluna gerada: "GENERATED ALWAYS AS (expr) [VIRTUAL|STORED]" ou "AS (expr) ...".
    public static bool TryGeneratedColumn(string sql, string columnName, out string expression, out bool stored)
    {
        expression = string.Empty;
        stored = false;
        var tokens = Significant(sql);
        foreach (var item in ParenthesizedItems(sql, tokens, 0, out _))
        {
            if (item.Tokens.Count == 0 || IsTableConstraint(item.Tokens))
            {
                continue;
            }

            if (!string.Equals(UnquoteIdentifier(item.Tokens[0].Text, item.Tokens[0].Type), columnName, StringComparison.Ordinal))
            {
                continue;
            }

            for (var i = 1; i < item.Tokens.Count; i++)
            {
                if (!IsWord(item.Tokens[i], "AS"))
                {
                    continue;
                }

                var keywordIndex = IndexOf(sql, tokens, item, i);
                if (keywordIndex < 0 || !TryParenExpression(sql, tokens, keywordIndex, out expression, out var close))
                {
                    continue;
                }

                stored = item.Tokens.Any(t => IsWord(t, "STORED"));
                return true;
            }
        }

        return false;
    }

    // Colunas de uma tabela em ordem declarada, para classificar AUTOINCREMENT e CHECK.
    public static bool HasAutoIncrement(string sql) =>
        Significant(sql).Any(t => IsWord(t, "AUTOINCREMENT"));

    // Trigger: tempo (BEFORE/AFTER/INSTEAD OF) e eventos (INSERT/UPDATE/DELETE) a partir do cabeçalho.
    public static TriggerModel? ParseTrigger(string name, string sql, string schemaNamespace)
    {
        var tokens = Significant(sql);
        var i = 0;
        if (!IsWord(tokens, i, "CREATE")) return null;
        i++;
        if (IsWord(tokens, i, "TEMP") || IsWord(tokens, i, "TEMPORARY")) i++;
        if (!IsWord(tokens, i, "TRIGGER")) return null;
        i++;
        if (IsWord(tokens, i, "IF")) i += 3; // IF NOT EXISTS
        i++; // nome

        var timing = TriggerTiming.After;
        var events = TriggerEvents.None;
        for (; i < tokens.Count && !IsWord(tokens[i], "ON"); i++)
        {
            var t = tokens[i];
            if (IsWord(t, "BEFORE")) timing = TriggerTiming.Before;
            else if (IsWord(t, "AFTER")) timing = TriggerTiming.After;
            else if (IsWord(t, "INSTEAD")) timing = TriggerTiming.InsteadOf;
            else if (IsWord(t, "INSERT")) events |= TriggerEvents.Insert;
            else if (IsWord(t, "UPDATE")) events |= TriggerEvents.Update;
            else if (IsWord(t, "DELETE")) events |= TriggerEvents.Delete;
        }

        if (i + 1 >= tokens.Count) return null;
        var table = UnquoteIdentifier(tokens[i + 1].Text, tokens[i + 1].Type);
        return new TriggerModel(name, new ObjectName(schemaNamespace, table), timing, events);
    }

    // Colunas de um índice (expressões incluídas como texto original, na ordem do índice).
    public static IReadOnlyList<string> IndexExpressions(string indexSql)
    {
        var tokens = Significant(indexSql);
        var onIndex = tokens.ToList().FindIndex(t => IsWord(t, "ON"));
        if (onIndex < 0) return [];

        return ParenthesizedItems(indexSql, tokens, onIndex, out _)
            .Select(item => Regex.Replace(item.Text, @"\s+(ASC|DESC)\s*$", string.Empty, RegexOptions.IgnoreCase).Trim())
            .ToList();
    }

    // Predicado de índice parcial (texto após WHERE de nível superior).
    public static string? IndexPredicate(string indexSql)
    {
        var tokens = Significant(indexSql);
        var onIndex = tokens.ToList().FindIndex(t => IsWord(t, "ON"));
        if (onIndex < 0) return null;

        ParenthesizedItems(indexSql, tokens, onIndex, out var close);
        if (close < 0) return null;
        for (var i = close + 1; i < tokens.Count; i++)
        {
            if (IsWord(tokens[i], "WHERE"))
            {
                return NormalizeWhitespace(indexSql[(tokens[i].StartOffset + tokens[i].Length)..].Trim());
            }
        }

        return null;
    }

    public static string UnquoteIdentifier(string text, SqlTokenType type)
    {
        if (type != SqlTokenType.QuotedIdentifier || text.Length < 2)
        {
            return text;
        }

        var inner = text[1..^1];
        return text[0] switch
        {
            '"' => inner.Replace("\"\"", "\"", StringComparison.Ordinal),
            '`' => inner.Replace("``", "`", StringComparison.Ordinal),
            '[' => inner,
            _ => text,
        };
    }

    public static string NormalizeWhitespace(string text) =>
        Regex.Replace(text.Trim(), @"\s+", " ");

    private static void AddItem(string sql, IReadOnlyList<SqlToken> tokens, List<Item> items, int from, int to)
    {
        if (to <= from) return;
        var slice = tokens.Skip(from).Take(to - from).ToList();
        var start = slice[0].StartOffset;
        var end = slice[^1].StartOffset + slice[^1].Length;
        items.Add(new Item(NormalizeWhitespace(sql[start..end]), slice));
    }

    private static int FindOperator(IReadOnlyList<SqlToken> tokens, string text, int from)
    {
        for (var i = from; i < tokens.Count; i++)
        {
            if (tokens[i].Type == SqlTokenType.Operator && tokens[i].Text == text) return i;
        }

        return -1;
    }

    private static bool IsTableConstraint(IReadOnlyList<SqlToken> item)
    {
        if (item.Count == 0) return false;
        var first = item[0];
        return IsWord(first, "CONSTRAINT") || IsWord(first, "PRIMARY") || IsWord(first, "UNIQUE")
            || IsWord(first, "CHECK") || IsWord(first, "FOREIGN");
    }

    private static (string? Name, int First) ConstraintNameOf(IReadOnlyList<SqlToken> item)
    {
        if (item.Count >= 3 && IsWord(item[0], "CONSTRAINT"))
        {
            return (UnquoteIdentifier(item[1].Text, item[1].Type), 2);
        }

        return (null, 0);
    }

    // Índice do token `position` dentro da lista completa de tokens significativos.
    private static int IndexOf(string sql, IReadOnlyList<SqlToken> tokens, Item item, int position)
    {
        var target = item.Tokens[position];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].StartOffset == target.StartOffset && tokens[i].Length == target.Length) return i;
        }

        return -1;
    }

    private static bool ReferencesColumn(string expression, string column) =>
        Regex.IsMatch(expression, @"(?<![\w])" + Regex.Escape(column) + @"(?![\w])", RegexOptions.IgnoreCase);

    private static bool IsWord(SqlToken token, string word) =>
        token.Type == SqlTokenType.Word && string.Equals(token.Text, word, StringComparison.OrdinalIgnoreCase);

    private static bool IsWord(IReadOnlyList<SqlToken> tokens, int index, string word) =>
        index < tokens.Count && IsWord(tokens[index], word);
}
