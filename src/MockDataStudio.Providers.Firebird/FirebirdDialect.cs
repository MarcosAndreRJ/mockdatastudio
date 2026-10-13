using System.Globalization;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Firebird;

// Dialeto Firebird (docs/10 §6, docs/18 §4–5). Namespace PUBLIC é virtual: o nome nunca é qualificado.
// Um registro por INSERT, commits a cada 5.000 linhas, 200 tuplas por condição e predicado com OR (sem comparação
// de tuplas). Resync por versão: FB 2.5 usa SET GENERATOR; no FB 3 "RESTART WITH v" faz o próximo valor ser v+1
// e no FB 4+ faz ser v (PV-005).
public sealed class FirebirdDialect(int majorVersion = 3) : ISqlDialect
{
    public const string VirtualNamespace = "PUBLIC";

    public DatabaseEngine Engine => DatabaseEngine.Firebird;

    public int MajorVersion { get; } = majorVersion;

    public ISqlLiteralFormatter Literals { get; } = new FirebirdLiteralFormatter(majorVersion);

    public string StatementTerminator => ";";

    // isql abre a transação sozinho; o script só emite COMMIT.
    public string? BeginTransaction => null;

    public string Commit => "COMMIT;";

    public int RecommendedRowsPerInsert => 1;

    public long RecommendedCommitRows => 5_000;

    public int MaxTuplesPerPredicate => 200;

    public string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public string QualifiedName(ObjectName name) => QuoteIdentifier(name.Name);

    public IReadOnlyList<string> SessionPreamble(ScriptOptions options) => [];

    public string? DisableFkChecks() => null;

    public string? EnableFkChecks() => null;

    public string? IdentityOverrideClause(ColumnModel identityColumn) =>
        MajorVersion >= 4 && identityColumn.Identity?.Kind == IdentityKind.IdentityAlways ? "OVERRIDING SYSTEM VALUE" : null;

    public IReadOnlyList<string> ResyncSequenceStatements(ObjectName table, ColumnModel column, string? sequenceName, long nextValue)
    {
        if (string.IsNullOrEmpty(sequenceName))
        {
            return [];
        }

        var name = QuoteIdentifier(sequenceName);
        return MajorVersion switch
        {
            < 3 => [$"SET GENERATOR {name} TO {(nextValue - 1).ToString(CultureInfo.InvariantCulture)};"],
            3 => [$"ALTER SEQUENCE {name} RESTART WITH {(nextValue - 1).ToString(CultureInfo.InvariantCulture)};"],
            _ => [$"ALTER SEQUENCE {name} RESTART WITH {nextValue.ToString(CultureInfo.InvariantCulture)};"],
        };
    }

    // Firebird não aceita INSERT multi-linha: um comando por linha.
    public string BuildInsert(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalRows, string? overrideClause)
    {
        var head = "INSERT INTO " + QualifiedName(table) + " (" + string.Join(", ", columns.Select(QuoteIdentifier)) + ")"
                   + (string.IsNullOrEmpty(overrideClause) ? string.Empty : " " + overrideClause) + " VALUES (";
        return string.Join("\n", literalRows.Select(row => head + string.Join(", ", row) + ");"));
    }

    public string BuildTuplePredicate(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalTuples)
    {
        if (columns.Count == 1)
        {
            return QuoteIdentifier(columns[0]) + " IN (" + string.Join(", ", literalTuples.Select(t => t[0])) + ")";
        }

        return string.Join(" OR ", literalTuples.Select(t =>
            "(" + string.Join(" AND ", columns.Select((c, i) => QuoteIdentifier(c) + " = " + t[i])) + ")"));
    }

    // Firebird não tem LIMIT: SELECT FIRST n.
    public string BuildLimitedSelect(string columns, string target, int limit) =>
        "SELECT FIRST " + limit.ToString(CultureInfo.InvariantCulture) + " " + columns + " FROM " + target;
}

public sealed class FirebirdLiteralFormatter(int majorVersion = 3) : ISqlLiteralFormatter
{
    public string Null => "NULL";

    public string Format(object? value, DataTypeInfo type)
    {
        return value switch
        {
            null => Null,
            // BOOLEAN só existe a partir do Firebird 3; no 2.5 a convenção é SMALLINT 0/1.
            bool b => majorVersion >= 3 && type.LogicalType == LogicalType.Boolean ? (b ? "TRUE" : "FALSE") : (b ? "1" : "0"),
            sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            float f => ((double)f).ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            // Firebird guarda até décimos de milissegundo (4 casas).
            DateTime dt => Text(dt.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture)),
            DateTimeOffset dto => Text(dto.ToString("yyyy-MM-dd HH:mm:ss.ffff zzz", CultureInfo.InvariantCulture)),
            DateOnly d => Text(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            TimeOnly t => Text(t.ToString("HH:mm:ss.ffff", CultureInfo.InvariantCulture)),
            Guid g => "CHAR_TO_UUID(" + Text(g.ToString("D", CultureInfo.InvariantCulture)) + ")",
            byte[] bytes => bytes.Length == 0 ? "''" : "x'" + Convert.ToHexString(bytes) + "'",
            string s => Text(s),
            _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
        };
    }

    public static string Text(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}
