using MockDataStudio.Core.Naming;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Engine;

// Oráculo de chave UUID v4 (16 §5.3): key(i) é função pura de (seed, tabela, coluna, i), sem estado nem ordem de chamada.
// Usa a mesma regra de bits de UuidV4.Create, com um fluxo próprio ("key"), separado do fluxo do gerador de valores.
// Filhos que referenciam a chave consultam este oráculo, e não o gerador, para obter o mesmo valor.
public sealed class UuidKeyOracle : IKeyOracle
{
    private readonly ulong _seed;
    private readonly Dictionary<ObjectName, (string Column, long Rows)> _tables = [];

    public UuidKeyOracle(ulong seed) => _seed = seed;

    public void Register(ObjectName table, string column, long rowCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        _tables[table] = (column, rowCount);
    }

    public object?[] GetKey(ObjectName table, IReadOnlyList<string> keyColumns, long rowIndex)
    {
        ArgumentNullException.ThrowIfNull(keyColumns);
        if (!_tables.TryGetValue(table, out var entry))
        {
            throw new NotSupportedException($"Tabela '{table.CanonicalId}' não tem chave UUID registrada.");
        }

        if (keyColumns.Count != 1 || keyColumns[0] != entry.Column)
        {
            throw new NotSupportedException($"Chave de '{table.CanonicalId}' não suportada (coluna única '{entry.Column}').");
        }

        if (rowIndex < 0 || rowIndex >= entry.Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex), $"Índice {rowIndex} fora de 0..{entry.Rows - 1}.");
        }

        var key = StreamKey.Derive(_seed, $"{table.CanonicalId}.{entry.Column}", "key");
        var rng = DeterministicRandom.For(key, rowIndex);
        return [UuidV4.Create(ref rng)];
    }

    public long RowCount(ObjectName table) =>
        _tables.TryGetValue(table, out var entry) ? entry.Rows : throw new NotSupportedException($"Tabela '{table.CanonicalId}' não está registrada.");
}
