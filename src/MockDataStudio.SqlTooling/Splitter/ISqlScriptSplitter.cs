using MockDataStudio.SqlTooling.Dialect;

namespace MockDataStudio.SqlTooling.Splitter;

/// <summary>
/// Contrato do divisor de scripts SQL por dialeto conforme ADR-007 e docs/20 §4.
/// </summary>
public interface ISqlScriptSplitter
{
    IReadOnlyList<SqlCommandStatement> Split(string script, SqlDialectFeatures dialect);
}
