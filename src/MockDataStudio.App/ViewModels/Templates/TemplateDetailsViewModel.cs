using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.App.Resources;
using MockDataStudio.Application;
using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Editing;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.App.ViewModels.Templates;

/// <summary>
/// Detalhes do template selecionado e edição (TC-101). A edição por formulário é provisória: a Fase 3 a substitui pelo
/// assistente, mantendo as mesmas regras de gravação (<see cref="TemplateEditor"/> e <see cref="CompositeTemplateEditor"/>).
/// </summary>
public sealed partial class TemplateDetailsViewModel : ObservableObject
{
    private readonly TemplateBrowser _browser;
    private readonly TemplateEditor _editor;
    private readonly CompositeTemplateEditor _composite;
    private string? _shownCode;
    private int _suppressDirty;

    public TemplateDetailsViewModel(TemplateBrowser browser, TemplateEditor editor, CompositeTemplateEditor composite)
    {
        _browser = browser;
        _editor = editor;
        _composite = composite;

        NewTemplateCommand = new RelayCommand(NewTemplate);
        SaveTemplateCommand = new AsyncRelayCommand(async () => { await SaveTemplateAsync(); });
        LoadTemplateCommand = new AsyncRelayCommand(LoadTemplateAsync);
        CloneTemplateCommand = new AsyncRelayCommand(CloneTemplateAsync);
        DeleteTemplateCommand = new AsyncRelayCommand(DeleteTemplateAsync);
        UseScalarExampleCommand = new RelayCommand(UseScalarExample);
        UseCompositeExampleCommand = new RelayCommand(UseCompositeExample);
    }

    /// <summary>Disparado depois de salvar, copiar ou excluir; o argumento é o código a selecionar (nulo = manter).</summary>
    public event EventHandler<string?>? CatalogChanged;

    public IRelayCommand NewTemplateCommand { get; }
    public IAsyncRelayCommand SaveTemplateCommand { get; }
    public IAsyncRelayCommand LoadTemplateCommand { get; }
    public IAsyncRelayCommand CloneTemplateCommand { get; }
    public IAsyncRelayCommand DeleteTemplateCommand { get; }
    public IRelayCommand UseScalarExampleCommand { get; }
    public IRelayCommand UseCompositeExampleCommand { get; }

    public ObservableCollection<TemplateFieldInfo> Fields { get; } = [];
    public ObservableCollection<string> Aliases { get; } = [];
    public ObservableCollection<string> SampleValues { get; } = [];

    // ---------- Detalhes ----------

    [ObservableProperty]
    private string _title = TemplatesText.DetailsNoSelection;

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

    // ---------- Formulário (provisório até a Fase 3) ----------

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

    [ObservableProperty]
    private bool _formIsComposite;

    [ObservableProperty]
    private string _formFields = string.Empty;

    [ObservableProperty]
    private string _formRecords = string.Empty;

    /// <summary>Há alterações no formulário ainda não salvas (alimenta a guarda de navegação).</summary>
    [ObservableProperty]
    private bool _isDirty;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_suppressDirty == 0 && e.PropertyName is { } name && name.StartsWith("Form", StringComparison.Ordinal) && name != nameof(FormStatus))
        {
            IsDirty = true;
        }
    }

    // Altera o formulário sem marcá-lo como modificado (carga de um template, "Novo", depois de salvar).
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

    // ---------- Seleção ----------

    /// <summary>Mostra os detalhes do template selecionado na lista (nulo limpa a área).</summary>
    public async Task ShowAsync(TemplateRowItem? row)
    {
        Fields.Clear();
        Aliases.Clear();
        SampleValues.Clear();
        HasSelection = row is not null;
        _shownCode = row?.Code;
        if (row is null)
        {
            Title = TemplatesText.DetailsNoSelection;
            return;
        }

        var details = await _browser.GetDetailsAsync(row.Code, CancellationToken.None);
        if (details is null || _shownCode != row.Code)
        {
            return;
        }

        Title = details.Info.Name;
        Subtitle = details.Info.Code;
        Description = details.Info.Description ?? TemplatesText.DetailsNoDescription;
        KindText = details.IsComposite ? TemplatesText.KindComposite : TemplatesText.KindScalar;
        LogicalTypeText = details.Info.LogicalType?.ToString() ?? "—";
        ValueCountText = details.Info.ValueCount.ToString("N0", CultureInfo.CurrentCulture);
        PackText = $"{details.Info.PackCode}{(details.Info.IsBuiltIn ? TemplatesText.EmbeddedSuffix : string.Empty)} · {details.Info.Locale}";
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

    // ---------- Exemplos ----------

    private void UseScalarExample()
    {
        var example = TemplateExamples.Scalar();
        FillForm(example.Code, example.Name, example.Description, isComposite: false);
        FormLogicalType = example.LogicalType.ToString();
        FormValues = string.Join(Environment.NewLine, example.Values);
        FormAliases = string.Join(Environment.NewLine, example.Aliases);
        FormStatus = TemplatesText.ExampleScalarLoaded(example.Code);
    }

    private void UseCompositeExample()
    {
        FillForm(TemplateExamples.CompositeCode, TemplateExamples.CompositeName, TemplateExamples.CompositeDescription, isComposite: true);
        FormFields = TemplateExamples.CompositeFields;
        FormRecords = TemplateExamples.CompositeRecords;
        FormAliases = TemplateExamples.CompositeAliases;
        FormStatus = TemplatesText.ExampleCompositeLoaded(TemplateExamples.CompositeCode);
    }

    private void FillForm(string code, string name, string? description, bool isComposite)
    {
        FormCode = code;
        FormName = name;
        FormDescription = description ?? string.Empty;
        FormIsComposite = isComposite;
        if (!isComposite)
        {
            FormFields = string.Empty;
            FormRecords = string.Empty;
        }
    }

    // ---------- Edição ----------

    private void NewTemplate()
    {
        Clean(() =>
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
            FormStatus = TemplatesText.FormNew;
        });
    }

    /// <summary>Salva o formulário. Retorna verdadeiro se gravou (usado também pela guarda de navegação).</summary>
    public async Task<bool> SaveTemplateAsync()
    {
        var saved = FormIsComposite ? await SaveCompositeAsync() : await SaveScalarAsync();
        if (saved)
        {
            IsDirty = false;
            CatalogChanged?.Invoke(this, TemplateEditor.NormalizeCode(FormCode));
        }

        return saved;
    }

    private async Task<bool> SaveScalarAsync()
    {
        if (!Enum.TryParse<LogicalType>(FormLogicalType, ignoreCase: true, out var logicalType))
        {
            FormStatus = TemplatesText.FormInvalidLogicalType(FormLogicalType);
            return false;
        }

        var code = TemplateEditor.NormalizeCode(FormCode);
        FormCode = code;
        var result = await _editor.SaveScalarAsync(new ScalarTemplateForm(
            code, FormName.Trim(), FormDescription, logicalType,
            TemplateEditor.ParseLines(FormValues), TemplateEditor.ParseLines(FormAliases)), CancellationToken.None);
        FormStatus = result.IsSuccess ? TemplatesText.FormSaved(code) : result.Error.Message;
        return result.IsSuccess;
    }

    private async Task<bool> SaveCompositeAsync()
    {
        var code = TemplateEditor.NormalizeCode(FormCode);
        FormCode = code;
        var fields = CompositeTemplateEditor.ParseFields(FormFields);
        if (!fields.IsSuccess)
        {
            FormStatus = fields.Error.Message;
            return false;
        }

        var records = CompositeTemplateEditor.ParseRecords(FormRecords, fields.Value!);
        if (!records.IsSuccess)
        {
            FormStatus = records.Error.Message;
            return false;
        }

        var result = await _composite.SaveAsync(new CompositeTemplateForm(
            code, FormName.Trim(), FormDescription, fields.Value!,
            TemplateEditor.ParseLines(FormAliases), records.Value!.Select(r => (IReadOnlyDictionary<string, string?>)r).ToList()),
            CancellationToken.None);
        FormStatus = result.IsSuccess ? TemplatesText.FormSavedComposite(code) : result.Error.Message;
        return result.IsSuccess;
    }

    /// <summary>Descarta as alterações do formulário (escolha "Descartar" da guarda de navegação).</summary>
    public void DiscardChanges() => NewTemplate();

    private async Task LoadTemplateAsync()
    {
        if (_shownCode is null)
        {
            FormStatus = TemplatesText.FormSelectTemplate;
            return;
        }

        var details = await _browser.GetDetailsAsync(_shownCode, CancellationToken.None);
        if (details is not null && details.IsComposite)
        {
            await LoadCompositeAsync(_shownCode);
            return;
        }

        var loaded = await _editor.LoadScalarAsync(_shownCode, CancellationToken.None);
        if (!loaded.IsSuccess)
        {
            FormStatus = loaded.Error.Message;
            return;
        }

        var form = loaded.Value!;
        Clean(() =>
        {
            FormIsComposite = false;
            FormCode = form.Code;
            FormName = form.Name;
            FormDescription = form.Description ?? string.Empty;
            FormLogicalType = form.LogicalType.ToString();
            FormValues = string.Join(Environment.NewLine, form.Values);
            FormAliases = string.Join(Environment.NewLine, form.Aliases);
            FormStatus = TemplatesText.FormLoaded(form.Code);
        });
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
        Clean(() =>
        {
            FormIsComposite = true;
            FormCode = form.Code;
            FormName = form.Name;
            FormDescription = form.Description ?? string.Empty;
            FormFields = CompositeTemplateEditor.FormatFields(form.Fields);
            FormRecords = CompositeTemplateEditor.FormatRecords(form.Records, form.Fields);
            FormAliases = string.Join(Environment.NewLine, form.Aliases);
            FormStatus = TemplatesText.FormLoadedComposite(form.Code);
        });
    }

    private async Task CloneTemplateAsync()
    {
        if (_shownCode is null)
        {
            FormStatus = TemplatesText.FormCloneSelect;
            return;
        }

        var newCode = TemplateEditor.NormalizeCode(FormCode);
        if (newCode.Length == 0)
        {
            FormStatus = TemplatesText.FormCloneNeedsCode;
            return;
        }

        FormCode = newCode;
        var result = await _editor.CloneAsync(_shownCode, newCode, CancellationToken.None);
        FormStatus = result.IsSuccess ? TemplatesText.FormCloned(newCode) : result.Error.Message;
        if (result.IsSuccess)
        {
            CatalogChanged?.Invoke(this, newCode);
        }
    }

    private async Task DeleteTemplateAsync()
    {
        if (_shownCode is null)
        {
            FormStatus = TemplatesText.FormDeleteSelect;
            return;
        }

        var code = _shownCode;
        var result = await _editor.DeleteAsync(code, CancellationToken.None);
        FormStatus = result.IsSuccess && result.Value
            ? TemplatesText.FormDeleted(code)
            : result.IsSuccess ? TemplatesText.FormNotFound : result.Error.Message;
        if (result.IsSuccess)
        {
            CatalogChanged?.Invoke(this, null);
        }
    }
}
