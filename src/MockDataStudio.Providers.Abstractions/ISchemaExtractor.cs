using System.Data.Common;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface ISchemaExtractor
{
    Task<Result<SchemaModel>> ExtractAsync(
        DbConnection connection,
        CaptureOptions options,
        IProgress<ExtractionProgress>? progress,
        CancellationToken ct);

    Task<Result<TableModel>> ExtractTableAsync(
        DbConnection connection,
        ObjectName table,
        CancellationToken ct);

    Task<IReadOnlyList<ObjectName>> ListTablesAsync(
        DbConnection connection,
        CancellationToken ct);
}
