namespace MockDataStudio.Application.Explorer;

// Uma execução registrada no histórico do editor (C6r.4, T-SQL-HIST-001). Só texto e metadados: nunca credenciais.
public sealed record HistoryEntry(DateTimeOffset At, string Text, StatementStatus Status, long? Rows, TimeSpan Duration);

// Histórico em memória, limitado em tamanho. Mais recente primeiro.
public sealed class ExecutionHistory(int capacity = 200)
{
    private readonly List<HistoryEntry> _entries = [];

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public IReadOnlyList<HistoryEntry> Entries => _entries.AsReadOnly();

    public void Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Insert(0, entry);
        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
    }

    public void Clear() => _entries.Clear();
}
