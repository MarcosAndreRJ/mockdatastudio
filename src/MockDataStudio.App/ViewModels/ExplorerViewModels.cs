using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MockDataStudio.Application;
using MockDataStudio.Application.Explorer;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Naming;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Splitter;

namespace MockDataStudio.App.ViewModels;

// Explorer e SQL Editor sobre a conexão ativa (ActiveConnectionSession). Sem conexão, as telas convidam a abrir uma
// em Connections; nada vem de dados de exemplo.

// Linha da inspeção de colunas (formato de exibição; os valores vêm de TableModel real).
public sealed record ExplorerColumnRow(string Name, string Type, string Key, string Nullable);

public sealed class ExplorerNode(string text, string glyph, ObjectName? table = null) : ObservableObject
{
    private bool _isExpanded;

    public string Text { get; } = text;
    public string Glyph { get; } = glyph;
    public ObjectName? Table { get; } = table;
    public ObservableCollection<ExplorerNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public sealed partial class SqlResultsViewModel : ObservableObject
{
    [ObservableProperty]
    private DataView? _rows;

    [ObservableProperty]
    private string _statusText = "Nenhuma consulta executada.";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _messagesText = string.Empty;

    public SqlResultsViewModel()
    {
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => Rows is not null);
    }

    // Exporta as linhas exibidas na grade (primeira página). Exportação completa por cursor é pendência da C6.
    public AsyncRelayCommand ExportCommand { get; }

    public void Clear(string status, bool isError = false)
    {
        Rows = null;
        StatusText = status;
        IsError = isError;
        MessagesText = string.Empty;
    }

    // Mostra o plano de execução (EXPLAIN) na mesma grade, sem tocar nos cursores de resultado.
    public void ShowPlan(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var table = new DataTable();
        foreach (var column in plan.Columns)
        {
            table.Columns.Add(string.IsNullOrWhiteSpace(column) ? "?column?" : column, typeof(string));
        }

        foreach (var row in plan.Rows)
        {
            table.Rows.Add(row.Select(cell => cell is null or DBNull ? null : Convert.ToString(cell, CultureInfo.CurrentCulture)).ToArray());
        }

        IsError = false;
        MessagesText = string.Empty;
        Rows = table.Columns.Count > 0 ? table.DefaultView : null;
        StatusText = $"Plano de execução: {plan.Rows.Count} linha(s).";
        ExportCommand.NotifyCanExecuteChanged();
    }

    private async Task ExportAsync()
    {
        if (Rows?.Table is not { } table)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            DefaultExt = ".csv",
            FileName = "resultado.csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var format = Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Json
            : ExportFormat.Csv;
        var columns = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var rows = table.Rows.Cast<DataRow>().Select(r => r.ItemArray.Select(v => v is DBNull ? null : v).ToArray()).ToList();

        await using var stream = File.Create(dialog.FileName);
        var written = await ResultExportService.ExportAsync(stream, format, columns, rows, CancellationToken.None);
        StatusText = $"{written} linha(s) exportada(s) para {dialog.FileName}.";
    }

    // Copia a primeira página do último comando com resultado para uma tabela em memória e libera os cursores.
    public async Task ShowAsync(QueryRun run)
    {
        var messages = run.Messages.Select(m => m.Text).ToList();
        StatementOutcome? shown = null;
        foreach (var s in run.Statements)
        {
            messages.AddRange(s.Messages.Select(m => m.Text));
            if (s.Error is { } error)
            {
                messages.Add(error.Message);
            }

            if (s.Columns.Count > 0 || shown is null)
            {
                shown = s;
            }
        }

        var table = new DataTable();
        if (shown is not null)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in shown.Columns)
            {
                var name = string.IsNullOrWhiteSpace(column.Name) ? "?column?" : column.Name;
                var unique = name;
                for (var i = 2; !used.Add(unique); i++)
                {
                    unique = name + "_" + i.ToString(CultureInfo.InvariantCulture);
                }

                table.Columns.Add(unique, typeof(string));
            }

            foreach (var row in shown.FirstPage)
            {
                var cells = new object?[table.Columns.Count];
                for (var i = 0; i < cells.Length && i < row.Length; i++)
                {
                    cells[i] = row[i] is null or DBNull ? null : Convert.ToString(row[i], CultureInfo.CurrentCulture);
                }

                table.Rows.Add(cells);
            }
        }

        IsError = shown is { Status: StatementStatus.Failed or StatementStatus.Blocked or StatementStatus.RequiresConfirmation or StatementStatus.Cancelled }
                  || (shown is null && messages.Count > 0);
        Rows = table.Columns.Count > 0 ? table.DefaultView : null;
        MessagesText = string.Join(Environment.NewLine, messages);
        StatusText = shown switch
        {
            null => messages.Count > 0 ? messages[0] : "Nada a executar.",
            { Status: StatementStatus.Failed } => "Falha ao executar o comando.",
            { Status: StatementStatus.Blocked } => "Comando bloqueado pela política de execução.",
            { Status: StatementStatus.RequiresConfirmation } => "Comando exige confirmação.",
            { Status: StatementStatus.Cancelled } => "Execução cancelada.",
            { Columns.Count: > 0 } s => $"{s.FirstPage.Count} linha(s){(s.HasMore ? " (primeira página; há mais linhas)" : string.Empty)} em {s.Duration.TotalMilliseconds:0} ms",
            { RecordsAffected: { } n } s => $"{n} linha(s) afetada(s) em {s.Duration.TotalMilliseconds:0} ms",
            var s => $"Comando executado em {s.Duration.TotalMilliseconds:0} ms",
        };

        ExportCommand.NotifyCanExecuteChanged();

        foreach (var s in run.Statements)
        {
            await s.DisposeAsync();
        }

        await run.DisposeAsync();
    }
}

public sealed partial class SqlEditorViewModel : ObservableObject, IDisposable
{
    private readonly ActiveConnectionSession _session;
    private readonly ISqlScriptSplitter _splitter;
    private CancellationTokenSource? _cts;

    public SqlEditorViewModel(ActiveConnectionSession session, ISqlScriptSplitter splitter, SqlResultsViewModel results)
    {
        _session = session;
        _splitter = splitter;
        Results = results;
        RunCommand = new AsyncRelayCommand(RunAsync, () => HasConnection && !IsRunning);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        ExplainCommand = new AsyncRelayCommand(ExplainAsync, () => HasConnection && !IsRunning);
        FormatCommand = new RelayCommand(() => QueryText = SqlFormatter.Format(QueryText));
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
        OpenScriptCommand = new AsyncRelayCommand(OpenScriptAsync);
        _session.Changed += (_, _) => RefreshConnectionState();
        Results.Clear(HasConnection ? "Nenhuma consulta executada." : "Abra uma conexão em Connections.");
    }

    public SqlResultsViewModel Results { get; }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }
    public AsyncRelayCommand ExplainCommand { get; }
    public RelayCommand FormatCommand { get; }
    public AsyncRelayCommand SaveScriptCommand { get; }
    public AsyncRelayCommand OpenScriptCommand { get; }

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _scriptStatus = string.Empty;

    public bool HasConnection => _session.Current is not null;

    public string Title => _session.Current is { } c ? $"Query - {Database(c)}" : "Query";

    public string DatabaseName => _session.Current is { } c ? $"{Database(c)} ({c.Provider.DisplayName})" : "Sem conexão";

    // Barra de estado: conexão, ambiente e se a somente leitura foi aplicada de fato (null sem conexão).
    public ConnectionStatusInfo? StatusBar => _session.Current is { } c
        ? ConnectionStatus.Build(c.Profile.Name, c.Profile.Engine, Database(c), c.Profile.Environment,
            c.Profile.IsReadOnly, c.IsServerReadOnly)
        : null;

    private static string Database(OpenedConnection c) =>
        string.IsNullOrWhiteSpace(c.Server.DatabaseName) ? c.Profile.Name : c.Server.DatabaseName;

    partial void OnIsRunningChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ExplainCommand.NotifyCanExecuteChanged();
    }

    private void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(HasConnection));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DatabaseName));
        OnPropertyChanged(nameof(StatusBar));
        RunCommand.NotifyCanExecuteChanged();
        ExplainCommand.NotifyCanExecuteChanged();
        Results.Clear(HasConnection ? "Nenhuma consulta executada." : "Abra uma conexão em Connections.");
    }

    // Restrição efetiva de sessão por SGBD para perfis somente leitura. Firebird não oferece: a execução é bloqueada.
    private static string? ReadOnlyRestriction(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.Sqlite => "PRAGMA query_only = ON;",
        DatabaseEngine.PostgreSql => "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;",
        DatabaseEngine.MySql => "SET SESSION TRANSACTION READ ONLY;",
        _ => null,
    };

    private static SqlDialectFeatures DialectOf(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.PostgreSql => SqlDialectFeatures.PostgreSql,
        DatabaseEngine.MySql => SqlDialectFeatures.MySql,
        DatabaseEngine.Firebird => SqlDialectFeatures.Firebird,
        _ => SqlDialectFeatures.Sqlite,
    };

    public async Task RunAsync()
    {
        var opened = _session.Current;
        if (opened is null || string.IsNullOrWhiteSpace(QueryText))
        {
            Results.Clear(opened is null ? "Abra uma conexão em Connections." : "Digite um comando SQL.");
            return;
        }

        var profile = opened.Profile;
        var policy = new ExecutionPolicy(profile.IsReadOnly, profile.Environment, ReadOnlyRestriction(profile.Engine));
        var confirmed = false;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        try
        {
            await _session.Gate.WaitAsync(_cts.Token);
            try
            {
                while (true)
                {
                    var request = new ExecutionRequest(QueryText, DialectOf(profile.Engine), policy, Confirmed: confirmed,
                        CommandTimeoutSec: profile.Settings.CommandTimeoutSec);
                    var run = await new QueryService(_splitter, opened.Provider.ErrorTranslator).ExecuteAsync(opened.Connection, request, _cts.Token);

                    var pending = run.Statements.FirstOrDefault(s => s.Status == StatementStatus.RequiresConfirmation);
                    if (pending is not null && !confirmed)
                    {
                        var reason = string.Join(Environment.NewLine, pending.Messages.Select(m => m.Text));
                        var answer = MessageBox.Show($"{reason}\n\nExecutar mesmo assim?", "Confirmar execução",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (answer == MessageBoxResult.Yes)
                        {
                            await run.DisposeAsync();
                            confirmed = true;
                            continue;
                        }
                    }

                    await Results.ShowAsync(run);
                    break;
                }
            }
            finally
            {
                _session.Gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Results.Clear("Execução cancelada.");
        }
        catch (Exception ex)
        {
            Results.Clear(ex.Message, isError: true);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // EXPLAIN do texto atual (só SELECT/WITH; ver ExplainService). Não executa a consulta.
    public async Task ExplainAsync()
    {
        var opened = _session.Current;
        if (opened is null || string.IsNullOrWhiteSpace(QueryText))
        {
            Results.Clear(opened is null ? "Abra uma conexão em Connections." : "Digite um comando SQL.", isError: opened is not null);
            return;
        }

        try
        {
            await _session.Gate.WaitAsync();
            try
            {
                var plan = await ExplainService.ExplainAsync(opened.Connection, opened.Profile.Engine, QueryText, CancellationToken.None);
                Results.ShowPlan(plan);
            }
            finally
            {
                _session.Gate.Release();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            Results.Clear(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            Results.Clear("Falha ao gerar o plano: " + ex.Message, isError: true);
        }
    }

    private async Task SaveScriptAsync()
    {
        var dialog = new SaveFileDialog { Filter = "Scripts SQL (*.sql)|*.sql|Todos (*.*)|*.*", DefaultExt = ".sql" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ScriptFileService.SaveAsync(dialog.FileName, QueryText, CancellationToken.None);
        ScriptStatus = "Script salvo: " + dialog.FileName;
    }

    private async Task OpenScriptAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Scripts SQL (*.sql)|*.sql|Todos (*.*)|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        QueryText = await ScriptFileService.OpenAsync(dialog.FileName, CancellationToken.None);
        ScriptStatus = "Script aberto: " + dialog.FileName;
    }
}

public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly ActiveConnectionSession _session;
    private readonly List<SchemaCatalog> _catalog = [];
    private DatabaseNavigator? _navigator;
    private ObjectName? _selectedTable;

    public ExplorerViewModel(ActiveConnectionSession session, SqlEditorViewModel sqlEditor, SqlResultsViewModel sqlResults)
    {
        _session = session;
        SqlEditor = sqlEditor;
        SqlResults = sqlResults;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ReferencesCommand = new AsyncRelayCommand(LoadReferencesAsync);
        _session.Changed += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    private sealed record SchemaCatalog(string Schema, IReadOnlyList<ObjectName> Tables, IReadOnlyList<ObjectName> Views);

    public SqlEditorViewModel SqlEditor { get; }
    public SqlResultsViewModel SqlResults { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ReferencesCommand { get; }

    public ObservableCollection<ExplorerNode> Nodes { get; } = [];
    public ObservableCollection<ExplorerColumnRow> SelectedColumns { get; } = [];
    public ObservableCollection<IncomingReference> IncomingReferences { get; } = [];

    [ObservableProperty]
    private string _connectionTitle = "Sem conexão";

    [ObservableProperty]
    private string _connectionSubtitle = "Abra uma conexão em Connections (Open in Explorer).";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _selectedTableName = "Nenhuma tabela selecionada";

    [ObservableProperty]
    private string _selectedTableQualified = string.Empty;

    [ObservableProperty]
    private string _referencesStatus = string.Empty;

    // Busca na árvore. Filtra o catálogo já carregado; não consulta o banco de novo.
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildTree();

    private async Task LoadAsync()
    {
        Nodes.Clear();
        _catalog.Clear();
        SelectedColumns.Clear();
        IncomingReferences.Clear();
        ReferencesStatus = string.Empty;
        _selectedTable = null;
        SelectedTableName = "Nenhuma tabela selecionada";
        SelectedTableQualified = string.Empty;

        var opened = _session.Current;
        if (opened is null)
        {
            _navigator = null;
            ConnectionTitle = "Sem conexão";
            ConnectionSubtitle = "Abra uma conexão em Connections (Open in Explorer).";
            StatusText = string.Empty;
            return;
        }

        ConnectionTitle = opened.Provider.DisplayName;
        ConnectionSubtitle = $"{opened.Server.DatabaseName} • {opened.Profile.Settings.Host ?? "arquivo local"}"
            + (opened.Profile.Settings.Port is { } p ? $":{p}" : string.Empty);
        StatusText = "Carregando objetos...";
        _navigator = new DatabaseNavigator(opened.Provider);

        try
        {
            await _session.Gate.WaitAsync();
            try
            {
                foreach (var schema in await _navigator.ListSchemasAsync(opened.Connection, CancellationToken.None))
                {
                    var tables = await _navigator.ListTablesAsync(opened.Connection, schema, CancellationToken.None);
                    var views = await _navigator.ListViewsAsync(opened.Connection, schema, CancellationToken.None);
                    _catalog.Add(new SchemaCatalog(schema, tables, views));
                }
            }
            finally
            {
                _session.Gate.Release();
            }

            RebuildTree();
            StatusText = _catalog.Count == 0 ? "Nenhuma tabela ou view encontrada." : string.Empty;
        }
        catch (NotImplementedException)
        {
            // Introspecção de PostgreSQL/MySQL/Firebird ainda não existe (Sprints B2–B4); o SQL Editor já funciona.
            StatusText = $"A navegação de objetos para {opened.Provider.DisplayName} ainda não foi implementada " +
                         "(introspecção prevista nas sprints B2–B4). O SQL Editor funciona nesta conexão.";
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao listar objetos: " + ex.Message;
        }
    }

    // Monta a árvore a partir do catálogo, aplicando a busca. Um schema que casa com a busca mostra todos os seus objetos.
    private void RebuildTree()
    {
        Nodes.Clear();
        foreach (var entry in _catalog)
        {
            var schemaMatches = ExplorerFilter.Matches(entry.Schema, SearchText);
            var tables = schemaMatches ? entry.Tables.ToList() : ExplorerFilter.Filter(entry.Tables, SearchText);
            var views = schemaMatches ? entry.Views.ToList() : ExplorerFilter.Filter(entry.Views, SearchText);
            if (!schemaMatches && tables.Count == 0 && views.Count == 0)
            {
                continue;
            }

            var schemaNode = new ExplorerNode(entry.Schema, "") { IsExpanded = true };

            var tablesNode = new ExplorerNode($"Tables ({tables.Count})", "") { IsExpanded = true };
            foreach (var t in tables)
            {
                tablesNode.Children.Add(new ExplorerNode(t.Name, "", t));
            }

            schemaNode.Children.Add(tablesNode);

            var viewsNode = new ExplorerNode($"Views ({views.Count})", "");
            foreach (var v in views)
            {
                viewsNode.Children.Add(new ExplorerNode(v.Name, "", v));
            }

            schemaNode.Children.Add(viewsNode);
            Nodes.Add(schemaNode);
        }

        if (_catalog.Count > 0 && Nodes.Count == 0 && !string.IsNullOrWhiteSpace(SearchText))
        {
            StatusText = $"Nenhum objeto corresponde a '{SearchText.Trim()}'.";
        }
        else if (!string.IsNullOrWhiteSpace(SearchText) || Nodes.Count > 0)
        {
            StatusText = string.Empty;
        }
    }

    public async Task SelectNodeAsync(ExplorerNode? node)
    {
        var opened = _session.Current;
        if (node?.Table is not { } table || opened is null || _navigator is null)
        {
            return;
        }

        _selectedTable = table;
        SelectedTableName = table.Name;
        SelectedTableQualified = table.Namespace + "." + table.Name;
        SelectedColumns.Clear();
        IncomingReferences.Clear();
        ReferencesStatus = string.Empty;

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

                var inspection = ObjectInspector.Inspect(table, described.Value!);
                foreach (var c in inspection.Columns)
                {
                    var key = c.IsPrimaryKey ? "PK" : c.IsForeignKey ? "FK" : c.IsUnique ? "UQ" : string.Empty;
                    SelectedColumns.Add(new ExplorerColumnRow(c.Name, c.NativeType, key, c.Nullable ? "Yes" : "No"));
                }

                var dialect = opened.Provider.GetDialect(opened.Server.Version);
                SqlEditor.QueryText = TablePreview.BuildSql(table, described.Value!, dialect.QuoteIdentifier, TablePreview.DefaultRows) + ";";
                StatusText = string.Empty;
            }
            finally
            {
                _session.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao descrever o objeto: " + ex.Message;
        }
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

public sealed class SqlEditorPageViewModel(SqlEditorViewModel editor, SqlResultsViewModel results)
{
    public SqlEditorViewModel Editor { get; } = editor;
    public SqlResultsViewModel Results { get; } = results;
}
