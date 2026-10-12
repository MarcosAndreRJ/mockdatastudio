using System.Data.Common;
using System.Security.Cryptography;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Application;

public sealed class ConnectionService : IConnectionService
{
    private readonly IConnectionProfileStore _store;
    private readonly ISecretStore _secretStore;
    private readonly IProviderRegistry _providerRegistry;

    public ConnectionService(
        IConnectionProfileStore store,
        ISecretStore secretStore,
        IProviderRegistry providerRegistry)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
    }

    public async Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken ct = default)
    {
        var records = await _store.ListAsync(ct);
        return records.Select(r => r.Profile).ToList();
    }

    public async Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var record = await _store.GetAsync(id, ct);
        return record?.Profile;
    }

    public async Task<Result<Guid>> SaveAsync(ConnectionProfile profile, string? newPassword, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var validationResult = await ValidateProfileAsync(profile, ct);
        if (!validationResult.IsSuccess)
        {
            return Result<Guid>.Fail(validationResult.Error!.Code, validationResult.Error.Message);
        }

        byte[]? protectedSecret = null;

        if (profile.PromptPassword)
        {
            // Perfil "Sempre perguntar": senha nunca é persistida (docs/23 §3, Prompt §5.1)
            protectedSecret = null;
        }
        else if (newPassword != null)
        {
            // Nova senha informada: criptografar com DPAPI CurrentUser
            protectedSecret = _secretStore.Protect(newPassword);
        }
        else
        {
            // Manter credencial protegida existente se estiver editando o perfil
            var existing = await _store.GetAsync(profile.Id, ct);
            if (existing != null)
            {
                protectedSecret = existing.ProtectedSecret;
            }
        }

        await _store.SaveAsync(profile, protectedSecret, ct);
        return Result<Guid>.Ok(profile.Id);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var deleted = await _store.DeleteAsync(id, ct);
        if (deleted)
        {
            return Result<bool>.Ok(true);
        }

        return Result<bool>.Fail(ErrorCodes.PrvConnFailed, "Perfil de conexão não encontrado para exclusão.");
    }

    public async Task<Result<ServerInfo>> TestAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        IDatabaseProvider provider;
        try
        {
            provider = _providerRegistry.Get(profile.Engine);
        }
        catch (Exception ex)
        {
            return Result<ServerInfo>.Fail(ErrorCodes.PrvUnsupportedVersion, ex.Message);
        }

        // Recuperar senha: se fornecida explicitamente (override) usa ela, senão tenta ler do store
        string? password = passwordOverride;
        if (password == null && !profile.PromptPassword)
        {
            try
            {
                var record = await _store.GetAsync(profile.Id, ct);
                if (record?.ProtectedSecret != null)
                {
                    password = _secretStore.Unprotect(record.ProtectedSecret);
                }
            }
            catch (CryptographicException)
            {
                return Result<ServerInfo>.Fail(ErrorCodes.SecNoAuthorization, "Não foi possível recuperar a credencial protegida por DPAPI nesta máquina.");
            }
        }

        var timeoutSec = profile.Settings.ConnectTimeoutSec > 0 ? profile.Settings.ConnectTimeoutSec : 15;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        DbConnection? connection = null;
        try
        {
            connection = await provider.OpenConnectionAsync(profile.Settings, password, linkedCts.Token);
            var serverInfo = await provider.GetServerInfoAsync(connection, linkedCts.Token);
            return Result<ServerInfo>.Ok(serverInfo);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Result<ServerInfo>.Fail(ErrorCodes.PrvTimeout, $"A tentativa de conexão excedeu o tempo limite configurado ({timeoutSec} segundos).");
        }
        catch (OperationCanceledException)
        {
            return Result<ServerInfo>.Fail(ErrorCodes.PrvCancelled, "Teste de conexão cancelado pelo usuário.");
        }
        catch (Exception ex)
        {
            var mdsError = provider.ErrorTranslator.Translate(ex);
            return Result<ServerInfo>.Fail(mdsError.Code, mdsError.Message);
        }
        finally
        {
            if (connection != null)
            {
                try
                {
                    await connection.DisposeAsync();
                }
                catch
                {
                    // Supressão de erros ao fechar conexão de teste
                }
            }
        }
    }

    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        IDatabaseProvider provider;
        try
        {
            provider = _providerRegistry.Get(profile.Engine);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Fail(ErrorCodes.PrvUnsupportedVersion, ex.Message);
        }

        string? password = passwordOverride;
        if (password == null && !profile.PromptPassword)
        {
            try
            {
                var record = await _store.GetAsync(profile.Id, ct);
                if (record?.ProtectedSecret != null)
                {
                    password = _secretStore.Unprotect(record.ProtectedSecret);
                }
            }
            catch (CryptographicException)
            {
                return Result<IReadOnlyList<string>>.Fail(ErrorCodes.SecNoAuthorization, "Não foi possível recuperar a credencial protegida por DPAPI nesta máquina.");
            }
        }

        var timeoutSec = profile.Settings.ConnectTimeoutSec > 0 ? profile.Settings.ConnectTimeoutSec : 15;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await using var connection = await provider.OpenConnectionAsync(profile.Settings, password, linkedCts.Token);
            var names = await provider.ListDatabasesAsync(connection, linkedCts.Token);
            return Result<IReadOnlyList<string>>.Ok(names);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Result<IReadOnlyList<string>>.Fail(ErrorCodes.PrvTimeout, $"A listagem de bancos excedeu o tempo limite ({timeoutSec} segundos).");
        }
        catch (OperationCanceledException)
        {
            return Result<IReadOnlyList<string>>.Fail(ErrorCodes.PrvCancelled, "Operação cancelada pelo usuário.");
        }
        catch (Exception ex)
        {
            var mdsError = provider.ErrorTranslator.Translate(ex);
            return Result<IReadOnlyList<string>>.Fail(mdsError.Code, mdsError.Message);
        }
    }

    public async Task<Result<OpenedConnection>> OpenAsync(Guid profileId, ConnectionPurpose purpose, CancellationToken ct = default)
    {
        var record = await _store.GetAsync(profileId, ct);
        if (record == null)
        {
            return Result<OpenedConnection>.Fail(ErrorCodes.PrvConnFailed, $"Perfil de conexão com ID '{profileId}' não encontrado.");
        }

        var profile = record.Profile;
        var provider = _providerRegistry.Get(profile.Engine);

        string? password = null;
        if (record.ProtectedSecret != null)
        {
            try
            {
                password = _secretStore.Unprotect(record.ProtectedSecret);
            }
            catch (CryptographicException)
            {
                return Result<OpenedConnection>.Fail(ErrorCodes.SecNoAuthorization, "Falha ao descriptografar a senha protegida do perfil.");
            }
        }
        else if (profile.PromptPassword)
        {
            return Result<OpenedConnection>.Fail(ErrorCodes.PrvConnFailed, "Este perfil exige que a senha seja fornecida para conectar.");
        }

        var timeoutSec = profile.Settings.ConnectTimeoutSec > 0 ? profile.Settings.ConnectTimeoutSec : 15;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        DbConnection connection;
        try
        {
            connection = await provider.OpenConnectionAsync(profile.Settings, password, linkedCts.Token);
        }
        catch (Exception ex)
        {
            var mdsError = provider.ErrorTranslator.Translate(ex);
            return Result<OpenedConnection>.Fail(mdsError.Code, mdsError.Message);
        }

        ServerInfo serverInfo;
        try
        {
            serverInfo = await provider.GetServerInfoAsync(connection, linkedCts.Token);
        }
        catch
        {
            serverInfo = new ServerInfo("Unknown", provider.DisplayName, profile.Settings.Database, profile.Settings.Host, "unknown");
        }

        var isServerReadOnly = false;
        if (profile.IsReadOnly)
        {
            try
            {
                isServerReadOnly = await provider.ApplySessionReadOnlyAsync(connection, linkedCts.Token);
            }
            catch
            {
                // Limitação registrada
            }
        }

        await _store.UpdateLastUsedAsync(profileId, DateTimeOffset.UtcNow, ct);

        var opened = new OpenedConnection(profile, connection, provider, serverInfo, isServerReadOnly);
        return Result<OpenedConnection>.Ok(opened);
    }

    private async Task<Result<bool>> ValidateProfileAsync(ConnectionProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return Result<bool>.Fail(ErrorCodes.PrvConnFailed, "O nome da conexão é obrigatório.");
        }

        var requiresDatabase = true;
        try
        {
            requiresDatabase = _providerRegistry.Get(profile.Engine).RequiresDatabase;
        }
        catch
        {
            // Provedor desconhecido: mantém a exigência conservadora do banco.
        }

        if (requiresDatabase && string.IsNullOrWhiteSpace(profile.Settings.Database))
        {
            return Result<bool>.Fail(ErrorCodes.PrvConnFailed, "O banco de dados / caminho do arquivo é obrigatório.");
        }

        if (profile.Settings.Port is < 1 or > 65535)
        {
            return Result<bool>.Fail(ErrorCodes.PrvConnFailed, "A porta deve ser um valor numérico válido entre 1 e 65535.");
        }

        if (profile.Settings.ConnectTimeoutSec < 1)
        {
            return Result<bool>.Fail(ErrorCodes.PrvConnFailed, "O timeout de conexão deve ser de pelo menos 1 segundo.");
        }

        // Validar unicidade do nome (case-insensitive, C02, FR-001)
        var allProfiles = await _store.ListAsync(ct);
        if (allProfiles.Any(p => p.Profile.Id != profile.Id && string.Equals(p.Profile.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<bool>.Fail(ErrorCodes.CatProfileSecret, $"Já existe uma conexão cadastrada com o nome '{profile.Name}'.");
        }

        return Result<bool>.Ok(true);
    }
}
