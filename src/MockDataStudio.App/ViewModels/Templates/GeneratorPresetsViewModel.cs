using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.App.Resources;
using MockDataStudio.Application;
using MockDataStudio.Catalog;

namespace MockDataStudio.App.ViewModels.Templates;

// Linha da lista de presets (FR-059). Embutidos não podem ser alterados nem excluídos.
public sealed record PresetRow(long Id, string Code, string Name, string GeneratorId, int GeneratorVersion, string ParametersJson, string? Description, bool IsBuiltIn);

/// <summary>Presets de gerador (FR-059), sugeridos no mapeamento pelo nome da coluna (TC-101).</summary>
public sealed partial class GeneratorPresetsViewModel : ObservableObject
{
    private readonly MappingLearningService _learning;
    private int _suppressDirty;

    public GeneratorPresetsViewModel(MappingLearningService learning)
    {
        _learning = learning;
        NewPresetCommand = new RelayCommand(NewPreset);
        SavePresetCommand = new AsyncRelayCommand(async () => { await SaveAsync(); });
        DeletePresetCommand = new AsyncRelayCommand(DeletePresetAsync);
        LoadPresetCommand = new RelayCommand(LoadPreset);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _ = RefreshAsync();
    }

    public ObservableCollection<PresetRow> Presets { get; } = [];

    public IRelayCommand NewPresetCommand { get; }
    public IAsyncRelayCommand SavePresetCommand { get; }
    public IAsyncRelayCommand DeletePresetCommand { get; }
    public IRelayCommand LoadPresetCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    [ObservableProperty]
    private PresetRow? _selectedPreset;

    [ObservableProperty]
    private string _presetCode = string.Empty;

    [ObservableProperty]
    private string _presetName = string.Empty;

    [ObservableProperty]
    private string _presetGenerator = string.Empty;

    [ObservableProperty]
    private string _presetVersion = "1";

    [ObservableProperty]
    private string _presetParameters = "{}";

    [ObservableProperty]
    private string _presetDescription = string.Empty;

    [ObservableProperty]
    private string _presetStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateMessage = TemplatesText.StateLoading;

    [ObservableProperty]
    private bool _isDirty;

    public bool IsReady => State == ViewState.Ready;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_suppressDirty == 0 && e.PropertyName is { } name && name.StartsWith("Preset", StringComparison.Ordinal)
            && name != nameof(PresetStatus))
        {
            IsDirty = true;
        }
    }


    private void Clean(Action change)
    {
        _suppressDirty++;
        try
        {
            change();
        }
        finally
        {
            _suppressDirty--;
        }

        IsDirty = false;
    }

    public async Task RefreshAsync()
    {
        State = ViewState.Loading;
        StateMessage = TemplatesText.StateLoading;
        try
        {
            var presets = await _learning.ListPresetsAsync(CancellationToken.None);
            Presets.Clear();
            foreach (var p in presets.OrderBy(p => p.Code, StringComparer.Ordinal))
            {
                Presets.Add(new PresetRow(p.Id, p.Code, p.Name, p.GeneratorId, p.GeneratorVersion, p.ParametersJson, p.Description, p.IsBuiltIn));
            }

            State = Presets.Count == 0 ? ViewState.Empty : ViewState.Ready;
            StateMessage = Presets.Count == 0 ? TemplatesText.PresetsEmpty : TemplatesText.PresetsCount(Presets.Count);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Data.Common.DbException)
        {
            State = ViewState.Error;
            StateMessage = TemplatesText.StateError(ex.Message);
        }
    }

    private void NewPreset()
    {
        Clean(() =>
        {
            SelectedPreset = null;
            PresetCode = string.Empty;
            PresetName = string.Empty;
            PresetGenerator = string.Empty;
            PresetVersion = "1";
            PresetParameters = "{}";
            PresetDescription = string.Empty;
            PresetStatus = TemplatesText.PresetNew;
        });
    }

    private void LoadPreset()
    {
        if (SelectedPreset is null)
        {
            PresetStatus = TemplatesText.PresetSelect;
            return;
        }

        var preset = SelectedPreset;
        Clean(() =>
        {
            PresetCode = preset.Code;
            PresetName = preset.Name;
            PresetGenerator = preset.GeneratorId;
            PresetVersion = preset.GeneratorVersion.ToString(CultureInfo.InvariantCulture);
            PresetParameters = preset.ParametersJson;
            PresetDescription = preset.Description ?? string.Empty;
            PresetStatus = preset.IsBuiltIn ? TemplatesText.PresetBuiltIn(preset.Code) : TemplatesText.PresetLoaded(preset.Code);
        });
    }

    /// <summary>Salva o formulário. Retorna verdadeiro se gravou (usado também pela guarda de navegação).</summary>
    public async Task<bool> SaveAsync()
    {
        if (!int.TryParse(PresetVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            PresetStatus = TemplatesText.PresetVersionInvalid;
            return false;
        }

        var result = await _learning.SavePresetAsync(new GeneratorPresetInfo(
            SelectedPreset?.Id ?? 0, PresetCode.Trim(), PresetName.Trim(), PresetGenerator.Trim(), version,
            string.IsNullOrWhiteSpace(PresetParameters) ? "{}" : PresetParameters.Trim(),
            string.IsNullOrWhiteSpace(PresetDescription) ? null : PresetDescription.Trim(), IsBuiltIn: false), CancellationToken.None);
        PresetStatus = result.IsSuccess ? TemplatesText.PresetSaved(PresetCode.Trim()) : result.Error.Message;
        if (!result.IsSuccess)
        {
            return false;
        }

        IsDirty = false;
        await RefreshAsync();
        return true;
    }

    /// <summary>Descarta as alterações do formulário (escolha "Descartar" da guarda de navegação).</summary>
    public void DiscardChanges() => NewPreset();

    private async Task DeletePresetAsync()
    {
        var code = SelectedPreset?.Code ?? PresetCode.Trim();
        var result = await _learning.DeletePresetAsync(code, CancellationToken.None);
        PresetStatus = result.IsSuccess && result.Value
            ? TemplatesText.PresetDeleted(code)
            : result.IsSuccess ? TemplatesText.PresetNotFound : result.Error.Message;
        if (result.IsSuccess)
        {
            await RefreshAsync();
        }
    }
}
