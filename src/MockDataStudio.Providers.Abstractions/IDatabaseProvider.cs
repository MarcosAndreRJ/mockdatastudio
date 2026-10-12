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
}
