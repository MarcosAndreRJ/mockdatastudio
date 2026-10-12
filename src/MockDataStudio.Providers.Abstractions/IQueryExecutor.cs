using System.Data.Common;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface IQueryExecutor
{
    IAsyncEnumerable<QueryResultPage> ExecuteAsync(
        DbConnection connection,
        DbTransaction? tx,
        string sql,
        QueryOptions options,
        CancellationToken ct);
}
