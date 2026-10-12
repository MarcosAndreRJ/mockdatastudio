using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MockDataStudio.App.Data;
using MockDataStudio.App.ViewModels;

namespace MockDataStudio.App.Composition;

// Composition root da interface. Único lugar do App que registra serviços e fontes de dados.
public static class ServiceRegistration
{
    public static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<IDesignDataSource, SampleDesignDataSource>();

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
