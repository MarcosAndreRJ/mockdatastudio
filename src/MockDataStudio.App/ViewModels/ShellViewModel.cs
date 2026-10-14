using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using MockDataStudio.App.Resources;

namespace MockDataStudio.App.ViewModels;

// Shell: lista de navegação e página atual. A página é criada pelo contêiner de DI a cada seleção.
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly MockDataStudio.App.Services.NavigationGate _gate;
    private NavEntry? _confirmedNav;
    private bool _navigating;

    [ObservableProperty]
    private NavEntry? _selectedNav;

    [ObservableProperty]
    private object? _currentPage;

    public ShellViewModel(IServiceProvider services, MockDataStudio.App.Services.ShellNavigator navigator, MockDataStudio.App.Services.NavigationGate gate)
    {
        _services = services;
        _gate = gate;

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
        if (_navigating || value == _confirmedNav)
        {
            return;
        }

        // TC-104: a página atual pode ter alterações não salvas. Devolve a seleção e só navega se a guarda liberar.
        var current = CurrentPage;
        _navigating = true;
        SelectedNav = _confirmedNav;
        _navigating = false;
        _ = LeaveAsync(current, value);
    }

    private async Task LeaveAsync(object? current, NavEntry? target)
    {
        if (!await _gate.CanLeaveAsync(current))
        {
            return;
        }

        _navigating = true;
        try
        {
            _confirmedNav = target;
            SelectedNav = target;
            CurrentPage = target?.CreatePage(_services);
        }
        finally
        {
            _navigating = false;
        }
    }
}

// Entrada da navegação lateral.
public sealed record NavEntry(string Key, string Title, string Glyph, Func<IServiceProvider, object> Factory)
{
    public object CreatePage(IServiceProvider services) => Factory(services);
}
