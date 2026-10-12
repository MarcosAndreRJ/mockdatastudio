using System.Data.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface IBulkInserter : IAsyncDisposable
{
    BulkInsertMethod Method { get; }
    Task PrepareAsync(ObjectName table, IReadOnlyList<ColumnModel> columns, InsertOptions options, CancellationToken ct);
    Task<BulkInsertResult> InsertAsync(IReadOnlyList<object?[]> rows, DbTransaction transaction, CancellationToken ct);
}
