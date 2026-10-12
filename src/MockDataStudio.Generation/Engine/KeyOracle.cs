using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Generation.Planning;

namespace MockDataStudio.Generation.Engine;

// Fornece a chave da linha i sem armazenar o conjunto (16 §5.3). Esta etapa cobre só a chave Explicit inteira de coluna única:
// key(i) = start + i·step. Captured, DatabaseDefault, chaves compostas e ExistingPool (fontes Existing/Fixed/Dataset/Query)
// ainda não têm implementação e lançam NotSupportedException.
public interface IKeyOracle
{
    object?[] GetKey(ObjectName table, IReadOnlyList<string> keyColumns, long rowIndex);

    long RowCount(ObjectName table);
}

public sealed class ExplicitKeyOracle : IKeyOracle
{
    private readonly Dictionary<ObjectName, (KeyColumnPlan Key, long Rows)> _tables = [];

    public ExplicitKeyOracle(GenerationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var table in plan.Tables.Values)
        {
            var key = table.Keys.FirstOrDefault(k => k.Strategy == KeyStrategy.Explicit && k.Start is not null);
            if (key is not null)
            {
                _tables[table.Table] = (key, table.RowCount);
            }
        }
    }

    public object?[] GetKey(ObjectName table, IReadOnlyList<string> keyColumns, long rowIndex)
    {
        ArgumentNullException.ThrowIfNull(keyColumns);
        if (!_tables.TryGetValue(table, out var entry))
        {
            throw new NotSupportedException($"Tabela '{table.CanonicalId}' não tem chave Explicit inteira no plano.");
        }

        if (keyColumns.Count != 1 || keyColumns[0] != entry.Key.Column)
        {
            throw new NotSupportedException($"Chave de '{table.CanonicalId}' não suportada nesta etapa (coluna única '{entry.Key.Column}').");
        }

        if (rowIndex < 0 || rowIndex >= entry.Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex), $"Índice {rowIndex} fora de 0..{entry.Rows - 1}.");
        }

        return [checked(entry.Key.Start!.Value + (rowIndex * entry.Key.Step))];
    }

    public long RowCount(ObjectName table) =>
        _tables.TryGetValue(table, out var entry) ? entry.Rows : throw new NotSupportedException($"Tabela '{table.CanonicalId}' não está no plano.");
}
