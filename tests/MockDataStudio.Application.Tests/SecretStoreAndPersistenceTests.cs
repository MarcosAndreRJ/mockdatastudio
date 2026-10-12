using System.Runtime.InteropServices;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Infrastructure.Security;
using MockDataStudio.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests;

public sealed class SecretStoreAndPersistenceTests
{
    [Fact]
    public async Task FileConnectionProfileStore_ShouldSaveAndReloadProfiles_WithoutPlaintextSecrets()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "MDS_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var jsonFile = Path.Combine(tempDir, "connections.json");

        try
        {
            var profile = new ConnectionProfile(
                Id: Guid.NewGuid(),
                Name: "Production Cluster",
                Engine: DatabaseEngine.PostgreSql,
                Settings: new ConnectionSettings(
                    Host: "db.prod.internal",
                    Port: 5432,
                    Database: "finance",
                    User: "admin",
                    UseSsl: false,
                    Charset: null,
                    ConnectTimeoutSec: 30,
                    CommandTimeoutSec: 0,
                    Extra: null
                ),
                Environment: ConnectionEnvironment.Production,
                ColorHex: "#B91C1C",
                IsReadOnly: true,
                PromptPassword: false,
                Notes: "Confidential DB"
            );

            // Act - Salvar perfil com segredo cifrado simulado
            byte[] fakeEncryptedBytes = System.Text.Encoding.UTF8.GetBytes("encrypted_blob_data_here");
            string fakeEncryptedBase64 = Convert.ToBase64String(fakeEncryptedBytes);

            using (var store = new FileConnectionProfileStore(jsonFile))
            {
                await store.SaveAsync(profile, fakeEncryptedBytes, ct);
            }

            // Assert - Verificar arquivo físico no disco
            File.Exists(jsonFile).ShouldBeTrue();
            var fileContent = await File.ReadAllTextAsync(jsonFile, ct);

            // Regra mandatória de segurança (§6): Senhas em texto puro NUNCA aparecem nos arquivos persistidos
            fileContent.ShouldNotContain("mypassword");
            fileContent.ShouldNotContain("encrypted_blob_data_here"); // Texto puro não aparece, apenas o Base64
            fileContent.ShouldContain("Production Cluster");
            fileContent.ShouldContain(fakeEncryptedBase64);

            // Act - Recarregar na nova instância
            using (var reloadStore = new FileConnectionProfileStore(jsonFile))
            {
                var record = await reloadStore.GetAsync(profile.Id, ct);
                record.ShouldNotBeNull();
                record.Profile.Name.ShouldBe("Production Cluster");
                record.Profile.Environment.ShouldBe(ConnectionEnvironment.Production);
                record.Profile.IsReadOnly.ShouldBeTrue();
                record.Profile.Settings.Host.ShouldBe("db.prod.internal");

                record.ProtectedSecret.ShouldNotBeNull();
                record.ProtectedSecret.ShouldBe(fakeEncryptedBytes);

                // Act - Deletar perfil
                var deleted = await reloadStore.DeleteAsync(profile.Id, ct);
                deleted.ShouldBeTrue();

                var listAfterDelete = await reloadStore.ListAsync(ct);
                listAfterDelete.ShouldBeEmpty();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DpapiSecretStore_ShouldProtectAndUnprotectSecret_OnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // DPAPI só opera em Windows

        var tempKeyDir = Path.Combine(Path.GetTempPath(), "MDS_Entropy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempKeyDir);
        var keyPath = Path.Combine(tempKeyDir, "install.key");

        try
        {
            var store = new DpapiSecretStore(keyPath);
            var secret = "P@ssw0rd_Super_Secret_#2026!";

            // Act - Proteger segredo
            var protectedBytes = store.Protect(secret);
            protectedBytes.ShouldNotBeNull();
            protectedBytes.Length.ShouldBeGreaterThan(0);

            // Assert - Desproteger segredo
            var unprotected = store.Unprotect(protectedBytes);
            unprotected.ShouldBe(secret);
        }
        finally
        {
            if (Directory.Exists(tempKeyDir))
                Directory.Delete(tempKeyDir, recursive: true);
        }
    }
}
