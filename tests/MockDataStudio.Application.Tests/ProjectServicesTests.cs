using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Diff;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Mapping;
using MockDataStudio.Projects;
using MockDataStudio.Providers.Models;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests;

// T-APP-002 (DigestService) e T-APP-003 (MappingService): serviços com fakes de catálogo e SQLite real como origem.
public sealed class ProjectServicesTests
{
    internal static readonly StructureHasher Hasher = new();
    internal static readonly DigestSerializer Digests = new(Hasher);

    internal sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    internal static readonly FixedClock Clock = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

    // Abre sempre o banco do workbench, qualquer que seja o perfil pedido.
    internal sealed class WorkbenchConnectionService(SqliteWorkbench workbench) : IConnectionService
    {
        public Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result<Guid>> SaveAsync(ConnectionProfile profile, string? newPassword, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result<ServerInfo>> TestAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<Result<OpenedConnection>> OpenAsync(Guid profileId, ConnectionPurpose purpose, CancellationToken ct = default)
        {
            var connection = await workbench.OpenAsync(ct);
            var server = await workbench.Provider.GetServerInfoAsync(connection, ct);
            var profile = new ConnectionProfile(profileId, "teste", DatabaseEngine.Sqlite, workbench.Settings,
                ConnectionEnvironment.Development, null, false, false, null);
            return Result<OpenedConnection>.Ok(new OpenedConnection(profile, connection, workbench.Provider, server, false));
        }
    }

    internal static DigestService NewDigestService(SqliteWorkbench workbench) =>
        new(new WorkbenchConnectionService(workbench), Digests, Hasher, new SchemaDiffer(Hasher), Clock);

    internal static async Task<SnapshotEntry> CaptureAsync(SqliteWorkbench workbench, CancellationToken ct)
    {
        var result = await NewDigestService(workbench).CaptureAsync(Guid.NewGuid(), new CaptureOptions(), null, ct);
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        return result.Value;
    }

    internal static MdsProject NewProject(SnapshotEntry? snapshot)
    {
        var output = new OutputSettings(OutputKind.SqlScript);
        var profile = new GenerationProfile("default", 42, Clock.UtcNow, ReferenceInstantMode.Fixed, "UTC",
            new Dictionary<string, TableSettings>(), KeyStrategy.Explicit, output, new ValidationSettings());
        var snapshots = snapshot is null ? [] : new[] { snapshot with { Id = "snap-0001" } };
        return new MdsProject(Guid.NewGuid(), "Teste", "1.0", Clock.UtcNow, Clock.UtcNow, ProjectConnections.Empty, snapshots,
            snapshot is null ? null : "snap-0001", new Dictionary<string, ColumnBinding>(), [profile], "default", [], [], null);
    }

    // ---------------------------------------------------------------- T-APP-002

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-APP-002")]
    public async Task DigestService_CaptureExportImport_RoundTripsTheStructure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var workbench = await SqliteWorkbench.CreateAsync(ct: ct);
        var service = NewDigestService(workbench);

        var snapshot = await CaptureAsync(workbench, ct);
        snapshot.Id.ShouldBe(DigestService.ProvisionalId);
        snapshot.StructureHash.ShouldStartWith("sha256:");
        snapshot.SourceFingerprintHash.ShouldNotBeNullOrEmpty();
        Digests.Deserialize(snapshot.DigestJson).IsSuccess.ShouldBeTrue();

        var path = Path.Combine(workbench.Folder, "estrutura.json");
        (await service.ExportAsync(snapshot, path, indented: true, ct)).IsSuccess.ShouldBeTrue();
        (await File.ReadAllTextAsync(path, ct)).ShouldContain("\n  ");

        var imported = await service.ImportAsync(path, ct);
        imported.IsSuccess.ShouldBeTrue();
        imported.Value.StructureHash.ShouldBe(snapshot.StructureHash);
        imported.Value.SourceFingerprintHash.ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-APP-002")]
    public async Task DigestService_Compare_DetectsAddedColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var workbench = await SqliteWorkbench.CreateAsync(ct: ct);
        var service = NewDigestService(workbench);
        var before = await CaptureAsync(workbench, ct);

        service.Compare(before, before).Value.IsEmpty.ShouldBeTrue();

        await using (var connection = new SqliteConnection("Data Source=" + workbench.File))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE cliente ADD COLUMN telefone TEXT";
            await command.ExecuteNonQueryAsync(ct);
        }

        var after = await CaptureAsync(workbench, ct);
        after.StructureHash.ShouldNotBe(before.StructureHash);

        var diff = service.Compare(before, after);
        diff.IsSuccess.ShouldBeTrue();
        diff.Value.Changes.ShouldContain(c => c.Kind == ChangeKind.Added && c.Path.Contains("telefone", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-APP-002")]
    public async Task DigestService_Import_RejectsInvalidDigest_AndNextSnapshotIdIsSequential()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var workbench = await SqliteWorkbench.CreateAsync(ct: ct);
        var service = NewDigestService(workbench);

        var path = Path.Combine(workbench.Folder, "invalido.json");
        await File.WriteAllTextAsync(path, "{ \"format\": \"mds.digest\" }", ct);
        var result = await service.ImportAsync(path, ct);
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldStartWith("DIG-");

        var project = NewProject(await CaptureAsync(workbench, ct));
        DigestService.NextSnapshotId(project).ShouldBe("snap-0002");
        DigestService.NextSnapshotId(NewProject(null)).ShouldBe("snap-0001");
    }

    // ---------------------------------------------------------------- T-APP-003

    internal sealed class FakeCatalog : ITemplateRepository, IMappingRuleRepository, IMappingFeedbackRepository, IGeneratorPresetRepository
    {
        public List<MappingRuleInfo> SavedRules { get; } = [];

        public List<(string Column, string Target, bool Accepted)> Feedback { get; } = [];

        public Task<IReadOnlyList<TemplateInfo>> ListAsync(TemplateQuery query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TemplateInfo>>(
            [
                new TemplateInfo(1, "EMAIL", "E-mail", TemplateKind.Scalar, LogicalType.String, "pt-BR", [], null, "BASE", true, 50, "sha256:" + new string('a', 64)),
                new TemplateInfo(2, "NOME", "Nome", TemplateKind.Scalar, LogicalType.String, "pt-BR", [], null, "BASE", true, 400, "sha256:" + new string('b', 64)),
            ]);

        public Task<TemplateInfo?> GetAsync(string code, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<TemplateFieldInfo>> GetFieldsAsync(string code, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TemplateFieldInfo>>([]);

        public Task<IReadOnlyList<TemplateAliasInfo>> GetAllAliasesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TemplateAliasInfo>>(
            [
                new TemplateAliasInfo(1, "EMAIL", null, "email", "email", 1.0, null),
                new TemplateAliasInfo(2, "NOME", null, "nome", "nome", 1.0, null),
            ]);

        public Task<Result<long>> SaveAsync(TemplateDraft draft, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<bool>> DeleteAsync(string code, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<string>> CloneAsync(string code, string newCode, CancellationToken ct) => throw new NotSupportedException();

        Task<IReadOnlyList<MappingRuleInfo>> IMappingRuleRepository.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MappingRuleInfo>>(SavedRules);

        Task<IReadOnlyList<GeneratorPresetInfo>> IGeneratorPresetRepository.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GeneratorPresetInfo>>(Presets);

        public List<GeneratorPresetInfo> Presets { get; } = [];

        public Task<Result<long>> UpsertAsync(GeneratorPresetInfo preset, CancellationToken ct) => Task.FromResult(Result<long>.Ok(0));

        public Task<Result<long>> UpsertAsync(MappingRuleInfo rule, CancellationToken ct)
        {
            SavedRules.Add(rule);
            return Task.FromResult(Result<long>.Ok(SavedRules.Count));
        }

        public Task DeleteAsync(long id, CancellationToken ct) => Task.CompletedTask;

        public Task RecordAsync(string columnCompact, RuleTargetKind targetKind, string targetRef, bool accepted, CancellationToken ct)
        {
            Feedback.Add((columnCompact, targetRef, accepted));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MappingFeedbackInfo>> ListAsync(string columnCompact, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MappingFeedbackInfo>>([]);
    }

    internal static MappingService NewMappingService(FakeCatalog catalog) =>
        new(catalog, catalog, catalog, catalog, new MappingEngine(new StructuralBinder()), new NameNormalizer(), Digests, Clock);

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-APP-003")]
    public async Task MappingService_BuildContext_RequiresActiveSnapshot()
    {
        var service = NewMappingService(new FakeCatalog());

        var result = await service.BuildContextAsync(NewProject(null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.PrjSchema);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-APP-003")]
    public async Task MappingService_AutoBindAll_BindsStructuralAndSemanticColumns_AndKeepsUserChoices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var workbench = await SqliteWorkbench.CreateAsync(ct: ct);
        var project = NewProject(await CaptureAsync(workbench, ct));
        var service = NewMappingService(new FakeCatalog());

        var context = await service.BuildContextAsync(project, ct);
        context.IsSuccess.ShouldBeTrue(context.IsSuccess ? string.Empty : context.Error.Message);

        var suggestions = service.Suggest(context.Value, new ObjectName("main", "cliente"));
        suggestions.ShouldNotBeEmpty();

        var bound = service.AutoBindAll(project, context.Value);
        bound.Bindings.ShouldContainKey(new ColumnRef(new ObjectName("main", "cliente"), "id").CanonicalId);
        bound.Bindings.ShouldContainKey(new ColumnRef(new ObjectName("main", "pedido"), "cliente_id").CanonicalId);
        bound.ModifiedAt.ShouldBe(Clock.UtcNow);

        // Escolha do usuário sobrevive a um novo AutoBindAll.
        var email = new ColumnRef(new ObjectName("main", "cliente"), "email");
        var custom = new ColumnBinding(email, new GeneratorSpec("core.uuid", 1, new JsonObject()), 0, BindingOrigin.User, "0123456789abcdef");
        var withUser = await service.SetBindingAsync(bound, custom, learnAsRule: false, ct);
        var second = await service.BuildContextAsync(withUser, ct);
        var again = service.AutoBindAll(withUser, second.Value);

        again.Bindings[email.CanonicalId].Generator.GeneratorId.ShouldBe("core.uuid");
        again.Bindings[email.CanonicalId].Origin.ShouldBe(BindingOrigin.User);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-APP-003")]
    public async Task MappingService_SetBinding_LearnsRuleAndFeedback_OnlyWhenAsked()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalog = new FakeCatalog();
        var service = NewMappingService(catalog);
        var project = NewProject(null);
        var column = new ColumnRef(new ObjectName("main", "cliente"), "nome");
        var binding = new ColumnBinding(column,
            new GeneratorSpec("catalog.template", 1, new JsonObject { ["template"] = "NOME" }), 0, BindingOrigin.Auto, "0123456789abcdef");

        var quiet = await service.SetBindingAsync(project, binding, learnAsRule: false, ct);
        catalog.SavedRules.ShouldBeEmpty();
        catalog.Feedback.ShouldBeEmpty();
        quiet.Bindings[column.CanonicalId].Origin.ShouldBe(BindingOrigin.User);
        quiet.Bindings[column.CanonicalId].ConfidenceLabel.ShouldBe("confirmed");

        await service.SetBindingAsync(project, binding, learnAsRule: true, ct);
        catalog.SavedRules.Count.ShouldBe(1);
        catalog.SavedRules[0].TargetKind.ShouldBe(RuleTargetKind.Template);
        catalog.SavedRules[0].TargetRef.ShouldBe("NOME");
        catalog.SavedRules[0].Source.ShouldBe(RuleSource.Learned);
        catalog.Feedback.ShouldContain(f => f.Target == "NOME" && f.Accepted);
    }
}
