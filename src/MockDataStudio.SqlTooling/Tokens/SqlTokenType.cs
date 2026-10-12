using System.Diagnostics.CodeAnalysis;

namespace MockDataStudio.SqlTooling.Tokens;

/// <summary>
/// Tipos de tokens léxicos SQL conforme especificado em docs/20 §4 e ADR-007.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifiers should not contain type names", Justification = "Conforme especificação de tokens em docs/20 §4")]
public enum SqlTokenType
{
    Whitespace,
    LineComment,
    BlockComment,
    String,
    NationalString,
    EscapeString,
    DollarQuoted,
    QuotedIdentifier,
    Number,
    Word,
    Operator,
    Terminator,
    ClientDirective
}
