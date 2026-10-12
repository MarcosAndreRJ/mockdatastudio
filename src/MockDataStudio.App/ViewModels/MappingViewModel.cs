using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MockDataStudio.App.Samples;
using MockDataStudio.Application;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Naming;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Mapping;

namespace MockDataStudio.App.ViewModels;

public sealed record MappingTableItem(ObjectName Table, string Name, string FieldInfo, string Status, Brush StatusFg, Brush StatusBg);

public sealed record CandidateItem(int Index, string Label, string Score, string Confidence, string Generator);

public sealed record GeneratorChoice(string Id, string Display)
{
    public override string ToString() => Display;
}

/// <summary>Uma coluna na grade de mapeamento, com os textos e cores já resolvidos.</summary>
public sealed class MappingFieldItem(MappingFieldRow row)
{
    public MappingFieldRow Row { get; } = row;
    public string Name => Row.Name;
    public string Glyph => Row.Constraints.Contains("PK", StringComparison.Ordinal) ? "" : Row.Constraints.Contains("FK", StringComparison.Ordinal) ? "" : "";
    public string Type => Row.NativeType;
    public string Constraints => Row.Constraints;
    public string SuggestedTemplate => Row.SuggestedTemplate ?? "—";
    public string Generator => Row.Generator ?? "—";
    public string Confidence => Row.ConfidenceLabel;
    public Brush ConfidenceFg => Theme.Brush(MappingViewModel.ConfidenceBrushKey(Row.ConfidenceLabel, soft: false));
    public Brush ConfidenceBg => Theme.Brush(MappingViewModel.ConfidenceBrushKey(Row.ConfidenceLabel, soft: true));
    public string Status => MappingViewModel.StatusText(Row.Status);
    public Brush StatusFg => Theme.Brush(MappingViewModel.StatusBrushKey(Row.Status, soft: false));
    public Brush StatusBg => Theme.Brush(MappingViewModel.StatusBrushKey(Row.Status, soft: true));
}

/// <summary>Parâmetro editável de um gerador, montado a partir do descriptor.</summary>
public sealed partial class ParameterItem(GeneratorParameterDefinition definition, string value) : ObservableObject
{
    public GeneratorParameterDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    public string Label => Definition.Required ? Definition.Label + " *" : Definition.Label;
    public string? Help => Definition.Help;
    public IReadOnlyList<string> Options => Definition.EnumValues ?? (Definition.Type == "bool" ? ["true", "false"] : []);
    public bool HasOptions => Options.Count > 0;
    public bool IsMultiline => Definition.Type is "stringList" or "doubleList";

    [ObservableProperty]
    private string _value = value;
}

/// <summary>Mapeamento semântico (S07): sugestões com evidências, aceitar/alterar vínculo, editor de gerador, preview e desfazer.</summary>
public sealed partial class MappingViewModel : ObservableObject
{
    public static readonly string[] StatusFilters = ["Todos", "Pendentes", "Mapeados"];

    private readonly MappingSession _session;
    private readonly ProjectWorkspace _workspace;
    private readonly GeneratorPreviewService _preview;
    private readonly IGeneratorRegistry _registry;
    private readonly ICatalogInitializer _catalog;
    private string _snapshotSignature = "?";
    private int _previewRound;
    private bool _loading;

    public MappingViewModel(
        MappingSession session, ProjectWorkspace workspace, GeneratorPreviewService preview,
        IGeneratorRegistry registry, ICatalogInitializer catalog)
    {
        _session = session;
        _workspace = workspace;
        _preview = preview;
        _registry = registry;
        _catalog = catalog;

        GeneratorChoices = [.. registry.All.OrderBy(d => d.Category).ThenBy(d => d.DisplayName).Select(d => new GeneratorChoice(d.GeneratorId, $"{d.DisplayName}  ({d.GeneratorId})"))];

        AutoMapCommand = new RelayCommand(AutoMap, () => IsReady);
        ReviewCommand = new RelayCommand(() => StatusFilter = StatusFilters[1], () => IsReady);
        ValidateCommand = new RelayCommand(Validate, () => IsReady);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => _workspace.Current is not null);
        UndoCommand = new RelayCommand(Undo, () => _workspace.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => _workspace.CanRedo);
        AcceptCandidateCommand = new AsyncRelayCommand<CandidateItem>(AcceptAsync);
        ApplyGeneratorCommand = new AsyncRelayCommand(ApplyGeneratorAsync, () => SelectedField is not null && SelectedGenerator is not null);
        ApplyOptionsCommand = new RelayCommand(ApplyOptions, () => SelectedField?.Row.Binding is not null);
        ClearCommand = new RelayCommand(ClearBinding, () => SelectedField?.Row.Binding is not null);
        RegenerateCommand = new AsyncRelayCommand(PreviewAsync, () => SelectedField is not null);
        ReloadCommand = new AsyncRelayCommand(LoadAsync);

        _workspace.Changed += (_, _) => OnWorkspaceChanged();
        _ = LoadAsync();
    }

    public IRelayCommand AutoMapCommand { get; }
    public IRelayCommand ReviewCommand { get; }
    public IRelayCommand ValidateCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IAsyncRelayCommand<CandidateItem> AcceptCandidateCommand { get; }
    public IAsyncRelayCommand ApplyGeneratorCommand { get; }
    public IRelayCommand ApplyOptionsCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IAsyncRelayCommand RegenerateCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }

    public IReadOnlyList<GeneratorChoice> GeneratorChoices { get; }

    public ObservableCollection<MappingTableItem> Tables { get; } = [];
    public ObservableCollection<MappingFieldItem> Fields { get; } = [];
    public ObservableCollection<CandidateItem> Candidates { get; } = [];
    public ObservableCollection<string> MatchedAliases { get; } = [];
    public ObservableCollection<ScoreReason> ScoringReasons { get; } = [];
    public ObservableCollection<ParameterItem> Parameters { get; } = [];
    public ObservableCollection<string> PreviewValues { get; } = [];

    [ObservableProperty]
    private MappingTableItem? _selectedTable;

    [ObservableProperty]
    private MappingFieldItem? _selectedField;

    [ObservableProperty]
    private CandidateItem? _selectedCandidate;

    [ObservableProperty]
    private GeneratorChoice? _selectedGenerator;

    [ObservableProperty]
    private string _fieldSearch = string.Empty;

    [ObservableProperty]
    private string _statusFilter = StatusFilters[0];

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private string _message = "Carregando…";

    [ObservableProperty]
    private string _projectName = "Nenhum projeto";

    [ObservableProperty]
    private string _tablesTitle = "Tabelas";

    [ObservableProperty]
    private string _fieldsTitle = "Campos";

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private string _detailTitle = "Selecione um campo";

    [ObservableProperty]
    private string _detailSubtitle = string.Empty;

    [ObservableProperty]
    private string _detailStatus = string.Empty;

    [ObservableProperty]
    private Brush _detailStatusFg = Theme.Brush("TextSecondaryBrush");

    [ObservableProperty]
    private Brush _detailStatusBg = Theme.Brush("SurfaceAltBrush");

    [ObservableProperty]
    private string _nullRateText = "0";

    [ObservableProperty]
    private bool _isUnique;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string _editorError = string.Empty;

    [ObservableProperty]
    private string _previewError = string.Empty;

    [ObservableProperty]
    private string _undoLabel = "Desfazer";

    public static string StatusText(FieldStatus status) => status switch
    {
        FieldStatus.Mapped => "Mapeado",
        FieldStatus.Suggested => "Sugerido",
        FieldStatus.Ambiguous => "Ambíguo",
        FieldStatus.NeedsReview => "Revisar",
        FieldStatus.Orphaned => "Órfão",
        _ => "Sem mapeamento",
    };

    public static string StatusBrushKey(FieldStatus status, bool soft) => (status switch
    {
        FieldStatus.Mapped => "Success",
        FieldStatus.Suggested => "Accent",
        FieldStatus.Ambiguous or FieldStatus.NeedsReview => "Warning",
        _ => "Danger",
    }) + (soft ? "SoftBrush" : "Brush");

    public static string ConfidenceBrushKey(string label, bool soft) => (label.ToLowerInvariant() switch
    {
        "confirmed" or "high" => "Success",
        "medium" => "Warning",
        _ => "Danger",
    }) + (soft ? "SoftBrush" : "Brush");

    partial void OnSelectedTableChanged(MappingTableItem? value)
    {
        if (!_loading)
        {
            RebuildFields();
        }
    }

    partial void OnSelectedFieldChanged(MappingFieldItem? value)
    {
        ApplyGeneratorCommand.NotifyCanExecuteChanged();
        ApplyOptionsCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        RegenerateCommand.NotifyCanExecuteChanged();
        ShowField(value);
    }

    partial void OnSelectedGeneratorChanged(GeneratorChoice? value)
    {
        ApplyGeneratorCommand.NotifyCanExecuteChanged();
        if (value is not null && SelectedField is { } field && !_loading)
        {
            BuildParameters(value.Id, field.Row.Binding?.Generator.GeneratorId == value.Id ? field.Row.Binding.Generator.Parameters : null);
        }
    }

    partial void OnFieldSearchChanged(string value) => RebuildFields();

    partial void OnStatusFilterChanged(string value) => RebuildFields();

    partial void OnIsReadyChanged(bool value)
    {
        AutoMapCommand.NotifyCanExecuteChanged();
        ReviewCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- carga

    private static string SignatureOf(Projects.MdsProject? project) =>
        project is null ? "-" : project.ActiveSnapshotId + "|" + string.Join(",", project.Snapshots.Select(s => s.StructureHash));

    private void OnWorkspaceChanged()
    {
        ProjectName = _workspace.Current?.Name ?? "Nenhum projeto";
        RefreshHistoryCommands();

        if (SignatureOf(_workspace.Current) != _snapshotSignature)
        {
            _ = LoadAsync();
            return;
        }

        if (_session.IsLoaded)
        {
            _session.SyncWithProject();
            RebuildTables();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            ProjectName = _workspace.Current?.Name ?? "Nenhum projeto";
            _snapshotSignature = SignatureOf(_workspace.Current);
            RefreshHistoryCommands();

            var ready = await _catalog.EnsureReadyAsync(CancellationToken.None);
            if (!ready.IsSuccess)
            {
                IsReady = false;
                Message = "Catálogo indisponível: " + ready.Error.Message;
                return;
            }

            var loaded = await _session.LoadAsync(CancellationToken.None);
            IsReady = loaded.IsSuccess;
            if (!loaded.IsSuccess)
            {
                Tables.Clear();
                Fields.Clear();
                Message = _workspace.Current is null
                    ? "Nenhum projeto aberto. Crie um projeto em Home e capture a estrutura em Schema Digest."
                    : "O projeto não tem estrutura capturada. Use Schema Digest para capturar ou importar um digest.";
                return;
            }

            Message = string.Empty;
            RebuildTables();
        }
        finally
        {
            _loading = false;
        }

        RebuildFields();
    }

    private void RebuildTables()
    {
        var selected = SelectedTable?.Table;
        var previousLoading = _loading;
        _loading = true;
        try
        {
            Tables.Clear();
            foreach (var table in _session.Tables())
            {
                Tables.Add(new MappingTableItem(
                    table.Table, table.Name,
                    $"{table.Fields} campos • {table.Pending} pendente(s)",
                    table.IsComplete ? "Completo" : "Pendente",
                    Theme.Brush(table.IsComplete ? "SuccessBrush" : "WarningBrush"),
                    Theme.Brush(table.IsComplete ? "SuccessSoftBrush" : "WarningSoftBrush")));
            }

            SelectedTable = Tables.FirstOrDefault(t => t.Table == selected) ?? Tables.FirstOrDefault();
            PendingCount = _session.Tables().Sum(t => t.Pending);
            TablesTitle = $"Tabelas ({Tables.Count})";
        }
        finally
        {
            _loading = previousLoading;
        }

        if (!previousLoading)
        {
            RebuildFields();
        }
    }

    private void RebuildFields()
    {
        var selected = SelectedField?.Row.Column;
        Fields.Clear();
        if (SelectedTable is not { } table)
        {
            FieldsTitle = "Campos";
            SelectedField = null;
            return;
        }

        var search = FieldSearch.Trim();
        foreach (var row in _session.Fields(table.Table))
        {
            var mapped = row.Status == FieldStatus.Mapped;
            if (StatusFilter == StatusFilters[1] && mapped)
            {
                continue;
            }

            if (StatusFilter == StatusFilters[2] && !mapped)
            {
                continue;
            }

            if (search.Length > 0 && !row.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Fields.Add(new MappingFieldItem(row));
        }

        FieldsTitle = $"Campos - {table.Name} ({Fields.Count})";
        SelectedField = Fields.FirstOrDefault(f => f.Row.Column == selected) ?? Fields.FirstOrDefault();
    }

    // ---------------------------------------------------------------- detalhes do campo

    private void ShowField(MappingFieldItem? field)
    {
        Candidates.Clear();
        MatchedAliases.Clear();
        ScoringReasons.Clear();
        Parameters.Clear();
        PreviewValues.Clear();
        EditorError = string.Empty;
        PreviewError = string.Empty;

        if (field is null)
        {
            DetailTitle = "Selecione um campo";
            DetailSubtitle = string.Empty;
            DetailStatus = string.Empty;
            SelectedGenerator = null;
            return;
        }

        var row = field.Row;
        DetailTitle = row.Name;
        DetailSubtitle = $"Tipo: {row.NativeType}  |  Tabela: {row.Column.Table.Name}" + (row.Constraints.Length > 0 ? $"  |  {row.Constraints}" : string.Empty);
        DetailStatus = field.Status;
        DetailStatusFg = field.StatusFg;
        DetailStatusBg = field.StatusBg;
        NullRateText = (row.Binding?.NullRate ?? 0).ToString("0.##", CultureInfo.InvariantCulture);
        IsUnique = row.Binding?.Unique ?? false;
        Note = row.Binding?.Note ?? string.Empty;

        var index = 0;
        foreach (var candidate in row.Suggestion.Candidates)
        {
            Candidates.Add(new CandidateItem(index++, MappingSession.DescribeTarget(candidate.Target),
                candidate.Score.ToString("0", CultureInfo.InvariantCulture), candidate.Confidence.ToString(), candidate.Target.Spec.GeneratorId));
        }

        SelectedCandidate = Candidates.FirstOrDefault();
        ShowEvidence(row.Suggestion.Candidates.Count > 0 ? row.Suggestion.Candidates[0] : null);

        var spec = row.Binding?.Generator ?? row.Suggestion.Best?.Target.Spec;
        var previous = _loading;
        _loading = true;
        SelectedGenerator = spec is null ? null : GeneratorChoices.FirstOrDefault(g => g.Id == spec.GeneratorId);
        _loading = previous;
        if (spec is not null && SelectedGenerator is not null)
        {
            BuildParameters(spec.GeneratorId, spec.Parameters);
        }

        _ = PreviewAsync();
    }

    partial void OnSelectedCandidateChanged(CandidateItem? value)
    {
        if (value is not null && SelectedField is { } field)
        {
            ShowEvidence(field.Row.Suggestion.Candidates.ElementAtOrDefault(value.Index));
        }
    }

    private void ShowEvidence(MappingCandidate? candidate)
    {
        MatchedAliases.Clear();
        ScoringReasons.Clear();
        if (candidate is null)
        {
            return;
        }

        foreach (var evidence in candidate.Evidence)
        {
            if (evidence.Kind is EvidenceKind.ExactAlias or EvidenceKind.NormalizedAlias or EvidenceKind.CoreTokenAlias
                or EvidenceKind.ExactTemplateCode or EvidenceKind.TokenOverlap)
            {
                MatchedAliases.Add(evidence.Description);
            }

            ScoringReasons.Add(new ScoreReason(evidence.Description,
                (evidence.Points >= 0 ? "+" : string.Empty) + evidence.Points.ToString("0.#", CultureInfo.InvariantCulture)));
        }
    }

    private void BuildParameters(string generatorId, JsonObject? current)
    {
        Parameters.Clear();
        if (!_registry.TryGet(generatorId, out var factory))
        {
            return;
        }

        var text = GeneratorParameterBinder.ToText(factory.Descriptor, current);
        foreach (var definition in factory.Descriptor.Parameters)
        {
            Parameters.Add(new ParameterItem(definition, text[definition.Name]));
        }
    }

    // ---------------------------------------------------------------- ações

    private void AutoMap()
    {
        _session.AutoMapAll();
        Message = $"Mapeamento automático aplicado. {PendingCount} campo(s) ainda pendente(s).";
    }

    private async Task AcceptAsync(CandidateItem? candidate)
    {
        if (candidate is null || SelectedField is not { } field)
        {
            return;
        }

        await _session.AcceptAsync(field.Row.Column, candidate.Index, learn: true, CancellationToken.None);
        Message = $"{field.Name} mapeado para {candidate.Label}.";
    }

    private async Task ApplyGeneratorAsync()
    {
        EditorError = string.Empty;
        if (SelectedField is not { } field || SelectedGenerator is not { } choice || !_registry.TryGet(choice.Id, out var factory))
        {
            return;
        }

        var values = Parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var parameters = GeneratorParameterBinder.FromText(factory.Descriptor, values);
        if (!parameters.IsSuccess)
        {
            EditorError = parameters.Error.Message;
            return;
        }

        var spec = new GeneratorSpec(choice.Id, factory.Descriptor.CurrentVersion, parameters.Value);
        var column = field.Row.Column;
        var model = _session.Context?.Schema.Namespaces.FirstOrDefault(n => n.Name == column.Table.Namespace)?
            .Tables.FirstOrDefault(t => t.Name == column.Table.Name)?.Columns.FirstOrDefault(c => c.Name == column.Column);
        if (model is not null)
        {
            var check = await _preview.PreviewAsync(spec, column.Table, model, 1, 1, CancellationToken.None);
            if (!check.IsSuccess)
            {
                EditorError = check.Error.Message;
                return;
            }
        }

        await _session.SetGeneratorAsync(column, spec, learn: true, CancellationToken.None);
        Message = $"Gerador de {field.Name} definido: {factory.Descriptor.DisplayName}.";
    }

    private void ApplyOptions()
    {
        EditorError = string.Empty;
        if (SelectedField is not { } field)
        {
            return;
        }

        if (!double.TryParse(NullRateText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || rate is < 0 or > 1)
        {
            EditorError = "A taxa de nulos deve ser um número entre 0 e 1 (ex.: 0.05).";
            return;
        }

        _session.SetOptions(field.Row.Column, rate, IsUnique, Note);
        Message = $"Opções de {field.Name} atualizadas.";
    }

    private void ClearBinding()
    {
        if (SelectedField is { } field)
        {
            _session.ClearBinding(field.Row.Column);
            Message = $"Vínculo de {field.Name} removido.";
        }
    }

    private async Task PreviewAsync()
    {
        PreviewValues.Clear();
        PreviewError = string.Empty;
        if (SelectedField is not { } field)
        {
            return;
        }

        var spec = field.Row.Binding?.Generator ?? field.Row.Suggestion.Best?.Target.Spec;
        var column = field.Row.Column;
        var model = _session.Context?.Schema.Namespaces.FirstOrDefault(n => n.Name == column.Table.Namespace)?
            .Tables.FirstOrDefault(t => t.Name == column.Table.Name)?.Columns.FirstOrDefault(c => c.Name == column.Column);
        if (spec is null || model is null)
        {
            return;
        }

        var seed = _workspace.Current?.Profiles.FirstOrDefault(p => p.Name == _workspace.Current.ActiveProfile)?.Seed ?? 1;
        var result = await _preview.PreviewAsync(spec, column.Table, model, seed + (ulong)_previewRound++, 8, CancellationToken.None);
        if (SelectedField?.Row.Column != column)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            PreviewError = result.Error.Message;
            return;
        }

        foreach (var value in result.Value)
        {
            PreviewValues.Add(value);
        }
    }

    private void Validate()
    {
        var all = _session.Tables();
        var pending = all.Sum(t => t.Pending);
        Message = pending == 0
            ? "Todos os campos estão mapeados."
            : $"{pending} campo(s) sem vínculo confirmado em {all.Count(t => !t.IsComplete)} tabela(s). Use Mapear automaticamente ou revise as pendências.";
    }

    private async Task SaveAsync()
    {
        if (_workspace.Path is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salvar projeto como",
                Filter = "Projetos Mock Data Studio (*.mdsproj)|*.mdsproj",
                DefaultExt = ".mdsproj",
                FileName = _workspace.Current?.Name ?? "projeto",
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var saved = await _workspace.SaveAsAsync(dialog.FileName, CancellationToken.None);
            Message = saved.IsSuccess ? "Projeto salvo." : saved.Error.Message;
            return;
        }

        var result = await _workspace.SaveAsync(CancellationToken.None);
        Message = result.IsSuccess ? "Projeto salvo." : result.Error.Message;
    }

    private void Undo()
    {
        if (_workspace.Undo())
        {
            Message = "Alteração desfeita.";
        }
    }

    private void Redo()
    {
        if (_workspace.Redo())
        {
            Message = "Alteração refeita.";
        }
    }

    private void RefreshHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        UndoLabel = _workspace.UndoDescription is { } text ? $"Desfazer: {text}" : "Desfazer";
    }
}
