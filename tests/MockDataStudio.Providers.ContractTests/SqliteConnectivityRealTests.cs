using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Models;
using MockDataStudio.Providers.Sqlite;
using Shouldly;
using Xunit;

namespace MockDataStudio.Providers.ContractTests;

public sealed class SqliteConnectivityRealTests
{
    [Fact]
    public async Task Sqlite_OpenConnectionAsync_WithExistingFile_ShouldConnectAndReturnServerInfo()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"mds_test_{Guid.NewGuid():N}.db");
        try
        {
            // Cria um banco de dados real
            using (var initConn = new SqliteConnection($"Data Source={tempFile}"))
            {
                await initConn.OpenAsync(ct);
                using var cmd = initConn.CreateCommand();
                cmd.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO users VALUES (1, 'Alice');";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var provider = new SqliteProvider();
            var settings = new ConnectionSettings(
                Host: null,
                Port: null,
                Database: tempFile,
                User: null,
                ConnectTimeoutSec: 5
            );

            // Act - Conexão real
            await using var connection = await provider.OpenConnectionAsync(settings, null, ct);

            // Assert
            connection.ShouldNotBeNull();
            connection.State.ShouldBe(System.Data.ConnectionState.Open);

            // Verifica ServerInfo
            var info = await provider.GetServerInfoAsync(connection, ct);
            info.Version.ShouldStartWith("3.");
            info.FingerprintHash.ShouldNotBeNullOrWhiteSpace();

            // Executa query real
            using var readCmd = connection.CreateCommand();
            readCmd.CommandText = "SELECT name FROM users WHERE id = 1";
            var scalar = await readCmd.ExecuteScalarAsync(ct);
            scalar.ShouldBe("Alice");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public async Task Sqlite_OpenConnectionAsync_NonExistentFile_MustThrowFileNotFoundException()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.db");
        var provider = new SqliteProvider();
        var settings = new ConnectionSettings(
            Host: null,
            Port: null,
            Database: nonExistentPath,
            User: null,
            ConnectTimeoutSec: 5
        );

        // Act & Assert - Não deve criar arquivo novo silenciosamente ao testar conexão existente (Prompt §5.2)
        await Should.ThrowAsync<FileNotFoundException>(async () =>
        {
            await provider.OpenConnectionAsync(settings, null, ct);
        });

        File.Exists(nonExistentPath).ShouldBeFalse("O arquivo não deve ser criado silenciosamente ao testar uma conexão inexistente.");
    }

    [Fact]
    public async Task Sqlite_OpenConnectionAsync_MemoryDatabase_ShouldSucceed()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var provider = new SqliteProvider();
        var settings = new ConnectionSettings(
            Host: null,
            Port: null,
            Database: ":memory:",
            User: null,
            ConnectTimeoutSec: 5
        );

        // Act
        await using var connection = await provider.OpenConnectionAsync(settings, null, ct);

        // Assert
        connection.ShouldNotBeNull();
        connection.State.ShouldBe(System.Data.ConnectionState.Open);
    }

    [Fact]
    public void SqliteErrorTranslator_ShouldTranslateKnownErrors()
    {
        var translator = new SqliteErrorTranslator();

        // Syntax error
        var syntaxEx = new SqliteException("syntax error near FOO", 1);
        var cat1 = translator.Categorize(syntaxEx);
        cat1.ShouldBe(DbErrorCategory.Syntax);

        // Constraint violation (código estendido 2067 = SQLITE_CONSTRAINT_UNIQUE)
        var constraintEx = new SqliteException("UNIQUE constraint failed", 19, 2067);
        var cat2 = translator.Categorize(constraintEx);
        cat2.ShouldBe(DbErrorCategory.UniqueViolation);
    }
}
