using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MockDataStudio.App.Samples;
using MockDataStudio.App.Services;
using MockDataStudio.Application;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Connections;

namespace MockDataStudio.App.ViewModels;

/// <summary>Recursos de tema (pincéis) para ViewModels, sempre pelos tokens do tema ativo.</summary>
internal static class Theme
{
    public static Brush Brush(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>Item da lista de projetos recentes (S01).</summary>
public sealed record RecentProjectItem(string Name, string Description, string Updated, string Path, bool Exists, IRelayCommand<string> OpenCommand)
{
    public string Glyph => Exists ? "" : "";

    public Brush Tint => Theme.Brush(Exists ? "AccentBrush" : "DangerBrush");

    public Brush TintBg => Theme.Brush(Exists ? "AccentSoftBrush" : "DangerSoftBrush");
}

/// <summary>Home e workspace do projeto (S01/S10): criar, abrir, salvar, recentes e visão geral reais.</summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    private const string ProjectFilter = "Projetos Mock Data Studio (*.mdsproj)|*.mdsproj|Todos os arquivos (*.*)|*.*";

    private readonly ProjectWorkspace _workspace;
    private readonly IConnectionService _connections;
    private readonly ICatalogInitializer _catalog;
    private readonly ITemplateRepository _templates;
    private readonly ShellNavigator _navigator;

    public HomeViewModel(
        ProjectWorkspace workspace, IConnectionService connections, ICatalogInitializer catalog,
        ITemplateRepository templates, ShellNavigator navigator)
    {
        _workspace = workspace;
        _connections = connections;
        _catalog = catalog;
        _templates = templates;
        _navigator = navigator;

        NewProjectCommand = new RelayCommand(NewProject);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        OpenRecentCommand = new AsyncRelayCommand<string>(OpenRecentAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasProject);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync, () => HasProject);
        GoToCommand = new RelayCommand<string>(key => _navigator.NavigateTo(key ?? "home"));

        _workspace.Changed += OnWorkspaceChanged;
        Refresh();
        _ = LoadAsync();
    }

    public IRelayCommand NewProjectCommand { get; }
    public IAsyncRelayCommand OpenProjectCommand { get; }
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand SaveAsCommand { get; }
    public IRelayCommand<string> GoToCommand { get; }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];
    public ObservableCollection<ConnectionItem> Connections { get; } = [];
    public ObservableCollection<ActivityItem> RecentActivity { get; } = [];

    [ObservableProperty]
    private string _projectName = "Nenhum projeto aberto";

    [ObservableProperty]
    private string _projectDetail = "Crie um projeto novo ou abra um arquivo .mdsproj para começar.";

    [ObservableProperty]
    private string _projectState = string.Empty;

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _connectionsCount = "0";

    [ObservableProperty]
    private string _connectionsDetail = "Nenhuma conexão salva";

    [ObservableProperty]
    private string _projectsCount = "0";

    [ObservableProperty]
    private string _templatesCount = "…";

    [ObservableProperty]
    private string _templatesDetail = "Carregando catálogo";

    [ObservableProperty]
    private string _mappedCount = "0";

    [ObservableProperty]
    private string _mappedDetail = "Sem projeto aberto";

    partial void OnHasProjectChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        Refresh();
        _ = LoadRecentsAsync();
    }

    private void Refresh()
    {
        var project = _workspace.Current;
        HasProject = project is not null;
        if (project is null)
        {
            ProjectName = "Nenhum projeto aberto";
            ProjectDetail = "Crie um projeto novo ou abra um arquivo .mdsproj para começar.";
            ProjectState = string.Empty;
            MappedCount = "0";
            MappedDetail = "Sem projeto aberto";
            return;
        }

        ProjectName = project.Name;
        ProjectDetail = _workspace.Path ?? "Ainda não salvo em disco";
        ProjectState = _workspace.IsDirty ? "Alterações não salvas" : "Salvo";
        MappedCount = project.Bindings.Count.ToString(CultureInfo.InvariantCulture);
        MappedDetail = project.Snapshots.Count == 0
            ? "Capture a estrutura em Schema Digest"
            : $"{project.Snapshots.Count} snapshot(s) • perfil \"{project.ActiveProfile}\"";
    }

    private async Task LoadAsync()
    {
        await LoadRecentsAsync();
        await LoadConnectionsAsync();

        var ready = await _catalog.EnsureReadyAsync(CancellationToken.None);
        if (!ready.IsSuccess)
        {
            TemplatesCount = "—";
            TemplatesDetail = "Catálogo indisponível: " + ready.Error.Message;
            return;
        }

        var all = await _templates.ListAsync(new TemplateQuery(), CancellationToken.None);
        TemplatesCount = all.Count.ToString(CultureInfo.InvariantCulture);
        TemplatesDetail = $"{all.Count(t => t.IsBuiltIn)} embutidos";
    }

    private Task LoadRecentsAsync()
    {
        RecentProjects.Clear();
        RecentActivity.Clear();
        foreach (var path in _workspace.RecentProjects)
        {
            var exists = File.Exists(path);
            var updated = exists ? File.GetLastWriteTime(path) : (DateTime?)null;
            RecentProjects.Add(new RecentProjectItem(
                Path.GetFileNameWithoutExtension(path), path,
                updated is { } when ? when.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) : "arquivo não encontrado",
                path, exists, OpenRecentCommand));

            RecentActivity.Add(new ActivityItem(
                exists ? $"Projeto \"{Path.GetFileNameWithoutExtension(path)}\" aberto ou salvo" : $"Projeto \"{Path.GetFileNameWithoutExtension(path)}\" não encontrado",
                updated is { } t ? Relative(t) : "—",
                Theme.Brush(exists ? "AccentBrush" : "DangerBrush")));
        }

        ProjectsCount = RecentProjects.Count.ToString(CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    }

    private async Task LoadConnectionsAsync()
    {
        Connections.Clear();
        var profiles = await _connections.ListAsync();
        foreach (var profile in profiles.Take(8))
        {
            var details = new List<KeyValue>
            {
                new("Provider", profile.Engine.ToString()),
                new("Destino", profile.Settings.Host is { } host ? $"{host}:{profile.Settings.Port}" : profile.Settings.Database),
                new("Ambiente", profile.Environment.ToString()),
                new("Modo", profile.IsReadOnly ? "Somente leitura" : "Leitura e escrita"),
            };
            Connections.Add(new ConnectionItem(profile.Name, true, "", Theme.Brush("AccentBrush"), Theme.Brush("AccentSoftBrush"), details));
        }

        ConnectionsCount = profiles.Count.ToString(CultureInfo.InvariantCulture);
        ConnectionsDetail = profiles.Count == 0 ? "Nenhuma conexão salva" : $"{profiles.Count(p => p.IsReadOnly)} somente leitura";
    }

    private static string Relative(DateTime value)
    {
        var span = DateTime.Now - value;
        return span.TotalMinutes < 1 ? "agora"
            : span.TotalHours < 1 ? $"há {(int)span.TotalMinutes} min"
            : span.TotalDays < 1 ? $"há {(int)span.TotalHours} h"
            : span.TotalDays < 30 ? $"há {(int)span.TotalDays} d"
            : value.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
    }

    private void NewProject()
    {
        _workspace.CreateNew("Novo projeto");
        StatusMessage = "Projeto novo criado. Capture a estrutura de um banco em Schema Digest.";
    }

    private async Task OpenProjectAsync()
    {
        var dialog = new OpenFileDialog { Title = "Abrir projeto", Filter = ProjectFilter, CheckFileExists = true };
        if (dialog.ShowDialog() == true)
        {
            await OpenRecentAsync(dialog.FileName);
        }
    }

    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = await _workspace.OpenAsync(path, CancellationToken.None);
        StatusMessage = result.IsSuccess ? $"Projeto \"{result.Value.Name}\" aberto." : result.Error.Message;
    }

    private async Task SaveAsync()
    {
        if (_workspace.Path is null)
        {
            await SaveAsAsync();
            return;
        }

        var result = await _workspace.SaveAsync(CancellationToken.None);
        StatusMessage = result.IsSuccess ? "Projeto salvo." : result.Error.Message;
    }

    private async Task SaveAsAsync()
    {
        var current = _workspace.Current;
        if (current is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Salvar projeto como",
            Filter = ProjectFilter,
            DefaultExt = ".mdsproj",
            FileName = current.Name,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // Projeto novo ainda com o nome padrão: adota o nome do arquivo escolhido.
        if (current.Name == "Novo projeto")
        {
            _workspace.Update("Nomear projeto", p => p with { Name = Path.GetFileNameWithoutExtension(dialog.FileName) });
        }

        var result = await _workspace.SaveAsAsync(dialog.FileName, CancellationToken.None);
        StatusMessage = result.IsSuccess ? "Projeto salvo." : result.Error.Message;
    }

    public void Dispose() => _workspace.Changed -= OnWorkspaceChanged;
}
