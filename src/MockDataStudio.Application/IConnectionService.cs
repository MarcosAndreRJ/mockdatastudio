using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Application;

/// <summary>
/// Serviço de aplicação para gerenciamento de perfis, conectividade e credenciais (C09).
/// </summary>
public interface IConnectionService
{
    Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken ct = default);
    Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Senha salva (DPAPI) do perfil, para exibir e editar no formulário de conexões (DA-310). Null quando não há senha
    /// salva ou o perfil pede a senha a cada uso. Falha SEC-NO-AUTHORIZATION se a credencial foi gravada por outro usuário/máquina.
    /// </summary>
    Task<Result<string?>> GetStoredPasswordAsync(Guid id, CancellationToken ct = default);

    Task<Result<Guid>> SaveAsync(ConnectionProfile profile, string? newPassword, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<Result<ServerInfo>> TestAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct = default);

    /// <summary>Conecta com o perfil (banco opcional quando o provedor permite) e lista os bancos visíveis ao usuário.</summary>
    Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct = default);

    Task<Result<OpenedConnection>> OpenAsync(Guid profileId, ConnectionPurpose purpose, CancellationToken ct = default);

    /// <summary>
    /// Abre o perfil num database específico do mesmo servidor (ex.: aba do editor SQL com outro database). Null ou
    /// vazio usa o database salvo no perfil. O perfil salvo não é alterado.
    /// </summary>
    Task<Result<OpenedConnection>> OpenAsync(Guid profileId, ConnectionPurpose purpose, string? database, CancellationToken ct = default);
}
