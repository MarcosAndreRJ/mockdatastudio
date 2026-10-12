using Shouldly;
using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Data;
using MockDataStudio.Catalog.Database;
using MockDataStudio.Catalog.Maintenance;
using MockDataStudio.Catalog.Packs;
using MockDataStudio.Catalog.Repositories;
using MockDataStudio.Catalog.Staging;
using MockDataStudio.SqlTooling.Splitter;

namespace MockDataStudio.Catalog.Tests;

// Ambiente descartável: catálogo migrado numa pasta temporária, com todos os serviços ligados.
public sealed class CatalogTestEnv : IDisposable
{
    public CatalogTestEnv()
    {
        Root = Path.Combine(Path.GetTempPath(), "mds-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "staging"));
        Directory.CreateDirectory(Path.Combine(Root, "backups"));

        Database = new CatalogDatabase(Path.Combine(Root, "catalog.db"));
        Migrate(Migration.LoadEmbedded()).IsSuccess.ShouldBeTrue();

        Maintenance = new CatalogMaintenance(Database, Path.Combine(Root, "backups"));
        Importer = new StagingImporter(Database, new SqlScriptSplitter(), Maintenance, Path.Combine(Root, "staging"));
        Templates = new TemplateRepository(Database);
        Rules = new MappingRuleRepository(Database);
        Presets = new GeneratorPresetRepository(Database);
        Profiles = new ConnectionProfileRepository(Database);
        History = new QueryHistoryRepository(Database);
        Feedback = new MappingFeedbackRepository(Database);
        Runs = new GenerationRunRepository(Database);
        DataSource = new CatalogTemplateDataSource(Database);
        Pack = new BuiltInPackInstaller(Database, Importer);
    }

    public string Root { get; }

    public CatalogDatabase Database { get; }

    public CatalogMaintenance Maintenance { get; }

    public StagingImporter Importer { get; }

    public TemplateRepository Templates { get; }

    public MappingRuleRepository Rules { get; }

    public GeneratorPresetRepository Presets { get; }

    public ConnectionProfileRepository Profiles { get; }

    public QueryHistoryRepository History { get; }

    public MappingFeedbackRepository Feedback { get; }

    public GenerationRunRepository Runs { get; }

    public CatalogTemplateDataSource DataSource { get; }

    public BuiltInPackInstaller Pack { get; }

    public MockDataStudio.Core.Common.Result<int> Migrate(IReadOnlyList<Migration> migrations) =>
        new CatalogMigrator(Database, migrations).Migrate();

    public async Task<MockDataStudio.Core.Common.Result<StagingAnalysis>> AnalyzeAsync(string script, MergeMode mode = MergeMode.Upsert) =>
        await Importer.AnalyzeAsync(new StagingImportRequest(script, mode, null), null, CancellationToken.None);

    // Analisa e mescla; devolve o relatório. Falha o teste se a análise não puder ser mesclada.
    public async Task<MockDataStudio.Core.Common.Result<MergeReport>> ImportAsync(string script, MergeMode mode = MergeMode.Upsert)
    {
        var analysis = await AnalyzeAsync(script, mode);
        analysis.IsSuccess.ShouldBeTrue(analysis.IsSuccess ? string.Empty : analysis.Error.Message);
        analysis.Value.CanMerge.ShouldBeTrue(string.Join("; ", analysis.Value.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Code + ": " + i.Message)));
        return await Importer.MergeAsync(analysis.Value.StagingSessionId, CancellationToken.None);
    }

    public long Count(string table)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Exec(string sql)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public int Version()
    {
        using var connection = Database.Open();
        return CatalogMigrator.CurrentVersion(connection);
    }

    public string Scalar(string sql)
    {
        using var connection = Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    public void Dispose()
    {
        // Conexões são fechadas por operação (sem pool): a pasta pode ser removida.
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Arquivo temporário ainda referenciado pelo sistema: limpeza fica para a pasta temporária.
        }
    }
}
