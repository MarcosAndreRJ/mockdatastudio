using MockDataStudio.Catalog.Database;
using MockDataStudio.Catalog.Repositories;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog.Data;

// Implementação de ITemplateDataSource (C05 §2) sobre o catálogo: entrega TemplateDataSet ordenado e com ContentHash.
public sealed class CatalogTemplateDataSource(CatalogDatabase database) : ITemplateDataSource
{
    public Task<TemplateDataSet> LoadAsync(string templateCode, CancellationToken ct)
    {
        using var connection = database.Open();
        var result = TemplateLoader.Load(connection, templateCode);
        return result.IsSuccess
            ? Task.FromResult(result.Value)
            : throw new InvalidOperationException(result.Error.Message);
    }
}
