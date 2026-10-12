using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.App.Samples;
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

    public void Clear(string status, bool isError = false)
    {
        Rows = null;
        StatusText = status;
        IsError = isError;
        MessagesText = string.Empty;
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

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    public bool HasConnection => _session.Current is not null;

    public string Title => _session.Current is { } c ? $"Query - {Database(c)}" : "Query";

    public string DatabaseName => _session.Current is { } c ? $"{Database(c)} ({c.Provider.DisplayName})" : "Sem conexão";

    private static string Database(OpenedConnection c) =>
        string.IsNullOrWhiteSpace(c.Server.DatabaseName) ? c.Profile.Name : c.Server.DatabaseName;

    partial void OnIsRunningChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(HasConnection));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DatabaseName));
        RunCommand.NotifyCanExecuteChanged();
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
}

public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly ActiveConnectionSession _session;
    private DatabaseNavigator? _navigator;

    public ExplorerViewModel(ActiveConnectionSession session, SqlEditorViewModel sqlEditor, SqlResultsViewModel sqlResults)
    {
        _session = session;
        SqlEditor = sqlEditor;
        SqlResults = sqlResults;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _session.Changed += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    public SqlEditorViewModel SqlEditor { get; }
    public SqlResultsViewModel SqlResults { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public ObservableCollection<ExplorerNode> Nodes { get; } = [];
    public ObservableCollection<ColumnRow> SelectedColumns { get; } = [];

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

    private async Task LoadAsync()
    {
        Nodes.Clear();
        SelectedColumns.Clear();
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
                    var schemaNode = new ExplorerNode(schema, "") { IsExpanded = true };

                    var tables = await _navigator.ListTablesAsync(opened.Connection, schema, CancellationToken.None);
                    var tablesNode = new ExplorerNode($"Tables ({tables.Count})", "") { IsExpanded = true };
                    foreach (var t in tables)
                    {
                        tablesNode.Children.Add(new ExplorerNode(t.Name, "", t));
                    }

                    schemaNode.Children.Add(tablesNode);

                    var views = await _navigator.ListViewsAsync(opened.Connection, schema, CancellationToken.None);
                    var viewsNode = new ExplorerNode($"Views ({views.Count})", "");
                    foreach (var v in views)
                    {
                        viewsNode.Children.Add(new ExplorerNode(v.Name, "", v));
                    }

                    schemaNode.Children.Add(viewsNode);
                    Nodes.Add(schemaNode);
                }
            }
            finally
            {
                _session.Gate.Release();
            }

            StatusText = Nodes.Count == 0 ? "Nenhuma tabela ou view encontrada." : string.Empty;
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

    public async Task SelectNodeAsync(ExplorerNode? node)
    {
        var opened = _session.Current;
        if (node?.Table is not { } table || opened is null || _navigator is null)
            return;

        SelectedTableName = table.Name;
        SelectedTableQualified = table.Namespace + "." + table.Name;
        SelectedColumns.Clear();

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
                    SelectedColumns.Add(new ColumnRow(c.Name, c.NativeType, key, c.Nullable ? "Yes" : "No"));
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
}

public sealed class SqlEditorPageViewModel(SqlEditorViewModel editor, SqlResultsViewModel results)
{
    public SqlEditorViewModel Editor { get; } = editor;
    public SqlResultsViewModel Results { get; } = results;
}
