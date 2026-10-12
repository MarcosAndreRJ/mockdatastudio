using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Sqlite;

// Provider SQLite (docs/10 §3, TASK-02.02). Senha não se aplica a SQLite e é ignorada.
public sealed class SqliteProvider : IDatabaseProvider
{
    // Conexões abertas por este provider guardam o armazenamento de datas do perfil, para o bulk e o inspector.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbConnection, string> _dateStorage = new();

    private readonly SqliteTypeMapper _typeMapper = new();
    private readonly SqliteErrorTranslator _errorTranslator = new();

    public DatabaseEngine Engine => DatabaseEngine.Sqlite;

    public string DisplayName => "SQLite";

    public bool RequiresHostPort => false;
    public string? LogoResourceName => "sqlite";

    public IReadOnlyList<ConnectionSettingDefinition> ExtraSettings { get; } =
    [
        new ConnectionSettingDefinition("foreignKeys", "Verificar chaves estrangeiras", "bool", "true", null),
        new ConnectionSettingDefinition("mode", "Modo de abertura", "enum", "ReadWrite", ["ReadWrite", "ReadOnly"]),
        new ConnectionSettingDefinition("dateStorage", "Armazenamento de datas", "enum", SqliteValues.DateStorageText,
            [SqliteValues.DateStorageText, SqliteValues.DateStorageUnix, SqliteValues.DateStorageJulian]),
    ];

    public ITypeMapper TypeMapper => _typeMapper;

    public IErrorTranslator ErrorTranslator => _errorTranslator;

    public ProviderCapabilities GetCapabilities(string serverVersion)
    {
        var returning = Version.TryParse(serverVersion, out var v) && v >= new Version(3, 35);
        return new ProviderCapabilities(
            SupportsNamespaces: false,
            SupportsReturning: returning,
            SupportsMultiRowValues: true,
            SupportsDeferrableConstraints: true,
            SupportsDisableFkChecksPerSession: true,
            SupportsIdentityOverride: true,
            SupportsSequences: false,
            SupportsTransactionalDdl: true,
            MaxParametersPerCommand: 32766,
            MaxIdentifierLength: int.MaxValue);
    }

    public string BuildConnectionString(ConnectionSettings settings, string? password)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = settings.Database,
            Mode = ParseMode(settings.Extra),
            ForeignKeys = ParseBool(settings.Extra, "foreignKeys", defaultValue: true),
        };
        return builder.ConnectionString;
    }

    public async Task<DbConnection> OpenConnectionAsync(ConnectionSettings settings, string? password, CancellationToken ct)
    {
        var isMemory = string.Equals(settings.Database, ":memory:", StringComparison.OrdinalIgnoreCase);
        var createNew = ParseBool(settings.Extra, "createNew", defaultValue: false);
        if (!isMemory && !createNew && !File.Exists(settings.Database))
        {
            throw new FileNotFoundException($"Arquivo SQLite não encontrado: {settings.Database}", settings.Database);
        }

        var connection = new SqliteConnection(BuildConnectionString(settings, password));
        await connection.OpenAsync(ct);
        _dateStorage.AddOrUpdate(connection, ParseDateStorage(settings.Extra));
        return connection;
    }

    public Task<bool> ApplySessionReadOnlyAsync(DbConnection connection, CancellationToken ct)
    {
        // SQLite não possui comando 'SET SESSION READ ONLY'; a garantia exige abrir a conexão com Mode=ReadOnly (C02, docs/20 §8).
        return Task.FromResult(false);
    }

    public async Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct)
    {
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT sqlite_version()";
        var versionText = Convert.ToString(await version.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        await using var list = connection.CreateCommand();
        list.CommandText = "PRAGMA database_list";
        await using var reader = await list.ExecuteReaderAsync(ct);
        var file = string.Empty;
        while (await reader.ReadAsync(ct))
        {
            if (string.Equals(reader.GetString(1), "main", StringComparison.Ordinal))
            {
                file = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            }
        }

        var fullPath = string.IsNullOrEmpty(file) ? string.Empty : Path.GetFullPath(file).ToLowerInvariant();
        var fingerprint = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("sqlite|" + fullPath))).ToLowerInvariant();
        return new ServerInfo(versionText, "SQLite", Path.GetFileName(file), Host: null, FingerprintHash: fingerprint);
    }

    public ISchemaExtractor CreateSchemaExtractor() => new SqliteSchemaExtractor(_typeMapper);

    // Dialeto para um servidor. Armazenamento de datas padrão (text); use CreateDialect para outro perfil.
    public ISqlDialect GetDialect(string serverVersion) => new SqliteDialect(SqliteValues.DateStorageText);

    public static ISqlDialect CreateDialect(string dateStorage) => new SqliteDialect(dateStorage);

    public IBulkInserter CreateBulkInserter(DbConnection connection, string serverVersion) =>
        new SqliteBulkInserter(connection, DateStorageOf(connection));

    public ITargetInspector CreateTargetInspector(DbConnection connection, string serverVersion) =>
        new SqliteTargetInspector(connection);

    private string DateStorageOf(DbConnection connection) =>
        _dateStorage.TryGetValue(connection, out var value) ? value : SqliteValues.DateStorageText;

    private static SqliteOpenMode ParseMode(IReadOnlyDictionary<string, string>? extra) =>
        extra is not null && extra.TryGetValue("mode", out var mode) && string.Equals(mode, "ReadOnly", StringComparison.OrdinalIgnoreCase)
            ? SqliteOpenMode.ReadOnly
            : SqliteOpenMode.ReadWrite;

    private static bool ParseBool(IReadOnlyDictionary<string, string>? extra, string key, bool defaultValue) =>
        extra is not null && extra.TryGetValue(key, out var text) && bool.TryParse(text, out var value) ? value : defaultValue;

    private static string ParseDateStorage(IReadOnlyDictionary<string, string>? extra)
    {
        if (extra is not null && extra.TryGetValue("dateStorage", out var text)
            && text is SqliteValues.DateStorageText or SqliteValues.DateStorageUnix or SqliteValues.DateStorageJulian)
        {
            return text;
        }

        return SqliteValues.DateStorageText;
    }
}

// Módulo de registro do provider SQLite no container de DI (docs/31 §2).
public sealed class SqliteModule : IMdsModule
{
    public string Id => "mds.providers.sqlite";

    public int ContractVersion => MdsContracts.Version;

    public void Register(IServiceCollection services) =>
        services.AddSingleton<IDatabaseProvider, SqliteProvider>();
}
