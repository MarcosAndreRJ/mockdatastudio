using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.Application;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.App.ViewModels;

public sealed record TemplateRowItem(string Code, string Name, string Description, string Count, string Glyph, Brush Tint, Brush TintBg);

/// <summary>Catálogo de templates (S06), somente consulta: lista, busca, detalhes, campos, aliases e valores de exemplo.</summary>
public sealed partial class TemplatesViewModel : ObservableObject, IDisposable
{
    private readonly TemplateBrowser _browser;
    private readonly ICatalogInitializer _catalog;
    private CancellationTokenSource? _loadCts;

    public TemplatesViewModel(TemplateBrowser browser, ICatalogInitializer catalog)
    {
        _browser = browser;
        _catalog = catalog;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _ = LoadAsync();
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public ObservableCollection<TemplateRowItem> Templates { get; } = [];
    public ObservableCollection<TemplateFieldInfo> Fields { get; } = [];
    public ObservableCollection<string> Aliases { get; } = [];
    public ObservableCollection<string> SampleValues { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TemplateRowItem? _selectedTemplate;

    [ObservableProperty]
    private string _statusMessage = "Carregando o catálogo…";

    [ObservableProperty]
    private string _title = "Nenhum template selecionado";

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _kindText = string.Empty;

    [ObservableProperty]
    private string _logicalTypeText = string.Empty;

    [ObservableProperty]
    private string _valueCountText = string.Empty;

    [ObservableProperty]
    private string _packText = string.Empty;

    [ObservableProperty]
    private string _hashText = string.Empty;

    [ObservableProperty]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private bool _hasSelection;

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    partial void OnSelectedTemplateChanged(TemplateRowItem? value) => _ = ShowDetailsAsync(value);

    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        try
        {
            var ready = await _catalog.EnsureReadyAsync(cts.Token);
            if (!ready.IsSuccess)
            {
                StatusMessage = "Catálogo indisponível: " + ready.Error.Message;
                return;
            }

            var selected = SelectedTemplate?.Code;
            var infos = await _browser.ListAsync(SearchText, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            Templates.Clear();
            foreach (var info in infos)
            {
                var composite = info.Kind == TemplateKind.Composite;
                Templates.Add(new TemplateRowItem(
                    info.Code, info.Name, info.Description ?? info.Locale,
                    info.ValueCount.ToString("N0", CultureInfo.CurrentCulture),
                    composite ? "" : "",
                    Theme.Brush(composite ? "PurpleBrush" : "AccentBrush"),
                    Theme.Brush(composite ? "PurpleSoftBrush" : "AccentSoftBrush")));
            }

            StatusMessage = infos.Count == 0 ? "Nenhum template encontrado." : $"{infos.Count} template(s)";
            SelectedTemplate = Templates.FirstOrDefault(t => t.Code == selected) ?? Templates.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // Uma busca mais nova substituiu esta.
        }
    }

    private async Task ShowDetailsAsync(TemplateRowItem? row)
    {
        Fields.Clear();
        Aliases.Clear();
        SampleValues.Clear();
        HasSelection = row is not null;
        if (row is null)
        {
            Title = "Nenhum template selecionado";
            return;
        }

        var details = await _browser.GetDetailsAsync(row.Code, CancellationToken.None);
        if (details is null || SelectedTemplate?.Code != row.Code)
        {
            return;
        }

        Title = details.Info.Name;
        Subtitle = details.Info.Code;
        Description = details.Info.Description ?? "Sem descrição.";
        KindText = details.IsComposite ? "Composto (registros com vários campos)" : "Escalar (lista de valores)";
        LogicalTypeText = details.Info.LogicalType?.ToString() ?? "—";
        ValueCountText = details.Info.ValueCount.ToString("N0", CultureInfo.CurrentCulture);
        PackText = $"{details.Info.PackCode}{(details.Info.IsBuiltIn ? " (embutido)" : string.Empty)} · {details.Info.Locale}";
        HashText = details.Info.ContentHash;
        TagsText = details.Info.Tags.Count == 0 ? "—" : string.Join(", ", details.Info.Tags);

        foreach (var field in details.Fields)
        {
            Fields.Add(field);
        }

        foreach (var alias in details.Aliases)
        {
            Aliases.Add(alias);
        }

        foreach (var value in details.SampleValues)
        {
            SampleValues.Add(value);
        }
    }
}
