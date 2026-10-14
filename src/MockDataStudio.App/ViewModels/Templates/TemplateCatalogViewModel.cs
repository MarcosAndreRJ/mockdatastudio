using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.App.Resources;
using MockDataStudio.Application;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.App.ViewModels.Templates;

public sealed record TemplateRowItem(string Code, string Name, string Description, string Count, string Glyph, Brush Tint, Brush TintBg);

/// <summary>
/// Lista de templates do catálogo (TC-101): carga, busca, seleção e estado visual. Não edita nada: quem precisa reagir à
/// seleção assina <see cref="SelectionChanged"/>, e quem altera o catálogo pede <see cref="ReloadAsync"/>.
/// </summary>
public sealed partial class TemplateCatalogViewModel : ObservableObject, IDisposable
{
    private readonly TemplateBrowser _browser;
    private readonly ICatalogInitializer _catalog;
    private CancellationTokenSource? _loadCts;

    public TemplateCatalogViewModel(TemplateBrowser browser, ICatalogInitializer catalog)
    {
        _browser = browser;
        _catalog = catalog;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        _ = ReloadAsync();
    }

    /// <summary>Disparado quando o template selecionado muda (inclusive para nenhum).</summary>
    public event EventHandler<TemplateRowItem?>? SelectionChanged;

    /// <summary>Disparado ao final de cada carga bem-sucedida da lista.</summary>
    public event EventHandler? Reloaded;

    public IAsyncRelayCommand ReloadCommand { get; }

    public ObservableCollection<TemplateRowItem> Templates { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TemplateRowItem? _selectedTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private ViewState _state = ViewState.Loading;

    /// <summary>Texto do estado atual (vazio, sem resultados, erro) ou contagem quando pronto.</summary>
    [ObservableProperty]
    private string _statusMessage = TemplatesText.StateLoading;

    public bool IsReady => State == ViewState.Ready;

    partial void OnSearchTextChanged(string value) => _ = ReloadAsync();

    partial void OnSelectedTemplateChanged(TemplateRowItem? value) => SelectionChanged?.Invoke(this, value);

    /// <summary>Seleciona pelo código, se existir na lista atual.</summary>
    public void Select(string code) =>
        SelectedTemplate = Templates.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal)) ?? SelectedTemplate;

    public async Task ReloadAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        if (Templates.Count == 0)
        {
            StatusMessage = TemplatesText.StateLoading;
            State = ViewState.Loading; // com itens já exibidos (ex.: durante a busca) a lista continua visível até a resposta chegar
        }

        try
        {
            var ready = await _catalog.EnsureReadyAsync(cts.Token);
            if (!ready.IsSuccess)
            {
                Fail(ready.Error.Message);
                return;
            }

            var selected = SelectedTemplate?.Code;
            var search = SearchText.Trim();
            var infos = await _browser.ListAsync(search, cts.Token);
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

            if (infos.Count == 0)
            {
                State = search.Length == 0 ? ViewState.Empty : ViewState.NoResults;
                StatusMessage = search.Length == 0 ? TemplatesText.StateEmpty : TemplatesText.StateNoResults(search);
            }
            else
            {
                State = ViewState.Ready;
                StatusMessage = TemplatesText.ListCount(infos.Count);
            }

            SelectedTemplate = Templates.FirstOrDefault(t => t.Code == selected) ?? Templates.FirstOrDefault();
            Reloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Uma busca ou atualização mais nova substituiu esta.
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Data.Common.DbException)
        {
            Fail(ex.Message);
        }
    }

    private void Fail(string message)
    {
        Templates.Clear();
        SelectedTemplate = null;
        State = ViewState.Error;
        StatusMessage = TemplatesText.StateError(message);
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}
