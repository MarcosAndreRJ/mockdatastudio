using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using MockDataStudio.App.Controls;
using MockDataStudio.Application;
using MockDataStudio.Application.Explorer;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Splitter;

namespace MockDataStudio.App.ViewModels;

// SQL Editor em abas (DA-312). Cada aba tem a própria conexão (perfil + database), o próprio documento e os próprios
// resultados; o Explorer e a página SQL Editor mostram o mesmo conjunto de abas.

// Perfil salvo como opção de seleção. A instância é estável por Id para que os ComboBoxes não percam a seleção quando
// a lista é atualizada (ver SqlEditorWorkspaceViewModel.RefreshProfilesAsync).
public sealed partial class ProfileOption(Guid id, string name, DatabaseEngine engine) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _name = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private DatabaseEngine _engine = engine;

    public string Display => $"{Name} ({Engine})";

    public override string ToString() => Display;
}

// Grade de resultado: valores convertidos para texto (exibição) e nomes de coluna únicos.
internal static class ResultGrid
{
    public static DataTable Build(IEnumerable<string> columns, IEnumerable<object?[]> rows)
    {
        var table = new DataTable { Locale = CultureInfo.CurrentCulture };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            var name = string.IsNullOrWhiteSpace(column) ? "?column?" : column;
            var unique = name;
            for (var i = 2; !used.Add(unique); i++)
            {
                unique = name + "_" + i.ToString(CultureInfo.InvariantCulture);
            }

            table.Columns.Add(unique, typeof(string));
        }

        foreach (var row in rows)
        {
            var cells = new object?[table.Columns.Count];
            for (var i = 0; i < cells.Length && i < row.Length; i++)
            {
                cells[i] = row[i] is null or DBNull ? null : Convert.ToString(row[i], CultureInfo.CurrentCulture);
            }

            table.Rows.Add(cells);
        }

        return table;
    }

    // Filtro "contém" em todas as colunas (DataView.RowFilter). Curingas e aspas do texto são escapados.
    public static string RowFilter(DataTable table, string text)
    {
        var value = new StringBuilder();
        foreach (var ch in text.Trim())
        {
            _ = ch switch
            {
                '*' or '%' or '[' or ']' => value.Append('[').Append(ch).Append(']'),
                '\'' => value.Append("''"),
                _ => value.Append(ch),
            };
        }

        return string.Join(" OR ", table.Columns.Cast<DataColumn>().Select(c =>
            "[" + c.ColumnName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)
            + "] LIKE '%" + value + "%'"));
    }
}

public sealed partial class SqlResultsViewModel : ObservableObject
{
    public const int ResultsTab = 0;
    public const int MessagesTab = 1;
    public const int PlanTab = 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private DataView? _rows;

    [ObservableProperty]
    private DataView? _planRows;

    [ObservableProperty]
    private string _statusText = "Nenhuma consulta executada.";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _messagesText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _rowCountText = string.Empty;

    public SqlResultsViewModel()
    {
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => Rows is not null);
    }

    // Exporta as linhas visíveis na grade (primeira página, com o filtro aplicado). Exportação completa por cursor é pendência da C6.
    public AsyncRelayCommand ExportCommand { get; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnRowsChanged(DataView? value) => ApplyFilter();

    private void ApplyFilter()
    {
        if (Rows is not { Table: { } table } view)
        {
            RowCountText = string.Empty;
            return;
        }

        try
        {
            view.RowFilter = string.IsNullOrWhiteSpace(SearchText) || table.Columns.Count == 0
                ? string.Empty
                : ResultGrid.RowFilter(table, SearchText);
        }
        catch (EvaluateException)
        {
            view.RowFilter = string.Empty;
        }
        catch (SyntaxErrorException)
        {
            view.RowFilter = string.Empty;
        }

        RowCountText = string.IsNullOrEmpty(view.RowFilter)
            ? $"{table.Rows.Count} linha(s)"
            : $"{view.Count} de {table.Rows.Count} linha(s)";
    }

    public void Clear(string status, bool isError = false)
    {
        Rows = null;
        StatusText = status;
        IsError = isError;
        MessagesText = isError ? status : string.Empty;
        SelectedTabIndex = ResultsTab;
    }

    // Mostra o plano de execução (EXPLAIN) na aba Query Plan, sem tocar no último resultado.
    public void ShowPlan(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var table = ResultGrid.Build(plan.Columns, plan.Rows);
        IsError = false;
        PlanRows = table.Columns.Count > 0 ? table.DefaultView : null;
        StatusText = $"Plano de execução: {plan.Rows.Count} linha(s).";
        SelectedTabIndex = PlanTab;
    }

    private async Task ExportAsync()
    {
        if (Rows is not { Table: { } table } view)
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
        var rows = view.Cast<DataRowView>().Select(r => r.Row.ItemArray.Select(v => v is DBNull ? null : v).ToArray()).ToList();

        try
        {
            await using var stream = File.Create(dialog.FileName);
            var written = await ResultExportService.ExportAsync(stream, format, columns, rows, CancellationToken.None);
            StatusText = $"{written} linha(s) exportada(s) para {dialog.FileName}.";
        }
        catch (IOException ex)
        {
            StatusText = "Falha ao exportar: " + ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText = "Falha ao exportar: " + ex.Message;
        }
    }

    // Copia a primeira página do último comando com resultado para uma tabela em memória e libera os cursores.
    public async Task ShowAsync(QueryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var messages = run.Messages.Select(m => m.Text).ToList();
        foreach (var s in run.Statements)
        {
            var prefix = run.Statements.Count > 1 ? $"[{s.Index + 1}] " : string.Empty;
            // QueryService já descreve cada comando (linhas, tempo, erro com a linha do script).
            messages.AddRange(s.Messages.Select(m => prefix + m.Text));
        }

        // A grade mostra o último comando com linhas; o estado mostra o comando que interrompeu o script, se houver.
        var grid = run.Statements.LastOrDefault(s => s.Columns.Count > 0);
        var shown = run.Statements.FirstOrDefault(s => s.Status != StatementStatus.Succeeded) ?? (run.Statements.Count > 0 ? run.Statements[^1] : null);
        var table = grid is null ? new DataTable() : ResultGrid.Build(grid.Columns.Select(c => c.Name), grid.FirstPage);

        IsError = shown is { Status: not StatementStatus.Succeeded } || (shown is null && messages.Count > 0);
        Rows = table.Columns.Count > 0 ? table.DefaultView : null;
        MessagesText = string.Join(Environment.NewLine, messages);
        StatusText = shown switch
        {
            null => messages.Count > 0 ? messages[0] : "Nada a executar.",
            { Status: StatementStatus.Failed } s => $"Falha ao executar o comando (linha {s.Line}): {s.Error?.Message}",
            { Status: StatementStatus.Blocked } => "Comando bloqueado pela política de execução.",
            { Status: StatementStatus.RequiresConfirmation } => "Comando exige confirmação.",
            { Status: StatementStatus.Cancelled } => "Execução cancelada.",
            { Columns.Count: > 0 } s => $"{s.FirstPage.Count} linha(s){(s.HasMore ? " (primeira página; há mais linhas)" : string.Empty)} em {s.Duration.TotalMilliseconds:0} ms",
            { RecordsAffected: { } n and >= 0 } s => $"{n} linha(s) afetada(s) em {s.Duration.TotalMilliseconds:0} ms",
            var s => $"Comando executado em {s.Duration.TotalMilliseconds:0} ms",
        };
        SelectedTabIndex = IsError || Rows is null ? MessagesTab : ResultsTab;

        await run.DisposeAsync();
    }
}

public sealed partial class SqlEditorTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IConnectionService _connections;
    private readonly ISqlScriptSplitter _splitter;

    // Um DbConnection não aceita comandos simultâneos: execução, EXPLAIN e troca de conexão passam por aqui.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OpenedConnection? _opened;
    private CancellationTokenSource? _cts;
    private int _connectVersion;
    private bool _suppressSelection;

    public SqlEditorTabViewModel(int number, IConnectionService connections, ISqlScriptSplitter splitter)
    {
        Number = number;
        _connections = connections;
        _splitter = splitter;
        RunCommand = new AsyncRelayCommand(RunAsync, () => HasConnection && !IsRunning && !IsConnecting);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        ExplainCommand = new AsyncRelayCommand(ExplainAsync, () => HasConnection && !IsRunning && !IsConnecting);
        FormatCommand = new RelayCommand(FormatText);
        SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
        OpenScriptCommand = new AsyncRelayCommand(OpenScriptAsync);
        ReconnectCommand = new AsyncRelayCommand(() => ConnectAsync(SelectedProfile, SelectedDatabase), () => SelectedProfile is not null && !IsRunning);
        Results.Clear("Selecione a conexão e o database desta aba.");
    }

    public int Number { get; }

    // Documento do AvalonEdit da aba: texto, desfazer/refazer e seleção são de cada aba.
    public TextDocument Document { get; } = new();

    public SqlResultsViewModel Results { get; } = new();

    public ObservableCollection<string> Databases { get; } = [];

    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }
    public AsyncRelayCommand ExplainCommand { get; }
    public RelayCommand FormatCommand { get; }
    public AsyncRelayCommand SaveScriptCommand { get; }
    public AsyncRelayCommand OpenScriptCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }

    public string QueryText
    {
        get => Document.Text;
        set => Document.Text = value ?? string.Empty;
    }

    // Seleção atual no editor (mantida pela view). Run e Explain usam a seleção quando houver; senão, o texto todo.
    public string SelectedText { get; set; } = string.Empty;

    // Último texto gerado pelo Explorer; a aba pode ser reaproveitada enquanto o usuário não o editar.
    internal string? GeneratedText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private ProfileOption? _selectedProfile;

    [ObservableProperty]
    private string? _selectedDatabase;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string _scriptStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _scriptPath;

    public bool HasConnection => _opened is not null;

    public bool HasDatabaseChoice => Databases.Count > 0;

    public Guid? ProfileId => SelectedProfile?.Id;

    public string CurrentDatabase => _opened is { } c ? DatabaseOf(c) : string.Empty;

    public string Title
    {
        get
        {
            var name = ScriptPath is { } path ? Path.GetFileName(path) : "Query " + Number.ToString(CultureInfo.InvariantCulture);
            return _opened is { } c && DatabaseOf(c) is { Length: > 0 } db ? $"{name} - {db}" : name;
        }
    }

    public string DatabaseLabel => _opened is { } c
        ? $"{(DatabaseOf(c) is { Length: > 0 } db ? db : "(sem database)")} ({c.Provider.DisplayName})"
        : IsConnecting ? "Conectando..." : "Sem conexão";

    // Barra de estado: conexão, ambiente e se a somente leitura foi aplicada de fato (null sem conexão).
    public ConnectionStatusInfo? StatusBar => _opened is { } c
        ? ConnectionStatus.Build(c.Profile.Name, c.Profile.Engine, DatabaseOf(c), c.Profile.Environment,
            c.Profile.IsReadOnly, c.IsServerReadOnly)
        : null;

    internal static string DatabaseOf(OpenedConnection c) =>
        !string.IsNullOrWhiteSpace(c.Server.DatabaseName) ? c.Server.DatabaseName
        : c.Profile.Settings.Database ?? string.Empty;

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        if (!_suppressSelection)
        {
            _ = ConnectAsync(value, null);
        }
    }

    partial void OnSelectedDatabaseChanged(string? value)
    {
        if (!_suppressSelection && !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, CurrentDatabase, StringComparison.Ordinal))
        {
            _ = ConnectAsync(SelectedProfile, value);
        }
    }

    partial void OnIsRunningChanged(bool value) => RefreshCommands();

    partial void OnIsConnectingChanged(bool value)
    {
        RefreshCommands();
        OnPropertyChanged(nameof(DatabaseLabel));
    }

    private void RefreshCommands()
    {
        RunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ExplainCommand.NotifyCanExecuteChanged();
        ReconnectCommand.NotifyCanExecuteChanged();
    }

    private void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(HasConnection));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DatabaseLabel));
        OnPropertyChanged(nameof(StatusBar));
        OnPropertyChanged(nameof(ProfileId));
        RefreshCommands();
    }

    // Define perfil e database sem disparar a reconexão automática da seleção e conecta (usado ao criar a aba).
    public Task AttachAsync(ProfileOption? profile, string? database)
    {
        _suppressSelection = true;
        try
        {
            SelectedProfile = profile;
        }
        finally
        {
            _suppressSelection = false;
        }

        return ConnectAsync(profile, database);
    }

    // Abre a conexão da aba (perfil salvo + database opcional). Uma troca mais recente descarta o resultado desta.
    public async Task ConnectAsync(ProfileOption? profile, string? database)
    {
        var version = ++_connectVersion;
        _cts?.Cancel();

        var previous = _opened;
        _opened = null;
        if (previous is not null)
        {
            await _gate.WaitAsync();
            try
            {
                await previous.DisposeAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        if (profile is null)
        {
            SetDatabases([], null);
            Results.Clear("Selecione a conexão e o database desta aba.");
            RefreshConnectionState();
            return;
        }

        IsConnecting = true;
        Results.Clear($"Conectando a {profile.Name}...");
        try
        {
            var result = await _connections.OpenAsync(profile.Id, ConnectionPurpose.Query, database);
            if (version != _connectVersion)
            {
                if (result.IsSuccess)
                {
                    await result.Value!.DisposeAsync();
                }

                return;
            }

            if (!result.IsSuccess)
            {
                SetDatabases([], null);
                Results.Clear(result.Error!.Message, isError: true);
                return;
            }

            var opened = result.Value!;
            IReadOnlyList<string> databases = [];
            try
            {
                databases = await opened.Provider.ListDatabasesAsync(opened.Connection, CancellationToken.None);
            }
            catch (DbException)
            {
                // Sem permissão para listar: a aba continua no database aberto.
            }

            if (version != _connectVersion)
            {
                await opened.DisposeAsync();
                return;
            }

            _opened = opened;
            SetDatabases(databases, DatabaseOf(opened));
            Results.Clear($"Conectado a {opened.Server.Product} {opened.Server.Version}. Execute com F9 ou Ctrl+Enter.");
        }
        catch (Exception ex)
        {
            if (version == _connectVersion)
            {
                Results.Clear("Falha ao conectar: " + ex.Message, isError: true);
            }
        }
        finally
        {
            if (version == _connectVersion)
            {
                IsConnecting = false;
                RefreshConnectionState();
            }
        }
    }

    private void SetDatabases(IReadOnlyList<string> databases, string? current)
    {
        _suppressSelection = true;
        try
        {
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

    // Texto a executar: a seleção, quando houver; senão, o script inteiro.
    private string ScriptToRun() => string.IsNullOrWhiteSpace(SelectedText) ? QueryText : SelectedText;

    public async Task RunAsync()
    {
        var opened = _opened;
        var script = ScriptToRun();
        if (opened is null || string.IsNullOrWhiteSpace(script))
        {
            Results.Clear(opened is null ? "Selecione a conexão desta aba." : "Digite um comando SQL.");
            return;
        }

        var profile = opened.Profile;
        var policy = new ExecutionPolicy(profile.IsReadOnly, profile.Environment, ReadOnlyRestriction(profile.Engine));
        var confirmed = false;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        Results.StatusText = "Executando...";
        Results.IsError = false;
        try
        {
            await _gate.WaitAsync(_cts.Token);
            try
            {
                while (true)
                {
                    var request = new ExecutionRequest(script, DialectOf(profile.Engine), policy, Confirmed: confirmed,
                        CommandTimeoutSec: profile.Settings.CommandTimeoutSec);
                    var run = await new QueryService(_splitter, opened.Provider.ErrorTranslator).ExecuteAsync(opened.Connection, request, _cts.Token);

                    var pending = run.Statements.FirstOrDefault(s => s.Status == StatementStatus.RequiresConfirmation);
                    if (pending is not null && !confirmed && Confirm(pending, profile.Environment))
                    {
                        await run.DisposeAsync();
                        confirmed = true;
                        continue;
                    }

                    await Results.ShowAsync(run);
                    break;
                }
            }
            finally
            {
                _gate.Release();
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

    private static bool Confirm(StatementOutcome pending, ConnectionEnvironment environment)
    {
        var reason = string.Join(Environment.NewLine, pending.Messages.Select(m => m.Text));
        var verdict = new AccessVerdict(AccessDecision.RequiresConfirmation, reason);
        var confirmation = ExecutionGuard.RequestFor(ExecutionPolicy.AccessOf(pending.Kind), verdict, environment);
        var owner = System.Windows.Application.Current?.MainWindow;
        return confirmation.Level switch
        {
            ConfirmationLevel.Typed => TypedConfirmDialog.Ask(owner!, "Confirmar execução",
                $"{reason}\n\nAmbiente: {environment}. Esta ação altera o banco.", confirmation.TypedWord!, "Executar"),
            ConfirmationLevel.Simple => ConfirmDialog.Ask(owner!, "Confirmar execução", $"{reason}\n\nExecutar mesmo assim?", "Executar"),
            _ => true,
        };
    }

    // EXPLAIN do texto (ou da seleção). Só SELECT/WITH; ver ExplainService. Não executa a consulta.
    public async Task ExplainAsync()
    {
        var opened = _opened;
        var script = ScriptToRun();
        if (opened is null || string.IsNullOrWhiteSpace(script))
        {
            Results.Clear(opened is null ? "Selecione a conexão desta aba." : "Digite um comando SQL.", isError: opened is not null);
            return;
        }

        IsRunning = true;
        try
        {
            await _gate.WaitAsync();
            try
            {
                var plan = await ExplainService.ExplainAsync(opened.Connection, opened.Profile.Engine, script.Trim().TrimEnd(';'), CancellationToken.None);
                Results.ShowPlan(plan);
            }
            finally
            {
                _gate.Release();
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
        finally
        {
            IsRunning = false;
        }
    }

    private void FormatText()
    {
        if (!string.IsNullOrWhiteSpace(QueryText))
        {
            QueryText = SqlFormatter.Format(QueryText);
        }
    }

    private async Task SaveScriptAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Scripts SQL (*.sql)|*.sql|Todos (*.*)|*.*",
            DefaultExt = ".sql",
            FileName = ScriptPath is { } path ? Path.GetFileName(path) : $"query{Number}.sql",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ScriptFileService.SaveAsync(dialog.FileName, QueryText, CancellationToken.None);
        ScriptPath = dialog.FileName;
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
        ScriptPath = dialog.FileName;
        ScriptStatus = "Script aberto: " + dialog.FileName;
    }

    public async ValueTask DisposeAsync()
    {
        ++_connectVersion;
        _cts?.Cancel();
        if (_opened is { } opened)
        {
            _opened = null;
            await opened.DisposeAsync();
        }

        RefreshConnectionState();
    }
}

public sealed partial class SqlEditorWorkspaceViewModel : ObservableObject
{
    private readonly IConnectionService _connections;
    private readonly ISqlScriptSplitter _splitter;
    private readonly ActiveConnectionSession _session;
    private int _nextNumber = 1;

    public SqlEditorWorkspaceViewModel(IConnectionService connections, ISqlScriptSplitter splitter, ActiveConnectionSession session)
    {
        _connections = connections;
        _splitter = splitter;
        _session = session;
        NewTabCommand = new AsyncRelayCommand(() => NewTabAsync());
        CloseTabCommand = new AsyncRelayCommand<SqlEditorTabViewModel?>(CloseTabAsync);
        RefreshProfilesCommand = new AsyncRelayCommand(RefreshProfilesAsync);

        SelectedTab = CreateTab();
        _session.Changed += (_, _) => _ = AdoptSessionAsync();
        _ = AdoptSessionAsync();
    }

    public ObservableCollection<SqlEditorTabViewModel> Tabs { get; } = [];

    public ObservableCollection<ProfileOption> Profiles { get; } = [];

    public AsyncRelayCommand NewTabCommand { get; }
    public AsyncRelayCommand<SqlEditorTabViewModel?> CloseTabCommand { get; }
    public AsyncRelayCommand RefreshProfilesCommand { get; }

    [ObservableProperty]
    private SqlEditorTabViewModel? _selectedTab;

    private SqlEditorTabViewModel CreateTab()
    {
        var tab = new SqlEditorTabViewModel(_nextNumber++, _connections, _splitter);
        Tabs.Add(tab);
        return tab;
    }

    // Sincroniza a lista de perfis salvos sem trocar as instâncias existentes (a seleção dos ComboBoxes é preservada).
    public async Task RefreshProfilesAsync()
    {
        var profiles = await _connections.ListAsync();
        foreach (var stale in Profiles.Where(o => profiles.All(p => p.Id != o.Id)).ToList())
        {
            Profiles.Remove(stale);
        }

        foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (Profiles.FirstOrDefault(o => o.Id == profile.Id) is { } existing)
            {
                existing.Name = profile.Name;
                existing.Engine = profile.Engine;
            }
            else
            {
                Profiles.Add(new ProfileOption(profile.Id, profile.Name, profile.Engine));
            }
        }
    }

    public async Task<ProfileOption?> FindProfileAsync(Guid id)
    {
        if (Profiles.All(p => p.Id != id))
        {
            await RefreshProfilesAsync();
        }

        return Profiles.FirstOrDefault(p => p.Id == id);
    }

    // Abas ainda sem conexão adotam a conexão aberta no Explorer (perfil + database).
    private async Task AdoptSessionAsync()
    {
        await RefreshProfilesAsync();
        if (_session.Current is not { } opened || await FindProfileAsync(opened.Profile.Id) is not { } profile)
        {
            return;
        }

        foreach (var tab in Tabs.Where(t => t.SelectedProfile is null && !t.IsConnecting).ToList())
        {
            await tab.AttachAsync(profile, SqlEditorTabViewModel.DatabaseOf(opened));
        }
    }

    // Nova aba: conexão do Explorer quando houver; senão, a mesma da aba atual.
    public async Task<SqlEditorTabViewModel> NewTabAsync(Guid? profileId = null, string? database = null, string? text = null)
    {
        if (profileId is null)
        {
            if (_session.Current is { } opened)
            {
                profileId = opened.Profile.Id;
                database = SqlEditorTabViewModel.DatabaseOf(opened);
            }
            else if (SelectedTab?.ProfileId is { } current)
            {
                profileId = current;
                database = SelectedTab.CurrentDatabase;
            }
        }

        var tab = CreateTab();
        if (text is not null)
        {
            tab.QueryText = text;
        }

        SelectedTab = tab;
        var profile = profileId is { } id ? await FindProfileAsync(id) : null;
        await tab.AttachAsync(profile, database);
        return tab;
    }

    // Texto gerado pelo Explorer (SELECT, DDL, INSERT de exemplo). Reaproveita uma aba do mesmo perfil e database
    // cujo texto o usuário não editou; senão abre uma nova. Nunca sobrescreve um texto digitado pelo usuário.
    public async Task<SqlEditorTabViewModel> OpenGeneratedAsync(Guid profileId, string database, string text, bool run)
    {
        var tab = Tabs.FirstOrDefault(t => t.ProfileId == profileId && t.HasConnection && !t.IsRunning
                && string.Equals(t.CurrentDatabase, database, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(t.QueryText) || t.QueryText == t.GeneratedText));
        if (tab is null)
        {
            tab = await NewTabAsync(profileId, database, text);
        }
        else
        {
            tab.QueryText = text;
            SelectedTab = tab;
        }

        tab.GeneratedText = text;
        if (run)
        {
            tab.SelectedText = string.Empty;
            await tab.RunAsync();
        }

        return tab;
    }

    private async Task CloseTabAsync(SqlEditorTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(tab.QueryText) && tab.QueryText != tab.GeneratedText && tab.ScriptPath is null)
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is not null && !ConfirmDialog.Ask(owner, "Fechar aba", $"Fechar '{tab.Title}'? O texto não salvo será perdido.", "Fechar"))
            {
                return;
            }
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
            await NewTabAsync();
        }
        else if (ReferenceEquals(SelectedTab, tab) || SelectedTab is null)
        {
            SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }

        await tab.DisposeAsync();
    }
}

public sealed class SqlEditorPageViewModel(SqlEditorWorkspaceViewModel workspace)
{
    public SqlEditorWorkspaceViewModel Workspace { get; } = workspace;
}
