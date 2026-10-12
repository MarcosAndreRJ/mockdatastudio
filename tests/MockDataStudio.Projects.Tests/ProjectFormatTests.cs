using System.Text.Json.Nodes;
using Json.Schema;
using MockDataStudio.Core.Common;
using MockDataStudio.Projects;
using Shouldly;
using Xunit;

namespace MockDataStudio.Projects.Tests;

public sealed class ProjectFormatTests
{
    private static readonly ProjectSerializer Serializer = new();

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MockDataStudio.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada."), relative);
    }

    private static string ExampleJson() => File.ReadAllText(RepoFile("docs/examples/project-loja.mdsproj"));

    private static MdsProject ExampleProject()
    {
        var result = Serializer.Deserialize(ExampleJson());
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        return result.Value;
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-001")]
    public void RoundTrip_ExampleProject_SerializesIdentically()
    {
        var original = ExampleJson();
        var serialized = Serializer.Serialize(ExampleProject());

        JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(serialized)).ShouldBeTrue("a estrutura deve ser idêntica");
        serialized.Replace("\r\n", "\n").TrimEnd().ShouldBe(original.Replace("\r\n", "\n").TrimEnd(), "o texto deve ser idêntico (diff vazio)");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-002")]
    public void Serialize_IsStable_RegardlessOfMapInsertionOrder()
    {
        var project = ExampleProject();
        var shuffled = project with
        {
            Bindings = project.Bindings.Reverse().ToDictionary(b => b.Key, b => b.Value),
            Profiles = [project.Profiles[0] with { Tables = project.Profiles[0].Tables.Reverse().ToDictionary(t => t.Key, t => t.Value) }],
        };

        var first = Serializer.Serialize(project);
        Serializer.Serialize(shuffled).ShouldBe(first);

        var keys = ((JsonObject)JsonNode.Parse(first)!["bindings"]!).Select(p => p.Key).ToList();
        keys.ShouldBe(keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        first.ShouldNotContain("\r");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-003")]
    public async Task Project_NeverContainsSecretsOrConnectionDetails()
    {
        var project = ExampleProject();
        var json = Serializer.Serialize(project);
        foreach (var forbidden in new[] { "password", "pwd", "connectionString", "\"host\":", "\"user\":", "\"username\":" })
        {
            json.ShouldNotContain(forbidden, Case.Insensitive);
        }

        var path = Path.Combine(Path.GetTempPath(), "mds-prj-" + Guid.NewGuid().ToString("N") + ".mdsproj");
        try
        {
            var store = new ProjectStore(Serializer);
            (await store.ExportSanitizedAsync(project, path, new SanitizeOptions(KeepOnlyActiveSnapshot: true), TestContext.Current.CancellationToken))
                .IsSuccess.ShouldBeTrue();

            var exported = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
            var text = exported.ToJsonString();
            text.ShouldNotContain("profileId");
            text.ShouldNotContain("\"fingerprint\"");
            text.ShouldNotContain("sourceFingerprint");
            text.ShouldNotContain("\"runs\"");
            Serializer.Deserialize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-004")]
    public void Migration_NewerMinorOpens_NewerMajorIsRejected()
    {
        var minor = ExampleJson().Replace("\"formatVersion\": \"1.0\"", "\"formatVersion\": \"1.7\"");
        var opened = Serializer.Deserialize(minor);
        opened.IsSuccess.ShouldBeTrue();

        var major = Serializer.Deserialize(ExampleJson().Replace("\"formatVersion\": \"1.0\"", "\"formatVersion\": \"2.0\""));
        major.IsSuccess.ShouldBeFalse();
        major.Error.Code.ShouldBe(ErrorCodes.PrjMajorUnsupported);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-005")]
    public void Deserialize_ReportsPrjCodes()
    {
        Serializer.Deserialize("{ não é json").Error.Code.ShouldBe(ErrorCodes.PrjJson);
        Serializer.Deserialize("[1,2]").Error.Code.ShouldBe(ErrorCodes.PrjJson);

        var noName = JsonNode.Parse(ExampleJson())!.AsObject();
        noName.Remove("name");
        var schema = Serializer.Deserialize(noName.ToJsonString());
        schema.Error.Code.ShouldBe(ErrorCodes.PrjSchema);
        schema.Error.Message.ShouldContain("$.name");

        var broken = JsonNode.Parse(ExampleJson())!.AsObject();
        broken["snapshots"]![0]!["digest"]!["namespaces"]![0]!["tables"]![0]!["pk"] = new JsonArray("coluna_inexistente");
        var tampered = broken.ToJsonString();
        var digest = Serializer.Deserialize(tampered);
        digest.IsSuccess.ShouldBeFalse();
        digest.Error.Code.ShouldBe(ErrorCodes.PrjDigest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-005")]
    public void Validator_FlagsOrphanBindings_AndCatalogDrift()
    {
        var project = ExampleProject();
        project.Bindings.Count.ShouldBeGreaterThan(0);
        ProjectValidator.Validate(project).ShouldBeEmpty();

        var first = project.Bindings.First();
        var bindings = new Dictionary<string, MockDataStudio.Core.Bindings.ColumnBinding>(project.Bindings)
        {
            ["public.nao_existe.coluna"] = first.Value with { Column = new MockDataStudio.Core.Naming.ColumnRef(new MockDataStudio.Core.Naming.ObjectName("public", "nao_existe"), "coluna") },
        };

        var issues = ProjectValidator.Validate(project with { Bindings = bindings },
            new Dictionary<string, string> { ["NOME"] = "sha256:" + new string('0', 64) });

        issues.ShouldContain(i => i.Code == ErrorCodes.PrjBindingColumn && i.JsonPath == "$.bindings.public.nao_existe.coluna");
        issues.Count(i => i.Code == ErrorCodes.PrjCatalog).ShouldBeGreaterThan(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-006")]
    public async Task Save_IsAtomic_KeepsBackup_AndSurvivesWriteFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = Path.Combine(Path.GetTempPath(), "mds-prj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "p.mdsproj");
        try
        {
            var store = new ProjectStore(Serializer);
            var project = ExampleProject();

            (await store.SaveAsync(project, path, ct)).IsSuccess.ShouldBeTrue();
            var firstContent = await File.ReadAllTextAsync(path, ct);

            (await store.SaveAsync(project with { Name = "Renomeado" }, path, ct)).IsSuccess.ShouldBeTrue();
            File.Exists(path + ".bak").ShouldBeTrue();
            (await File.ReadAllTextAsync(path + ".bak", ct)).ShouldBe(firstContent);

            // Falha simulada: o arquivo temporário não pode ser criado (já existe um diretório com esse nome).
            var before = await File.ReadAllTextAsync(path, ct);
            Directory.CreateDirectory(path + ".tmp");
            (await store.SaveAsync(project with { Name = "Quebrado" }, path, ct)).IsSuccess.ShouldBeFalse();
            (await File.ReadAllTextAsync(path, ct)).ShouldBe(before);

            var reopened = await store.OpenAsync(path, ct);
            reopened.IsSuccess.ShouldBeTrue();
            reopened.Value.Name.ShouldBe("Renomeado");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static JsonSchema ReferenceSchema()
    {
        var node = JsonNode.Parse(File.ReadAllText(RepoFile("docs/contracts/schemas/project-1.0.schema.json")))!;
        // O digest embutido é validado por DigestValidator (PRJ-DIGEST) e pelos testes T-DIG; aqui basta que seja um objeto.
        node["$defs"]!["snapshot"]!["properties"]!["digest"] = new JsonObject { ["type"] = "object" };
        return JsonSchema.FromText(node.ToJsonString());
    }

    private static bool SchemaAccepts(string json) =>
        ReferenceSchema().Evaluate(JsonNode.Parse(json), new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid;

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRJ-010")]
    public void CodedValidator_AgreesWithJsonSchema_OnValidAndInvalidFixtures()
    {
        var fixtures = new List<(string Name, string Json, bool Valid)> { ("exemplo", ExampleJson(), true) };

        void Invalid(string name, Action<JsonObject> mutate)
        {
            var node = JsonNode.Parse(ExampleJson())!.AsObject();
            mutate(node);
            fixtures.Add((name, node.ToJsonString(), false));
        }

        Invalid("sem name", n => n.Remove("name"));
        Invalid("sem profiles", n => n.Remove("profiles"));
        Invalid("perfis vazios", n => n["profiles"] = new JsonArray());
        Invalid("formato errado", n => n["format"] = "outro");
        Invalid("versão sem minor", n => n["formatVersion"] = "1");
        Invalid("engine desconhecido", n => n["connections"]!["source"]!["engine"] = "oracle");
        Invalid("propriedade extra", n => n["extra"] = 1);
        Invalid("seed numérica", n => n["profiles"]![0]!["seed"] = 20261011);
        Invalid("enum de saída inválido", n => n["profiles"]![0]!["output"]!["kind"] = "pdf");
        Invalid("guid inválido", n => n["id"] = "não-é-guid");

        foreach (var (name, json, valid) in fixtures)
        {
            SchemaAccepts(json).ShouldBe(valid, $"schema: {name}");
            Serializer.Deserialize(json).IsSuccess.ShouldBe(valid, $"validador codificado: {name}");
        }
    }
}
