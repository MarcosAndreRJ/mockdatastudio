using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.Core.Common;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Infrastructure.Catalog;
using MockDataStudio.Infrastructure.Settings;
using MockDataStudio.Projects;
using Shouldly;
using Xunit;

using Services = MockDataStudio.Application.Tests.ProjectServicesTests;

namespace MockDataStudio.Application.Tests;

// Fluxo da C3: workspace de projeto, sessão de mapeamento, preview de gerador, templates e catálogo local.
public sealed class ProjectFlowTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "mds-flow-" + Guid.NewGuid().ToString("N"));

    private static ProjectWorkspace NewWorkspace(string dir) =>
        new(new ProjectStore(new ProjectSerializer()), new JsonAppSettings(Path.Combine(dir, "settings.json")), Services.Clock);

    // ---------------------------------------------------------------- workspace

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-UI-FLOW-001")]
    public async Task Workspace_NewSaveOpen_TracksDirtyAndRecents()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var workspace = NewWorkspace(dir);
            workspace.CreateNew("Loja");
            workspace.Current!.Name.ShouldBe("Loja");
            workspace.IsDirty.ShouldBeTrue();
            workspace.Path.ShouldBeNull();
            (await workspace.SaveAsync(ct)).IsSuccess.ShouldBeFalse();

            var path = Path.Combine(dir, "loja.mdsproj");
            (await workspace.SaveAsAsync(path, ct)).IsSuccess.ShouldBeTrue();
            workspace.IsDirty.ShouldBeFalse();
            workspace.RecentProjects.ShouldContain(Path.GetFullPath(path));

            var other = NewWorkspace(dir);
            other.RecentProjects.ShouldContain(Path.GetFullPath(path));
            (await other.OpenAsync(path, ct)).IsSuccess.ShouldBeTrue();
            other.Current!.Id.ShouldBe(workspace.Current.Id);
            other.IsDirty.ShouldBeFalse();

            // Arquivo inexistente: falha e sai dos recentes.
            File.Delete(path);
            (await other.OpenAsync(path, ct)).IsSuccess.ShouldBeFalse();
            other.RecentProjects.ShouldNotContain(Path.GetFullPath(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Workspace_UndoRedo_RestoresPreviousProjects()
    {
        var dir = TempDir();
        try
        {
            var workspace = NewWorkspace(dir);
            workspace.CreateNew("A");
            workspace.CanUndo.ShouldBeFalse();

            workspace.Update("Renomear", p => p with { Name = "B" });
            workspace.Update("Renomear", p => p with { Name = "C" });
            workspace.Current!.Name.ShouldBe("C");
            workspace.UndoDescription.ShouldBe("Renomear");

            workspace.Undo().ShouldBeTrue();
            workspace.Current.Name.ShouldBe("B");
            workspace.Redo().ShouldBeTrue();
            workspace.Current.Name.ShouldBe("C");

            workspace.Undo();
            workspace.Update("Renomear", p => p with { Name = "D" });
            workspace.CanRedo.ShouldBeFalse("uma nova alteração descarta o refazer");

            while (workspace.Undo())
            {
            }

            workspace.Current.Name.ShouldBe("A");
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
    public async Task Workspace_AttachSnapshot_AssignsSequentialIdsAndActivates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var workbench = await SqliteWorkbench.CreateAsync(ct: ct);
        var dir = TempDir();
        try
        {
            var workspace = NewWorkspace(dir);
            workspace.CreateNew("Loja");
            var snapshot = await Services.CaptureAsync(workbench, ct);

            var first = workspace.AttachSnapshot(snapshot, new ConnectionRef(Guid.NewGuid(), "teste", MockDataStudio.Core.Common.DatabaseEngine.Sqlite, null));
            var second = workspace.AttachSnapshot(snapshot, null);

            first.Id.ShouldBe("snap-0001");
            second.Id.ShouldBe("snap-0002");
            workspace.Current!.ActiveSnapshotId.ShouldBe("snap-0002");
            workspace.Current.Connections.Source.ShouldNotBeNull();
            workspace.Current.Snapshots.Count.ShouldBe(2);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    // ---------------------------------------------------------------- mapeamento

    private static async Task<(MappingSession Session, ProjectWorkspace Workspace, string Dir)> NewSessionAsync(SqliteWorkbench workbench, CancellationToken ct)
    {
        var dir = TempDir();
        var workspace = NewWorkspace(dir);
        workspace.CreateNew("Loja");
        workspace.AttachSnapshot(await Services.CaptureAsync(workbench, ct), null);
        var session = new MappingSession(Services.NewMappingService(new Services.FakeCatalog()), workspace);
        (await session.LoadAsync(ct)).IsSuccess.ShouldBeTrue();
        return (session, workspace, dir);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-UI-FLOW-001")]
    public async Task MappingSession_AutoMap_Accept_Options_AndUndo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var workbench = await SqliteWorkbench.CreateAsync(ct: ct);
        var (session, workspace, dir) = await NewSessionAsync(workbench, ct);
        try
        {
            var tables = session.Tables();
            tables.Select(t => t.Name).ShouldBe(["cliente", "pedido"], ignoreOrder: true);
            tables.All(t => t.Mapped == 0).ShouldBeTrue("nada é vinculado antes de aceitar ou mapear");

            var cliente = new ObjectName("main", "cliente");
            var email = session.Fields(cliente).First(f => f.Name == "email");
            email.Status.ShouldNotBe(FieldStatus.Mapped);
            email.Constraints.ShouldContain("UQ");
            email.NativeType.ShouldNotBeNullOrEmpty();

            session.AutoMapAll();
            workspace.CanUndo.ShouldBeTrue();
            session.Fields(cliente).First(f => f.Name == "id").Status.ShouldBe(FieldStatus.Mapped);
            session.Tables().Sum(t => t.Mapped).ShouldBeGreaterThan(0);

            // Escolha do usuário: gerador próprio, com opções.
            var emailRef = new ColumnRef(cliente, "email");
            await session.SetGeneratorAsync(emailRef, new GeneratorSpec("core.uuid", 1, new System.Text.Json.Nodes.JsonObject()), learn: false, ct);
            session.SetOptions(emailRef, nullRate: 0.25, unique: true, note: "dado de teste");

            var row = session.Fields(cliente).First(f => f.Name == "email");
            row.Status.ShouldBe(FieldStatus.Mapped);
            row.Generator.ShouldBe("core.uuid");
            row.Binding!.NullRate.ShouldBe(0.25);
            row.Binding.Unique.ShouldBeTrue();
            row.Binding.Origin.ShouldBe(BindingOrigin.User);

            // Desfazer as opções e o vínculo restaura o estado anterior.
            workspace.Undo();
            workspace.Undo();
            session.SyncWithProject();
            var restored = session.Fields(cliente).First(f => f.Name == "email").Binding;
            restored.ShouldNotBeNull();
            restored.Origin.ShouldBe(BindingOrigin.Auto, "voltou ao vínculo automático anterior à escolha do usuário");
            restored.Generator.GeneratorId.ShouldNotBe("core.uuid");

            session.ClearBinding(new ColumnRef(cliente, "id"));
            session.Fields(cliente).First(f => f.Name == "id").Status.ShouldNotBe(FieldStatus.Mapped);

            Should.Throw<ArgumentOutOfRangeException>(() => session.SetOptions(emailRef, 1.5, false, null));
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
    public async Task MappingSession_Load_FailsWithoutProjectOrSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = TempDir();
        try
        {
            var workspace = NewWorkspace(dir);
            var session = new MappingSession(Services.NewMappingService(new Services.FakeCatalog()), workspace);
            (await session.LoadAsync(ct)).IsSuccess.ShouldBeFalse();

            workspace.CreateNew("Vazio");
            (await session.LoadAsync(ct)).IsSuccess.ShouldBeFalse();
            session.Tables().ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    // ---------------------------------------------------------------- preview de gerador

    private sealed class EmptyTemplateSource : ITemplateDataSource
    {
        public Task<TemplateDataSet> LoadAsync(string templateCode, CancellationToken ct) =>
            throw new KeyNotFoundException(templateCode);
    }

    private static ColumnModel Column(string name) =>
        new(name, 1, new DataTypeInfo("INTEGER", LogicalType.Integer), false);

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GeneratorPreview_IsDeterministic_AndReportsErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new GeneratorPreviewService(GeneratorRegistry.Standard(), new EmptyTemplateSource());
        var table = new ObjectName("main", "cliente");
        var spec = new GeneratorSpec("core.random.int", 1, new System.Text.Json.Nodes.JsonObject { ["min"] = 1, ["max"] = 1000 });

        var first = await service.PreviewAsync(spec, table, Column("id"), seed: 7, count: 8, ct);
        var again = await service.PreviewAsync(spec, table, Column("id"), seed: 7, count: 8, ct);
        var other = await service.PreviewAsync(spec, table, Column("id"), seed: 8, count: 8, ct);

        first.IsSuccess.ShouldBeTrue(first.IsSuccess ? string.Empty : first.Error.Message);
        first.Value.Count.ShouldBe(8);
        again.Value.ShouldBe(first.Value);
        other.Value.ShouldNotBe(first.Value);

        (await service.PreviewAsync(new GeneratorSpec("nao.existe", 1, new System.Text.Json.Nodes.JsonObject()), table, Column("x"), 1, 3, ct))
            .IsSuccess.ShouldBeFalse();

        var missingTemplate = new GeneratorSpec("catalog.template", 1, new System.Text.Json.Nodes.JsonObject { ["template"] = "NAO_EXISTE" });
        (await service.PreviewAsync(missingTemplate, table, Column("x"), 1, 3, ct)).IsSuccess.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- catálogo real e templates

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-UI-FLOW-001")]
    public async Task CatalogBootstrap_InstallsPackOnce_AndTemplatesAreBrowsable()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = TempDir();
        try
        {
            using var catalog = new CatalogBootstrap(dir);
            (await catalog.EnsureReadyAsync(ct)).IsSuccess.ShouldBeTrue();
            (await catalog.EnsureReadyAsync(ct)).IsSuccess.ShouldBeTrue("idempotente");

            var browser = new TemplateBrowser(catalog.Templates, catalog.DataSource);
            var all = await browser.ListAsync(null, ct);
            all.Count.ShouldBeGreaterThan(5);

            var filtered = await browser.ListAsync("nome", ct);
            filtered.ShouldNotBeEmpty();
            filtered.Count.ShouldBeLessThanOrEqualTo(all.Count);

            var code = all.First(t => t.Kind == TemplateKind.Scalar).Code;
            var details = await browser.GetDetailsAsync(code, ct);
            details.ShouldNotBeNull();
            details.SampleValues.ShouldNotBeEmpty();
            details.SampleValues.Count.ShouldBeLessThanOrEqualTo(TemplateBrowser.SampleSize);
            (await browser.GetDetailsAsync("NAO_EXISTE", ct)).ShouldBeNull();

            // Preview de um gerador de catálogo sobre dados reais.
            var preview = new GeneratorPreviewService(GeneratorRegistry.Standard(), catalog.DataSource);
            var spec = new GeneratorSpec("catalog.template", 1, new System.Text.Json.Nodes.JsonObject { ["template"] = code });
            var values = await preview.PreviewAsync(spec, new ObjectName("main", "t"), Column("c"), 1, 5, ct);
            values.IsSuccess.ShouldBeTrue(values.IsSuccess ? string.Empty : values.Error.Message);
            values.Value.Count.ShouldBe(5);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                // SQLite ainda segura o arquivo: a pasta temporária é limpa pelo sistema.
            }
        }
    }
}

public sealed class GeneratorParameterBinderTests
{
    private static GeneratorDescriptor Descriptor(params GeneratorParameterDefinition[] parameters) =>
        new("test.gen", "Teste", GeneratorCategory.Basic, 1, [1], [], parameters, Reproducibility.Stable, "teste");

    [Fact]
    [Trait("Category", "Unit")]
    public void FromText_ParsesTypes_OmitsEmptyOptionals_AndEnforcesRules()
    {
        var descriptor = Descriptor(
            new GeneratorParameterDefinition("min", "int", true, null, "Mínimo", null, Min: 0, Max: 100),
            new GeneratorParameterDefinition("p", "double", false, System.Text.Json.Nodes.JsonValue.Create(0.5), "Probabilidade", null),
            new GeneratorParameterDefinition("on", "bool", false, null, "Ligado", null),
            new GeneratorParameterDefinition("mode", "enum", false, null, "Modo", null, EnumValues: ["a", "b"]),
            new GeneratorParameterDefinition("items", "stringList", false, null, "Itens", null),
            new GeneratorParameterDefinition("w", "doubleList", false, null, "Pesos", null));

        var ok = GeneratorParameterBinder.FromText(descriptor, new Dictionary<string, string>
        {
            ["min"] = "7", ["p"] = "", ["on"] = "true", ["mode"] = "b", ["items"] = "x; y", ["w"] = "1,2.5",
        });

        ok.IsSuccess.ShouldBeTrue(ok.IsSuccess ? string.Empty : ok.Error.Message);
        ok.Value["min"]!.GetValue<long>().ShouldBe(7);
        ok.Value.ContainsKey("p").ShouldBeFalse("opcional vazio vale o padrão do descriptor");
        ok.Value["on"]!.GetValue<bool>().ShouldBeTrue();
        ok.Value["items"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["x", "y"]);
        ok.Value["w"]!.AsArray().Select(n => n!.GetValue<double>()).ShouldBe([1d, 2.5d]);

        GeneratorParameterBinder.FromText(descriptor, new Dictionary<string, string>()).IsSuccess.ShouldBeFalse("obrigatório vazio");
        GeneratorParameterBinder.FromText(descriptor, new Dictionary<string, string> { ["min"] = "abc" }).Error.Message.ShouldContain("Mínimo");
        GeneratorParameterBinder.FromText(descriptor, new Dictionary<string, string> { ["min"] = "101" }).IsSuccess.ShouldBeFalse();
        GeneratorParameterBinder.FromText(descriptor, new Dictionary<string, string> { ["min"] = "1", ["mode"] = "z" }).IsSuccess.ShouldBeFalse();
        GeneratorParameterBinder.FromText(descriptor, new Dictionary<string, string> { ["min"] = "1", ["w"] = "1;x" }).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToText_UsesSpecThenDefault_AndRoundTrips()
    {
        var descriptor = Descriptor(
            new GeneratorParameterDefinition("max", "int", true, null, "Máximo", null),
            new GeneratorParameterDefinition("p", "double", false, System.Text.Json.Nodes.JsonValue.Create(0.5), "Probabilidade", null),
            new GeneratorParameterDefinition("items", "stringList", false, null, "Itens", null));

        var spec = new System.Text.Json.Nodes.JsonObject
        {
            ["max"] = 9,
            ["items"] = new System.Text.Json.Nodes.JsonArray("a", "b"),
        };

        var text = GeneratorParameterBinder.ToText(descriptor, spec);
        text["max"].ShouldBe("9");
        text["p"].ShouldBe("0.5");
        text["items"].ShouldBe("a\nb");

        var back = GeneratorParameterBinder.FromText(descriptor, text);
        back.Value["max"]!.GetValue<long>().ShouldBe(9);
        back.Value["items"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StandardRegistry_EveryDescriptor_HasEditableDefaults()
    {
        foreach (var descriptor in GeneratorRegistry.Standard().All)
        {
            var text = GeneratorParameterBinder.ToText(descriptor, null);
            text.Count.ShouldBe(descriptor.Parameters.Count, descriptor.GeneratorId);
        }
    }
}

public sealed class MappingGenerationContractTests
{
    // Todo gerador que o Mapping pode produzir precisa existir no registro do motor (senão a geração falha só na execução).
    [Fact]
    [Trait("Category", "Unit")]
    public void EveryGeneratorProducedByMapping_ExistsInTheGenerationRegistry()
    {
        var registry = GeneratorRegistry.Standard();
        var produced = new[]
        {
            "core.bool", "core.date.range", "core.date.relative", "core.fixed", "core.format", "core.list.random",
            "core.lorem.paragraph", "core.random.bytes", "core.random.decimal", "core.random.int",
            "catalog.template", "catalog.template.field", "core.uuid",
        };

        var missing = produced.Where(id => !registry.TryGet(id, out _)).ToList();

        missing.ShouldBeEmpty("geradores emitidos pelo Mapping e ausentes no registro: " + string.Join(", ", missing));
    }
}

public sealed class PresetResolutionTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task SetBinding_ResolvesPresetToItsGenerator()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalog = new ProjectServicesTests.FakeCatalog();
        catalog.Presets.Add(new GeneratorPresetInfo(1, "PERCENTUAL", "Percentual", "core.random.decimal", 1,
            "{\"min\":0,\"max\":100}", null, true));
        var service = ProjectServicesTests.NewMappingService(catalog);
        var project = ProjectServicesTests.NewProject(null);

        // O contexto carrega os presets (falha sem snapshot, mas depois de ler o catálogo não importa aqui): usa projeto com snapshot.
        await using var workbench = await MockDataStudio.Application.Tests.TestSupport.SqliteWorkbench.CreateAsync(ct: ct);
        project = ProjectServicesTests.NewProject(await ProjectServicesTests.CaptureAsync(workbench, ct));
        (await service.BuildContextAsync(project, ct)).IsSuccess.ShouldBeTrue();

        var column = new MockDataStudio.Core.Naming.ColumnRef(new MockDataStudio.Core.Naming.ObjectName("main", "pedido"), "total");
        var preset = new MockDataStudio.Core.Bindings.GeneratorSpec("core.preset", 1,
            new System.Text.Json.Nodes.JsonObject { ["preset"] = "PERCENTUAL" });
        var updated = await service.SetBindingAsync(project,
            new MockDataStudio.Core.Bindings.ColumnBinding(column, preset, 0, MockDataStudio.Core.Bindings.BindingOrigin.User, "0123456789abcdef"),
            learnAsRule: false, ct);

        var resolved = updated.Bindings[column.CanonicalId].Generator;
        resolved.GeneratorId.ShouldBe("core.random.decimal");
        resolved.Parameters["max"]!.GetValue<int>().ShouldBe(100);
        GeneratorRegistry.Standard().TryGet(resolved.GeneratorId, out _).ShouldBeTrue();
    }
}

public sealed class RealCatalogMappingTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-UI-FLOW-001")]
    public async Task MappingSession_WithRealCatalog_AutoMapsTheLojaSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = Path.Combine(Path.GetTempPath(), "mds-realmap-" + Guid.NewGuid().ToString("N"));
        await using var workbench = await MockDataStudio.Application.Tests.TestSupport.SqliteWorkbench.CreateAsync(ct: ct);
        try
        {
            using var catalog = new CatalogBootstrap(Path.Combine(dir, "catalog"));
            (await catalog.EnsureReadyAsync(ct)).IsSuccess.ShouldBeTrue();

            var mapping = new MappingService(catalog.Templates, catalog.Rules, new MockDataStudio.Catalog.Repositories.GeneratorPresetRepository(catalog.Database),
                catalog.Feedback, new MockDataStudio.Mapping.MappingEngine(new MockDataStudio.Mapping.StructuralBinder()),
                new MockDataStudio.Mapping.NameNormalizer(), ProjectServicesTests.Digests, ProjectServicesTests.Clock);
            var workspace = new ProjectWorkspace(new ProjectStore(new ProjectSerializer()),
                new JsonAppSettings(Path.Combine(dir, "settings.json")), ProjectServicesTests.Clock);
            workspace.CreateNew("Loja");
            workspace.AttachSnapshot(await ProjectServicesTests.CaptureAsync(workbench, ct), null);

            var session = new MappingSession(mapping, workspace);
            var loaded = await session.LoadAsync(ct);
            loaded.IsSuccess.ShouldBeTrue(loaded.IsSuccess ? string.Empty : loaded.Error.Message);

            session.AutoMapAll();

            var fields = session.Tables().SelectMany(t => session.Fields(t.Table)).ToList();
            fields.Count.ShouldBeGreaterThan(5);
            fields.Count(f => f.Status == FieldStatus.Mapped).ShouldBeGreaterThan(2);
            var registry = GeneratorRegistry.Standard();
            // rel.key e rel.fk são vínculos estruturais resolvidos pelo planejador, não pelo registro de geradores.
            foreach (var field in fields.Where(f => f.Binding is not null && !f.Binding.Generator.GeneratorId.StartsWith("rel.", StringComparison.Ordinal)))
            {
                registry.TryGet(field.Binding!.Generator.GeneratorId, out _).ShouldBeTrue(field.Binding.Generator.GeneratorId);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                // SQLite ainda segura o arquivo.
            }
        }
    }
}
