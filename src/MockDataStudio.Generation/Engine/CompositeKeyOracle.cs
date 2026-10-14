using MockDataStudio.Core.Naming;

namespace MockDataStudio.Generation.Engine;

// Índice de grupos de filhos (docs/16 §6.2 e §5.3): counts[g] é quantas linhas filhas pertencem ao pai g.
// Resolve, em O(log n), a qual grupo pertence uma linha e qual é o rel.child_seq (1-based) dentro do grupo.
public sealed class ChildSequenceIndex
{
    private readonly long[] _prefix;

    public ChildSequenceIndex(IReadOnlyList<long> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        _prefix = new long[counts.Count + 1];
        for (var g = 0; g < counts.Count; g++)
        {
            if (counts[g] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(counts), "Contagem de filhos não pode ser negativa.");
            }

            _prefix[g + 1] = checked(_prefix[g] + counts[g]);
        }
    }

    public long RowCount => _prefix[^1];

    public int GroupCount => _prefix.Length - 1;

    // Maior grupo g com prefix[g] <= row. Grupos vazios são pulados naturalmente.
    public int GroupOf(long row)
    {
        if (row < 0 || row >= RowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row), $"Linha {row} fora de 0..{RowCount - 1}.");
        }

        int low = 0, high = GroupCount - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (_prefix[mid] <= row)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    // rel.child_seq: posição 1-based da linha dentro do seu grupo.
    public long SeqOf(long row) => row - _prefix[GroupOf(row)] + 1;
}

// Componente de uma chave composta. Cada componente fornece o valor da linha i para uma coluna.
public abstract class KeyComponent(string column)
{
    public string Column { get; } = column;

    public abstract object? ValueAt(long row);
}

// Valor vindo da chave de uma linha do pai (ex.: item_pedido.pedido_id = chave do pedido do grupo da linha).
public sealed class ParentKeyComponent(string column, IKeyOracle parentOracle, ObjectName parentTable, string parentColumn, ChildSequenceIndex grouping)
    : KeyComponent(column)
{
    public override object? ValueAt(long row) =>
        parentOracle.GetKey(parentTable, [parentColumn], grouping.GroupOf(row))[0];
}

// Valor de sequência dentro do grupo (ex.: item_pedido.item_seq = 1, 2, ... por pedido).
public sealed class SequenceComponent(string column, ChildSequenceIndex grouping) : KeyComponent(column)
{
    public override object? ValueAt(long row) => grouping.SeqOf(row);
}

// Oráculo de chaves compostas (16 §5.3): a tupla da linha i é a combinação dos componentes, na ordem das colunas da chave.
// Cada tabela é registrada uma vez com seus componentes; chaves simples continuam no ExplicitKeyOracle.
public sealed class CompositeKeyOracle
{
    private readonly Dictionary<ObjectName, (IReadOnlyList<KeyComponent> Components, long Rows)> _tables = [];

    public void Register(ObjectName table, IReadOnlyList<KeyComponent> components, long rowCount)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count < 2)
        {
            throw new ArgumentException("Chave composta precisa de pelo menos dois componentes.", nameof(components));
        }

        if (components.Select(c => c.Column).Distinct(StringComparer.Ordinal).Count() != components.Count)
        {
            throw new ArgumentException("Componentes da chave com coluna repetida.", nameof(components));
        }

        _tables[table] = (components, rowCount);
    }

    public object?[] GetKey(ObjectName table, IReadOnlyList<string> keyColumns, long rowIndex)
    {
        ArgumentNullException.ThrowIfNull(keyColumns);
        if (!_tables.TryGetValue(table, out var entry))
        {
            throw new NotSupportedException($"Tabela '{table.CanonicalId}' não tem chave composta registrada.");
        }

        if (keyColumns.Count != entry.Components.Count
            || !keyColumns.SequenceEqual(entry.Components.Select(c => c.Column), StringComparer.Ordinal))
        {
            throw new NotSupportedException($"Colunas pedidas não são a chave composta de '{table.CanonicalId}'.");
        }

        if (rowIndex < 0 || rowIndex >= entry.Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex), $"Índice {rowIndex} fora de 0..{entry.Rows - 1}.");
        }

        return entry.Components.Select(c => c.ValueAt(rowIndex)).ToArray();
    }

    public long RowCount(ObjectName table) =>
        _tables.TryGetValue(table, out var entry) ? entry.Rows : throw new NotSupportedException($"Tabela '{table.CanonicalId}' não está registrada.");
}
