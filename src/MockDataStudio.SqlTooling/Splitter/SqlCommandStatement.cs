using MockDataStudio.SqlTooling.Classification;
using MockDataStudio.SqlTooling.Tokens;

namespace MockDataStudio.SqlTooling.Splitter;

/// <summary>
/// Representa um comando SQL individual resultante da divisão de um script por ISqlScriptSplitter.
/// </summary>
public sealed record SqlCommandStatement(
    string Text,
    int StartOffset,
    int Length,
    int LineNumber,
    string Terminator,
    StatementKind Kind,
    bool IsDestructive,
    bool HasWhereClause,
    IReadOnlyList<SqlToken> Tokens);
