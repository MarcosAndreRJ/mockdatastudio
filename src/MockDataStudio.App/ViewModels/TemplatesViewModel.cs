using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.App.Resources;
using MockDataStudio.App.Services;
using MockDataStudio.App.ViewModels.Templates;

namespace MockDataStudio.App.ViewModels;

/// <summary>
/// Tela Templates (S06): orquestrador fino (TC-101). Mantém as quatro abas e liga os ViewModels especializados entre si;
/// a seleção e a busca vivem em <see cref="Catalog"/> e por isso sobrevivem à troca de aba. Também é a guarda de navegação
/// da tela: sair com formulário modificado e não salvo pede confirmação (TC-104).
/// </summary>
public sealed partial class TemplatesViewModel : ObservableObject, INavigationGuard, IDisposable
{
    public TemplatesViewModel(
        TemplateCatalogViewModel catalog, TemplateDetailsViewModel details, TemplateImportExportViewModel importExport,
        GeneratorPresetsViewModel presets, LearnedRulesViewModel learnedRules)
    {
        Catalog = catalog;
        Details = details;
        ImportExport = importExport;
        Presets = presets;
        LearnedRules = learnedRules;
        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);

        Catalog.SelectionChanged += (_, row) => _ = Details.ShowAsync(row);
        if (Catalog.SelectedTemplate is { } current)
        {
            _ = Details.ShowAsync(current); // a primeira carga pode ter terminado antes de assinarmos o evento
        }
        Details.CatalogChanged += async (_, code) => await ReloadCatalogAsync(code);
        ImportExport.CatalogChanged += async (_, _) => await ReloadCatalogAsync(null);
    }

    public TemplateCatalogViewModel Catalog { get; }
    public TemplateDetailsViewModel Details { get; }
    public TemplateImportExportViewModel ImportExport { get; }
    public GeneratorPresetsViewModel Presets { get; }
    public LearnedRulesViewModel LearnedRules { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Aba atual, na ordem de <see cref="TemplatesTab"/>. Ligada ao TabControl.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTab))]
    private int _selectedTabIndex;

    public TemplatesTab SelectedTab => Enum.IsDefined(typeof(TemplatesTab), SelectedTabIndex) ? (TemplatesTab)SelectedTabIndex : TemplatesTab.Templates;

    // Presets e regras podem ter mudado fora desta tela (ex.: "Lembrar" no Mapeamento): recarrega ao abrir a aba.
    partial void OnSelectedTabIndexChanged(int value)
    {
        switch (SelectedTab)
        {
            case TemplatesTab.Presets:
                _ = Presets.RefreshAsync();
                break;
            case TemplatesTab.LearnedRules:
                _ = LearnedRules.RefreshAsync();
                break;
        }
    }

    private async Task ReloadCatalogAsync(string? selectCode)
    {
        await Catalog.ReloadAsync();
        if (selectCode is not null)
        {
            Catalog.Select(selectCode);
        }
    }

    private Task RefreshAllAsync() =>
        Task.WhenAll(Catalog.ReloadAsync(), Presets.RefreshAsync(), LearnedRules.RefreshAsync());

    // ---------- INavigationGuard ----------

    public bool HasUnsavedChanges => Details.IsDirty || Presets.IsDirty;

    public string UnsavedChangesDescription =>
        Details.IsDirty && Presets.IsDirty ? TemplatesText.UnsavedBoth
        : Presets.IsDirty ? TemplatesText.UnsavedPreset
        : TemplatesText.UnsavedTemplate;

    public async Task<bool> SaveChangesAsync()
    {
        if (Details.IsDirty && !await Details.SaveTemplateAsync())
        {
            SelectedTabIndex = (int)TemplatesTab.Templates;
            return false;
        }

        if (Presets.IsDirty && !await Presets.SaveAsync())
        {
            SelectedTabIndex = (int)TemplatesTab.Presets;
            return false;
        }

        return true;
    }

    public void DiscardChanges()
    {
        if (Details.IsDirty)
        {
            Details.DiscardChanges();
        }

        if (Presets.IsDirty)
        {
            Presets.DiscardChanges();
        }
    }

    public void Dispose() => Catalog.Dispose();
}
