using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.MySql;

public sealed class MySqlProvider : IDatabaseProvider
{
    private readonly MySqlErrorTranslator _errorTranslator = new();

    public DatabaseEngine Engine => DatabaseEngine.MySql;

    public string DisplayName => "MySQL";
    public int? DefaultPort => 3306;
    public string? DefaultUser => "root";
    public bool RequiresDatabase => false;

    public IReadOnlyList<ConnectionSettingDefinition> ExtraSettings { get; } =
    [
        new ConnectionSettingDefinition("sslMode", "Modo SSL", "enum", "Preferred",
            ["None", "Preferred", "Required", "VerifyCA", "VerifyFull"]),
        new ConnectionSettingDefinition("tinyint1IsBool", "Tratar TINYINT(1) como Boolean", "bool", "true", null),
    ];

    public IErrorTranslator ErrorTranslator => _errorTranslator;

    // tinyint(1) → Boolean é o padrão (DA-011). A opção por perfil ainda não chega aqui: CreateSchemaExtractor não
    // recebe as configurações da conexão.
    private readonly MySqlTypeMapper _typeMapper = new();
    private readonly MySqlDialect _dialect = new();

    public ITypeMapper TypeMapper => _typeMapper;

    public ProviderCapabilities GetCapabilities(string serverVersion) =>
        new(
            SupportsNamespaces: true,
            SupportsReturning: false, // MySQL nativo não suporta INSERT ... RETURNING
            SupportsMultiRowValues: true,
            SupportsDeferrableConstraints: false,
            SupportsDisableFkChecksPerSession: true,
            SupportsIdentityOverride: true,
            SupportsSequences: false,
            SupportsTransactionalDdl: false,
            MaxParametersPerCommand: 65535,
            MaxIdentifierLength: 64);

    public string BuildConnectionString(ConnectionSettings settings, string? password)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(settings.Host) ? "localhost" : settings.Host,
            Port = (uint)(settings.Port ?? 3306),
            Database = settings.Database,
            UserID = settings.User,
            ConnectionTimeout = (uint)(settings.ConnectTimeoutSec > 0 ? settings.ConnectTimeoutSec : 15),
            DefaultCommandTimeout = (uint)settings.CommandTimeoutSec,
            CharacterSet = "utf8mb4",
            AllowLoadLocalInfile = false, // SEGURANÇA: Proibido upload local sem autorização explícita (docs/10 §4, Prompt §5.2)
            AllowUserVariables = false,
            ConvertZeroDateTime = true,
        };

        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        if (settings.Extra != null && settings.Extra.TryGetValue("sslMode", out var sslModeStr) &&
            Enum.TryParse<MySqlSslMode>(sslModeStr, ignoreCase: true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }
        else if (settings.UseSsl)
        {
            builder.SslMode = MySqlSslMode.Required;
        }
        else
        {
            builder.SslMode = MySqlSslMode.Preferred;
        }

        // O builder já emite AllowLoadLocalInfile=False, AllowUserVariables=False e character set=utf8mb4 (docs/10 §4).
        return builder.ConnectionString;
    }

    public async Task<DbConnection> OpenConnectionAsync(ConnectionSettings settings, string? password, CancellationToken ct)
    {
        var connectionString = BuildConnectionString(settings, password);
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }


    public async Task<IReadOnlyList<string>> ListDatabasesAsync(DbConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_name FROM information_schema.schemata " +
            "WHERE schema_name NOT IN ('information_schema','mysql','performance_schema','sys') ORDER BY schema_name;";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
    public async Task<bool> ApplySessionReadOnlyAsync(DbConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION TRANSACTION READ ONLY;";
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        string versionText = string.Empty;
        string serverUuid = string.Empty;
        string dbName = connection.Database;

        // Versão e database primeiro; @@server_uuid à parte porque não existe no MariaDB (uma consulta única falhava
        // inteira e a versão virava só "MySQL").
        await using (var versionCmd = connection.CreateCommand())
        {
            versionCmd.CommandText = "SELECT VERSION(), DATABASE();";
            await using var reader = await versionCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                versionText = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                dbName = reader.IsDBNull(1) ? connection.Database : reader.GetString(1);
            }
        }

        try
        {
            await using var uuidCmd = connection.CreateCommand();
            uuidCmd.CommandText = "SELECT @@server_uuid;";
            serverUuid = Convert.ToString(await uuidCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch (MySqlException)
        {
            serverUuid = string.Empty; // MariaDB: sem server_uuid; o fingerprint usa host, porta e database
        }

        var product = versionText.Contains("MariaDB", StringComparison.OrdinalIgnoreCase) ? "MariaDB" : "MySQL";

        var host = (connection.DataSource ?? "localhost").ToLowerInvariant();
        var port = 3306;
        var colonIdx = host.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx >= 0 && int.TryParse(host[(colonIdx + 1)..], out var parsedPort))
        {
            port = parsedPort;
            host = host[..colonIdx];
        }

        var normalizedDb = (dbName ?? string.Empty).ToLowerInvariant();
        var rawIdentity = $"mysql|{host}|{port}|{normalizedDb}|{serverUuid}";
        var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity))).ToLowerInvariant();

        return new ServerInfo(versionText, product, dbName ?? string.Empty, host, fingerprint);
    }

    public ISchemaExtractor CreateSchemaExtractor() => new MySqlSchemaExtractor(_typeMapper);

    public ISqlDialect GetDialect(string serverVersion) => _dialect;

    public IBulkInserter CreateBulkInserter(DbConnection connection, string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de execução direta (Sprint B3).");

    public ITargetInspector CreateTargetInspector(DbConnection connection, string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de inspeção de destino (Sprint B3).");
}

public sealed class MySqlModule : IMdsModule
{
    public string Id => "mds.providers.mysql";

    public int ContractVersion => MdsContracts.Version;

    public void Register(IServiceCollection services) =>
        services.AddSingleton<IDatabaseProvider, MySqlProvider>();
}
