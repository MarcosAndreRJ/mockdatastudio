namespace MockDataStudio.SqlTooling.Tokens;

/// <summary>
/// Representa um token SQL com posição original no script.
/// </summary>
public readonly record struct SqlToken(
    SqlTokenType Type,
    string Text,
    int StartOffset,
    int Length,
    int LineNumber,
    int ColumnNumber)
{
    public override string ToString() => $"[{Type}] '{Text}' ({LineNumber}:{ColumnNumber})";
}
