using System.Globalization;
using System.Text;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Sqlite;

// Dialeto SQLite: quoting, literais, INSERT e sessão (docs/10 §3). Namespace virtual "main" não aparece no SQL.
public sealed class SqliteDialect(string dateStorage = SqliteValues.DateStorageText) : ISqlDialect
{
    public DatabaseEngine Engine => DatabaseEngine.Sqlite;

    public ISqlLiteralFormatter Literals { get; } = new SqliteLiteralFormatter(dateStorage);

    public string StatementTerminator => ";";

    public string? BeginTransaction => "BEGIN;";

    public string Commit => "COMMIT;";

    // Limite conservador de linhas por INSERT multi-linha (VALUES).
    public int RecommendedRowsPerInsert => 500;

    public IReadOnlyList<string> DeferConstraintStatements() => ["PRAGMA defer_foreign_keys = ON;"];

    public string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public string QualifiedName(ObjectName name) => QuoteIdentifier(name.Name);

    public IReadOnlyList<string> SessionPreamble(ScriptOptions options) => ["PRAGMA foreign_keys = ON;"];

    public string? DisableFkChecks() => "PRAGMA foreign_keys = OFF;";

    public string? EnableFkChecks() => "PRAGMA foreign_keys = ON;";

    // Sem identity explícito: INTEGER PRIMARY KEY aceita valor explícito (rowid).
    public string? IdentityOverrideClause(ColumnModel identityColumn) => null;

    // SQLite não tem sequences; o próximo rowid é max(rowid)+1 automaticamente (T-PRV-SQLITE-010).
    public IReadOnlyList<string> ResyncSequenceStatements(ObjectName table, ColumnModel column, string? sequenceName, long nextValue) => [];

    public string BuildInsert(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalRows, string? overrideClause)
    {
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(QualifiedName(table)).Append(" (")
          .Append(string.Join(",", columns.Select(QuoteIdentifier))).Append(") VALUES\n");
        for (var r = 0; r < literalRows.Count; r++)
        {
            sb.Append('(').Append(string.Join(",", literalRows[r])).Append(')');
            sb.Append(r == literalRows.Count - 1 ? ";" : ",\n");
        }

        return sb.ToString();
    }
}

// Literais SQLite: escapes por aspas simples; NUL embutido vira char(0) (SQL textual não comporta \0).
public sealed class SqliteLiteralFormatter(string dateStorage) : ISqlLiteralFormatter
{
    public string Null => "NULL";

    public string Format(object? value, DataTypeInfo type)
    {
        var stored = SqliteValues.ToStorage(value, type, dateStorage);
        return stored switch
        {
            null => Null,
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d when double.IsFinite(d) => d.ToString("R", CultureInfo.InvariantCulture),
            double => throw new ArgumentOutOfRangeException(nameof(value), "SQLite não aceita NaN/Infinity em literais."),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
            string s => QuoteText(s),
            _ => QuoteText(stored.ToString() ?? string.Empty),
        };
    }

    private static string QuoteText(string text)
    {
        var parts = text.Split('\0');
        return string.Join("||char(0)||", parts.Select(p => "'" + p.Replace("'", "''", StringComparison.Ordinal) + "'"));
    }
}
