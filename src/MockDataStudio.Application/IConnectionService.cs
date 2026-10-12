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

    Task<Result<Guid>> SaveAsync(ConnectionProfile profile, string? newPassword, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<Result<ServerInfo>> TestAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct = default);

    Task<Result<OpenedConnection>> OpenAsync(Guid profileId, ConnectionPurpose purpose, CancellationToken ct = default);
}
