using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Providers;

namespace MockDataStudio.Application.Explorer;

// Scripts gerados a partir do TableModel real (C5.4, menu de contexto da árvore). O dialeto decide a citação e os literais.
public static class ObjectScripts
{
    // CREATE TABLE a partir do schema capturado: tipo nativo, NOT NULL e chave primária. Não inclui FKs, índices nem checks.
    public static string CreateTable(ObjectName name, TableModel model, ISqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(dialect);
        var lines = new List<string>();
        foreach (var column in model.Columns.OrderBy(c => c.Ordinal))
        {
            var parts = new List<string> { dialect.QuoteIdentifier(column.Name), NativeTypeParser.Format(column.Type) };
            if (!column.IsNullable)
            {
                parts.Add("NOT NULL");
            }

            lines.Add("  " + string.Join(" ", parts));
        }

        if (model.PrimaryKey is { Columns.Count: > 0 } pk)
        {
            lines.Add("  PRIMARY KEY (" + string.Join(", ", pk.Columns.Select(dialect.QuoteIdentifier)) + ")");
        }

        return "CREATE TABLE " + dialect.QualifiedName(name) + " (\n" + string.Join(",\n", lines) + "\n);";
    }

    // INSERT de exemplo com uma linha: NULL onde a coluna aceita nulo; 0 para numéricos; 'exemplo' para o resto.
    // Colunas geradas ficam de fora. Um valor de exemplo pode não ser válido para tipos de data em todos os SGBDs.
    public static string InsertExample(ObjectName name, TableModel model, ISqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(dialect);
        var columns = model.Columns.Where(c => c.IsInsertable).OrderBy(c => c.Ordinal).ToList();
        if (columns.Count == 0)
        {
            return "-- Tabela sem colunas inseríveis.";
        }

        var values = columns
            .Select(c => c.IsNullable ? dialect.Literals.Null : dialect.Literals.Format(SampleValue(c.Type), c.Type))
            .ToList();
        return dialect.BuildInsert(name, columns.Select(c => c.Name).ToList(), [values], overrideClause: null);
    }

    private static object SampleValue(DataTypeInfo type) =>
        type.IntegerBits is not null || type.LogicalType == LogicalType.Decimal ? 0 : "exemplo";
}
