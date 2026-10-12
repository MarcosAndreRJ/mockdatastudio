using System.Globalization;
using System.Text;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Generation.Expressions;

// MDS-Expr (docs/15 §9.1): léxico e parser recursivo descendente. Limites de segurança em §9.3.
public static class MdsExprParser
{
    public const int MaxSourceLength = 4096;
    public const int MaxDepth = 64;

    public static Result<ExprNode> Parse(string source, ExprMode mode, out IReadOnlyList<ExprNode> templateParts)
    {
        ArgumentNullException.ThrowIfNull(source);
        templateParts = [];
        if (Encoding.UTF8.GetByteCount(source) > MaxSourceLength)
        {
            return Result<ExprNode>.Fail(ErrorCodes.GenExprLimit, "Expressão maior que 4 KB.");
        }

        if (mode == ExprMode.Expression)
        {
            return ParseExpression(source);
        }

        var parts = new List<ExprNode>();
        var literal = new StringBuilder();
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if ((ch == '{' || ch == '}') && i + 1 < source.Length && source[i + 1] == ch)
            {
                literal.Append(ch);
                i++;
                continue;
            }

            if (ch == '{')
            {
                var close = FindClosingBrace(source, i + 1);
                if (close < 0)
                {
                    return Result<ExprNode>.Fail(ErrorCodes.GenExprSyntax, "Chave '{' sem fechamento no modo template.");
                }

                if (literal.Length > 0)
                {
                    parts.Add(new LiteralNode(literal.ToString()));
                    literal.Clear();
                }

                var inner = ParseExpression(source[(i + 1)..close]);
                if (!inner.IsSuccess)
                {
                    return inner;
                }

                parts.Add(inner.Value);
                i = close;
                continue;
            }

            if (ch == '}')
            {
                return Result<ExprNode>.Fail(ErrorCodes.GenExprSyntax, "'}' sem abertura no modo template (use '}}').");
            }

            literal.Append(ch);
        }

        if (literal.Length > 0)
        {
            parts.Add(new LiteralNode(literal.ToString()));
        }

        templateParts = parts;
        return Result<ExprNode>.Ok(new TemplateNode(parts));
    }

    private static int FindClosingBrace(string source, int start)
    {
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '}')
            {
                return i;
            }
        }

        return -1;
    }

    private static Result<ExprNode> ParseExpression(string source)
    {
        try
        {
            var parser = new Parser(Lex(source));
            var node = parser.ParseExpression(depth: 0);
            if (!parser.AtEnd)
            {
                return Result<ExprNode>.Fail(ErrorCodes.GenExprSyntax, $"Token inesperado '{parser.Current.Text}'.");
            }

            return Result<ExprNode>.Ok(node);
        }
        catch (ExprSyntaxException ex)
        {
            return Result<ExprNode>.Fail(ex.Code, ex.Message);
        }
    }

    private static List<Token> Lex(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var ch = source[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (char.IsDigit(ch))
            {
                var start = i;
                while (i < source.Length && char.IsDigit(source[i]))
                {
                    i++;
                }

                if (i + 1 < source.Length && source[i] == '.' && char.IsDigit(source[i + 1]))
                {
                    i++;
                    while (i < source.Length && char.IsDigit(source[i]))
                    {
                        i++;
                    }
                }

                tokens.Add(new Token(TokenKind.Number, source[start..i]));
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, source[start..i]));
                continue;
            }

            if (ch == '\'')
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= source.Length)
                    {
                        throw new ExprSyntaxException(ErrorCodes.GenExprSyntax, "String sem fechamento.");
                    }

                    if (source[i] == '\'')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(source[i++]);
                }

                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            if (ch == '"' || (ch == '@' && i + 1 < source.Length && source[i + 1] == '"'))
            {
                var isColumn = ch == '@';
                i += isColumn ? 2 : 1;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= source.Length)
                    {
                        throw new ExprSyntaxException(ErrorCodes.GenExprSyntax, "Identificador entre aspas sem fechamento.");
                    }

                    if (source[i] == '"')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(source[i++]);
                }

                tokens.Add(new Token(isColumn ? TokenKind.Column : TokenKind.QuotedIdentifier, sb.ToString()));
                continue;
            }

            if (ch == '@')
            {
                var start = ++i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }

                if (i == start)
                {
                    throw new ExprSyntaxException(ErrorCodes.GenExprSyntax, "'@' deve ser seguido de nome de coluna.");
                }

                tokens.Add(new Token(TokenKind.Column, source[start..i]));
                continue;
            }

            var two = i + 1 < source.Length ? source.Substring(i, 2) : string.Empty;
            if (two is "==" or "!=" or "<=" or ">=" or "||")
            {
                tokens.Add(new Token(TokenKind.Operator, two == "==" ? "=" : two));
                i += 2;
                continue;
            }

            if ("+-*/%=<>(),".Contains(ch))
            {
                tokens.Add(new Token(TokenKind.Operator, ch.ToString()));
                i++;
                continue;
            }

            throw new ExprSyntaxException(ErrorCodes.GenExprSyntax, $"Caractere inesperado '{ch}'.");
        }

        tokens.Add(new Token(TokenKind.End, string.Empty));
        return tokens;
    }

    private enum TokenKind { Number, String, Identifier, QuotedIdentifier, Column, Operator, End }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class ExprSyntaxException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    // Parser recursivo descendente, uma função por nível da gramática (15 §9.1).
    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        public Token Current => tokens[_position];

        public bool AtEnd => Current.Kind == TokenKind.End;

        public ExprNode ParseExpression(int depth)
        {
            if (depth > MaxDepth)
            {
                throw new ExprSyntaxException(ErrorCodes.GenExprLimit, "Expressão excede a profundidade máxima de 64.");
            }

            return ParseOr(depth);
        }

        private ExprNode ParseOr(int depth)
        {
            var left = ParseAnd(depth);
            while (IsKeyword("or"))
            {
                _position++;
                left = new BinaryNode("or", left, ParseAnd(depth + 1));
            }

            return left;
        }

        private ExprNode ParseAnd(int depth)
        {
            var left = ParseNot(depth);
            while (IsKeyword("and"))
            {
                _position++;
                left = new BinaryNode("and", left, ParseNot(depth + 1));
            }

            return left;
        }

        private ExprNode ParseNot(int depth)
        {
            if (IsKeyword("not"))
            {
                _position++;
                return new UnaryNode("not", ParseComparison(depth + 1));
            }

            return ParseComparison(depth);
        }

        private ExprNode ParseComparison(int depth)
        {
            var left = ParseAdditive(depth);
            if (Current.Kind == TokenKind.Operator && Current.Text is "=" or "!=" or "<" or "<=" or ">" or ">=")
            {
                var op = Current.Text;
                _position++;
                return new BinaryNode(op, left, ParseAdditive(depth + 1));
            }

            return left;
        }

        private ExprNode ParseAdditive(int depth)
        {
            var left = ParseMultiplicative(depth);
            while (Current.Kind == TokenKind.Operator && Current.Text is "+" or "-" or "||")
            {
                var op = Current.Text;
                _position++;
                left = new BinaryNode(op, left, ParseMultiplicative(depth + 1));
            }

            return left;
        }

        private ExprNode ParseMultiplicative(int depth)
        {
            var left = ParseUnary(depth);
            while (Current.Kind == TokenKind.Operator && Current.Text is "*" or "/" or "%")
            {
                var op = Current.Text;
                _position++;
                left = new BinaryNode(op, left, ParseUnary(depth + 1));
            }

            return left;
        }

        private ExprNode ParseUnary(int depth)
        {
            if (Current.Kind == TokenKind.Operator && Current.Text == "-")
            {
                _position++;
                return new UnaryNode("-", ParseUnary(depth + 1));
            }

            return ParsePrimary(depth);
        }

        private ExprNode ParsePrimary(int depth)
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    _position++;
                    return new LiteralNode(token.Text.Contains('.', StringComparison.Ordinal)
                        ? decimal.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
                        : long.Parse(token.Text, CultureInfo.InvariantCulture));

                case TokenKind.String:
                    _position++;
                    return new LiteralNode(token.Text);

                case TokenKind.Column:
                    _position++;
                    return new ColumnNode(token.Text);

                case TokenKind.Identifier:
                    _position++;
                    return token.Text.ToLowerInvariant() switch
                    {
                        "true" => new LiteralNode(true),
                        "false" => new LiteralNode(false),
                        "null" => new LiteralNode(null),
                        _ => ParseCall(token.Text, depth),
                    };

                case TokenKind.Operator when token.Text == "(":
                    _position++;
                    var inner = ParseExpression(depth + 1);
                    Expect(")");
                    return inner;

                default:
                    throw new ExprSyntaxException(ErrorCodes.GenExprSyntax, $"Token inesperado '{token.Text}'.");
            }
        }

        private CallNode ParseCall(string name, int depth)
        {
            if (!(Current.Kind == TokenKind.Operator && Current.Text == "("))
            {
                throw new ExprSyntaxException(ErrorCodes.GenExprSyntax, $"'{name}' deve ser chamada com parênteses.");
            }

            _position++;
            var args = new List<ExprNode>();
            if (!(Current.Kind == TokenKind.Operator && Current.Text == ")"))
            {
                args.Add(ParseExpression(depth + 1));
                while (Current.Kind == TokenKind.Operator && Current.Text == ",")
                {
                    _position++;
                    args.Add(ParseExpression(depth + 1));
                }
            }

            Expect(")");
            return new CallNode(name.ToLowerInvariant(), args);
        }

        private void Expect(string text)
        {
            if (!(Current.Kind == TokenKind.Operator && Current.Text == text))
            {
                throw new ExprSyntaxException(ErrorCodes.GenExprSyntax, $"Esperado '{text}'.");
            }

            _position++;
        }

        private bool IsKeyword(string word) =>
            Current.Kind == TokenKind.Identifier && string.Equals(Current.Text, word, StringComparison.OrdinalIgnoreCase);
    }
}

public enum ExprMode { Expression, Template }

// AST do MDS-Expr.
public abstract record ExprNode;

public sealed record LiteralNode(object? Value) : ExprNode;

public sealed record ColumnNode(string Name) : ExprNode;

public sealed record UnaryNode(string Operator, ExprNode Operand) : ExprNode;

public sealed record BinaryNode(string Operator, ExprNode Left, ExprNode Right) : ExprNode;

public sealed record CallNode(string Name, IReadOnlyList<ExprNode> Arguments) : ExprNode;

public sealed record TemplateNode(IReadOnlyList<ExprNode> Parts) : ExprNode;
