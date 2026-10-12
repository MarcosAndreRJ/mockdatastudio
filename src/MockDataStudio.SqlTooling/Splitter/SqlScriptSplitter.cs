using System.Text.RegularExpressions;
using MockDataStudio.SqlTooling.Classification;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Lexer;
using MockDataStudio.SqlTooling.Tokens;

namespace MockDataStudio.SqlTooling.Splitter;

/// <summary>
/// Implementação do divisor de scripts SQL por dialeto conforme ADR-007 e docs/20 §4.
/// Trata terminadores customizados (DELIMITER, SET TERM), blocos BEGIN/END aninhados e corpos dollar-quoted.
/// </summary>
public sealed class SqlScriptSplitter : ISqlScriptSplitter
{
    private static readonly Regex MySqlDelimiterRegex = new(@"^\s*DELIMITER\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FirebirdSetTermRegex = new(@"^\s*SET\s+TERM\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<SqlCommandStatement> Split(string script, SqlDialectFeatures dialect)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Array.Empty<SqlCommandStatement>();
        }

        var results = new List<SqlCommandStatement>();
        var activeTerminator = ";";
        var lexer = new SqlLexer(script, dialect, activeTerminator);

        var currentTokens = new List<SqlToken>();
        var statementStartOffset = -1;
        var statementStartLine = 1;

        // Block tracking
        var blockDepth = 0;
        var inRoutinesBlock = false;
        var hasSeenFirstBegin = false;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Length == 0 && token.Type == SqlTokenType.Terminator)
            {
                // End of script
                break;
            }

            // 1. Client directives handling
            if (token.Type == SqlTokenType.ClientDirective)
            {
                var text = token.Text.Trim();
                if (dialect.DelimiterDirective)
                {
                    var match = MySqlDelimiterRegex.Match(text);
                    if (match.Success)
                    {
                        activeTerminator = match.Groups[1].Value;
                        lexer.Terminator = activeTerminator;
                        continue;
                    }
                }
                else if (dialect.SetTermDirective)
                {
                    var match = FirebirdSetTermRegex.Match(text);
                    if (match.Success)
                    {
                        activeTerminator = match.Groups[1].Value;
                        lexer.Terminator = activeTerminator;
                        continue;
                    }
                }
            }

            // Record start offset of statement if not started
            if (token.Type != SqlTokenType.Whitespace &&
                token.Type != SqlTokenType.LineComment &&
                token.Type != SqlTokenType.BlockComment)
            {
                if (statementStartOffset < 0)
                {
                    statementStartOffset = token.StartOffset;
                    statementStartLine = token.LineNumber;
                }
            }

            currentTokens.Add(token);

            // 2. Detect routine / procedure / trigger block start without custom delimiter
            if (activeTerminator == ";")
            {
                if (!inRoutinesBlock && currentTokens.Count(t => t.Type == SqlTokenType.Word) <= 5)
                {
                    var wordTokens = currentTokens
                        .Where(t => t.Type == SqlTokenType.Word)
                        .Select(t => t.Text.ToUpperInvariant())
                        .ToList();

                    if (dialect.StartsRoutineBody(wordTokens))
                    {
                        inRoutinesBlock = true;
                    }
                }

                // Track block depth (BEGIN / END, CASE / END)
                if (token.Type == SqlTokenType.Word)
                {
                    var upper = token.Text.ToUpperInvariant();
                    if (upper == "BEGIN" || upper == "CASE")
                    {
                        blockDepth++;
                        hasSeenFirstBegin = true;
                    }
                    else if (upper == "END")
                    {
                        if (blockDepth > 0)
                        {
                            blockDepth--;
                        }
                    }
                }
            }

            // 3. Statement termination check
            var isTerminatorToken = token.Type == SqlTokenType.Terminator && token.Text == activeTerminator;

            // If we are tracking blocks (in routine without custom delimiter), only terminate if blockDepth is 0
            var canTerminate = !inRoutinesBlock || (hasSeenFirstBegin && blockDepth == 0);

            if (isTerminatorToken && canTerminate)
            {
                // Emit statement
                EmitStatement(results, script, currentTokens, statementStartOffset, statementStartLine, activeTerminator);

                // Reset state
                currentTokens.Clear();
                statementStartOffset = -1;
                inRoutinesBlock = false;
                hasSeenFirstBegin = false;
                blockDepth = 0;
            }
        }

        // Remaining tokens after script ends (without trailing terminator)
        if (currentTokens.Count > 0 && statementStartOffset >= 0)
        {
            EmitStatement(results, script, currentTokens, statementStartOffset, statementStartLine, activeTerminator);
        }

        return results;
    }

    private static void EmitStatement(
        List<SqlCommandStatement> results,
        string script,
        List<SqlToken> tokens,
        int startOffset,
        int startLine,
        string terminator)
    {
        var nonTriviaTokens = tokens
            .Where(t => t.Type != SqlTokenType.Whitespace &&
                        t.Type != SqlTokenType.LineComment &&
                        t.Type != SqlTokenType.BlockComment &&
                        t.Type != SqlTokenType.Terminator)
            .ToList();

        if (nonTriviaTokens.Count == 0 || startOffset < 0)
        {
            return;
        }

        var lastToken = tokens.Last(t => t.Type != SqlTokenType.Whitespace);
        var endOffset = lastToken.StartOffset + lastToken.Length;

        var rawText = script.Substring(startOffset, endOffset - startOffset).Trim();

        var (kind, isDestructive, hasWhere) = SqlClassifier.Classify(nonTriviaTokens);

        results.Add(new SqlCommandStatement(
            rawText,
            startOffset,
            endOffset - startOffset,
            startLine,
            terminator,
            kind,
            isDestructive,
            hasWhere,
            nonTriviaTokens));
    }
}
