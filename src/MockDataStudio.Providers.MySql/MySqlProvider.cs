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

    public IReadOnlyList<ConnectionSettingDefinition> ExtraSettings { get; } =
    [
        new ConnectionSettingDefinition("sslMode", "Modo SSL", "enum", "Preferred",
            ["None", "Preferred", "Required", "VerifyCA", "VerifyFull"]),
        new ConnectionSettingDefinition("tinyint1IsBool", "Tratar TINYINT(1) como Boolean", "bool", "true", null),
    ];

    public IErrorTranslator ErrorTranslator => _errorTranslator;

    public ITypeMapper TypeMapper => throw new NotImplementedException("Disponível a partir do módulo de introspecção / tipos.");

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

        await using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "SELECT @@version, coalesce(@@server_uuid, ''), DATABASE();";

        string versionText = string.Empty;
        string serverUuid = string.Empty;
        string dbName = connection.Database;

        try
        {
            await using var reader = await versionCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                versionText = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                serverUuid = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                dbName = reader.IsDBNull(2) ? connection.Database : reader.GetString(2);
            }
        }
        catch
        {
            versionText = "MySQL";
        }

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

        return new ServerInfo(versionText, "MySQL", dbName ?? string.Empty, host, fingerprint);
    }

    public ISchemaExtractor CreateSchemaExtractor() =>
        throw new NotImplementedException("Disponível a partir do módulo de introspecção (Sprint B3).");

    public ISqlDialect GetDialect(string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de dialetos (Sprint B3).");

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
