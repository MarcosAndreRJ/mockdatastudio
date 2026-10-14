using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MockDataStudio.App.Resources;
using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Editing;

namespace MockDataStudio.App.ViewModels.Templates;

/// <summary>
/// Importação (script SQL ou pack JSON, com análise isolada antes de gravar) e exportação de packs (TC-101).
/// A análise nunca altera o catálogo; só a importação confirmada altera.
/// </summary>
public sealed partial class TemplateImportExportViewModel : ObservableObject
{
    private readonly TemplateImportService _import;
    private readonly TemplatePackExporter _exporter;
    private string? _importSessionId;

    public TemplateImportExportViewModel(TemplateImportService import, TemplatePackExporter exporter)
    {
        _import = import;
        _exporter = exporter;
        AnalyzeImportCommand = new AsyncRelayCommand(AnalyzeImportAsync);
        MergeImportCommand = new AsyncRelayCommand(MergeImportAsync, () => CanMergeImport);
        DiscardImportCommand = new RelayCommand(DiscardImport);
        CopyInstructionsCommand = new RelayCommand(CopyInstructions);
        UseScriptExampleCommand = new RelayCommand(UseScriptExample);
        UsePackExampleCommand = new RelayCommand(UsePackExample);
        ExportPackCommand = new AsyncRelayCommand(ExportPackAsync);
    }

    /// <summary>Disparado depois de uma importação gravada: a lista de templates precisa ser recarregada.</summary>
    public event EventHandler? CatalogChanged;

    public IAsyncRelayCommand AnalyzeImportCommand { get; }
    public IAsyncRelayCommand MergeImportCommand { get; }
    public IRelayCommand DiscardImportCommand { get; }
    public IRelayCommand CopyInstructionsCommand { get; }
    public IRelayCommand UseScriptExampleCommand { get; }
    public IRelayCommand UsePackExampleCommand { get; }
    public IAsyncRelayCommand ExportPackCommand { get; }

    [ObservableProperty]
    private string _importText = string.Empty;

    [ObservableProperty]
    private bool _importIsPackJson;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeImportCommand))]
    private bool _canMergeImport;

    [ObservableProperty]
    private string _packExportCode = "MEU_PACK";

    [ObservableProperty]
    private string _packExportName = "Meu pack";

    [ObservableProperty]
    private bool _isBusy;


    private void UseScriptExample()
    {
        ImportIsPackJson = false;
        ImportText = TemplateExamples.Script;
        ImportStatus = TemplatesText.ImportExampleScriptLoaded(TemplateExamples.ScriptCode);
    }

    private void UsePackExample()
    {
        ImportIsPackJson = true;
        ImportText = TemplateExamples.PackJson;
        ImportStatus = TemplatesText.ImportExamplePackLoaded;
    }

    private async Task AnalyzeImportAsync()
    {
        DiscardImport();
        IsBusy = true;
        try
        {
            var result = ImportIsPackJson
                ? await _import.AnalyzePackJsonAsync(ImportText, MergeMode.Upsert, CancellationToken.None)
                : await _import.AnalyzeScriptAsync(ImportText, MergeMode.Upsert, CancellationToken.None);

            if (!result.IsSuccess)
            {
                ImportStatus = result.Error.Message;
                return;
            }

            var analysis = result.Value!;
            _importSessionId = analysis.StagingSessionId;
            CanMergeImport = analysis.CanMerge;
            ImportStatus = analysis.CanMerge
                ? TemplatesText.ImportAnalysisOk(analysis.TemplatesNew, analysis.TemplatesChanged, analysis.ValuesNew, analysis.Issues.Count)
                : TemplatesText.ImportAnalysisBlocked(analysis.Issues.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MergeImportAsync()
    {
        if (_importSessionId is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var merged = await _import.MergeAsync(_importSessionId, CancellationToken.None);
            ImportStatus = merged.IsSuccess
                ? TemplatesText.ImportDone(merged.Value!.Inserted, merged.Value.Updated, merged.Value.Skipped, merged.Value.BackupPath)
                : merged.Error.Message;
            _importSessionId = null;
            CanMergeImport = false;
            if (merged.IsSuccess)
            {
                ImportText = string.Empty;
                CatalogChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DiscardImport()
    {
        if (_importSessionId is not null)
        {
            _import.Discard(_importSessionId);
        }

        _importSessionId = null;
        CanMergeImport = false;
    }

    /// <summary>Descarta o texto colado e a análise pendente (escolha "Descartar" da guarda de navegação).</summary>
    public void DiscardAll()
    {
        DiscardImport();
        ImportText = string.Empty;
        ImportStatus = string.Empty;
    }

    private void CopyInstructions()
    {
        System.Windows.Clipboard.SetText(TemplateInstructions.ForAi);
        ImportStatus = TemplatesText.ImportInstructionsCopied;
    }

    // Exporta os templates do usuário como pack JSON (C8a.3). Código e nome do pack vêm dos campos da tela.
    private async Task ExportPackAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = TemplatesText.ExportFileFilter,
            DefaultExt = ".json",
            FileName = PackExportCode.Trim() + ".json",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _exporter.ExportAsync(PackExportCode.Trim(), PackExportName.Trim(), string.Empty, null, CancellationToken.None);
            if (!result.IsSuccess)
            {
                ImportStatus = result.Error.Message;
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, result.Value!, new System.Text.UTF8Encoding(false));
            ImportStatus = TemplatesText.ExportDone(dialog.FileName);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
