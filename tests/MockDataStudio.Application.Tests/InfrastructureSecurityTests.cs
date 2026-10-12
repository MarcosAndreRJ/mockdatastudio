using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Infrastructure.Logging;
using MockDataStudio.Infrastructure.Security;
using MockDataStudio.Infrastructure.Settings;
using MockDataStudio.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests;

public sealed class InfrastructureSecurityTests
{
    [Theory]
    [InlineData("Server=db;User Id=sa;Password=Sup3r!Secret;Timeout=5", "Sup3r!Secret")]
    [InlineData("Host=h;Pwd=hunter2;Port=5432", "hunter2")]
    [InlineData("User 'admin' with pwd 'TopSecret!' failed to connect", "TopSecret!")]
    [InlineData("senha: abc123def", "abc123def")]
    [InlineData("falha em postgres://app:p4ssw0rd@db.local:5432/x", "p4ssw0rd")]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SEC-004")]
    public void Redact_RemovesSecrets(string message, string secret)
    {
        var redacted = SecretRedactor.Redact(message);

        redacted.ShouldNotContain(secret);
        redacted.ShouldContain(SecretRedactor.Mask);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SEC-004")]
    public void Logger_NeverWritesSecretsOrConnectionDetails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mds-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profile = new ConnectionProfile(Guid.NewGuid(), "Prod", DatabaseEngine.PostgreSql,
                new ConnectionSettings("db.interno.example", 5432, "financeiro", "admin_user"),
                ConnectionEnvironment.Production, null, true, false, null);

            using (var logger = LoggingSetup.CreateLogger(dir))
            {
                logger.Information("Abrindo {@Profile} com {@Settings}", profile, profile.Settings);
                logger.Error(new InvalidOperationException("Host=db;Password=Sup3r!Secret"), "Falha ao conectar: Pwd=hunter2");
            }

            var text = string.Concat(Directory.GetFiles(dir).Select(File.ReadAllText));
            text.ShouldContain("Prod");
            foreach (var forbidden in new[] { "Sup3r!Secret", "hunter2", "db.interno.example", "admin_user", "financeiro" })
            {
                text.ShouldNotContain(forbidden);
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SEC-012")]
    public async Task ProfileFile_NeverContainsPlaintextPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = Path.Combine(Path.GetTempPath(), "mds-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var secretStore = new DpapiSecretStore(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            var file = Path.Combine(dir, "connections.json");
            var profile = new ConnectionProfile(Guid.NewGuid(), "Dev", DatabaseEngine.MySql,
                new ConnectionSettings("localhost", 3306, "app", "root"), ConnectionEnvironment.Development, null, false, false, null);

            using var store = new FileConnectionProfileStore(file);
            await store.SaveAsync(profile, secretStore.Protect("MinhaSenha#2026"), ct);

            File.ReadAllText(file).ShouldNotContain("MinhaSenha#2026");
            secretStore.Unprotect((await store.GetAsync(profile.Id, ct))!.ProtectedSecret!).ShouldBe("MinhaSenha#2026");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AppSettings_RoundTrip_AndFallbackOnCorruptFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mds-set-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var settings = new JsonAppSettings(path) { Theme = "dark", Language = "en-US", DefaultReadOnly = false };
            settings.Save().ShouldBeTrue();

            var reloaded = new JsonAppSettings(path);
            reloaded.Theme.ShouldBe("dark");
            reloaded.Language.ShouldBe("en-US");
            reloaded.DefaultReadOnly.ShouldBeFalse();

            File.WriteAllText(path, "{ não é json");
            var fallback = new JsonAppSettings(path);
            fallback.Theme.ShouldBe("light");
            fallback.DefaultReadOnly.ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
