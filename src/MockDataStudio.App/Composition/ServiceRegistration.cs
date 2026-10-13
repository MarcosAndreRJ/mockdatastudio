using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using MockDataStudio.App.ViewModels;
using MockDataStudio.Application;
using MockDataStudio.Providers;

namespace MockDataStudio.App.Composition;

// Composition root da interface. Único lugar do App que registra serviços e fontes de dados.
public static class ServiceRegistration
{
    public static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        // Logging em arquivo com redação de segredos (docs/23 §6, docs/25 §2)
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(MockDataStudio.Infrastructure.Logging.LoggingSetup.CreateLogger(), dispose: true);
        builder.Services.AddSingleton<IAppSettings, MockDataStudio.Infrastructure.Settings.JsonAppSettings>();


        // Provedores de banco de dados (C02)
        builder.Services.AddSingleton<IDatabaseProvider, MockDataStudio.Providers.Sqlite.SqliteProvider>();
        builder.Services.AddSingleton<IDatabaseProvider, MockDataStudio.Providers.PostgreSql.PostgreSqlProvider>();
        builder.Services.AddSingleton<IDatabaseProvider, MockDataStudio.Providers.MySql.MySqlProvider>();
        builder.Services.AddSingleton<IDatabaseProvider, MockDataStudio.Providers.Firebird.FirebirdProvider>();
        builder.Services.AddSingleton<IProviderRegistry, ProviderRegistry>();

        // Segurança e Persistência de Infraestrutura (C09)
        builder.Services.AddSingleton<ISecretStore, MockDataStudio.Infrastructure.Security.DpapiSecretStore>();
        builder.Services.AddSingleton<IConnectionProfileStore, MockDataStudio.Infrastructure.Storage.FileConnectionProfileStore>();

        // Serviços de Aplicação
        builder.Services.AddSingleton<IConnectionService, MockDataStudio.Application.ConnectionService>();

        // Projeto e estrutura do banco (C2). O MappingService depende do catálogo e será registrado com a composição do catálogo (C3).
        builder.Services.AddSingleton<MockDataStudio.Core.Hashing.IStructureHasher, MockDataStudio.Core.Hashing.StructureHasher>();
        builder.Services.AddSingleton<MockDataStudio.Core.Digest.IDigestSerializer, MockDataStudio.Core.Digest.DigestSerializer>();
        builder.Services.AddSingleton<MockDataStudio.Core.Diff.ISchemaDiffer, MockDataStudio.Core.Diff.SchemaDiffer>();
        builder.Services.AddSingleton<MockDataStudio.Core.Common.IClock>(MockDataStudio.Core.Common.SystemClock.Instance);
        builder.Services.AddSingleton<MockDataStudio.Projects.IProjectSerializer, MockDataStudio.Projects.ProjectSerializer>();
        builder.Services.AddSingleton<MockDataStudio.Projects.IProjectStore, MockDataStudio.Projects.ProjectStore>();
        builder.Services.AddSingleton<IDigestService, DigestService>();

        // Catálogo local (templates, regras, feedback), preparado no primeiro uso
        builder.Services.AddSingleton<MockDataStudio.Infrastructure.Catalog.CatalogBootstrap>();
        builder.Services.AddSingleton<ICatalogInitializer>(sp => sp.GetRequiredService<MockDataStudio.Infrastructure.Catalog.CatalogBootstrap>());
        builder.Services.AddSingleton<MockDataStudio.Catalog.ITemplateRepository>(sp => sp.GetRequiredService<MockDataStudio.Infrastructure.Catalog.CatalogBootstrap>().Templates);
        builder.Services.AddSingleton<MockDataStudio.Catalog.IMappingRuleRepository>(sp => sp.GetRequiredService<MockDataStudio.Infrastructure.Catalog.CatalogBootstrap>().Rules);
        builder.Services.AddSingleton<MockDataStudio.Catalog.IMappingFeedbackRepository>(sp => sp.GetRequiredService<MockDataStudio.Infrastructure.Catalog.CatalogBootstrap>().Feedback);
        builder.Services.AddSingleton<MockDataStudio.Catalog.IGeneratorPresetRepository>(sp => new MockDataStudio.Catalog.Repositories.GeneratorPresetRepository(sp.GetRequiredService<MockDataStudio.Infrastructure.Catalog.CatalogBootstrap>().Database));
        builder.Services.AddSingleton<MockDataStudio.Core.Templates.ITemplateDataSource>(sp => sp.GetRequiredService<MockDataStudio.Infrastructure.Catalog.CatalogBootstrap>().DataSource);

        // Geração e mapeamento (motor e serviços de aplicação)
        builder.Services.AddSingleton<MockDataStudio.Generation.Generators.IGeneratorRegistry>(_ => MockDataStudio.Generation.Generators.GeneratorRegistry.Standard());
        builder.Services.AddSingleton<MockDataStudio.Mapping.INameNormalizer, MockDataStudio.Mapping.NameNormalizer>();
        builder.Services.AddSingleton<MockDataStudio.Mapping.IStructuralBinder, MockDataStudio.Mapping.StructuralBinder>();
        builder.Services.AddSingleton<MockDataStudio.Mapping.IMappingEngine, MockDataStudio.Mapping.MappingEngine>();
        builder.Services.AddSingleton<IMappingService, MappingService>();
        builder.Services.AddSingleton<MappingSession>();
        builder.Services.AddSingleton<GeneratorPreviewService>();
        builder.Services.AddSingleton<TemplateBrowser>();
        builder.Services.AddSingleton<ProjectWorkspace>();

        builder.Services.AddSingleton<ActiveConnectionSession>();
        builder.Services.AddSingleton<MockDataStudio.App.Services.ShellNavigator>();
        builder.Services.AddSingleton<MockDataStudio.SqlTooling.Splitter.ISqlScriptSplitter, MockDataStudio.SqlTooling.Splitter.SqlScriptSplitter>();

        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddTransient<ConnectionsViewModel>();
        builder.Services.AddSingleton<ExplorerViewModel>();
        builder.Services.AddSingleton<SqlEditorWorkspaceViewModel>();
        builder.Services.AddSingleton<SqlEditorPageViewModel>();
        builder.Services.AddSingleton<SchemaDigestViewModel>();
        builder.Services.AddSingleton<TemplatesViewModel>();
        builder.Services.AddSingleton<MappingViewModel>();

        return builder.Build();
    }
}
