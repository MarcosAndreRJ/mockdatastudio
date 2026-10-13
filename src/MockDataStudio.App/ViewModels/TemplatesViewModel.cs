using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.Application;
using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Editing;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.App.ViewModels;

public sealed record TemplateRowItem(string Code, string Name, string Description, string Count, string Glyph, Brush Tint, Brush TintBg);

/// <summary>Catálogo de templates (S06), somente consulta: lista, busca, detalhes, campos, aliases e valores de exemplo.</summary>
public sealed partial class TemplatesViewModel : ObservableObject, IDisposable
{
    private readonly TemplateBrowser _browser;
    private readonly ICatalogInitializer _catalog;
    private readonly TemplateEditor _editor;
    private readonly CompositeTemplateEditor _composite;
    private readonly TemplateImportService _import;
    private readonly TemplatePackExporter _exporter;
    private readonly MappingLearningService _learning;
    private CancellationTokenSource? _loadCts;
    private string? _importSessionId;

    public TemplatesViewModel(TemplateBrowser browser, ICatalogInitializer catalog, TemplateEditor editor,
        CompositeTemplateEditor composite, TemplateImportService import, TemplatePackExporter exporter,
        MappingLearningService learning)
    {
        _browser = browser;
        _catalog = catalog;
        _editor = editor;
        _composite = composite;
        _import = import;
        _exporter = exporter;
        _learning = learning;
        ExportPackCommand = new AsyncRelayCommand(ExportPackAsync);
        NewPresetCommand = new RelayCommand(NewPreset);
        SavePresetCommand = new AsyncRelayCommand(SavePresetAsync);
        DeletePresetCommand = new AsyncRelayCommand(DeletePresetAsync);
        LoadPresetCommand = new RelayCommand(LoadPreset);
        ForgetRuleCommand = new AsyncRelayCommand<LearnedRuleRow?>(ForgetRuleAsync);
        _ = RefreshPresetsAsync();
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        NewTemplateCommand = new RelayCommand(NewTemplate);
        SaveTemplateCommand = new AsyncRelayCommand(SaveTemplateAsync);
        LoadTemplateCommand = new AsyncRelayCommand(LoadTemplateAsync);
        CloneTemplateCommand = new AsyncRelayCommand(CloneTemplateAsync);
        DeleteTemplateCommand = new AsyncRelayCommand(DeleteTemplateAsync);
        AnalyzeImportCommand = new AsyncRelayCommand(AnalyzeImportAsync);
        MergeImportCommand = new AsyncRelayCommand(MergeImportAsync, () => CanMergeImport);
        DiscardImportCommand = new RelayCommand(DiscardImport);
        CopyInstructionsCommand = new RelayCommand(CopyInstructions);
        _ = LoadAsync();
    }

    public IRelayCommand NewTemplateCommand { get; }
    public IAsyncRelayCommand SaveTemplateCommand { get; }
    public IAsyncRelayCommand LoadTemplateCommand { get; }
    public IAsyncRelayCommand CloneTemplateCommand { get; }
    public IAsyncRelayCommand DeleteTemplateCommand { get; }
    public IAsyncRelayCommand AnalyzeImportCommand { get; }
    public IAsyncRelayCommand MergeImportCommand { get; }
    public IRelayCommand DiscardImportCommand { get; }
    public IRelayCommand CopyInstructionsCommand { get; }

    // Formulário de template escalar (S07/S08). Valores e aliases: um por linha.
    [ObservableProperty]
    private string _formCode = string.Empty;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string _formDescription = string.Empty;

    [ObservableProperty]
    private string _formLogicalType = "string";

    [ObservableProperty]
    private string _formValues = string.Empty;

    [ObservableProperty]
    private string _formAliases = string.Empty;

    [ObservableProperty]
    private string _formStatus = string.Empty;

    // Template composto: campos e registros em texto (ver CompositeTemplateEditor).
    [ObservableProperty]
    private bool _formIsComposite;

    [ObservableProperty]
    private string _formFields = string.Empty;

    [ObservableProperty]
    private string _formRecords = string.Empty;

    // Importação por script SQL ou pack JSON (S09).
    [ObservableProperty]
    private string _importText = string.Empty;

    [ObservableProperty]
    private bool _importIsPackJson;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MergeImportCommand))]
    private bool _canMergeImport;

    // Composto: campos e registros em texto (ver CompositeTemplateEditor para o formato).
    private async Task SaveCompositeAsync()
    {
        var fields = CompositeTemplateEditor.ParseFields(FormFields);
        if (!fields.IsSuccess)
        {
            FormStatus = fields.Error.Message;
            return;
        }

        var records = CompositeTemplateEditor.ParseRecords(FormRecords, fields.Value!);
        if (!records.IsSuccess)
        {
            FormStatus = records.Error.Message;
            return;
        }

        var result = await _composite.SaveAsync(new CompositeTemplateForm(
            FormCode.Trim(), FormName.Trim(), FormDescription, fields.Value!,
            TemplateEditor.ParseLines(FormAliases), records.Value!.Select(r => (IReadOnlyDictionary<string, string?>)r).ToList()),
            CancellationToken.None);
        FormStatus = result.IsSuccess ? $"Template composto '{FormCode.Trim()}' salvo." : result.Error.Message;
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private async Task LoadCompositeAsync(string code)
    {
        var loaded = await _composite.LoadAsync(code, CancellationToken.None);
        if (!loaded.IsSuccess)
        {
            FormStatus = loaded.Error.Message;
            return;
        }

        var form = loaded.Value!;
        FormIsComposite = true;
        FormCode = form.Code;
        FormName = form.Name;
        FormDescription = form.Description ?? string.Empty;
        FormFields = CompositeTemplateEditor.FormatFields(form.Fields);
        FormRecords = CompositeTemplateEditor.FormatRecords(form.Records, form.Fields);
        FormAliases = string.Join(Environment.NewLine, form.Aliases);
        FormStatus = $"Template composto '{form.Code}' carregado para edição.";
    }

    private void NewTemplate()
    {
        FormCode = string.Empty;
        FormName = string.Empty;
        FormDescription = string.Empty;
        FormLogicalType = "string";
        FormValues = string.Empty;
        FormAliases = string.Empty;
        FormIsComposite = false;
        FormFields = string.Empty;
        FormRecords = string.Empty;
        FormStatus = "Novo template: preencha código, nome e valores (ou campos e registros, se for composto).";
    }

    private async Task SaveTemplateAsync()
    {
        if (FormIsComposite)
        {
            await SaveCompositeAsync();
            return;
        }

        if (!Enum.TryParse<LogicalType>(FormLogicalType, ignoreCase: true, out var logicalType))
        {
            FormStatus = $"Tipo lógico '{FormLogicalType}' inválido (ex.: string, integer, date).";
            return;
        }

        var result = await _editor.SaveScalarAsync(new ScalarTemplateForm(
            FormCode.Trim(), FormName.Trim(), FormDescription, logicalType,
            TemplateEditor.ParseLines(FormValues), TemplateEditor.ParseLines(FormAliases)), CancellationToken.None);
        FormStatus = result.IsSuccess ? $"Template '{FormCode.Trim()}' salvo." : result.Error.Message;
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private async Task LoadTemplateAsync()
    {
        if (SelectedTemplate is null)
        {
            FormStatus = "Selecione um template na lista.";
            return;
        }

        var details = await _browser.GetDetailsAsync(SelectedTemplate.Code, CancellationToken.None);
        if (details is not null && details.IsComposite)
        {
            await LoadCompositeAsync(SelectedTemplate.Code);
            return;
        }

        var loaded = await _editor.LoadScalarAsync(SelectedTemplate.Code, CancellationToken.None);
        if (!loaded.IsSuccess)
        {
            FormStatus = loaded.Error.Message;
            return;
        }

        FormIsComposite = false;
        var form = loaded.Value!;
        FormCode = form.Code;
        FormName = form.Name;
        FormDescription = form.Description ?? string.Empty;
        FormLogicalType = form.LogicalType.ToString();
        FormValues = string.Join(Environment.NewLine, form.Values);
        FormAliases = string.Join(Environment.NewLine, form.Aliases);
        FormStatus = $"Template '{form.Code}' carregado para edição.";
    }

    private async Task CloneTemplateAsync()
    {
        if (SelectedTemplate is null)
        {
            FormStatus = "Selecione o template a copiar.";
            return;
        }

        var newCode = FormCode.Trim();
        if (newCode.Length == 0)
        {
            FormStatus = "Informe o código da cópia no campo Código.";
            return;
        }

        var result = await _editor.CloneAsync(SelectedTemplate.Code, newCode, CancellationToken.None);
        FormStatus = result.IsSuccess ? $"Cópia '{newCode}' criada." : result.Error.Message;
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private async Task DeleteTemplateAsync()
    {
        if (SelectedTemplate is null)
        {
            FormStatus = "Selecione um template para excluir.";
            return;
        }

        var result = await _editor.DeleteAsync(SelectedTemplate.Code, CancellationToken.None);
        FormStatus = result.IsSuccess && result.Value
            ? $"Template '{SelectedTemplate.Code}' excluído."
            : result.IsSuccess ? "Template não encontrado." : result.Error.Message;
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private async Task AnalyzeImportAsync()
    {
        DiscardImport();
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
        var issues = analysis.Issues.Count == 0 ? string.Empty : $" {analysis.Issues.Count} problema(s) encontrado(s).";
        ImportStatus = analysis.CanMerge
            ? $"Análise ok: {analysis.TemplatesNew} template(s) novo(s), {analysis.TemplatesChanged} alterado(s), {analysis.ValuesNew} valor(es) novo(s).{issues}"
            : $"Não é possível importar.{issues}";
    }

    private async Task MergeImportAsync()
    {
        if (_importSessionId is null)
        {
            return;
        }

        var merged = await _import.MergeAsync(_importSessionId, CancellationToken.None);
        ImportStatus = merged.IsSuccess
            ? $"Importado: {merged.Value!.Inserted} inserido(s), {merged.Value.Updated} atualizado(s), {merged.Value.Skipped} ignorado(s). Backup: {merged.Value.BackupPath}"
            : merged.Error.Message;
        _importSessionId = null;
        CanMergeImport = false;
        if (merged.IsSuccess)
        {
            await LoadAsync();
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

    // Copia as instruções para uma IA gerar o script no formato esperado (S09).
    // ---------- Presets de gerador (FR-059) e regras aprendidas (FR-064) ----------

    public ObservableCollection<PresetRow> Presets { get; } = [];
    public ObservableCollection<LearnedRuleRow> LearnedRules { get; } = [];

    public IRelayCommand NewPresetCommand { get; }
    public IAsyncRelayCommand SavePresetCommand { get; }
    public IAsyncRelayCommand DeletePresetCommand { get; }
    public IRelayCommand LoadPresetCommand { get; }
    public IAsyncRelayCommand<LearnedRuleRow?> ForgetRuleCommand { get; }

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
    private string _rulesStatus = string.Empty;

    private async Task RefreshPresetsAsync()
    {
        var presets = await _learning.ListPresetsAsync(CancellationToken.None);
        Presets.Clear();
        foreach (var p in presets.OrderBy(p => p.Code, StringComparer.Ordinal))
        {
            Presets.Add(new PresetRow(p.Id, p.Code, p.Name, p.GeneratorId, p.GeneratorVersion, p.ParametersJson, p.Description, p.IsBuiltIn));
        }

        LearnedRules.Clear();
        foreach (var rule in await _learning.LearnedRulesAsync(CancellationToken.None))
        {
            LearnedRules.Add(new LearnedRuleRow(rule.Id, rule.ColumnPattern, rule.TargetRef, rule.TargetKind.ToString()));
        }

        RulesStatus = LearnedRules.Count == 0
            ? "Nenhuma regra aprendida. Use \"Lembrar para outros projetos\" no Mapeamento."
            : $"{LearnedRules.Count} regra(s) aprendida(s).";
    }

    private void NewPreset()
    {
        SelectedPreset = null;
        PresetCode = string.Empty;
        PresetName = string.Empty;
        PresetGenerator = string.Empty;
        PresetVersion = "1";
        PresetParameters = "{}";
        PresetDescription = string.Empty;
        PresetStatus = "Novo preset: preencha código, nome e gerador.";
    }

    private void LoadPreset()
    {
        if (SelectedPreset is null)
        {
            PresetStatus = "Selecione um preset na lista.";
            return;
        }

        PresetCode = SelectedPreset.Code;
        PresetName = SelectedPreset.Name;
        PresetGenerator = SelectedPreset.GeneratorId;
        PresetVersion = SelectedPreset.GeneratorVersion.ToString(CultureInfo.InvariantCulture);
        PresetParameters = SelectedPreset.ParametersJson;
        PresetDescription = SelectedPreset.Description ?? string.Empty;
        PresetStatus = SelectedPreset.IsBuiltIn
            ? $"Preset embutido '{SelectedPreset.Code}': somente leitura."
            : $"Preset '{SelectedPreset.Code}' carregado.";
    }

    private async Task SavePresetAsync()
    {
        if (!int.TryParse(PresetVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            PresetStatus = "Versão do gerador deve ser um número inteiro.";
            return;
        }

        var result = await _learning.SavePresetAsync(new GeneratorPresetInfo(
            SelectedPreset?.Id ?? 0, PresetCode.Trim(), PresetName.Trim(), PresetGenerator.Trim(), version,
            string.IsNullOrWhiteSpace(PresetParameters) ? "{}" : PresetParameters.Trim(),
            string.IsNullOrWhiteSpace(PresetDescription) ? null : PresetDescription.Trim(), IsBuiltIn: false), CancellationToken.None);
        PresetStatus = result.IsSuccess ? $"Preset '{PresetCode.Trim()}' salvo." : result.Error.Message;
        if (result.IsSuccess)
        {
            await RefreshPresetsAsync();
        }
    }

    private async Task DeletePresetAsync()
    {
        var code = SelectedPreset?.Code ?? PresetCode.Trim();
        var result = await _learning.DeletePresetAsync(code, CancellationToken.None);
        PresetStatus = result.IsSuccess && result.Value
            ? $"Preset '{code}' excluído."
            : result.IsSuccess ? "Preset não encontrado." : result.Error.Message;
        if (result.IsSuccess)
        {
            await RefreshPresetsAsync();
        }
    }

    private async Task ForgetRuleAsync(LearnedRuleRow? rule)
    {
        if (rule is null)
        {
            return;
        }

        await _learning.ForgetAsync(rule.Id, CancellationToken.None);
        await RefreshPresetsAsync();
        RulesStatus = $"Regra '{rule.Column}' esquecida.";
    }

    // Exporta os templates do usuário como pack JSON (C8a.3). Código e nome do pack vêm dos campos da tela.
    public IAsyncRelayCommand ExportPackCommand { get; }

    [ObservableProperty]
    private string _packExportCode = "MEU_PACK";

    [ObservableProperty]
    private string _packExportName = "Meu pack";

    private async Task ExportPackAsync()
    {
        var dialog = new SaveFileDialog { Filter = "Pack de templates (*.json)|*.json", DefaultExt = ".json", FileName = PackExportCode.Trim() + ".json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await _exporter.ExportAsync(PackExportCode.Trim(), PackExportName.Trim(), string.Empty, null, CancellationToken.None);
        if (!result.IsSuccess)
        {
            ImportStatus = result.Error.Message;
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, result.Value!, new System.Text.UTF8Encoding(false));
        ImportStatus = $"Pack exportado para {dialog.FileName}.";
    }

    private void CopyInstructions()
    {
        System.Windows.Clipboard.SetText(TemplateInstructions.ForAi);
        ImportStatus = "Instruções copiadas. Cole na IA e depois cole o script aqui.";
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

// Linha da lista de presets (FR-059). Embutidos não podem ser alterados nem excluídos.
public sealed record PresetRow(long Id, string Code, string Name, string GeneratorId, int GeneratorVersion, string ParametersJson, string? Description, bool IsBuiltIn);

// Linha da lista de regras aprendidas (FR-064).
public sealed record LearnedRuleRow(long Id, string Column, string Target, string TargetKind);
