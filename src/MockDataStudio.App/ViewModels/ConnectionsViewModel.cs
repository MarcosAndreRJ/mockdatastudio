using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MockDataStudio.App.Samples;
using MockDataStudio.Application;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.App.ViewModels;

public enum ConnectionTestState
{
    Untested,
    Testing,
    Success,
    Failed,
    Cancelled,
}

public sealed class ConnectionProfileItemViewModel : ObservableObject
{
    private bool _online;
    private string _lastChecked;
    private bool _starred;

    public ConnectionProfileItemViewModel(ConnectionProfile profile, string? logoResourceName = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _lastChecked = "Nunca testado";
        Logo = LoadLogo(logoResourceName);
    }

    public ConnectionProfile Profile { get; set; }

    public Guid Id => Profile.Id;

    public string Name => Profile.Name;

    public DatabaseEngine Engine => Profile.Engine;

    public string Provider => Engine.ToString();

    public string HostPath => string.IsNullOrWhiteSpace(Profile.Settings.Host)
        ? Profile.Settings.Database
        : (Profile.Settings.Port.HasValue
            ? $"{Profile.Settings.Host}:{Profile.Settings.Port}"
            : Profile.Settings.Host);

    public string Database => Profile.Settings.Database;

    public string Environment => Profile.Environment.ToString();

    public Brush EnvBg => Profile.Environment switch
    {
        ConnectionEnvironment.Development => HexBrush("#DBEAFE"),
        ConnectionEnvironment.Test => HexBrush("#DCFCE7"),
        ConnectionEnvironment.Staging => HexBrush("#EDE9FE"),
        ConnectionEnvironment.Production => HexBrush("#FEE2E2"),
        _ => HexBrush("#F1F5F9"),
    };

    public Brush EnvFg => Profile.Environment switch
    {
        ConnectionEnvironment.Development => HexBrush("#1D4ED8"),
        ConnectionEnvironment.Test => HexBrush("#15803D"),
        ConnectionEnvironment.Staging => HexBrush("#6D28D9"),
        ConnectionEnvironment.Production => HexBrush("#B91C1C"),
        _ => HexBrush("#475569"),
    };

    public bool Online
    {
        get => _online;
        set => SetProperty(ref _online, value);
    }

    public string LastChecked
    {
        get => _lastChecked;
        set => SetProperty(ref _lastChecked, value);
    }

    public bool Starred
    {
        get => _starred;
        set => SetProperty(ref _starred, value);
    }

    public BitmapImage? Logo { get; }

    private static BitmapImage? LoadLogo(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            var uri = new Uri($"pack://application:,,,/Resources/Logos/{name}.png", UriKind.Absolute);
            var image = new BitmapImage(uri);
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush HexBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class ConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionService _connectionService;
    private readonly IProviderRegistry _providerRegistry;
    private readonly ActiveConnectionSession _session;
    private readonly MockDataStudio.App.Services.ShellNavigator _navigator;

    private ObservableCollection<ConnectionProfileItemViewModel> _savedConnections = [];
    private ConnectionProfileItemViewModel? _selectedConnection;
    private string _searchText = string.Empty;

    // Campos do formulário
    private Guid _formId;
    private string _formName = string.Empty;
    private DatabaseEngine _formEngine = DatabaseEngine.PostgreSql;
    private string _formHost = "localhost";
    private string _formPort = "5432";
    private string _formDatabase = string.Empty;
    private string _formUsername = string.Empty;
    private string _formPassword = string.Empty;
    private bool _formPromptPassword;
    private string _formSslMode = "Prefer";
    private string _formTimeoutSec = "15";
    private ConnectionEnvironment _formEnvironment = ConnectionEnvironment.Unknown;
    private bool _formIsReadOnly = true;
    private string? _formNotes;
    private string? _formCharset = "UTF8";

    // Estado do teste de conexão
    private ConnectionTestState _testState = ConnectionTestState.Untested;
    private string _testResultMessage = string.Empty;
    private string _testLatency = string.Empty;
    private string _serverVersionText = string.Empty;
    private CancellationTokenSource? _activeTestCts;

    // Indicadores do topo
    private int _totalConnectionsCount;
    private int _onlineConnectionsCount;
    private int _offlineConnectionsCount;
    private string _environmentSummaryText = "Unknown";

    // Listas auxiliares de teste e atividades
    private ObservableCollection<ConnectionTest> _recentConnectionTests = [];
    private ObservableCollection<ConnectionActivity> _connectionActivities = [];

    public ConnectionsViewModel(
        IConnectionService connectionService,
        IProviderRegistry providerRegistry,
        ActiveConnectionSession session,
        MockDataStudio.App.Services.ShellNavigator navigator)
    {
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

        // Inicializar coleções e comandos
        AvailableEngines = [DatabaseEngine.PostgreSql, DatabaseEngine.MySql, DatabaseEngine.Sqlite, DatabaseEngine.Firebird];
        AvailableEnvironments =
        [
            ConnectionEnvironment.Development,
            ConnectionEnvironment.Test,
            ConnectionEnvironment.Staging,
            ConnectionEnvironment.Production,
            ConnectionEnvironment.Unknown,
        ];

        NewConnectionCommand = new RelayCommand(PrepareNewConnection);
        SaveCommand = new AsyncRelayCommand(SaveConnectionAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteConnectionAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        CancelTestCommand = new RelayCommand(CancelTest);
        RefreshAllCommand = new AsyncRelayCommand(LoadConnectionsAsync);
        BrowseSqliteFileCommand = new RelayCommand(BrowseSqliteFile);
        OpenInExplorerCommand = new AsyncRelayCommand(OpenInExplorerAsync);

        // Carregar conexões salvas ao iniciar
        _ = LoadConnectionsAsync();
    }

    public IReadOnlyList<DatabaseEngine> AvailableEngines { get; }

    public IReadOnlyList<ConnectionEnvironment> AvailableEnvironments { get; }

    public ObservableCollection<ConnectionProfileItemViewModel> SavedConnections
    {
        get => _savedConnections;
        set => SetProperty(ref _savedConnections, value);
    }

    public ConnectionProfileItemViewModel? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (SetProperty(ref _selectedConnection, value) && value != null)
            {
                PopulateFormFromProfile(value.Profile);
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                // Filtro em memória
                _ = LoadConnectionsAsync();
            }
        }
    }

    // Formulário
    public string FormName
    {
        get => _formName;
        set { if (SetProperty(ref _formName, value)) InvalidateTestState(); }
    }

    public DatabaseEngine FormEngine
    {
        get => _formEngine;
        set
        {
            var previous = _formEngine;
            if (SetProperty(ref _formEngine, value))
            {
                ApplyEngineDefaults(previous, value);
                OnPropertyChanged(nameof(RequiresHostPort));
                OnPropertyChanged(nameof(RequiresCredentials));
                OnPropertyChanged(nameof(RequiresDatabase));
                OnPropertyChanged(nameof(IsSqlite));
                OnPropertyChanged(nameof(IsNetworkDatabase));
                OnPropertyChanged(nameof(CurrentProviderDisplayName));
                OnPropertyChanged(nameof(EngineLogo));
                InvalidateTestState();
            }
        }
    }

    // Ao trocar o provedor, porta e usuário acompanham os padrões do novo SGBD, sem sobrescrever valores personalizados.
    private void ApplyEngineDefaults(DatabaseEngine previous, DatabaseEngine current)
    {
        var oldProvider = _providerRegistry.Get(previous);
        var newProvider = _providerRegistry.Get(current);

        var oldPort = oldProvider.DefaultPort?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(_formPort) || _formPort == oldPort || !newProvider.RequiresHostPort)
        {
            FormPort = newProvider.DefaultPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_formUsername) || _formUsername == oldProvider.DefaultUser || !newProvider.RequiresHostPort)
        {
            FormUsername = newProvider.DefaultUser ?? string.Empty;
        }

        // O valor de database de um SGBD (nome ou caminho de arquivo) não vale para outro.
        FormDatabase = string.Empty;
        AvailableDatabases.Clear();
        OnPropertyChanged(nameof(AvailableSslModes));
        OnPropertyChanged(nameof(HasSslMode));
        FormSslMode = newProvider.ExtraSettings.FirstOrDefault(s => s.Key == "sslMode")?.Default ?? string.Empty;
    }

    public string FormHost
    {
        get => _formHost;
        set { if (SetProperty(ref _formHost, value)) InvalidateTestState(); }
    }

    public string FormPort
    {
        get => _formPort;
        set { if (SetProperty(ref _formPort, value)) InvalidateTestState(); }
    }

    public string FormDatabase
    {
        get => _formDatabase;
        set { if (SetProperty(ref _formDatabase, value)) InvalidateTestState(); }
    }

    public string FormUsername
    {
        get => _formUsername;
        set { if (SetProperty(ref _formUsername, value)) InvalidateTestState(); }
    }

    public string FormPassword
    {
        get => _formPassword;
        set { if (SetProperty(ref _formPassword, value)) InvalidateTestState(); }
    }

    public bool FormPromptPassword
    {
        get => _formPromptPassword;
        set { if (SetProperty(ref _formPromptPassword, value)) InvalidateTestState(); }
    }

    public string FormSslMode
    {
        get => _formSslMode;
        set { if (value is not null && SetProperty(ref _formSslMode, value)) InvalidateTestState(); }
    }

    public string FormTimeoutSec
    {
        get => _formTimeoutSec;
        set { if (SetProperty(ref _formTimeoutSec, value)) InvalidateTestState(); }
    }

    public ConnectionEnvironment FormEnvironment
    {
        get => _formEnvironment;
        set
        {
            if (SetProperty(ref _formEnvironment, value))
            {
                // Regra §11: Unknown e Production travam ReadOnly como true por padrão
                if (value is ConnectionEnvironment.Production or ConnectionEnvironment.Unknown)
                {
                    FormIsReadOnly = true;
                }
                InvalidateTestState();
            }
        }
    }

    public bool FormIsReadOnly
    {
        get => _formIsReadOnly;
        set => SetProperty(ref _formIsReadOnly, value);
    }

    public string? FormNotes
    {
        get => _formNotes;
        set => SetProperty(ref _formNotes, value);
    }

    public string? FormCharset
    {
        get => _formCharset;
        set { if (SetProperty(ref _formCharset, value)) InvalidateTestState(); }
    }

    public bool RequiresHostPort => _providerRegistry.Get(FormEngine).RequiresHostPort;

    public bool RequiresCredentials => _providerRegistry.Get(FormEngine).RequiresHostPort;

    public bool RequiresDatabase => _providerRegistry.Get(FormEngine).RequiresDatabase;

    public ObservableCollection<string> AvailableDatabases { get; } = [];

    private ConnectionSettingDefinition? SslDefinition =>
        _providerRegistry.Get(FormEngine).ExtraSettings.FirstOrDefault(s => s.Key == "sslMode");

    public IReadOnlyList<string> AvailableSslModes => SslDefinition?.Options ?? [];

    public bool HasSslMode => SslDefinition is not null;

    public bool IsSqlite => !RequiresHostPort;

    public bool IsNetworkDatabase => RequiresHostPort;

    public string CurrentProviderDisplayName => _providerRegistry.Get(FormEngine).DisplayName;

    public BitmapImage? EngineLogo => LoadLogo(_providerRegistry.Get(FormEngine).LogoResourceName);

    // Teste de conexão
    public ConnectionTestState TestState
    {
        get => _testState;
        set
        {
            if (SetProperty(ref _testState, value))
            {
                OnPropertyChanged(nameof(IsTesting));
                OnPropertyChanged(nameof(TestStatusText));
                OnPropertyChanged(nameof(TestStatusColor));
            }
        }
    }

    public bool IsTesting => _testState == ConnectionTestState.Testing;

    public string TestStatusText => _testState switch
    {
        ConnectionTestState.Untested => "Não testada",
        ConnectionTestState.Testing => "Testando conexão...",
        ConnectionTestState.Success => "Conectada com sucesso",
        ConnectionTestState.Failed => "Falha na conexão",
        ConnectionTestState.Cancelled => "Teste cancelado",
        _ => string.Empty,
    };

    public Brush TestStatusColor => _testState switch
    {
        ConnectionTestState.Success => HexBrush("#16A34A"),
        ConnectionTestState.Failed => HexBrush("#DC2626"),
        ConnectionTestState.Testing => HexBrush("#2563EB"),
        ConnectionTestState.Cancelled => HexBrush("#D97706"),
        _ => HexBrush("#64748B"),
    };

    public string TestResultMessage
    {
        get => _testResultMessage;
        set => SetProperty(ref _testResultMessage, value);
    }

    public string TestLatency
    {
        get => _testLatency;
        set => SetProperty(ref _testLatency, value);
    }

    public string ServerVersionText
    {
        get => _serverVersionText;
        set => SetProperty(ref _serverVersionText, value);
    }

    // Indicadores do topo
    public int TotalConnectionsCount
    {
        get => _totalConnectionsCount;
        set => SetProperty(ref _totalConnectionsCount, value);
    }

    public int OnlineConnectionsCount
    {
        get => _onlineConnectionsCount;
        set => SetProperty(ref _onlineConnectionsCount, value);
    }

    public int OfflineConnectionsCount
    {
        get => _offlineConnectionsCount;
        set => SetProperty(ref _offlineConnectionsCount, value);
    }

    public string EnvironmentSummaryText
    {
        get => _environmentSummaryText;
        set => SetProperty(ref _environmentSummaryText, value);
    }

    public ObservableCollection<ConnectionTest> RecentConnectionTests
    {
        get => _recentConnectionTests;
        set => SetProperty(ref _recentConnectionTests, value);
    }

    public ObservableCollection<ConnectionActivity> ConnectionActivities
    {
        get => _connectionActivities;
        set => SetProperty(ref _connectionActivities, value);
    }

    // Comandos
    public ICommand NewConnectionCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand CancelTestCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public ICommand BrowseSqliteFileCommand { get; }
    public ICommand OpenInExplorerCommand { get; }

    public async Task LoadConnectionsAsync()
    {
        try
        {
            var profiles = await _connectionService.ListAsync();
            var items = profiles.Select(p => new ConnectionProfileItemViewModel(p, _providerRegistry.Get(p.Engine).LogoResourceName)).ToList();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                items = items.Where(i =>
                    i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                    i.HostPath.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                    i.Database.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                    i.Provider.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            SavedConnections = new ObservableCollection<ConnectionProfileItemViewModel>(items);
            UpdateHeaderIndicators();

            if (SelectedConnection == null && SavedConnections.Count > 0)
            {
                SelectedConnection = SavedConnections[0];
            }
            else if (SavedConnections.Count == 0)
            {
                PrepareNewConnection();
            }
        }
        catch (Exception ex)
        {
            TestResultMessage = $"Erro ao carregar perfis: {ex.Message}";
        }
    }

    public void PrepareNewConnection()
    {
        _selectedConnection = null;
        OnPropertyChanged(nameof(SelectedConnection));

        _formId = Guid.NewGuid();
        FormName = "Nova Conexão";
        FormEngine = DatabaseEngine.PostgreSql;
        FormHost = "localhost";
        FormPort = "5432";
        FormDatabase = string.Empty;
        FormUsername = "postgres";
        FormPassword = string.Empty;
        FormPromptPassword = false;
        FormTimeoutSec = "15";
        FormEnvironment = ConnectionEnvironment.Unknown;
        FormIsReadOnly = true;
        FormNotes = null;
        FormCharset = "UTF8";

        InvalidateTestState();
    }

    private void PopulateFormFromProfile(ConnectionProfile profile)
    {
        _formId = profile.Id;
        _formName = profile.Name;
        _formEngine = profile.Engine;
        _formHost = profile.Settings.Host ?? "localhost";
        _formPort = profile.Settings.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        _formDatabase = profile.Settings.Database;
        _formUsername = profile.Settings.User ?? string.Empty;
        _formPassword = string.Empty; // Senhas em texto puro NUNCA são expostas na ViewModel após carregar (docs/23 §3)
        _formPromptPassword = profile.PromptPassword;
        _formTimeoutSec = profile.Settings.ConnectTimeoutSec.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _formEnvironment = profile.Environment;
        _formIsReadOnly = profile.IsReadOnly;
        _formNotes = profile.Notes;
        _formCharset = profile.Settings.Charset ?? "UTF8";

        if (profile.Settings.Extra != null && profile.Settings.Extra.TryGetValue("sslMode", out var ssl))
        {
            _formSslMode = ssl;
        }

        OnPropertyChanged(nameof(FormName));
        OnPropertyChanged(nameof(FormEngine));
        OnPropertyChanged(nameof(FormHost));
        OnPropertyChanged(nameof(FormPort));
        OnPropertyChanged(nameof(FormDatabase));
        OnPropertyChanged(nameof(FormUsername));
        OnPropertyChanged(nameof(FormPassword));
        OnPropertyChanged(nameof(FormPromptPassword));
        OnPropertyChanged(nameof(FormTimeoutSec));
        OnPropertyChanged(nameof(FormEnvironment));
        OnPropertyChanged(nameof(FormIsReadOnly));
        OnPropertyChanged(nameof(FormNotes));
        OnPropertyChanged(nameof(FormCharset));
        OnPropertyChanged(nameof(FormSslMode));
        OnPropertyChanged(nameof(AvailableSslModes));
        OnPropertyChanged(nameof(HasSslMode));
        OnPropertyChanged(nameof(RequiresDatabase));
        OnPropertyChanged(nameof(RequiresHostPort));
        OnPropertyChanged(nameof(RequiresCredentials));
        OnPropertyChanged(nameof(IsSqlite));
        OnPropertyChanged(nameof(IsNetworkDatabase));
        OnPropertyChanged(nameof(CurrentProviderDisplayName));
        OnPropertyChanged(nameof(EngineLogo));

        InvalidateTestState();
    }

    public async Task SaveConnectionAsync()
    {
        var profile = BuildProfileFromForm();
        var passwordParam = string.IsNullOrEmpty(FormPassword) ? null : FormPassword;

        var result = await _connectionService.SaveAsync(profile, passwordParam);
        if (!result.IsSuccess)
        {
            TestState = ConnectionTestState.Failed;
            TestResultMessage = result.Error!.Message;
            return;
        }

        AddActivity($"Perfil \"{profile.Name}\" salvo", "Configurações e credenciais atualizadas.", HexBrush("#2563EB"));

        await LoadConnectionsAsync();

        // Selecionar o item salvo
        SelectedConnection = SavedConnections.FirstOrDefault(c => c.Id == profile.Id);
    }

    public async Task DeleteConnectionAsync()
    {
        if (_formId == Guid.Empty)
            return;

        var profileName = FormName;
        var result = await _connectionService.DeleteAsync(_formId);
        if (result.IsSuccess)
        {
            AddActivity($"Perfil \"{profileName}\" excluído", "Perfil e segredo associado removidos.", HexBrush("#DC2626"));
            await LoadConnectionsAsync();
        }
        else
        {
            TestState = ConnectionTestState.Failed;
            TestResultMessage = result.Error!.Message;
        }
    }

    public async Task TestConnectionAsync()
    {
        var profile = BuildProfileFromForm();
        var passwordParam = string.IsNullOrEmpty(FormPassword) ? null : FormPassword;

        TestState = ConnectionTestState.Testing;
        TestResultMessage = "Conectando ao banco de dados...";
        ServerVersionText = string.Empty;
        TestLatency = string.Empty;

        _activeTestCts?.Cancel();
        _activeTestCts = new CancellationTokenSource();

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _connectionService.TestAsync(profile, passwordParam, _activeTestCts.Token);
            sw.Stop();

            var latencyMs = sw.ElapsedMilliseconds;
            TestLatency = $"{latencyMs} ms";

            if (result.IsSuccess)
            {
                TestState = ConnectionTestState.Success;
                var serverInfo = result.Value!;
                ServerVersionText = $"{serverInfo.Product} {serverInfo.Version}";
                TestResultMessage = $"Conectado com sucesso ao banco \"{serverInfo.DatabaseName}\" em {latencyMs} ms.";

                if (SelectedConnection != null)
                {
                    SelectedConnection.Online = true;
                    SelectedConnection.LastChecked = "Agora";
                }

                AddTestRecord("Success", HexBrush("#15803D"), HexBrush("#DCFCE7"), $"{latencyMs} ms", "Conexão bem-sucedida");
                AddActivity($"Conexão testada com sucesso (\"{profile.Name}\")", $"Latência: {latencyMs} ms • {serverInfo.Product}", HexBrush("#16A34A"));
                UpdateHeaderIndicators();

                await LoadDatabasesAsync(profile, passwordParam);
            }
            else
            {
                TestState = ConnectionTestState.Failed;
                TestResultMessage = result.Error!.Message;

                if (SelectedConnection != null)
                {
                    SelectedConnection.Online = false;
                    SelectedConnection.LastChecked = "Agora";
                }

                AddTestRecord("Failed", HexBrush("#B91C1C"), HexBrush("#FEE2E2"), "-", result.Error!.Message);
                AddActivity($"Falha ao conectar em \"{profile.Name}\"", result.Error!.Message, HexBrush("#DC2626"));
                UpdateHeaderIndicators();
            }
        }
        catch (OperationCanceledException)
        {
            TestState = ConnectionTestState.Cancelled;
            TestResultMessage = "Teste de conexão cancelado.";
        }
        catch (Exception ex)
        {
            TestState = ConnectionTestState.Failed;
            TestResultMessage = ex.Message;
        }
    }

    // Depois de conectar, lista os bancos visíveis ao usuário para que ele escolha um (quando o provedor permite).
    private async Task LoadDatabasesAsync(ConnectionProfile profile, string? password)
    {
        AvailableDatabases.Clear();
        if (RequiresDatabase)
            return;

        var result = await _connectionService.ListDatabasesAsync(profile, password);
        if (!result.IsSuccess)
        {
            TestResultMessage += $" Não foi possível listar os bancos: {result.Error!.Message}";
            return;
        }

        foreach (var name in result.Value!)
        {
            AvailableDatabases.Add(name);
        }

        if (AvailableDatabases.Count > 0 && string.IsNullOrWhiteSpace(FormDatabase))
        {
            TestResultMessage += $" {AvailableDatabases.Count} banco(s) disponível(is): selecione um em Database.";
        }
    }

    public void CancelTest()
    {
        _activeTestCts?.Cancel();
    }

    public void BrowseSqliteFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar arquivo de banco de dados SQLite",
            Filter = "Bancos SQLite (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|Todos os arquivos (*.*)|*.*",
            CheckFileExists = false,
        };

        if (dlg.ShowDialog() == true)
        {
            FormDatabase = dlg.FileName;
        }
    }

    public async Task OpenInExplorerAsync()
    {
        if (_formId == Guid.Empty)
            return;

        // O Explorer abre o perfil salvo (com a senha protegida): salva antes se for novo ou se houver senha digitada.
        if (SavedConnections.All(c => c.Id != _formId) || !string.IsNullOrEmpty(FormPassword))
        {
            await SaveConnectionAsync();
            if (TestState == ConnectionTestState.Failed)
                return;
        }

        TestState = ConnectionTestState.Testing;
        TestResultMessage = "Abrindo conexão para o Explorer...";

        var result = await _connectionService.OpenAsync(_formId, ConnectionPurpose.Explore);
        if (result.IsSuccess)
        {
            _session.Set(result.Value!);
            TestState = ConnectionTestState.Success;
            TestResultMessage = $"Conexão aberta para o Explorer ({result.Value!.Server.Product} {result.Value.Server.Version}).";
            _navigator.NavigateTo("explorer");
        }
        else
        {
            TestState = ConnectionTestState.Failed;
            TestResultMessage = result.Error!.Message;
        }
    }

    public void InvalidateTestState()
    {
        // Se os parâmetros foram alterados, o teste anterior não é mais válido (Prompt §9)
        if (_testState is ConnectionTestState.Success or ConnectionTestState.Failed)
        {
            _testState = ConnectionTestState.Untested;
            _testResultMessage = string.Empty;
            _testLatency = string.Empty;
            _serverVersionText = string.Empty;
            OnPropertyChanged(nameof(TestState));
            OnPropertyChanged(nameof(IsTesting));
            OnPropertyChanged(nameof(TestStatusText));
            OnPropertyChanged(nameof(TestStatusColor));
            OnPropertyChanged(nameof(TestResultMessage));
            OnPropertyChanged(nameof(TestLatency));
            OnPropertyChanged(nameof(ServerVersionText));
        }
    }

    private ConnectionProfile BuildProfileFromForm()
    {
        int? port = int.TryParse(FormPort, out var p) ? p : null;
        int timeout = int.TryParse(FormTimeoutSec, out var t) && t > 0 ? t : 15;

        var extra = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(FormSslMode))
        {
            extra["sslMode"] = FormSslMode;
        }
        if (!string.IsNullOrEmpty(FormCharset))
        {
            extra["charset"] = FormCharset;
        }

        var settings = new ConnectionSettings(
            Host: RequiresHostPort ? FormHost : null,
            Port: RequiresHostPort ? port : null,
            Database: FormDatabase,
            User: RequiresCredentials ? FormUsername : null,
            UseSsl: FormSslMode.StartsWith("Require", StringComparison.OrdinalIgnoreCase),
            Charset: FormCharset,
            ConnectTimeoutSec: timeout,
            CommandTimeoutSec: 0,
            Extra: extra);

        return new ConnectionProfile(
            Id: _formId == Guid.Empty ? Guid.NewGuid() : _formId,
            Name: FormName,
            Engine: FormEngine,
            Settings: settings,
            Environment: FormEnvironment,
            ColorHex: null,
            IsReadOnly: FormIsReadOnly,
            PromptPassword: FormPromptPassword,
            Notes: FormNotes);
    }

    private void UpdateHeaderIndicators()
    {
        TotalConnectionsCount = SavedConnections.Count;
        OnlineConnectionsCount = SavedConnections.Count(c => c.Online);
        OfflineConnectionsCount = SavedConnections.Count(c => !c.Online);

        var distinctEnvs = SavedConnections.Select(c => c.Environment).Distinct().ToList();
        EnvironmentSummaryText = distinctEnvs.Count > 0 ? string.Join(", ", distinctEnvs) : "Unknown";
    }

    private void AddTestRecord(string status, Brush statusFg, Brush statusBg, string latency, string message)
    {
        var nowText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        RecentConnectionTests.Insert(0, new ConnectionTest(nowText, status, statusFg, statusBg, latency, message));
        while (RecentConnectionTests.Count > 10)
        {
            RecentConnectionTests.RemoveAt(RecentConnectionTests.Count - 1);
        }
    }

    private void AddActivity(string title, string detail, Brush dot)
    {
        ConnectionActivities.Insert(0, new ConnectionActivity(title, detail, "Agora", dot));
        while (ConnectionActivities.Count > 10)
        {
            ConnectionActivities.RemoveAt(ConnectionActivities.Count - 1);
        }
    }

    private static BitmapImage? LoadLogo(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            var uri = new Uri($"pack://application:,,,/Resources/Logos/{name}.png", UriKind.Absolute);
            var image = new BitmapImage(uri);
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush HexBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        _activeTestCts?.Cancel();
        _activeTestCts?.Dispose();
        _activeTestCts = null;
    }
}
