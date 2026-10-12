using MockDataStudio.Application;
using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Data;
using MockDataStudio.Catalog.Database;
using MockDataStudio.Catalog.Maintenance;
using MockDataStudio.Catalog.Packs;
using MockDataStudio.Catalog.Repositories;
using MockDataStudio.Catalog.Staging;
using MockDataStudio.Core.Common;
using MockDataStudio.SqlTooling.Splitter;

namespace MockDataStudio.Infrastructure.Catalog;

/// <summary>
/// Composição do catálogo local em %LOCALAPPDATA%\MockDataStudio\catalog (catalog.db, backups, staging) e preparação no
/// primeiro uso: migrações e instalação do pacote embutido quando o catálogo está vazio.
/// </summary>
public sealed class CatalogBootstrap : ICatalogInitializer, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Result<bool>? _result;

    public CatalogBootstrap(string? directory = null)
    {
        var root = directory ?? Path.Combine(
            AppPaths.Root, "catalog");
        Directory.CreateDirectory(Path.Combine(root, "backups"));
        Directory.CreateDirectory(Path.Combine(root, "staging"));

        Database = new CatalogDatabase(Path.Combine(root, "catalog.db"));
        Maintenance = new CatalogMaintenance(Database, Path.Combine(root, "backups"));
        Importer = new StagingImporter(Database, new SqlScriptSplitter(), Maintenance, Path.Combine(root, "staging"));
        Templates = new TemplateRepository(Database);
        Rules = new MappingRuleRepository(Database);
        Feedback = new MappingFeedbackRepository(Database);
        DataSource = new CatalogTemplateDataSource(Database);
        Pack = new BuiltInPackInstaller(Database, Importer);
    }

    public CatalogDatabase Database { get; }

    public CatalogMaintenance Maintenance { get; }

    public StagingImporter Importer { get; }

    public TemplateRepository Templates { get; }

    public MappingRuleRepository Rules { get; }

    public MappingFeedbackRepository Feedback { get; }

    public CatalogTemplateDataSource DataSource { get; }

    public BuiltInPackInstaller Pack { get; }

    public void Dispose() => _gate.Dispose();

    public async Task<Result<bool>> EnsureReadyAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_result is { IsSuccess: true })
            {
                return _result.Value;
            }

            var migrated = await Task.Run(() => new CatalogMigrator(Database, Migration.LoadEmbedded()).Migrate(), ct);
            if (!migrated.IsSuccess)
            {
                _result = Result<bool>.Fail(migrated.Error);
                return _result.Value;
            }

            if ((await Templates.ListAsync(new TemplateQuery(), ct)).Count == 0)
            {
                var installed = await Pack.InstallAsync(ct);
                if (!installed.IsSuccess)
                {
                    _result = Result<bool>.Fail(installed.Error);
                    return _result.Value;
                }
            }

            _result = Result<bool>.Ok(true);
            return _result.Value;
        }
        finally
        {
            _gate.Release();
        }
    }
}
