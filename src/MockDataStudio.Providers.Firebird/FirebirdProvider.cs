using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using FirebirdSql.Data.FirebirdClient;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Firebird;

public sealed class FirebirdProvider : IDatabaseProvider
{
    private readonly FirebirdErrorTranslator _errorTranslator = new();

    public DatabaseEngine Engine => DatabaseEngine.Firebird;

    public string DisplayName => "Firebird";
    public int? DefaultPort => 3050;
    public string? DefaultUser => "SYSDBA";
    public string? LogoResourceName => "firebird";

    public IReadOnlyList<ConnectionSettingDefinition> ExtraSettings { get; } =
    [
        new ConnectionSettingDefinition("role", "SQL Role", "string", null, null),
        new ConnectionSettingDefinition("serverType", "Tipo de Servidor", "enum", "Default", ["Default", "Embedded"]),
        new ConnectionSettingDefinition("charset", "Conjunto de Caracteres (Charset)", "string", "UTF8", null),
    ];

    public IErrorTranslator ErrorTranslator => _errorTranslator;

    private readonly FirebirdTypeMapper _typeMapper = new();

    public ITypeMapper TypeMapper => _typeMapper;

    public ProviderCapabilities GetCapabilities(string serverVersion) =>
        new(
            SupportsNamespaces: false, // Firebird não possui schemas/namespaces separados (usa virtual)
            SupportsReturning: true,
            SupportsMultiRowValues: false, // Firebird não suporta INSERT ... VALUES (...), (...)
            SupportsDeferrableConstraints: false,
            SupportsDisableFkChecksPerSession: false,
            SupportsIdentityOverride: true,
            SupportsSequences: true, // generators
            SupportsTransactionalDdl: true,
            MaxParametersPerCommand: 1476,
            MaxIdentifierLength: 63);

    public string BuildConnectionString(ConnectionSettings settings, string? password)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = new FbConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(settings.Host) ? "localhost" : settings.Host,
            Port = settings.Port ?? 3050,
            Database = settings.Database,
            UserID = string.IsNullOrWhiteSpace(settings.User) ? "SYSDBA" : settings.User,
            Charset = string.IsNullOrWhiteSpace(settings.Charset) ? "UTF8" : settings.Charset,
            ConnectionTimeout = settings.ConnectTimeoutSec > 0 ? settings.ConnectTimeoutSec : 15,
            Dialect = 3,
        };

        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        if (settings.Extra != null)
        {
            if (settings.Extra.TryGetValue("role", out var role) && !string.IsNullOrWhiteSpace(role))
            {
                builder.Role = role;
            }

            if (settings.Extra.TryGetValue("serverType", out var serverTypeStr) &&
                Enum.TryParse<FbServerType>(serverTypeStr, ignoreCase: true, out var serverType))
            {
                builder.ServerType = serverType;
            }

            if (settings.Extra.TryGetValue("charset", out var charsetOverride) && !string.IsNullOrWhiteSpace(charsetOverride))
            {
                builder.Charset = charsetOverride;
            }
        }

        return builder.ConnectionString;
    }

    public async Task<DbConnection> OpenConnectionAsync(ConnectionSettings settings, string? password, CancellationToken ct)
    {
        var connectionString = BuildConnectionString(settings, password);
        var connection = new FbConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public Task<bool> ApplySessionReadOnlyAsync(DbConnection connection, CancellationToken ct)
    {
        // Firebird gerencia somente leitura por transação (isc_tpb_read), e não por comando de sessão global (C02, docs/10 §6).
        return Task.FromResult(false);
    }

    public async Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        string versionText = string.Empty;
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT rdb$get_context('SYSTEM','ENGINE_VERSION') FROM rdb$database;";
            versionText = Convert.ToString(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch
        {
            versionText = connection.ServerVersion ?? "Firebird";
        }

        var host = (connection.DataSource ?? "localhost").ToLowerInvariant();
        var port = 3050;
        var normalizedDb = (connection.Database ?? string.Empty).ToLowerInvariant();

        var rawIdentity = $"firebird|{host}|{port}|{normalizedDb}|{versionText}";
        var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity))).ToLowerInvariant();

        return new ServerInfo(versionText, "Firebird", connection.Database ?? string.Empty, host, fingerprint);
    }

    public ISchemaExtractor CreateSchemaExtractor() => new FirebirdSchemaExtractor(_typeMapper);

    // serverVersion vem de ENGINE_VERSION ("2.5.9", "3.0.12", "5.0.1"): a versão principal decide resync e BOOLEAN.
    public ISqlDialect GetDialect(string serverVersion)
    {
        var text = serverVersion ?? string.Empty;
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var major = int.TryParse(dot > 0 ? text[..dot] : text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 3;
        return new FirebirdDialect(major);
    }

    public IBulkInserter CreateBulkInserter(DbConnection connection, string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de execução direta (Sprint B4).");

    public ITargetInspector CreateTargetInspector(DbConnection connection, string serverVersion) =>
        throw new NotImplementedException("Disponível a partir do módulo de inspeção de destino (Sprint B4).");
}

public sealed class FirebirdModule : IMdsModule
{
    public string Id => "mds.providers.firebird";

    public int ContractVersion => MdsContracts.Version;

    public void Register(IServiceCollection services) =>
        services.AddSingleton<IDatabaseProvider, FirebirdProvider>();
}
