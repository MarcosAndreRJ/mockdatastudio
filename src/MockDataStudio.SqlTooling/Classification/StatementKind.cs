namespace MockDataStudio.SqlTooling.Classification;

/// <summary>
/// Classificação de alto nível de comandos SQL conforme especificado em docs/20 §5.
/// </summary>
public enum StatementKind
{
    Select,
    Insert,
    Update,
    Delete,
    Merge,
    Ddl,
    Transaction,
    Session,
    Procedure,
    Other
}
