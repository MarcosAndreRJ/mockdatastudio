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

    /// <summary>Porta padrão do SGBD (null para provedores baseados em arquivo).</summary>
    int? DefaultPort => null;

    /// <summary>Usuário administrativo padrão sugerido no formulário de conexão.</summary>
    string? DefaultUser => null;

    /// <summary>
    /// Indica se o campo database é obrigatório para conectar. Falso quando o servidor aceita conexão sem banco
    /// e permite listar os bancos disponíveis (<see cref="ListDatabasesAsync"/>).
    /// </summary>
    bool RequiresDatabase => true;

    /// <summary>Lista os bancos visíveis ao usuário na conexão aberta. Vazio quando o SGBD não suporta.</summary>
    Task<IReadOnlyList<string>> ListDatabasesAsync(DbConnection connection, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>([]);
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
