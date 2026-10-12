using System.Data.Common;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers;

namespace MockDataStudio.Application.Explorer;

// Navegação sob demanda sobre o contrato ISchemaExtractor (docs/11 §6). Cada consulta recupera somente o necessário:
// nomes para listagens e um único TableModel para inspeção. O cache vive na sessão e é descartado por Refresh().
public sealed class DatabaseNavigator(IDatabaseProvider provider)
{
    private readonly Dictionary<string, TableModel> _tables = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<string>> ListSchemasAsync(DbConnection connection, CancellationToken ct)
    {
        var tables = await provider.CreateSchemaExtractor().ListTablesAsync(connection, ct);
        var views = await provider.CreateSchemaExtractor().ListViewsAsync(connection, ct);
        return tables.Concat(views).Select(o => o.Namespace).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<ObjectName>> ListTablesAsync(DbConnection connection, string schema, CancellationToken ct) =>
        (await provider.CreateSchemaExtractor().ListTablesAsync(connection, ct))
        .Where(o => string.Equals(o.Namespace, schema, StringComparison.Ordinal))
        .OrderBy(o => o.Name, StringComparer.Ordinal)
        .ToList();

    public async Task<IReadOnlyList<ObjectName>> ListViewsAsync(DbConnection connection, string schema, CancellationToken ct) =>
        (await provider.CreateSchemaExtractor().ListViewsAsync(connection, ct))
        .Where(o => string.Equals(o.Namespace, schema, StringComparison.Ordinal))
        .OrderBy(o => o.Name, StringComparer.Ordinal)
        .ToList();

    public async Task<IReadOnlyList<TriggerModel>> ListTriggersAsync(DbConnection connection, string schema, CancellationToken ct) =>
        (await provider.CreateSchemaExtractor().ListTriggersAsync(connection, ct))
        .Where(t => t.Table is { } table && string.Equals(table.Namespace, schema, StringComparison.Ordinal))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToList();

    // Metadados de um objeto. Resultado em cache até Refresh().
    public async Task<Result<TableModel>> DescribeTableAsync(DbConnection connection, ObjectName table, CancellationToken ct)
    {
        if (_tables.TryGetValue(table.CanonicalId, out var cached))
        {
            return Result<TableModel>.Ok(cached);
        }

        var result = await provider.CreateSchemaExtractor().ExtractTableAsync(connection, table, ct);
        if (result.IsSuccess)
        {
            _tables[table.CanonicalId] = result.Value;
        }

        return result;
    }

    // Descarta o cache de metadados (atualização manual do usuário, ação "Atualizar").
    public void Refresh() => _tables.Clear();
}
