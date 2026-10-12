using System.Security.Cryptography;
using System.Text;
using MockDataStudio.Application;

namespace MockDataStudio.Infrastructure.Security;

/// <summary>
/// Implementação de armazenamento seguro de segredos usando Windows DPAPI (CurrentUser)
/// e chave de entropia da instalação em %LOCALAPPDATA%\MockDataStudio\install.key (docs/23 §3, Prompt §6).
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly byte[] _entropy;

    public DpapiSecretStore(string? installKeyPath = null)
    {
        _entropy = LoadOrCreateEntropy(installKeyPath);
    }

    public DpapiSecretStore(byte[] customEntropy)
    {
        ArgumentNullException.ThrowIfNull(customEntropy);
        _entropy = (byte[])customEntropy.Clone();
    }

    public byte[] Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var bytes = Encoding.UTF8.GetBytes(secret);
        return ProtectedData.Protect(bytes, _entropy, DataProtectionScope.CurrentUser);
    }

    public string Unprotect(byte[] protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        var bytes = ProtectedData.Unprotect(protectedSecret, _entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private static byte[] LoadOrCreateEntropy(string? customPath)
    {
        var path = customPath;
        if (string.IsNullOrEmpty(path))
        {
            var mdsDir = AppPaths.Root;
            Directory.CreateDirectory(mdsDir);
            path = Path.Combine(mdsDir, "install.key");
        }

        if (File.Exists(path))
        {
            try
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == 32)
                {
                    return existing;
                }
            }
            catch (IOException)
            {
                // Em caso de concorrência ou leitura temporária
            }
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var entropy = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(path, entropy);
        }
        catch (IOException)
        {
            // Se outro processo gravou concorrentemente, reler
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == 32)
                {
                    return existing;
                }
            }
        }

        return entropy;
    }
}
