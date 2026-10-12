namespace MockDataStudio.SqlTooling.Dialect;

/// <summary>
/// Características léxicas e de divisão de um dialeto SQL (docs/20 §4). O SqlTooling não conhece SGBDs:
/// cada provider usa a instância correspondente. Adicionar um dialeto não exige alterar lexer ou splitter.
/// </summary>
public sealed record SqlDialectFeatures
{
    public SqlDialectFeatures(
        string name,
        bool hashLineComments = false,
        bool executableComments = false,
        bool dollarQuotedStrings = false,
        bool escapeStrings = false,
        bool backslashEscapes = false,
        bool backtickIdentifiers = false,
        bool bracketIdentifiers = false,
        bool delimiterDirective = false,
        bool setTermDirective = false,
        Func<IReadOnlyList<string>, bool>? startsRoutineBody = null)
    {
        Name = name;
        HashLineComments = hashLineComments;
        ExecutableComments = executableComments;
        DollarQuotedStrings = dollarQuotedStrings;
        EscapeStrings = escapeStrings;
        BackslashEscapes = backslashEscapes;
        BacktickIdentifiers = backtickIdentifiers;
        BracketIdentifiers = bracketIdentifiers;
        DelimiterDirective = delimiterDirective;
        SetTermDirective = setTermDirective;
        StartsRoutineBody = startsRoutineBody ?? (_ => false);
    }

    public string Name { get; }

    // "#" inicia comentário de linha (MySQL).
    public bool HashLineComments { get; }

    // "/*! ... */" é tratado como código (MySQL, docs/20 §4).
    public bool ExecutableComments { get; }

    // Strings com dollar-quoting "$tag$ ... $tag$" (PostgreSQL).
    public bool DollarQuotedStrings { get; }

    // E'...' com escapes de barra invertida (PostgreSQL).
    public bool EscapeStrings { get; }

    // Barra invertida escapa caracteres dentro de strings.
    public bool BackslashEscapes { get; }

    // Identificadores entre crases `...`.
    public bool BacktickIdentifiers { get; }

    // Identificadores entre colchetes [...] (SQLite).
    public bool BracketIdentifiers { get; }

    // Diretiva "DELIMITER x" (cliente MySQL).
    public bool DelimiterDirective { get; }

    // Diretiva "SET TERM x y" (isql Firebird).
    public bool SetTermDirective { get; }

    // Indica se as palavras da instrução atual (maiúsculas) abrem um corpo de rotina com BEGIN/END.
    public Func<IReadOnlyList<string>, bool> StartsRoutineBody { get; }

    public static readonly SqlDialectFeatures Sqlite = new(
        "sqlite",
        backtickIdentifiers: true,
        bracketIdentifiers: true);

    public static readonly SqlDialectFeatures MySql = new(
        "mysql",
        hashLineComments: true,
        executableComments: true,
        backslashEscapes: true,
        backtickIdentifiers: true,
        delimiterDirective: true,
        startsRoutineBody: words => words.Contains("CREATE")
            && (words.Contains("PROCEDURE") || words.Contains("FUNCTION") || words.Contains("TRIGGER") || words.Contains("EVENT")));

    public static readonly SqlDialectFeatures PostgreSql = new(
        "postgresql",
        dollarQuotedStrings: true,
        escapeStrings: true,
        backslashEscapes: true,
        startsRoutineBody: words => words.Contains("BEGIN") && words.Contains("ATOMIC"));

    public static readonly SqlDialectFeatures Firebird = new(
        "firebird",
        setTermDirective: true,
        startsRoutineBody: words => (words.Contains("EXECUTE") && words.Contains("BLOCK"))
            || ((words.Contains("CREATE") || words.Contains("ALTER") || words.Contains("RECREATE"))
                && (words.Contains("PROCEDURE") || words.Contains("TRIGGER") || words.Contains("FUNCTION") || words.Contains("PACKAGE"))));

    // Localiza as características pelo nome do dialeto (ex.: "mysql"). Usado por testes e ferramentas de linha de comando.
    public static SqlDialectFeatures ByName(string name) => name.ToLowerInvariant() switch
    {
        "sqlite" => Sqlite,
        "mysql" => MySql,
        "postgresql" => PostgreSql,
        "firebird" => Firebird,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Dialeto SQL desconhecido."),
    };
}
