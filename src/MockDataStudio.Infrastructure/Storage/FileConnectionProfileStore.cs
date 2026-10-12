using System.Text.Json;
using System.Text.Json.Serialization;
using MockDataStudio.Application;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;

namespace MockDataStudio.Infrastructure.Storage;

public sealed class FileConnectionProfileStore : IConnectionProfileStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileConnectionProfileStore(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var mdsDir = AppPaths.Root;
            Directory.CreateDirectory(mdsDir);
            _filePath = Path.Combine(mdsDir, "connections.json");
        }
        else
        {
            _filePath = filePath;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    public async Task<IReadOnlyList<ConnectionProfileRecord>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var dtos = await LoadDtosAsync(ct);
            return dtos.Select(ToRecord).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ConnectionProfileRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var dtos = await LoadDtosAsync(ct);
            var found = dtos.FirstOrDefault(d => d.Id == id);
            return found != null ? ToRecord(found) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(ConnectionProfile profile, byte[]? protectedSecret, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _lock.WaitAsync(ct);
        try
        {
            var dtos = await LoadDtosAsync(ct);
            var existingIndex = dtos.FindIndex(d => d.Id == profile.Id);

            var now = DateTimeOffset.UtcNow;
            var createdAt = existingIndex >= 0 ? dtos[existingIndex].CreatedAt : now;
            var lastUsedAt = existingIndex >= 0 ? dtos[existingIndex].LastUsedAt : null;

            var newDto = new ProfileStorageDto
            {
                Id = profile.Id,
                Name = profile.Name,
                Engine = profile.Engine,
                Settings = profile.Settings,
                Environment = profile.Environment,
                ColorHex = profile.ColorHex,
                IsReadOnly = profile.IsReadOnly,
                PromptPassword = profile.PromptPassword,
                Notes = profile.Notes,
                ProtectedSecretBase64 = protectedSecret != null ? Convert.ToBase64String(protectedSecret) : null,
                CreatedAt = createdAt,
                UpdatedAt = now,
                LastUsedAt = lastUsedAt,
            };

            if (existingIndex >= 0)
            {
                dtos[existingIndex] = newDto;
            }
            else
            {
                dtos.Add(newDto);
            }

            await SaveDtosAsync(dtos, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var dtos = await LoadDtosAsync(ct);
            var countRemoved = dtos.RemoveAll(d => d.Id == id);
            if (countRemoved > 0)
            {
                await SaveDtosAsync(dtos, ct);
                return true;
            }

            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateLastUsedAsync(Guid id, DateTimeOffset lastUsedAt, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var dtos = await LoadDtosAsync(ct);
            var found = dtos.FirstOrDefault(d => d.Id == id);
            if (found != null)
            {
                found.LastUsedAt = lastUsedAt;
                await SaveDtosAsync(dtos, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ProfileStorageDto>> LoadDtosAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var result = await JsonSerializer.DeserializeAsync<List<ProfileStorageDto>>(stream, JsonOptions, ct);
            return result ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveDtosAsync(List<ProfileStorageDto> dtos, CancellationToken ct)
    {
        var tempFile = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, dtos, JsonOptions, ct);
            }

            File.Move(tempFile, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Ignora falha de limpeza temporária
                }
            }
        }
    }

    private static ConnectionProfileRecord ToRecord(ProfileStorageDto dto)
    {
        var profile = new ConnectionProfile(
            dto.Id,
            dto.Name,
            dto.Engine,
            dto.Settings,
            dto.Environment,
            dto.ColorHex,
            dto.IsReadOnly,
            dto.PromptPassword,
            dto.Notes);

        byte[]? secretBytes = null;
        if (!string.IsNullOrEmpty(dto.ProtectedSecretBase64))
        {
            secretBytes = Convert.FromBase64String(dto.ProtectedSecretBase64);
        }

        return new ConnectionProfileRecord(profile, secretBytes, dto.CreatedAt, dto.UpdatedAt, dto.LastUsedAt);
    }

    private sealed class ProfileStorageDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DatabaseEngine Engine { get; set; }
        public ConnectionSettings Settings { get; set; } = null!;
        public ConnectionEnvironment Environment { get; set; }
        public string? ColorHex { get; set; }
        public bool IsReadOnly { get; set; }
        public bool PromptPassword { get; set; }
        public string? Notes { get; set; }
        public string? ProtectedSecretBase64 { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? LastUsedAt { get; set; }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
