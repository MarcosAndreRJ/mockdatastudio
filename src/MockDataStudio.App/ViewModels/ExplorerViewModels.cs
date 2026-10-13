using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.Application;
using MockDataStudio.Application.Explorer;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.App.ViewModels;

// Explorer sobre a conexão ativa (ActiveConnectionSession), escolhida aqui (perfil + database) ou em Connections
// ("Open in Explorer"). Os comandos SQL rodam nas abas do editor (SqlEditorWorkspaceViewModel), cada uma com a sua conexão.

// Linhas das abas do inspetor (formato de exibição; os valores vêm de TableModel/TriggerModel reais).
public sealed record ExplorerColumnRow(string Name, string Type, string Key, string Nullable, string Default);

public sealed record ExplorerTriggerRow(string Name, string Timing, string Events, string Active);

public enum ExplorerNodeKind { Schema, Group, Table, View, Trigger, Routine, Sequence }

public sealed class ExplorerNode(string text, ExplorerNodeKind kind, ObjectName? table = null, string? detail = null) : ObservableObject
{
    private bool _isExpanded;

    public string Text { get; } = text;
    public ExplorerNodeKind Kind { get; } = kind;

    // Tabela ou view do nó; nulo nos demais tipos (os comandos do menu de contexto só valem com ela).
    public ObjectName? Table { get; } = table;

    public string? Detail { get; } = detail;
    public bool HasTable => Table is not null;
    public ObservableCollection<ExplorerNode> Children { get; } = [];

    // Segoe MDL2 Assets.
    public string Glyph => Kind switch
    {
        ExplorerNodeKind.Schema => "",
        ExplorerNodeKind.Group => "",
        ExplorerNodeKind.Table => "",
        ExplorerNodeKind.View => "",
        ExplorerNodeKind.Trigger => "",
        ExplorerNodeKind.Routine => "",
        _ => "",
    };

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public sealed partial class ExplorerViewModel : ObservableObject
{
    public const int PreviewTab = 6;

    private readonly ActiveConnectionSession _session;
    private readonly IConnectionService _connections;
    private readonly List<SchemaObjects> _catalog = [];
    private DatabaseNavigator? _navigator;
    private ObjectName? _selectedTable;
    private TableModel? _selectedModel;
    private bool _previewLoaded;
    private bool _suppressSelection;
    private int _loadVersion;

    public ExplorerViewModel(ActiveConnectionSession session, IConnectionService connections, SqlEditorWorkspaceViewModel workspace)
    {
        _session = session;
        _connections = connections;
        Workspace = workspace;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ReferencesCommand = new AsyncRelayCommand(LoadReferencesAsync);
        PreviewCommand = new AsyncRelayCommand(LoadPreviewAsync);
        NewQueryCommand = new AsyncRelayCommand(NewQueryAsync);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        SelectTopCommand = new AsyncRelayCommand<ExplorerNode?>(node => OpenSelectAsync(node, run: true));
        OpenInEditorCommand = new AsyncRelayCommand<ExplorerNode?>(node => OpenSelectAsync(node, run: false));
        CopyNameCommand = new RelayCommand<ExplorerNode?>(CopyQualifiedName);
        CreateTableCommand = new AsyncRelayCommand<ExplorerNode?>(node => OpenScriptAsync(node, insert: false));
        InsertExampleCommand = new AsyncRelayCommand<ExplorerNode?>(node => OpenScriptAsync(node, insert: true));
        ReferencesForNodeCommand = new AsyncRelayCommand<ExplorerNode?>(ShowReferencesForNodeAsync);
        OpenReferencedTableCommand = new AsyncRelayCommand<ForeignKeyInfo?>(OpenReferencedTableAsync);
        _session.Changed += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    public SqlEditorWorkspaceViewModel Workspace { get; }

    public ObservableCollection<ProfileOption> Profiles => Workspace.Profiles;
    public ObservableCollection<string> Databases { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ReferencesCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand NewQueryCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }

    // Menu de contexto da árvore: o parâmetro é o nó clicado.
    public AsyncRelayCommand<ExplorerNode?> SelectTopCommand { get; }
    public AsyncRelayCommand<ExplorerNode?> OpenInEditorCommand { get; }
    public RelayCommand<ExplorerNode?> CopyNameCommand { get; }
    public AsyncRelayCommand<ExplorerNode?> CreateTableCommand { get; }
    public AsyncRelayCommand<ExplorerNode?> InsertExampleCommand { get; }
    public AsyncRelayCommand<ExplorerNode?> ReferencesForNodeCommand { get; }

    // Navega para a tabela referenciada por uma FK, selecionando o nó correspondente na árvore.
    public AsyncRelayCommand<ForeignKeyInfo?> OpenReferencedTableCommand { get; }

    public ObservableCollection<ExplorerNode> Nodes { get; } = [];
    public ObservableCollection<ExplorerColumnRow> SelectedColumns { get; } = [];
    public ObservableCollection<IncomingReference> IncomingReferences { get; } = [];

    // Abas do inspetor (dados do TableModel real via ObjectInspector).
    public ObservableCollection<IndexInfo> Indexes { get; } = [];
    public ObservableCollection<ForeignKeyInfo> ForeignKeys { get; } = [];
    public ObservableCollection<CheckInfo> Checks { get; } = [];
    public ObservableCollection<ExplorerTriggerRow> Triggers { get; } = [];

    public bool HasDatabaseChoice => Databases.Count > 0;

    [ObservableProperty]
    private ProfileOption? _selectedProfile;

    [ObservableProperty]
    private string? _selectedDatabase;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _connectionTitle = "Sem conexão";

    [ObservableProperty]
    private string _connectionSubtitle = "Escolha uma conexão acima ou use Open in Explorer em Connections.";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _selectedTableName = "Nenhuma tabela selecionada";

    [ObservableProperty]
    private string _selectedTableQualified = string.Empty;

    [ObservableProperty]
    private string _referencesStatus = string.Empty;

    [ObservableProperty]
    private DataView? _previewRows;

    [ObservableProperty]
    private string _previewStatus = string.Empty;

    [ObservableProperty]
    private int _inspectorTabIndex;

    // Busca na árvore. Filtra o catálogo já carregado; não consulta o banco de novo.
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildTree();

    partial void OnInspectorTabIndexChanged(int value)
    {
        if (value == PreviewTab && !_previewLoaded && _selectedTable is not null)
        {
            _ = LoadPreviewAsync();
        }
    }

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        if (!_suppressSelection && value is not null)
        {
            _ = OpenSessionAsync(value, null);
        }
    }

    partial void OnSelectedDatabaseChanged(string? value)
    {
        if (!_suppressSelection && SelectedProfile is { } profile && !string.IsNullOrWhiteSpace(value)
            && (_session.Current is not { } opened || !string.Equals(SqlEditorTabViewModel.DatabaseOf(opened), value, StringComparison.Ordinal)))
        {
            _ = OpenSessionAsync(profile, value);
        }
    }

    // Abre o perfil (e o database) escolhido no Explorer e o torna a conexão ativa; Changed recarrega a árvore.
    private async Task OpenSessionAsync(ProfileOption profile, string? database)
    {
        IsBusy = true;
        StatusText = $"Conectando a {profile.Name}...";
        try
        {
            var result = await _connections.OpenAsync(profile.Id, ConnectionPurpose.Explore, database);
            if (!result.IsSuccess)
            {
                StatusText = result.Error!.Message;
                return;
            }

            // O Gate garante que nenhuma consulta do Explorer está usando a conexão anterior quando ela é descartada.
            await _session.Gate.WaitAsync();
            try
            {
                _session.Set(result.Value);
            }
            finally
            {
                _session.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao conectar: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        _navigator?.Refresh();
        await Workspace.RefreshProfilesAsync();
        await LoadAsync();
    }

    private void ClearInspector()
    {
        SelectedColumns.Clear();
        IncomingReferences.Clear();
        Indexes.Clear();
        ForeignKeys.Clear();
        Checks.Clear();
        Triggers.Clear();
        PreviewRows = null;
        PreviewStatus = string.Empty;
        ReferencesStatus = string.Empty;
        _previewLoaded = false;
        _selectedTable = null;
        _selectedModel = null;
        SelectedTableName = "Nenhuma tabela selecionada";
        SelectedTableQualified = string.Empty;
    }

    private void ClearSelection() => ClearInspector();

    private void SetSelectors(ProfileOption? profile, IReadOnlyList<string> databases, string? current)
    {
        _suppressSelection = true;
        try
        {
            SelectedProfile = profile;
            Databases.Clear();
            foreach (var name in databases)
            {
                Databases.Add(name);
            }

            if (!string.IsNullOrWhiteSpace(current) && Databases.Count > 0 && !Databases.Contains(current))
            {
                Databases.Insert(0, current);
            }

            SelectedDatabase = string.IsNullOrWhiteSpace(current) ? null : current;
        }
        finally
        {
            _suppressSelection = false;
        }

        OnPropertyChanged(nameof(HasDatabaseChoice));
    }

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        Nodes.Clear();
        _catalog.Clear();
        ClearInspector();

        await Workspace.RefreshProfilesAsync();
        var opened = _session.Current;
        if (opened is null)
        {
            _navigator = null;
            SetSelectors(SelectedProfile, [], null);
            ConnectionTitle = "Sem conexão";
            ConnectionSubtitle = "Escolha uma conexão acima ou use Open in Explorer em Connections.";
            StatusText = string.Empty;
            return;
        }

        var database = SqlEditorTabViewModel.DatabaseOf(opened);
        ConnectionTitle = $"{opened.Profile.Name} ({opened.Server.Product} {opened.Server.Version})";
        ConnectionSubtitle = (string.IsNullOrEmpty(database) ? "(sem database)" : database)
            + " • " + (opened.Profile.Settings.Host ?? "arquivo local")
            + (opened.Profile.Settings.Port is { } p ? ":" + p.ToString(CultureInfo.InvariantCulture) : string.Empty);
        StatusText = "Carregando objetos...";
        IsBusy = true;
        _navigator = new DatabaseNavigator(opened.Provider);

        try
        {
            IReadOnlyList<string> databases = [];
            IReadOnlyList<SchemaObjects> catalog;
            await _session.Gate.WaitAsync();
            try
            {
                if (!ReferenceEquals(opened, _session.Current))
                {
                    return; // trocada enquanto esperava; o Changed da nova conexão recarrega
                }

                try
                {
                    databases = await opened.Provider.ListDatabasesAsync(opened.Connection, CancellationToken.None);
                }
                catch (DbException)
                {
                    // Sem permissão para listar databases: fica só o atual.
                }

                catalog = await _navigator.LoadCatalogAsync(opened.Connection, CancellationToken.None);
            }
            finally
            {
                _session.Gate.Release();
            }

            if (version != _loadVersion)
            {
                return;
            }

            SetSelectors(await Workspace.FindProfileAsync(opened.Profile.Id), databases, database);
            _catalog.AddRange(catalog);
            RebuildTree();
            if (_catalog.Count == 0)
            {
                StatusText = Databases.Count > 0 && string.IsNullOrEmpty(database)
                    ? "Escolha um database na lista acima."
                    : "Nenhum objeto encontrado.";
            }
        }
        catch (NotImplementedException)
        {
            StatusText = $"A navegação de objetos para {opened.Provider.DisplayName} ainda não foi implementada. " +
                         "O SQL Editor funciona nesta conexão.";
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao listar objetos: " + ex.Message;
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private static string Describe(TriggerModel trigger) =>
        $"{trigger.Timing.ToString().ToUpperInvariant()} {trigger.Events.ToString().ToUpperInvariant().Replace(",", " OR", StringComparison.Ordinal)}"
        + (trigger.Table is { } t ? " ON " + t.Name : string.Empty)
        + (trigger.IsActive ? string.Empty : " (inativo)");

    private static string Describe(RoutineModel routine) =>
        $"{routine.Kind}, {routine.ParameterCount} parâmetro(s)" + (routine.ReturnType is { } r ? " → " + r : string.Empty);

    private static string Describe(SequenceModel sequence) =>
        "Sequence" + (sequence.Start is { } s ? ", início " + s.ToString(CultureInfo.CurrentCulture) : string.Empty)
        + (sequence.Increment is { } i ? ", incremento " + i.ToString(CultureInfo.CurrentCulture) : string.Empty);

    // Monta a árvore a partir do catálogo, aplicando a busca. Um schema que casa com a busca mostra todos os seus objetos.
    private void RebuildTree()
    {
        Nodes.Clear();
        var single = _catalog.Count == 1;
        foreach (var entry in _catalog)
        {
            var all = ExplorerFilter.Matches(entry.Schema, SearchText);
            var tables = all ? entry.Tables.ToList() : ExplorerFilter.Filter(entry.Tables, SearchText);
            var views = all ? entry.Views.ToList() : ExplorerFilter.Filter(entry.Views, SearchText);
            var triggers = entry.Triggers.Where(t => all || ExplorerFilter.Matches(t.Name, SearchText)).ToList();
            var routines = entry.Routines.Where(r => all || ExplorerFilter.Matches(r.Name, SearchText)).ToList();
            var sequences = entry.Sequences.Where(s => all || ExplorerFilter.Matches(s.Name, SearchText)).ToList();
            if (!all && tables.Count + views.Count + triggers.Count + routines.Count + sequences.Count == 0)
            {
                continue;
            }

            var filtering = !string.IsNullOrWhiteSpace(SearchText);
            var schemaNode = new ExplorerNode(entry.Schema, ExplorerNodeKind.Schema) { IsExpanded = single || filtering };
            AddGroup(schemaNode, "Tables", tables, t => new ExplorerNode(t.Name, ExplorerNodeKind.Table, t), expand: true, always: true);
            AddGroup(schemaNode, "Views", views, v => new ExplorerNode(v.Name, ExplorerNodeKind.View, v), expand: filtering, always: true);
            AddGroup(schemaNode, "Triggers", triggers, t => new ExplorerNode(t.Name, ExplorerNodeKind.Trigger, detail: Describe(t)), filtering);
            AddGroup(schemaNode, "Routines", routines, r => new ExplorerNode(r.Name, ExplorerNodeKind.Routine, detail: Describe(r)), filtering);
            AddGroup(schemaNode, "Sequences", sequences, s => new ExplorerNode(s.Name, ExplorerNodeKind.Sequence, detail: Describe(s)), filtering);
            Nodes.Add(schemaNode);
        }

        if (_catalog.Count > 0 && Nodes.Count == 0 && !string.IsNullOrWhiteSpace(SearchText))
        {
            StatusText = $"Nenhum objeto corresponde a '{SearchText.Trim()}'.";
        }
        else if (Nodes.Count > 0)
        {
            var total = _catalog.Sum(c => c.Tables.Count);
            StatusText = $"{_catalog.Count} schema(s), {total} tabela(s), {_catalog.Sum(c => c.Views.Count)} view(s).";
        }
    }

    private static void AddGroup<T>(ExplorerNode parent, string name, IReadOnlyList<T> items, Func<T, ExplorerNode> create,
        bool expand, bool always = false)
    {
        if (items.Count == 0 && !always)
        {
            return;
        }

        var group = new ExplorerNode($"{name} ({items.Count})", ExplorerNodeKind.Group) { IsExpanded = expand };
        foreach (var item in items)
        {
            group.Children.Add(create(item));
        }

        parent.Children.Add(group);
    }

    private async Task OpenReferencedTableAsync(ForeignKeyInfo? fk)
    {
        if (fk is null)
        {
            return;
        }

        var target = Nodes.SelectMany(s => s.Children).SelectMany(g => g.Children)
            .FirstOrDefault(n => n.Table is { } t && t == fk.Referenced);
        if (target is null)
        {
            StatusText = $"A tabela referenciada '{fk.Referenced.CanonicalId}' não está na árvore (filtro ativo ou fora do schema).";
            return;
        }

        await SelectNodeAsync(target);
    }

    public async Task SelectNodeAsync(ExplorerNode? node)
    {
        var opened = _session.Current;
        if (node is null || opened is null || _navigator is null)
        {
            return;
        }

        if (node.Table is not { } table)
        {
            if (node.Detail is { } detail)
            {
                StatusText = $"{node.Text}: {detail}";
            }

            return;
        }

        ClearInspector();
        _selectedTable = table;
        SelectedTableName = table.Name;
        SelectedTableQualified = (node.Kind == ExplorerNodeKind.View ? "View • " : "Tabela • ") + table.Namespace + "." + table.Name;

        try
        {
            await _session.Gate.WaitAsync();
            try
            {
                var described = await _navigator.DescribeTableAsync(opened.Connection, table, CancellationToken.None);
                if (!described.IsSuccess)
                {
                    StatusText = described.Error!.Message;
                    return;
                }

                if (_selectedTable != table)
                {
                    return; // outra seleção chegou enquanto esperava
                }

                _selectedModel = described.Value!;
            }
            finally
            {
                _session.Gate.Release();
            }

            FillInspector(table, _selectedModel);
            if (InspectorTabIndex == PreviewTab)
            {
                await LoadPreviewAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao descrever o objeto: " + ex.Message;
        }
    }

    private void FillInspector(ObjectName table, TableModel model)
    {
        var inspection = ObjectInspector.Inspect(table, model);
        foreach (var c in inspection.Columns)
        {
            var key = c.IsPrimaryKey ? "PK" : c.IsForeignKey ? "FK" : c.IsUnique ? "UQ" : string.Empty;
            var column = model.Columns.FirstOrDefault(m => m.Name == c.Name);
            SelectedColumns.Add(new ExplorerColumnRow(c.Name, c.NativeType, key, c.Nullable ? "Yes" : "No", column?.Default?.RawExpression ?? string.Empty));
        }

        foreach (var index in inspection.Indexes)
        {
            Indexes.Add(index);
        }

        foreach (var fk in inspection.ForeignKeys)
        {
            ForeignKeys.Add(fk);
        }

        foreach (var check in inspection.Checks)
        {
            Checks.Add(check);
        }

        foreach (var trigger in _catalog.SelectMany(s => s.Triggers).Where(t => t.Table == table))
        {
            Triggers.Add(new ExplorerTriggerRow(trigger.Name, trigger.Timing.ToString(),
                trigger.Events.ToString().Replace(", ", " | ", StringComparison.Ordinal), trigger.IsActive ? "Yes" : "No"));
        }

        StatusText = string.Empty;
    }

    // Primeiras linhas da tabela selecionada (SELECT com limite do dialeto), na própria conexão do Explorer.
    private async Task LoadPreviewAsync()
    {
        if (_session.Current is not { } opened || _selectedTable is not { } table || _selectedModel is not { } model)
        {
            PreviewStatus = "Selecione uma tabela.";
            return;
        }

        PreviewStatus = "Carregando...";
        try
        {
            var dialect = opened.Provider.GetDialect(opened.Server.Version);
            var sql = TablePreview.BuildSql(table, model, dialect, TablePreview.DefaultRows);
            await _session.Gate.WaitAsync();
            try
            {
                await using var command = opened.Connection.CreateCommand();
                command.CommandText = sql;
                await using var reader = await command.ExecuteReaderAsync();
                var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                var rows = new List<object?[]>();
                while (await reader.ReadAsync())
                {
                    var values = new object?[reader.FieldCount];
                    for (var i = 0; i < values.Length; i++)
                    {
                        try
                        {
                            values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
                        {
                            values[i] = "<" + reader.GetDataTypeName(i) + ">"; // valor fora do tipo .NET (ex.: data zero)
                        }
                    }

                    rows.Add(values);
                }

                if (_selectedTable != table)
                {
                    return;
                }

                PreviewRows = ResultGrid.Build(columns, rows).DefaultView;
                PreviewStatus = $"{rows.Count} linha(s) (limite {TablePreview.DefaultRows}).";
                _previewLoaded = true;
            }
            finally
            {
                _session.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            PreviewStatus = "Falha ao carregar o preview: " + ex.Message;
        }
    }

    // Descreve a tabela do nó pelo cache do navegador (ObjectInspector/TableModel real). Null quando o nó não é tabela.
    private async Task<TableModel?> DescribeNodeAsync(ExplorerNode? node)
    {
        var opened = _session.Current;
        if (node?.Table is not { } table || opened is null || _navigator is null)
        {
            return null;
        }

        await _session.Gate.WaitAsync();
        try
        {
            var described = await _navigator.DescribeTableAsync(opened.Connection, table, CancellationToken.None);
            if (!described.IsSuccess)
            {
                StatusText = described.Error!.Message;
                return null;
            }

            return described.Value;
        }
        finally
        {
            _session.Gate.Release();
        }
    }

    private Task<SqlEditorTabViewModel> OpenGeneratedAsync(OpenedConnection opened, string text, bool run) =>
        Workspace.OpenGeneratedAsync(opened.Profile.Id, SqlEditorTabViewModel.DatabaseOf(opened), text, run);

    // SELECT com limite (TablePreview) numa aba do editor com a mesma conexão do Explorer.
    private async Task OpenSelectAsync(ExplorerNode? node, bool run)
    {
        if (node?.Table is not { } table || await DescribeNodeAsync(node) is not { } model || _session.Current is not { } opened)
        {
            return;
        }

        var dialect = opened.Provider.GetDialect(opened.Server.Version);
        await OpenGeneratedAsync(opened, TablePreview.BuildSql(table, model, dialect, TablePreview.DefaultRows) + ";", run);
    }

    // DDL (CREATE TABLE) ou INSERT de exemplo, gerados a partir do schema capturado (ObjectScripts).
    private async Task OpenScriptAsync(ExplorerNode? node, bool insert)
    {
        if (node?.Table is not { } table || await DescribeNodeAsync(node) is not { } model || _session.Current is not { } opened)
        {
            return;
        }

        var dialect = opened.Provider.GetDialect(opened.Server.Version);
        var text = insert ? ObjectScripts.InsertExample(table, model, dialect) : ObjectScripts.CreateTable(table, model, dialect);
        await OpenGeneratedAsync(opened, text, run: false);
    }

    private async Task NewQueryAsync()
    {
        if (_session.Current is { } opened)
        {
            await Workspace.NewTabAsync(opened.Profile.Id, SqlEditorTabViewModel.DatabaseOf(opened));
        }
        else
        {
            await Workspace.NewTabAsync();
        }
    }

    // Copia o nome já citado pelo dialeto (ex.: "main"."cliente" conforme o SGBD).
    private void CopyQualifiedName(ExplorerNode? node)
    {
        if (node?.Table is not { } table || _session.Current is not { } opened)
        {
            return;
        }

        Clipboard.SetText(opened.Provider.GetDialect(opened.Server.Version).QualifiedName(table));
        StatusText = "Nome qualificado copiado.";
    }

    private async Task ShowReferencesForNodeAsync(ExplorerNode? node)
    {
        if (node?.Table is null)
        {
            return;
        }

        await SelectNodeAsync(node);
        InspectorTabIndex = 5;
        await LoadReferencesAsync();
    }

    // "Quem referencia" a tabela selecionada, por FKs de todas as tabelas do banco (TASK-12.03).
    private async Task LoadReferencesAsync()
    {
        var opened = _session.Current;
        if (opened is null || _selectedTable is not { } table)
        {
            ReferencesStatus = "Selecione uma tabela primeiro.";
            return;
        }

        IncomingReferences.Clear();
        ReferencesStatus = "Procurando referências...";
        try
        {
            await _session.Gate.WaitAsync();
            try
            {
                var found = await new ReferenceFinder(opened.Provider).FindIncomingAsync(opened.Connection, table, CancellationToken.None);
                foreach (var reference in found)
                {
                    IncomingReferences.Add(reference);
                }

                ReferencesStatus = found.Count == 0
                    ? "Nenhuma tabela referencia esta."
                    : $"{found.Count} referência(s) de entrada.";
            }
            finally
            {
                _session.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            ReferencesStatus = "Falha ao procurar referências: " + ex.Message;
        }
    }
}
