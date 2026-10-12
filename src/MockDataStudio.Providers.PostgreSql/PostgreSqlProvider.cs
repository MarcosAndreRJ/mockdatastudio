using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.PostgreSql;

public sealed class PostgreSqlProvider : IDatabaseProvider
{
    private readonly PostgreSqlErrorTranslator _errorTranslator = new();

    public DatabaseEngine Engine => DatabaseEngine.PostgreSql;

    public string DisplayName => "PostgreSQL";
    public string? LogoResourceName => "postgresql";

    public IReadOnlyList<ConnectionSettingDefinition> ExtraSettings { get; } =
    [
        new ConnectionSettingDefinition("sslMode", "Modo SSL", "enum", "Prefer",
            ["Disable", "Allow", "Prefer", "Require", "VerifyCA", "VerifyFull"]),
        new ConnectionSettingDefinition("searchPath", "Search Path", "string", null, null),
    ];

    public IErrorTranslator ErrorTranslator => _errorTranslator;

    public ITypeMapper TypeMapper => throw new NotImplementedException("Disponível a partir do módulo de introspecção / tipos.");

    public ProviderCapabilities GetCapabilities(string serverVersion) =>
        new(
            SupportsNamespaces: true,
            SupportsReturning: true,
            SupportsMultiRowValues: true,
            SupportsDeferrableConstraints: true,
            SupportsDisableFkChecksPerSession: false,
            SupportsIdentityOverride: true,
            SupportsSequences: true,
            SupportsTransactionalDdl: true,
            MaxParametersPerCommand: 65535,
            MaxIdentifierLength: 63);

    public string BuildConnectionString(ConnectionSettings settings, string? password)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(settings.Host) ? "localhost" : settings.Host,
            Port = settings.Port ?? 5432,
            Database = settings.Database,
            Username = settings.User,
            Timeout = settings.ConnectTimeoutSec > 0 ? settings.ConnectTimeoutSec : 15,
            CommandTimeout = settings.CommandTimeoutSec,
            ApplicationName = "MockDataStudio",
        };

        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        if (settings.Extra != null && settings.Extra.TryGetValue("sslMode", out var sslModeStr) &&
            Enum.TryParse<SslMode>(sslModeStr, ignoreCase: true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }
        else if (settings.UseSsl)
        {
            builder.SslMode = SslMode.Require;
        }
        else
        {
            builder.SslMode = SslMode.Prefer;
        }

        if (settings.Extra != null && settings.Extra.TryGetValue("searchPath", out var searchPath) && !string.IsNullOrWhiteSpace(searchPath))
        {
            builder.SearchPath = searchPath;
        }

        return builder.ConnectionString;
    }

    public async Task<DbConnection> OpenConnectionAsync(ConnectionSettings settings, string? password, CancellationToken ct)
    {
        var connectionString = BuildConnectionString(settings, password);
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<bool> ApplySessionReadOnlyAsync(DbConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;";
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "SELECT version();";
        var versionText = Convert.ToString(await versionCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) ?? string.Empty;

        await using var idCmd = connection.CreateCommand();
        idCmd.CommandText = "SELECT current_database(), coalesce(cast(inet_server_addr() as text), 'localhost'), inet_server_port();";
        string dbName = connection.Database;
        string host = "localhost";
        int port = 5432;

        try
        {
            await using var reader = await idCmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                dbName = reader.GetString(0);
                host = reader.IsDBNull(1) ? "localhost" : reader.GetString(1);
                port = reader.IsDBNull(2) ? 5432 : reader.GetInt32(2);
            }
        }
        catch
        {
            // Fallback usando propriedades de conexão
            dbName = string.IsNullOrEmpty(connection.Database) ? "postgres" : connection.Database;
        }

        var normalizedHost = host.ToLowerInvariant();
        var normalizedDb = dbName.ToLowerInvariant();
        var rawIdentity = $"postgresql|{normalizedHost}|{port}|{normalizedDb}|{versionText}";
        var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity))).ToLowerInvariant();

        return new ServerInfo(versionText, "PostgreSQL", dbName, normalizedHost, fingerprint);
    }

    public ISchemaExtractor CreateSchemaExtractor() =>
        throw new NotImplementedException("Disponível a partir do módulo de introspecção (Sprint B2).");

    public ISqlDialect GetDialect(string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de dialetos (Sprint B2).");

    public IBulkInserter CreateBulkInserter(DbConnection connection, string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de execução direta (Sprint B2).");

    public ITargetInspector CreateTargetInspector(DbConnection connection, string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de inspeção de destino (Sprint B2).");
}

public sealed class PostgreSqlModule : IMdsModule
{
    public string Id => "mds.providers.postgresql";

    public int ContractVersion => MdsContracts.Version;

    public void Register(IServiceCollection services) =>
        services.AddSingleton<IDatabaseProvider, PostgreSqlProvider>();
}
