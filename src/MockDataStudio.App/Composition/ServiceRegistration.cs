using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using MockDataStudio.App.Data;
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

        builder.Services.AddSingleton<IDesignDataSource, SampleDesignDataSource>();

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

        builder.Services.AddSingleton<ActiveConnectionSession>();
        builder.Services.AddSingleton<MockDataStudio.App.Services.ShellNavigator>();
        builder.Services.AddSingleton<MockDataStudio.SqlTooling.Splitter.ISqlScriptSplitter, MockDataStudio.SqlTooling.Splitter.SqlScriptSplitter>();

        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<ConnectionsViewModel>();
        builder.Services.AddSingleton<ExplorerViewModel>();
        builder.Services.AddSingleton<SqlEditorViewModel>();
        builder.Services.AddSingleton<SqlResultsViewModel>();
        builder.Services.AddSingleton<SqlEditorPageViewModel>();
        builder.Services.AddTransient<SchemaDigestViewModel>();
        builder.Services.AddTransient<TemplatesViewModel>();
        builder.Services.AddTransient<MappingViewModel>();

        return builder.Build();
    }
}
