using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Lexer;
using MockDataStudio.SqlTooling.Tokens;

namespace MockDataStudio.SqlTooling.Completion;

// Autocomplete básico (C6.2, docs/20): palavras-chave, relações e colunas a partir de um catálogo já capturado.
// Usa o lexer para não sugerir dentro de literais, identificadores entre aspas e comentários.
// Não é um parser: resolve apelidos por FROM/JOIN/UPDATE/INTO e ignora subconsultas e CTEs.
public enum CompletionKind
{
    Keyword,
    Relation,
    Column,
}

public sealed record CompletionItem(string Label, CompletionKind Kind, string InsertText);

// Lista de sugestões. ReplaceStart é o índice onde começa o prefixo digitado (a substituição vai de ReplaceStart até o cursor).
public sealed record CompletionResult(int ReplaceStart, IReadOnlyList<CompletionItem> Items)
{
    public static CompletionResult Empty(int caret) => new(caret, []);
}

// Catálogo de relações e colunas. Nomes são comparados sem diferenciar maiúsculas.
public sealed class CompletionCatalog
{
    public CompletionCatalog(IEnumerable<string> relations, IReadOnlyDictionary<string, IReadOnlyList<string>> columnsByRelation)
    {
        Relations = relations.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
        ColumnsByRelation = new Dictionary<string, IReadOnlyList<string>>(columnsByRelation, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Relations { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnsByRelation { get; }

    public IReadOnlyList<string> ColumnsOf(string relation) =>
        ColumnsByRelation.TryGetValue(relation, out var columns) ? columns : [];

    public bool HasRelation(string relation) => Relations.Contains(relation, StringComparer.OrdinalIgnoreCase);
}

public static class SqlCompletionEngine
{
    private static readonly string[] Keywords =
    [
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "AND", "OR", "NOT", "IN", "IS", "NULL",
        "LIKE", "BETWEEN", "ORDER BY", "GROUP BY", "HAVING", "LIMIT", "OFFSET", "INSERT INTO", "VALUES", "UPDATE", "SET",
        "DELETE FROM", "CREATE TABLE", "CREATE VIEW", "CREATE INDEX", "DROP TABLE", "ALTER TABLE", "AS", "DISTINCT", "CASE",
        "WHEN", "THEN", "ELSE", "END", "UNION", "ALL", "WITH", "EXISTS", "COUNT", "SUM", "AVG", "MIN", "MAX", "ASC", "DESC",
    ];

    // Palavras que, logo antes do cursor, pedem apenas nomes de relação.
    private static readonly HashSet<string> RelationContext = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "JOIN", "INTO", "UPDATE", "TABLE",
    };

    // Palavras que nunca são apelido de tabela, mesmo quando seguem o nome da relação.
    private static readonly HashSet<string> ReservedAfterRelation = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "GROUP", "ORDER", "HAVING", "LIMIT", "OFFSET", "UNION",
        "SET", "VALUES", "SELECT", "FROM", "AND", "OR", "AS", "WITH", "USING", "NATURAL", "CROSS", "FULL",
    };

    public static CompletionResult Complete(string text, int caret, CompletionCatalog catalog, SqlDialectFeatures? dialect = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(catalog);
        caret = Math.Clamp(caret, 0, text.Length);

        var tokens = SqlLexer.Tokenize(text, dialect);
        if (InsideLiteralOrComment(tokens, caret))
        {
            return CompletionResult.Empty(caret);
        }

        var (prefix, qualifier) = ReadPrefix(text, caret);
        var replaceStart = caret - prefix.Length;
        var aliases = ReferencedRelations(tokens, catalog);

        if (qualifier is not null)
        {
            var relation = aliases.TryGetValue(qualifier, out var resolved) ? resolved
                : catalog.HasRelation(qualifier) ? catalog.Relations.First(r => r.Equals(qualifier, StringComparison.OrdinalIgnoreCase))
                : null;
            return relation is null
                ? CompletionResult.Empty(caret)
                : new CompletionResult(replaceStart, Filter(catalog.ColumnsOf(relation), CompletionKind.Column, prefix));
        }

        var previousWord = PreviousWord(text, replaceStart);
        if (previousWord is not null && RelationContext.Contains(previousWord))
        {
            return new CompletionResult(replaceStart, Filter(catalog.Relations, CompletionKind.Relation, prefix));
        }

        var items = new List<CompletionItem>();
        items.AddRange(Filter(Keywords, CompletionKind.Keyword, prefix));
        items.AddRange(Filter(catalog.Relations, CompletionKind.Relation, prefix));
        foreach (var relation in aliases.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
        {
            items.AddRange(Filter(catalog.ColumnsOf(relation), CompletionKind.Column, prefix));
        }

        return new CompletionResult(replaceStart, items.DistinctBy(i => (i.Kind, i.Label.ToUpperInvariant())).ToList());
    }

    // Cursor dentro de literal ou comentário. Um token sem fechamento que termina no fim do texto ainda contém o cursor.
    private static bool InsideLiteralOrComment(IReadOnlyList<SqlToken> tokens, int caret)
    {
        foreach (var token in tokens)
        {
            var end = token.StartOffset + token.Length;
            var openAtEnd = caret == end && IsUnterminated(token);
            if (caret <= token.StartOffset || (caret >= end && !openAtEnd))
            {
                continue;
            }

            if (token.Type is SqlTokenType.String or SqlTokenType.NationalString or SqlTokenType.EscapeString
                or SqlTokenType.DollarQuoted or SqlTokenType.QuotedIdentifier or SqlTokenType.LineComment or SqlTokenType.BlockComment)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnterminated(SqlToken token) => token.Type switch
    {
        SqlTokenType.LineComment => true,
        SqlTokenType.BlockComment => !token.Text.EndsWith("*/", StringComparison.Ordinal),
        SqlTokenType.String or SqlTokenType.NationalString or SqlTokenType.EscapeString or SqlTokenType.QuotedIdentifier =>
            token.Text.Length < 2 || token.Text[^1] != token.Text[0],
        _ => false,
    };

    // Prefixo: identificador que termina no cursor. Qualificador: identificador antes de um '.' que precede o prefixo.
    private static (string Prefix, string? Qualifier) ReadPrefix(string text, int caret)
    {
        var start = caret;
        while (start > 0 && IsIdentifierChar(text[start - 1]))
        {
            start--;
        }

        var prefix = text[start..caret];
        if (start == 0 || text[start - 1] != '.')
        {
            return (prefix, null);
        }

        var qualifierEnd = start - 1;
        var qualifierStart = qualifierEnd;
        while (qualifierStart > 0 && IsIdentifierChar(text[qualifierStart - 1]))
        {
            qualifierStart--;
        }

        return qualifierStart == qualifierEnd ? (prefix, null) : (prefix, text[qualifierStart..qualifierEnd]);
    }

    // Palavra imediatamente anterior ao início do prefixo (em maiúsculas), ignorando espaços.
    private static string? PreviousWord(string text, int index)
    {
        var i = index;
        while (i > 0 && char.IsWhiteSpace(text[i - 1]))
        {
            i--;
        }

        var end = i;
        while (i > 0 && IsIdentifierChar(text[i - 1]))
        {
            i--;
        }

        return end > i ? text[i..end].ToUpperInvariant() : null;
    }

    // Apelido → relação, para FROM/JOIN/UPDATE/INTO seguidos de nome e apelido opcional.
    private static Dictionary<string, string> ReferencedRelations(IReadOnlyList<SqlToken> tokens, CompletionCatalog catalog)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var words = tokens.Where(t => t.Type is SqlTokenType.Word or SqlTokenType.QuotedIdentifier).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            if (!RelationContext.Contains(words[i].Text) || i + 1 >= words.Count)
            {
                continue;
            }

            var name = Unquote(words[i + 1].Text);
            if (!catalog.HasRelation(name))
            {
                continue;
            }

            var relation = catalog.Relations.First(r => r.Equals(name, StringComparison.OrdinalIgnoreCase));
            map[relation] = relation;

            var next = i + 2;
            if (next < words.Count && words[next].Text.Equals("AS", StringComparison.OrdinalIgnoreCase))
            {
                next++;
            }

            if (next < words.Count && !ReservedAfterRelation.Contains(words[next].Text))
            {
                map[Unquote(words[next].Text)] = relation;
            }
        }

        return map;
    }

    private static List<CompletionItem> Filter(IEnumerable<string> candidates, CompletionKind kind, string prefix) =>
        candidates
            .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CompletionItem(c, kind, c))
            .ToList();

    private static string Unquote(string text) =>
        text.Length >= 2 && (text[0] is '"' or '`' or '[') ? text[1..^1] : text;

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
