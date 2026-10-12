using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface ISqlDialect
{
    DatabaseEngine Engine { get; }
    string QuoteIdentifier(string identifier);
    string QualifiedName(ObjectName name);
    ISqlLiteralFormatter Literals { get; }
    string StatementTerminator { get; }
    string? BeginTransaction { get; }
    string Commit { get; }
    IReadOnlyList<string> SessionPreamble(ScriptOptions options);
    string? DisableFkChecks();
    string? EnableFkChecks();
    string? IdentityOverrideClause(ColumnModel identityColumn);
    IReadOnlyList<string> ResyncSequenceStatements(ObjectName table, ColumnModel column, string? sequenceName, long nextValue);
    string BuildInsert(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalRows, string? overrideClause);
    int RecommendedRowsPerInsert { get; }
}
