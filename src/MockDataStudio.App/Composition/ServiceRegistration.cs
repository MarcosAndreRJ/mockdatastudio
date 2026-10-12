using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<ConnectionsViewModel>();
        builder.Services.AddTransient<ExplorerViewModel>();
        builder.Services.AddTransient<SqlEditorViewModel>();
        builder.Services.AddTransient<SqlResultsViewModel>();
        builder.Services.AddTransient<SqlEditorPageViewModel>();
        builder.Services.AddTransient<SchemaDigestViewModel>();
        builder.Services.AddTransient<TemplatesViewModel>();
        builder.Services.AddTransient<MappingViewModel>();

        return builder.Build();
    }
}
