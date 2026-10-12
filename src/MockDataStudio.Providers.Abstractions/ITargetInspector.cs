using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Providers;

public interface ITargetInspector
{
    Task<long?> GetMaxIntegerAsync(ObjectName table, string column, CancellationToken ct);
    Task<long?> GetSequenceNextValueAsync(string sequenceName, CancellationToken ct);
    Task<long> CountRowsAsync(ObjectName table, CancellationToken ct);
    Task<IReadOnlyList<TriggerModel>> GetActiveInsertTriggersAsync(ObjectName table, CancellationToken ct);
    Task<bool> AllKeysExistAsync(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<object?[]> keys, CancellationToken ct);
    IAsyncEnumerable<object?[]> ReadKeysAsync(ObjectName table, IReadOnlyList<string> columns, long limit, CancellationToken ct);
}
