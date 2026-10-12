using System.Text;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Tokens;

namespace MockDataStudio.SqlTooling.Lexer;

/// <summary>
/// Analisador léxico SQL com suporte aos dialetos SQLite, MySQL, PostgreSQL e Firebird.
/// Conforme especificado em docs/20 §4 e ADR-007.
/// </summary>
public sealed class SqlLexer
{
    private readonly string _source;
    private readonly SqlDialectFeatures _dialect;
    private string _customTerminator;
    private int _offset;
    private int _line = 1;
    private int _column = 1;

    public string Terminator
    {
        get => _customTerminator;
        set => _customTerminator = string.IsNullOrEmpty(value) ? ";" : value;
    }

    public SqlLexer(string source, SqlDialectFeatures? dialect = null, string? customTerminator = null)
    {
        _source = source ?? string.Empty;
        _dialect = dialect ?? SqlDialectFeatures.Sqlite;
        _customTerminator = string.IsNullOrEmpty(customTerminator) ? ";" : customTerminator;
    }

    public static IReadOnlyList<SqlToken> Tokenize(string source, SqlDialectFeatures? dialect = null, string? customTerminator = null)
    {
        var lexer = new SqlLexer(source, dialect, customTerminator);
        return lexer.TokenizeAll();
    }

    public IReadOnlyList<SqlToken> TokenizeAll()
    {
        var tokens = new List<SqlToken>();
        while (_offset < _source.Length)
        {
            var token = NextToken();
            tokens.Add(token);
        }
        return tokens;
    }

    public SqlToken NextToken()
    {
        if (_offset >= _source.Length)
        {
            return new SqlToken(SqlTokenType.Terminator, string.Empty, _offset, 0, _line, _column);
        }

        var startOffset = _offset;
        var startLine = _line;
        var startColumn = _column;
        var ch = _source[_offset];

        // 1. Whitespace
        if (char.IsWhiteSpace(ch))
        {
            return ReadWhitespace(startOffset, startLine, startColumn);
        }

        // 2. Custom or Standard Terminator check
        if (MatchesTerminator())
        {
            return ReadTerminator(startOffset, startLine, startColumn);
        }

        // 3. Comments
        // 3a. Line comment: -- or (# in MySQL)
        if (ch == '-' && Peek(1) == '-')
        {
            return ReadLineComment(startOffset, startLine, startColumn);
        }
        if (ch == '#' && _dialect.HashLineComments)
        {
            return ReadLineComment(startOffset, startLine, startColumn);
        }

        // 3b. Block comment: /* ... */
        if (ch == '/' && Peek(1) == '*')
        {
            // MySQL conditional comment /*! ... */ treated as code per docs/20 §4
            if (_dialect.ExecutableComments && Peek(2) == '!')
            {
                // Advance past /*! and continue lexing inner content as code
                Advance(3);
                return new SqlToken(SqlTokenType.Operator, "/*!", startOffset, 3, startLine, startColumn);
            }

            return ReadBlockComment(startOffset, startLine, startColumn);
        }

        // MySQL closing of conditional comment */
        if (ch == '*' && Peek(1) == '/')
        {
            Advance(2);
            return new SqlToken(SqlTokenType.Operator, "*/", startOffset, 2, startLine, startColumn);
        }

        // 4. PostgreSQL Dollar-quoted strings ($tag$...$tag$)
        if (_dialect.DollarQuotedStrings && ch == '$')
        {
            var dollarToken = TryReadDollarQuoted(startOffset, startLine, startColumn);
            if (dollarToken.HasValue)
            {
                return dollarToken.Value;
            }
        }

        // 5. String literals
        // National string: N'...'
        if ((ch == 'N' || ch == 'n') && Peek(1) == '\'')
        {
            return ReadNationalString(startOffset, startLine, startColumn);
        }

        // PostgreSQL Escape string: E'...'
        if (_dialect.EscapeStrings && (ch == 'E' || ch == 'e') && Peek(1) == '\'')
        {
            return ReadEscapeString(startOffset, startLine, startColumn);
        }

        // Standard string: '...'
        if (ch == '\'')
        {
            return ReadString(startOffset, startLine, startColumn);
        }

        // 6. Quoted identifiers
        // Double quotes "..." (ANSI, SQLite, PostgreSQL, Firebird)
        if (ch == '"')
        {
            return ReadQuotedIdentifier('"', startOffset, startLine, startColumn);
        }

        // Backticks `...` (MySQL, SQLite)
        if (ch == '`' && _dialect.BacktickIdentifiers)
        {
            return ReadQuotedIdentifier('`', startOffset, startLine, startColumn);
        }

        // SQLite brackets [...]
        if (ch == '[' && _dialect.BracketIdentifiers)
        {
            return ReadBracketIdentifier(startOffset, startLine, startColumn);
        }

        // 7. Numbers (hex 0x... or decimal/integer)
        if (char.IsDigit(ch) || (ch == '.' && char.IsDigit(Peek(1))))
        {
            return ReadNumber(startOffset, startLine, startColumn);
        }

        // 8. Words / Identifiers / Keywords
        if (IsWordStart(ch))
        {
            return ReadWord(startOffset, startLine, startColumn);
        }

        // 9. Multi-character or single-character operators
        return ReadOperator(startOffset, startLine, startColumn);
    }

    private bool MatchesTerminator()
    {
        if (_source.Length - _offset < _customTerminator.Length)
            return false;

        for (var i = 0; i < _customTerminator.Length; i++)
        {
            if (_source[_offset + i] != _customTerminator[i])
                return false;
        }

        return true;
    }

    private SqlToken ReadTerminator(int startOffset, int startLine, int startColumn)
    {
        var len = _customTerminator.Length;
        Advance(len);
        return new SqlToken(SqlTokenType.Terminator, _customTerminator, startOffset, len, startLine, startColumn);
    }

    private SqlToken ReadWhitespace(int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();
        while (_offset < _source.Length && char.IsWhiteSpace(_source[_offset]))
        {
            sb.Append(Advance());
        }
        return new SqlToken(SqlTokenType.Whitespace, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadLineComment(int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();
        while (_offset < _source.Length)
        {
            var ch = _source[_offset];
            if (ch == '\r' || ch == '\n')
            {
                break;
            }
            sb.Append(Advance());
        }
        return new SqlToken(SqlTokenType.LineComment, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadBlockComment(int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();
        sb.Append(Advance()); // '/'
        sb.Append(Advance()); // '*'

        while (_offset < _source.Length)
        {
            if (_source[_offset] == '*' && Peek(1) == '/')
            {
                sb.Append(Advance()); // '*'
                sb.Append(Advance()); // '/'
                break;
            }
            sb.Append(Advance());
        }

        return new SqlToken(SqlTokenType.BlockComment, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken? TryReadDollarQuoted(int startOffset, int startLine, int startColumn)
    {
        // Tag format: $[a-zA-Z0-9_]*$
        var i = _offset + 1;
        while (i < _source.Length && (char.IsLetterOrDigit(_source[i]) || _source[i] == '_'))
        {
            i++;
        }

        if (i < _source.Length && _source[i] == '$')
        {
            var tagLength = i - _offset + 1;
            var tag = _source.Substring(_offset, tagLength);

            // Found opening tag, find matching closing tag
            var sb = new StringBuilder();
            for (var k = 0; k < tagLength; k++)
            {
                sb.Append(Advance());
            }

            while (_offset < _source.Length)
            {
                if (_source.Length - _offset >= tag.Length && _source.Substring(_offset, tag.Length) == tag)
                {
                    for (var k = 0; k < tag.Length; k++)
                    {
                        sb.Append(Advance());
                    }
                    return new SqlToken(SqlTokenType.DollarQuoted, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
                }

                sb.Append(Advance());
            }

            // Unclosed dollar quoted
            return new SqlToken(SqlTokenType.DollarQuoted, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
        }

        return null;
    }

    private SqlToken ReadNationalString(int startOffset, int startLine, int startColumn)
    {
        Advance(); // 'N'
        var strToken = ReadString(startOffset, startLine, startColumn);
        return new SqlToken(SqlTokenType.NationalString, "N" + strToken.Text, startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadEscapeString(int startOffset, int startLine, int startColumn)
    {
        Advance(); // 'E'
        var strToken = ReadString(startOffset, startLine, startColumn);
        return new SqlToken(SqlTokenType.EscapeString, "E" + strToken.Text, startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadString(int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();
        sb.Append(Advance()); // opening quote

        var allowBackslash = _dialect.BackslashEscapes;

        while (_offset < _source.Length)
        {
            var ch = _source[_offset];

            if (ch == '\\' && allowBackslash)
            {
                sb.Append(Advance());
                if (_offset < _source.Length)
                {
                    sb.Append(Advance());
                }
                continue;
            }

            if (ch == '\'')
            {
                sb.Append(Advance());
                if (_offset < _source.Length && _source[_offset] == '\'')
                {
                    // Escaped quote ''
                    sb.Append(Advance());
                    continue;
                }
                break;
            }

            sb.Append(Advance());
        }

        return new SqlToken(SqlTokenType.String, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadQuotedIdentifier(char quoteChar, int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();
        sb.Append(Advance()); // opening quote

        while (_offset < _source.Length)
        {
            var ch = _source[_offset];
            if (ch == quoteChar)
            {
                sb.Append(Advance());
                if (_offset < _source.Length && _source[_offset] == quoteChar)
                {
                    // Escaped quote
                    sb.Append(Advance());
                    continue;
                }
                break;
            }
            sb.Append(Advance());
        }

        return new SqlToken(SqlTokenType.QuotedIdentifier, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadBracketIdentifier(int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();
        sb.Append(Advance()); // '['

        while (_offset < _source.Length)
        {
            var ch = _source[_offset];
            sb.Append(Advance());
            if (ch == ']')
            {
                break;
            }
        }

        return new SqlToken(SqlTokenType.QuotedIdentifier, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadNumber(int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();

        // Check for hex: 0x...
        if (_source[_offset] == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
        {
            sb.Append(Advance());
            sb.Append(Advance());
            while (_offset < _source.Length && IsHexDigit(_source[_offset]))
            {
                sb.Append(Advance());
            }
            return new SqlToken(SqlTokenType.Number, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
        }

        var hasDot = false;
        var hasExponent = false;

        while (_offset < _source.Length)
        {
            var ch = _source[_offset];
            if (char.IsDigit(ch))
            {
                sb.Append(Advance());
            }
            else if (ch == '.' && !hasDot && !hasExponent && char.IsDigit(Peek(1)))
            {
                hasDot = true;
                sb.Append(Advance());
            }
            else if ((ch == 'e' || ch == 'E') && !hasExponent)
            {
                hasExponent = true;
                sb.Append(Advance());
                if (_offset < _source.Length && (_source[_offset] == '+' || _source[_offset] == '-'))
                {
                    sb.Append(Advance());
                }
            }
            else
            {
                break;
            }
        }

        return new SqlToken(SqlTokenType.Number, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadWord(int startOffset, int startLine, int startColumn)
    {
        var sb = new StringBuilder();
        while (_offset < _source.Length && IsWordChar(_source[_offset]))
        {
            sb.Append(Advance());
        }

        var text = sb.ToString();

        // Check for client directives (SET TERM or DELIMITER)
        if (string.Equals(text, "DELIMITER", StringComparison.OrdinalIgnoreCase) && _dialect.DelimiterDirective)
        {
            // Read until end of line
            while (_offset < _source.Length && _source[_offset] != '\r' && _source[_offset] != '\n')
            {
                sb.Append(Advance());
            }
            return new SqlToken(SqlTokenType.ClientDirective, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
        }

        if (string.Equals(text, "SET", StringComparison.OrdinalIgnoreCase) && _dialect.SetTermDirective)
        {
            // Check if next word is TERM
            var saveOffset = _offset;
            var saveLine = _line;
            var saveCol = _column;

            // skip spaces
            while (_offset < _source.Length && (_source[_offset] == ' ' || _source[_offset] == '\t'))
            {
                Advance();
            }

            if (_source.Length - _offset >= 4 &&
                string.Equals(_source.Substring(_offset, 4), "TERM", StringComparison.OrdinalIgnoreCase))
            {
                _offset = saveOffset;
                _line = saveLine;
                _column = saveCol;

                // Read until end of line or current terminator
                while (_offset < _source.Length && _source[_offset] != '\r' && _source[_offset] != '\n')
                {
                    sb.Append(Advance());
                }
                return new SqlToken(SqlTokenType.ClientDirective, sb.ToString(), startOffset, _offset - startOffset, startLine, startColumn);
            }

            _offset = saveOffset;
            _line = saveLine;
            _column = saveCol;
        }

        return new SqlToken(SqlTokenType.Word, text, startOffset, _offset - startOffset, startLine, startColumn);
    }

    private SqlToken ReadOperator(int startOffset, int startLine, int startColumn)
    {
        var ch = _source[_offset];
        var next = Peek(1);

        // 2-character operators
        string? twoCharOp = (ch, next) switch
        {
            (':', ':') => "::",
            (':', '=') => ":=",
            ('|', '|') => "||",
            ('<', '>') => "<>",
            ('!', '=') => "!=",
            ('<', '=') => "<=",
            ('>', '=') => ">=",
            ('=', '=') => "==",
            _ => null
        };

        if (twoCharOp != null)
        {
            Advance(2);
            return new SqlToken(SqlTokenType.Operator, twoCharOp, startOffset, 2, startLine, startColumn);
        }

        var single = Advance().ToString();
        return new SqlToken(SqlTokenType.Operator, single, startOffset, 1, startLine, startColumn);
    }

    private static bool IsWordStart(char ch) =>
        char.IsLetter(ch) || ch == '_' || ch == '$' || ch == '@';

    private static bool IsWordChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '$' || ch == '@';

    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

    private char Peek(int relativeOffset)
    {
        var index = _offset + relativeOffset;
        return index < _source.Length ? _source[index] : '\0';
    }

    private char Advance()
    {
        var ch = _source[_offset++];
        if (ch == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return ch;
    }

    private void Advance(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Advance();
        }
    }
}
