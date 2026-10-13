using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MockDataStudio.App.Samples;
using MockDataStudio.Application;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Diff;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Schema;
using MockDataStudio.Projects;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.App.ViewModels;

public sealed record SnapshotOption(string Id, string Label)
{
    public override string ToString() => $"{Id} · {Label}";
}

public sealed record RelationshipItem(string From, string To, string Rule);

/// <summary>Schema Digest (S02): captura com progresso e cancelamento, importação, exportação, árvore e comparação.</summary>
public sealed partial class SchemaDigestViewModel : ObservableObject, IDisposable
{
    private const int JsonPreviewLimit = 200_000;

    private readonly ProjectWorkspace _workspace;
    private readonly IConnectionService _connections;
    private readonly IDigestService _digest;
    private readonly IDigestSerializer _serializer;
    private CancellationTokenSource? _cts;
    private SchemaModel? _schema;

    public SchemaDigestViewModel(
        ProjectWorkspace workspace, IConnectionService connections, IDigestService digest, IDigestSerializer serializer)
    {
        _workspace = workspace;
        _connections = connections;
        _digest = digest;
        _serializer = serializer;

        CaptureCommand = new AsyncRelayCommand(CaptureAsync, () => !IsBusy && SelectedProfile is not null);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsBusy && SelectedSnapshot is not null);
        CompareCommand = new RelayCommand(Compare, () => CompareFrom is not null && CompareTo is not null);
        ValidateCommand = new RelayCommand(Validate, () => SelectedSnapshot is not null);
        RefreshProfilesCommand = new AsyncRelayCommand(LoadProfilesAsync);

        _workspace.Changed += (_, _) => ReloadSnapshots();
        ReloadSnapshots();
        _ = LoadProfilesAsync();
    }

    public IAsyncRelayCommand CaptureCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public IRelayCommand CompareCommand { get; }
    public IRelayCommand ValidateCommand { get; }
    public IAsyncRelayCommand RefreshProfilesCommand { get; }

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];
    public ObservableCollection<SnapshotOption> Snapshots { get; } = [];
    public ObservableCollection<StatTile> DigestStats { get; } = [];
    public ObservableCollection<KeyValue> DigestInfo { get; } = [];
    public ObservableCollection<TreeNode> DigestTree { get; } = [];
    public ObservableCollection<RelationshipItem> Relationships { get; } = [];
    public ObservableCollection<ChangeItem> DigestChanges { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private SnapshotOption? _selectedSnapshot;

    [ObservableProperty]
    private SnapshotOption? _compareFrom;

    [ObservableProperty]
    private SnapshotOption? _compareTo;

    [ObservableProperty]
    private string _digestJson = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Selecione uma conexão e extraia o digest, ou importe um arquivo.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _progressPhase = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _projectName = "Nenhum projeto";

    [ObservableProperty]
    private int _addedCount;

    [ObservableProperty]
    private int _removedCount;

    [ObservableProperty]
    private int _modifiedCount;

    [ObservableProperty]
    private string _changesSummary = "Selecione duas versões e compare.";

    public bool HasSnapshot => _schema is not null;

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    partial void OnIsBusyChanged(bool value)
    {
        CaptureCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value) => CaptureCommand.NotifyCanExecuteChanged();

    partial void OnCompareFromChanged(SnapshotOption? value) => CompareCommand.NotifyCanExecuteChanged();

    partial void OnCompareToChanged(SnapshotOption? value) => CompareCommand.NotifyCanExecuteChanged();

    partial void OnSearchTextChanged(string value) => RebuildTree();

    partial void OnSelectedSnapshotChanged(SnapshotOption? value)
    {
        ExportCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        if (value is not null && _workspace.Current is { } project && project.ActiveSnapshotId != value.Id)
        {
            _workspace.Update("Trocar snapshot ativo", p => p with { ActiveSnapshotId = value.Id });
        }

        ShowSnapshot();
    }

    private async Task LoadProfilesAsync()
    {
        var selected = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in await _connections.ListAsync())
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selected) ?? Profiles.FirstOrDefault();
    }

    private string _snapshotSignature = string.Empty;

    private void ReloadSnapshots()
    {
        var project = _workspace.Current;
        ProjectName = project?.Name ?? "Nenhum projeto";

        // Edições de vínculos também disparam Changed: só reconstrói quando a lista de snapshots ou o ativo mudou.
        var signature = string.Join("|", (project?.Snapshots ?? []).Select(s => s.Id + s.StructureHash)) + "#" + project?.ActiveSnapshotId;
        if (signature == _snapshotSignature)
        {
            return;
        }

        _snapshotSignature = signature;
        var previousFrom = CompareFrom?.Id;
        var previousTo = CompareTo?.Id;
        Snapshots.Clear();
        foreach (var snapshot in project?.Snapshots ?? [])
        {
            Snapshots.Add(new SnapshotOption(snapshot.Id, snapshot.Label));
        }

        SelectedSnapshot = Snapshots.FirstOrDefault(s => s.Id == project?.ActiveSnapshotId);
        ShowSnapshot();

        CompareFrom = Snapshots.FirstOrDefault(s => s.Id == previousFrom) ?? (Snapshots.Count > 1 ? Snapshots[^2] : null);
        CompareTo = Snapshots.FirstOrDefault(s => s.Id == previousTo) ?? (Snapshots.Count > 0 ? Snapshots[^1] : null);
    }

    private SnapshotEntry? Entry(SnapshotOption? option) =>
        option is null ? null : _workspace.Current?.Snapshots.FirstOrDefault(s => s.Id == option.Id);

    private void ShowSnapshot()
    {
        var entry = Entry(SelectedSnapshot);
        DigestStats.Clear();
        DigestInfo.Clear();
        Relationships.Clear();
        _schema = null;

        if (entry is null)
        {
            DigestJson = string.Empty;
            DigestTree.Clear();
            OnPropertyChanged(nameof(HasSnapshot));
            return;
        }

        var model = _serializer.Deserialize(entry.DigestJson);
        if (!model.IsSuccess)
        {
            StatusMessage = "Digest inválido: " + model.Error.Message;
            DigestJson = entry.DigestJson;
            DigestTree.Clear();
            OnPropertyChanged(nameof(HasSnapshot));
            return;
        }

        _schema = model.Value;
        var tables = _schema.Namespaces.SelectMany(n => n.Tables).ToList();
        DigestStats.Add(Tile(tables.Count, "Tabelas", "", "AccentBrush", "AccentSoftBrush"));
        DigestStats.Add(Tile(tables.Sum(t => t.Columns.Count), "Colunas", "", "PurpleBrush", "PurpleSoftBrush"));
        DigestStats.Add(Tile(tables.Sum(t => t.ForeignKeys.Count), "Chaves estrangeiras", "", "TealBrush", "TealSoftBrush"));
        DigestStats.Add(Tile(tables.Sum(t => t.Indexes.Count), "Índices", "", "OrangeBrush", "OrangeSoftBrush"));
        DigestStats.Add(Tile(_schema.Namespaces.Sum(n => n.Views.Count), "Views", "", "SuccessBrush", "SuccessSoftBrush"));
        DigestStats.Add(Tile(_schema.Namespaces.Sum(n => n.Sequences.Count), "Sequences", "", "WarningBrush", "WarningSoftBrush"));

        DigestInfo.Add(new KeyValue("Snapshot", entry.Id));
        DigestInfo.Add(new KeyValue("Banco", _schema.DatabaseName));
        DigestInfo.Add(new KeyValue("SGBD", _schema.Engine.ToString()));
        DigestInfo.Add(new KeyValue("Versão do servidor", _schema.ServerVersion));
        DigestInfo.Add(new KeyValue("Capturado em", entry.CapturedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture)));
        DigestInfo.Add(new KeyValue("Hash da estrutura", entry.StructureHash.Length > 24 ? entry.StructureHash[..24] + "…" : entry.StructureHash));

        foreach (var table in _schema.Namespaces.SelectMany(n => n.Tables.Select(t => (Namespace: n.Name, Table: t))))
        {
            foreach (var fk in table.Table.ForeignKeys)
            {
                Relationships.Add(new RelationshipItem(
                    $"{table.Table.Name}({string.Join(", ", fk.Columns)})",
                    $"{fk.Referenced.Name}({string.Join(", ", fk.ReferencedColumns)})",
                    $"ON DELETE {fk.OnDelete}"));
            }
        }

        var written = _serializer.Serialize(_schema, new DigestWriteOptions(Indented: true));
        DigestJson = written.Length > JsonPreviewLimit
            ? written[..JsonPreviewLimit] + $"\n… (prévia truncada em {JsonPreviewLimit:N0} caracteres; exporte para ver o arquivo completo)"
            : written;

        RebuildTree();
        OnPropertyChanged(nameof(HasSnapshot));
    }

    private static StatTile Tile(int value, string label, string glyph, string fg, string bg) =>
        new(value.ToString("N0", CultureInfo.CurrentCulture), label, glyph, Theme.Brush(fg), Theme.Brush(bg));

    private void RebuildTree()
    {
        DigestTree.Clear();
        if (_schema is null)
        {
            return;
        }

        var filter = SearchText.Trim();
        foreach (var ns in _schema.Namespaces)
        {
            var tableNodes = new List<TreeNode>();
            foreach (var table in ns.Tables)
            {
                var columns = table.Columns
                    .Where(c => filter.Length == 0 || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || table.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(c => new TreeNode($"{c.Name}  ·  {c.Type.NativeName}{(c.IsNullable ? string.Empty : " NOT NULL")}", "", []))
                    .ToList();
                if (filter.Length > 0 && columns.Count == 0)
                {
                    continue;
                }

                tableNodes.Add(new TreeNode(table.Name, "", columns, Expanded: filter.Length > 0));
            }

            if (tableNodes.Count > 0 || filter.Length == 0)
            {
                DigestTree.Add(new TreeNode($"{ns.Name} ({tableNodes.Count})", "", tableNodes, Expanded: true));
            }
        }
    }

    private async Task CaptureAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressPhase = "Conectando…";
        ProgressPercent = 0;
        StatusMessage = $"Capturando a estrutura de \"{profile.Name}\"…";
        try
        {
            var progress = new Progress<ExtractionProgress>(p =>
            {
                ProgressPhase = p.Phase;
                ProgressPercent = p.Total > 0 ? 100.0 * p.Done / p.Total : 0;
            });

            var result = await _digest.CaptureAsync(profile.Id, new CaptureOptions(), progress, _cts.Token);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            EnsureProject();
            var source = new ConnectionRef(profile.Id, profile.Name, profile.Engine, result.Value.SourceFingerprintHash);
            var attached = _workspace.AttachSnapshot(result.Value, source);
            var attention = MigrateBindings(result.Value.DigestJson);
            StatusMessage = attention == 0
                ? $"Estrutura capturada ({attached.Id}). Siga para Mapping."
                : $"Estrutura capturada ({attached.Id}). {attention} vínculo(s) precisam de revisão ou ficaram órfãos.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Captura cancelada.";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            ProgressPhase = string.Empty;
        }
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar digest",
            Filter = "Digest JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _digest.ImportAsync(dialog.FileName, CancellationToken.None);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            EnsureProject();
            var attached = _workspace.AttachSnapshot(result.Value, null);
            StatusMessage = $"Digest importado ({attached.Id}).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync()
    {
        var entry = Entry(SelectedSnapshot);
        if (entry is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar digest",
            Filter = "Digest JSON (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"digest-{_schema?.DatabaseName ?? "banco"}.json",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await _digest.ExportAsync(entry, dialog.FileName, indented: true, CancellationToken.None);
        StatusMessage = result.IsSuccess ? $"Digest exportado para {dialog.FileName}." : result.Error.Message;
    }

    private void Validate()
    {
        var entry = Entry(SelectedSnapshot);
        if (entry is null)
        {
            return;
        }

        var result = _serializer.Deserialize(entry.DigestJson);
        StatusMessage = result.IsSuccess
            ? $"O digest {entry.Id} é válido."
            : $"O digest {entry.Id} é inválido: {result.Error.Message}";
    }

    private void Compare()
    {
        DigestChanges.Clear();
        var from = Entry(CompareFrom);
        var to = Entry(CompareTo);
        if (from is null || to is null)
        {
            return;
        }

        var diff = _digest.Compare(from, to);
        if (!diff.IsSuccess)
        {
            ChangesSummary = diff.Error.Message;
            return;
        }

        AddedCount = diff.Value.Changes.Count(c => c.Kind == ChangeKind.Added);
        RemovedCount = diff.Value.Changes.Count(c => c.Kind == ChangeKind.Removed);
        ModifiedCount = diff.Value.Changes.Count(c => c.Kind is ChangeKind.Modified or ChangeKind.RenamedCandidate);
        ChangesSummary = diff.Value.IsEmpty
            ? "As duas versões têm a mesma estrutura."
            : $"{diff.Value.Changes.Count} alteração(ões) entre {from.Id} e {to.Id}.";

        foreach (var change in diff.Value.Changes)
        {
            var (label, brush) = change.Kind switch
            {
                ChangeKind.Added => ("Adicionado", "SuccessBrush"),
                ChangeKind.Removed => ("Removido", "DangerBrush"),
                ChangeKind.RenamedCandidate => ("Renomeação?", "WarningBrush"),
                _ => ("Modificado", "PurpleBrush"),
            };
            var detail = change.Note ?? (change.Before is null && change.After is null
                ? string.Empty
                : $"{change.Before ?? "∅"} → {change.After ?? "∅"}");
            DigestChanges.Add(new ChangeItem(change.Severity.ToString(), change.Path, label, Theme.Brush(brush), detail));
        }
    }

    // Migra os vínculos do projeto para o schema recém-capturado (C7a.2). Devolve quantos precisam de atenção.
    // O hasher é sem estado: criá-lo aqui evita mexer no registro de DI só para isso.
    private int MigrateBindings(string digestJson)
    {
        var project = _workspace.Current;
        if (project is null || project.Bindings.Count == 0)
        {
            return 0;
        }

        var schema = _serializer.Deserialize(digestJson);
        if (!schema.IsSuccess)
        {
            return 0;
        }

        var migration = BindingMigrator.Migrate(project.Bindings, schema.Value, new StructureHasher());
        _workspace.Update("Migrar vínculos para o novo snapshot", p => p with { Bindings = migration.Bindings });
        return migration.Entries.Count(e => e.Outcome is not MigrationOutcome.Kept);
    }

    // A captura e a importação precisam de um projeto: cria um quando ainda não há nenhum aberto.
    private void EnsureProject()
    {
        if (_workspace.Current is null)
        {
            _workspace.CreateNew("Novo projeto");
        }
    }
}
