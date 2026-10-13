using System.Data.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Providers;
using MockDataStudio.SqlTooling.Completion;

namespace MockDataStudio.Application.Explorer;

// Monta o catálogo do autocomplete (C6.2) a partir do schema real: tabelas, views e colunas.
// Lê com ISchemaExtractor, o mesmo contrato da árvore do Explorer. Cancelável.
public sealed class CompletionCatalogService(IDatabaseProvider provider)
{
    public async Task<CompletionCatalog> BuildAsync(DbConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var extractor = provider.CreateSchemaExtractor();

        var tables = await extractor.ListTablesAsync(connection, ct);
        var views = await extractor.ListViewsAsync(connection, ct);
        var relations = tables.Concat(views).Select(o => o.Name).ToList();

        var columns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            ct.ThrowIfCancellationRequested();
            var described = await extractor.ExtractTableAsync(connection, table, ct);
            if (described.IsSuccess)
            {
                columns[table.Name] = described.Value.Columns.OrderBy(c => c.Ordinal).Select(c => c.Name).ToList();
            }
        }

        // Views entram como relações sem colunas: ISchemaExtractor.ExtractTableAsync só descreve tabelas (limitação registrada).
        foreach (var view in views)
        {
            columns[view.Name] = [];
        }

        return new CompletionCatalog(relations, columns);
    }
}

