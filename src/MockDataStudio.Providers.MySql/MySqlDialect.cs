using System.Globalization;
using System.Text;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.MySql;

// Dialeto MySQL: crases, literais com escape de barra invertida (sql_mode padrão, sem NO_BACKSLASH_ESCAPES),
// INSERT multi-linha e desligamento de FK por sessão (docs/10 §4, docs/18).
public sealed class MySqlDialect : ISqlDialect
{
    public DatabaseEngine Engine => DatabaseEngine.MySql;

    public ISqlLiteralFormatter Literals { get; } = new MySqlLiteralFormatter();

    public string StatementTerminator => ";";

    public string? BeginTransaction => "START TRANSACTION;";

    public string Commit => "COMMIT;";

    public int RecommendedRowsPerInsert => 500;

    // docs/18 §5: commits mais frequentes no MySQL (logs de undo do InnoDB).
    public long RecommendedCommitRows => 10_000;

    public string QuoteIdentifier(string identifier) =>
        "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";

    // Namespace do MySQL é o database: `db`.`tabela`.
    public string QualifiedName(ObjectName name) =>
        string.IsNullOrEmpty(name.Namespace) ? QuoteIdentifier(name.Name) : QuoteIdentifier(name.Namespace) + "." + QuoteIdentifier(name.Name);

    public IReadOnlyList<string> SessionPreamble(ScriptOptions options) => ["SET NAMES utf8mb4;"];

    public string? DisableFkChecks() => "SET FOREIGN_KEY_CHECKS = 0;";

    public string? EnableFkChecks() => "SET FOREIGN_KEY_CHECKS = 1;";

    // AUTO_INCREMENT aceita valor explícito sem cláusula.
    public string? IdentityOverrideClause(ColumnModel identityColumn) => null;

    // O InnoDB ajusta o contador de AUTO_INCREMENT após valores explícitos maiores (docs/10 §4): nada a emitir.
    public IReadOnlyList<string> ResyncSequenceStatements(ObjectName table, ColumnModel column, string? sequenceName, long nextValue) => [];

    public string BuildInsert(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalRows, string? overrideClause)
    {
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(QualifiedName(table)).Append(" (")
          .Append(string.Join(", ", columns.Select(QuoteIdentifier))).Append(") VALUES\n");
        for (var r = 0; r < literalRows.Count; r++)
        {
            sb.Append('(').Append(string.Join(", ", literalRows[r])).Append(')');
            sb.Append(r == literalRows.Count - 1 ? ";" : ",\n");
        }

        return sb.ToString();
    }
}

// Literais MySQL. Escapa barra invertida, NUL, quebras de linha e Ctrl+Z (sql_mode sem NO_BACKSLASH_ESCAPES).
public sealed class MySqlLiteralFormatter : ISqlLiteralFormatter
{
    public string Null => "NULL";

    public string Format(object? value, DataTypeInfo type)
    {
        return value switch
        {
            null => Null,
            bool b => b ? "1" : "0",
            sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            float f => FloatingOrNull(f),
            double d => FloatingOrNull(d),
            DateTime dt => Text(dt.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)),
            DateTimeOffset dto => Text(dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)),
            DateOnly d => Text(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            TimeOnly t => Text(t.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture)),
            Guid g => Text(g.ToString("D", CultureInfo.InvariantCulture)),
            byte[] bytes => bytes.Length == 0 ? "''" : "X'" + Convert.ToHexString(bytes) + "'",
            string s => Text(s),
            _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
        };
    }

    public static string Text(string text)
    {
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('\'');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\'': sb.Append("''"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\0': sb.Append("\\0"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\u001A': sb.Append("\\Z"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.Append('\'').ToString();
    }

    // MySQL não tem NaN/Infinity em colunas numéricas: grava NULL em vez de um script inválido.
    private static string FloatingOrNull(double d) =>
        double.IsNaN(d) || double.IsInfinity(d) ? "NULL" : d.ToString("R", CultureInfo.InvariantCulture);
}
