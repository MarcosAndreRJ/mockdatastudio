using System.Data.Common;
using System.Text;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Models;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests;

public sealed class ConnectionServiceTests
{
    [Fact]
    public async Task SaveAsync_ShouldPersistProfileAndSecret()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql)]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("Dev DB", DatabaseEngine.PostgreSql);

        // Act
        var result = await service.SaveAsync(profile, "secretPassword123", ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var profiles = await service.ListAsync(ct);
        profiles.Count.ShouldBe(1);
        profiles[0].Name.ShouldBe("Dev DB");

        var record = await profileStore.GetAsync(profile.Id, ct);
        record.ShouldNotBeNull();
        record.ProtectedSecret.ShouldNotBeNull();
        secretStore.Unprotect(record.ProtectedSecret).ShouldBe("secretPassword123");
    }

    [Fact]
    public async Task SaveAsync_ShouldRejectDuplicateName()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql)]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile1 = CreateSampleProfile("MyDatabase", DatabaseEngine.PostgreSql);
        await service.SaveAsync(profile1, "pwd1", ct);

        var profile2 = CreateSampleProfile("MyDatabase", DatabaseEngine.PostgreSql);

        // Act
        var result = await service.SaveAsync(profile2, "pwd2", ct);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Message.ShouldContain("Já existe uma conexão cadastrada");
    }

    [Fact]
    public async Task SaveAsync_ShouldAllowUpdatingExistingProfileWithSameName()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql)]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("MyDatabase", DatabaseEngine.PostgreSql);
        await service.SaveAsync(profile, "pwd1", ct);

        // Act - Update notes on same profile
        var updatedProfile = profile with { Notes = "Updated notes" };
        var result = await service.SaveAsync(updatedProfile, "pwd2", ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var loaded = await service.GetAsync(profile.Id, ct);
        loaded.ShouldNotBeNull();
        loaded.Notes.ShouldBe("Updated notes");

        var record = await profileStore.GetAsync(profile.Id, ct);
        record.ShouldNotBeNull();
        record.ProtectedSecret.ShouldNotBeNull();
        secretStore.Unprotect(record.ProtectedSecret).ShouldBe("pwd2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task SaveAsync_ShouldRejectInvalidPort_WhenProviderRequiresHostPort(int invalidPort)
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql, requiresHostPort: true)]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("BadPort", DatabaseEngine.PostgreSql) with
        {
            Settings = new ConnectionSettings("localhost", invalidPort, "testdb", "user")
        };

        // Act
        var result = await service.SaveAsync(profile, null, ct);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Message.ShouldContain("porta deve ser um valor numérico válido");
    }

    [Fact]
    public async Task SaveAsync_ShouldRejectNegativeTimeout()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql)]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("BadTimeout", DatabaseEngine.PostgreSql) with
        {
            Settings = new ConnectionSettings("localhost", 5432, "testdb", "user", ConnectTimeoutSec: 0)
        };

        // Act
        var result = await service.SaveAsync(profile, null, ct);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Message.ShouldContain("timeout de conexão deve ser de pelo menos 1 segundo");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveProfile()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql)]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("ToDelete", DatabaseEngine.PostgreSql);
        await service.SaveAsync(profile, "pwd123", ct);

        // Act
        var result = await service.DeleteAsync(profile.Id, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var list = await service.ListAsync(ct);
        list.ShouldBeEmpty();

        var record = await profileStore.GetAsync(profile.Id, ct);
        record.ShouldBeNull();
    }

    [Fact]
    public async Task TestAsync_ShouldSucceedWhenProviderConnects()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var stubProvider = new StubDatabaseProvider(DatabaseEngine.PostgreSql);
        var registry = new ProviderRegistry([stubProvider]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("TestDb", DatabaseEngine.PostgreSql);

        // Act
        var result = await service.TestAsync(profile, "mypassword", ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Version.ShouldBe("Stub 1.0");
        result.Value.FingerprintHash.ShouldBe("stub-hash-123");
    }

    [Fact]
    public async Task TestAsync_ShouldReturnFailCancelledWhenCtsCancelled()
    {
        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var stubProvider = new StubDatabaseProvider(DatabaseEngine.PostgreSql, delayMs: 2000);
        var registry = new ProviderRegistry([stubProvider]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("TestDb", DatabaseEngine.PostgreSql);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        // Act
        var result = await service.TestAsync(profile, null, cts.Token);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.PrvCancelled);
    }

    [Fact]
    public async Task OpenAsync_ShouldSetIsServerReadOnlyCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var profileStore = new InMemoryConnectionProfileStore();
        var secretStore = new InMemorySecretStore();
        var stubProvider = new StubDatabaseProvider(DatabaseEngine.PostgreSql, supportsSessionReadOnly: true);
        var registry = new ProviderRegistry([stubProvider]);
        var service = new ConnectionService(profileStore, secretStore, registry);

        var profile = CreateSampleProfile("ReadOnlyProfile", DatabaseEngine.PostgreSql) with
        {
            IsReadOnly = true
        };
        await service.SaveAsync(profile, "pwd", ct);

        // Act
        var openResult = await service.OpenAsync(profile.Id, ConnectionPurpose.Explore, ct);

        // Assert
        openResult.IsSuccess.ShouldBeTrue();
        using var opened = openResult.Value!;
        opened.IsServerReadOnly.ShouldBeTrue();
        opened.Profile.IsReadOnly.ShouldBeTrue();
    }

    private static ConnectionProfile CreateSampleProfile(string name, DatabaseEngine engine)
    {
        return new ConnectionProfile(
            Id: Guid.NewGuid(),
            Name: name,
            Engine: engine,
            Settings: new ConnectionSettings(
                Host: "localhost",
                Port: 5432,
                Database: "sampledb",
                User: "sampleuser",
                UseSsl: false,
                Charset: null,
                ConnectTimeoutSec: 15,
                CommandTimeoutSec: 0,
                Extra: null
            ),
            Environment: ConnectionEnvironment.Development,
            ColorHex: "#2563EB",
            IsReadOnly: false,
            PromptPassword: false,
            Notes: null
        );
    }

    [Fact]
    public async Task SaveAsync_ShouldAcceptEmptyDatabase_WhenProviderDoesNotRequireIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql, requiresDatabase: false)]);
        var service = new ConnectionService(new InMemoryConnectionProfileStore(), new InMemorySecretStore(), registry);
        var profile = CreateSampleProfile("Sem banco", DatabaseEngine.PostgreSql);
        profile = profile with { Settings = profile.Settings with { Database = string.Empty } };

        (await service.SaveAsync(profile, null, ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAsync_ShouldRejectEmptyDatabase_WhenProviderRequiresIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql, requiresDatabase: true)]);
        var service = new ConnectionService(new InMemoryConnectionProfileStore(), new InMemorySecretStore(), registry);
        var profile = CreateSampleProfile("Sem banco", DatabaseEngine.PostgreSql);
        profile = profile with { Settings = profile.Settings with { Database = string.Empty } };

        (await service.SaveAsync(profile, null, ct)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task ListDatabasesAsync_ShouldReturnDatabasesFromProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new ProviderRegistry([new StubDatabaseProvider(DatabaseEngine.PostgreSql, requiresDatabase: false)]);
        var service = new ConnectionService(new InMemoryConnectionProfileStore(), new InMemorySecretStore(), registry);

        var result = await service.ListDatabasesAsync(CreateSampleProfile("Dev", DatabaseEngine.PostgreSql), "pwd", ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(["alpha", "beta"]);
    }

    // --- Helpers / Stubs ---

    private sealed class InMemoryConnectionProfileStore : IConnectionProfileStore
    {
        private readonly Dictionary<Guid, ConnectionProfileRecord> _items = new();

        public Task<IReadOnlyList<ConnectionProfileRecord>> ListAsync(CancellationToken ct = default)
        {
            IReadOnlyList<ConnectionProfileRecord> list = _items.Values.ToList();
            return Task.FromResult(list);
        }

        public Task<ConnectionProfileRecord?> GetAsync(Guid id, CancellationToken ct = default)
        {
            _items.TryGetValue(id, out var val);
            return Task.FromResult(val);
        }

        public Task SaveAsync(ConnectionProfile profile, byte[]? protectedSecret, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            _items[profile.Id] = new ConnectionProfileRecord(profile, protectedSecret, now, now, null);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            return Task.FromResult(_items.Remove(id));
        }

        public Task UpdateLastUsedAsync(Guid id, DateTimeOffset lastUsedAt, CancellationToken ct = default)
        {
            if (_items.TryGetValue(id, out var rec))
            {
                _items[id] = rec with { LastUsedAt = lastUsedAt };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        public byte[] Protect(string secret)
        {
            return Encoding.UTF8.GetBytes(secret);
        }

        public string Unprotect(byte[] protectedSecret)
        {
            return Encoding.UTF8.GetString(protectedSecret);
        }
    }

    private sealed class StubDatabaseProvider : IDatabaseProvider
    {
        private readonly bool _requiresHostPort;
        private readonly bool _supportsSessionReadOnly;
        private readonly int _delayMs;

        public StubDatabaseProvider(DatabaseEngine engine, bool requiresHostPort = true, bool supportsSessionReadOnly = false, int delayMs = 0, bool requiresDatabase = true)
        {
            Engine = engine;
            _requiresHostPort = requiresHostPort;
            _supportsSessionReadOnly = supportsSessionReadOnly;
            _delayMs = delayMs;
            RequiresDatabase = requiresDatabase;
        }

        public DatabaseEngine Engine { get; }
        public string DisplayName => Engine.ToString();
        public bool RequiresHostPort => _requiresHostPort;
        public bool RequiresDatabase { get; }
        public Task<IReadOnlyList<string>> ListDatabasesAsync(DbConnection connection, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(["alpha", "beta"]);
        public IReadOnlyList<ConnectionSettingDefinition> ExtraSettings => [];

        public ProviderCapabilities GetCapabilities(string serverVersion) =>
            new(SupportsNamespaces: true, SupportsReturning: true, SupportsMultiRowValues: true,
                SupportsDeferrableConstraints: true, SupportsDisableFkChecksPerSession: true,
                SupportsIdentityOverride: true, SupportsSequences: true, SupportsTransactionalDdl: true,
                MaxParametersPerCommand: 65535, MaxIdentifierLength: 63);

        public string BuildConnectionString(ConnectionSettings settings, string? password) => "Host=localhost";

        public async Task<DbConnection> OpenConnectionAsync(ConnectionSettings settings, string? password, CancellationToken ct)
        {
            if (_delayMs > 0)
                await Task.Delay(_delayMs, ct);

            return new StubDbConnection();
        }

        public Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct) =>
            Task.FromResult(new ServerInfo("Stub 1.0", "StubEngine", "sampledb", "localhost", "stub-hash-123"));

        public Task<bool> ApplySessionReadOnlyAsync(DbConnection connection, CancellationToken ct) =>
            Task.FromResult(_supportsSessionReadOnly);

        public ISchemaExtractor CreateSchemaExtractor() => throw new NotImplementedException();
        public ISqlDialect GetDialect(string serverVersion) => throw new NotImplementedException();
        public ITypeMapper TypeMapper => throw new NotImplementedException();
        public IBulkInserter CreateBulkInserter(DbConnection connection, string serverVersion) => throw new NotImplementedException();
        public ITargetInspector CreateTargetInspector(DbConnection connection, string serverVersion) => throw new NotImplementedException();
        public IErrorTranslator ErrorTranslator => new StubErrorTranslator();
    }

    private sealed class StubDbConnection : DbConnection
    {
        private System.Data.ConnectionState _state = System.Data.ConnectionState.Closed;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => string.Empty;
            set { }
        }

        public override string Database => "stub";
        public override string DataSource => "stub";
        public override string ServerVersion => "1.0";
        public override System.Data.ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = System.Data.ConnectionState.Closed;
        public override void Open() => _state = System.Data.ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) =>
            throw new NotImplementedException();

        protected override DbCommand CreateDbCommand() => throw new NotImplementedException();
    }

    private sealed class StubErrorTranslator : IErrorTranslator
    {
        public MdsError Translate(Exception ex) =>
            new(ex.GetType().Name, ex.Message, null);

        public DbErrorCategory Categorize(Exception ex) => DbErrorCategory.Other;
    }
}
