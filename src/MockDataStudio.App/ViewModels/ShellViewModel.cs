using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using MockDataStudio.App.Resources;

namespace MockDataStudio.App.ViewModels;

// Shell: lista de navegação e página atual. A página é criada pelo contêiner de DI a cada seleção.
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private NavEntry? _selectedNav;

    [ObservableProperty]
    private object? _currentPage;

    public ShellViewModel(IServiceProvider services, MockDataStudio.App.Services.ShellNavigator navigator)
    {
        _services = services;

        NavItems =
        [
            new NavEntry("home", Strings.NavHome, "\uE80F", s => s.GetRequiredService<HomeViewModel>()),
            new NavEntry("connections", Strings.NavConnections, "\uE8F1", s => s.GetRequiredService<ConnectionsViewModel>()),
            new NavEntry("explorer", Strings.NavExplorer, "\uE8A1", s => s.GetRequiredService<ExplorerViewModel>()),
            new NavEntry("sql", Strings.NavSqlEditor, "\uE943", s => s.GetRequiredService<SqlEditorPageViewModel>()),
            new NavEntry("digest", Strings.NavSchemaDigest, "\uE9F9", s => s.GetRequiredService<SchemaDigestViewModel>()),
            new NavEntry("templates", Strings.NavTemplates, "\uE9D5", s => s.GetRequiredService<TemplatesViewModel>()),
            new NavEntry("mapping", Strings.NavMapping, "\uE8AB", s => s.GetRequiredService<MappingViewModel>()),
            new NavEntry("generation", Strings.NavGeneration, "\uE768", s => new PlaceholderViewModel(Strings.NavGeneration)),
            new NavEntry("history", Strings.NavHistory, "\uE81C", s => new PlaceholderViewModel(Strings.NavHistory)),
            new NavEntry("settings", Strings.NavSettings, "\uE713", s => new PlaceholderViewModel(Strings.NavSettings)),
        ];

        SelectedNav = NavItems[0];
        navigator.Requested += key =>
        {
            var target = NavItems.FirstOrDefault(n => n.Key == key);
            if (target is not null)
                SelectedNav = target;
        };
    }

    public IReadOnlyList<NavEntry> NavItems { get; }

    partial void OnSelectedNavChanged(NavEntry? value)
    {
        CurrentPage = value?.CreatePage(_services);
    }
}

// Entrada da navegação lateral.
public sealed record NavEntry(string Key, string Title, string Glyph, Func<IServiceProvider, object> Factory)
{
    public object CreatePage(IServiceProvider services) => Factory(services);
}
