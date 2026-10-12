using MockDataStudio.Core.Connections;

namespace MockDataStudio.Application;

public sealed record ConnectionProfileRecord(
    ConnectionProfile Profile,
    byte[]? ProtectedSecret,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Contrato de persistência de perfis de conexão e seus segredos cifrados.
/// </summary>
public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfileRecord>> ListAsync(CancellationToken ct = default);

    Task<ConnectionProfileRecord?> GetAsync(Guid id, CancellationToken ct = default);

    Task SaveAsync(ConnectionProfile profile, byte[]? protectedSecret, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task UpdateLastUsedAsync(Guid id, DateTimeOffset lastUsedAt, CancellationToken ct = default);
}
