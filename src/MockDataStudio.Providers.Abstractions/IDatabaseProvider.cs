using System.Data.Common;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

/// <summary>
/// Ponto de entrada de um SGBD (C02). Cada assembly MockDataStudio.Providers.* implementa esta interface.
/// </summary>
public interface IDatabaseProvider
{
    DatabaseEngine Engine { get; }
    string DisplayName { get; }
    /// <summary>
    /// Indica se o provedor opera sobre rede (host/porta) ou arquivo local (SQLite).
    /// </summary>
    bool RequiresHostPort => true;
    string? LogoResourceName => null;
    IReadOnlyList<ConnectionSettingDefinition> ExtraSettings { get; }
    ProviderCapabilities GetCapabilities(string serverVersion);
    string BuildConnectionString(ConnectionSettings settings, string? password);
    Task<DbConnection> OpenConnectionAsync(ConnectionSettings settings, string? password, CancellationToken ct);
    Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct);
    ISchemaExtractor CreateSchemaExtractor();
    ISqlDialect GetDialect(string serverVersion);
    ITypeMapper TypeMapper { get; }
    IBulkInserter CreateBulkInserter(DbConnection connection, string serverVersion);
    ITargetInspector CreateTargetInspector(DbConnection connection, string serverVersion);
    IErrorTranslator ErrorTranslator { get; }

    /// <summary>
    /// Coloca a sessão em modo somente leitura no servidor (20 §8, 23 §5, C02).
    /// Retorna true se a restrição no servidor foi aplicada com sucesso, ou false se não suportado em nível de sessão.
    /// </summary>
    Task<bool> ApplySessionReadOnlyAsync(DbConnection connection, CancellationToken ct) => Task.FromResult(false);
}
