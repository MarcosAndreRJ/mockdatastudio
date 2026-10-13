using System.Text.Json.Nodes;
using MockDataStudio.Application;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Mapping;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests;

// C8a.4: presets (FR-059), regras aprendidas (FR-064) e diagnóstico de mapeamentos (FR-065).
public sealed class MappingLearningTests
{
    private static readonly NameNormalizer Normalizer = new();

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PresetCpfEhSugeridoParaColunaCpf()
    {
        var service = Service(presets: [new GeneratorPresetInfo(1, "CPF", "CPF", "br.cpf", 1, "{}", null, true)]);

        var suggestions = await service.SuggestPresetsAsync("cpf", TestContext.Current.CancellationToken);

        suggestions.Select(p => p.Code).ShouldBe(["CPF"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PresetNaoEhSugeridoParaNomeDiferente()
    {
        var service = Service(presets: [new GeneratorPresetInfo(1, "CPF", "CPF", "br.cpf", 1, "{}", null, true)]);

        (await service.SuggestPresetsAsync("telefone", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task LembrarGravaRegraAprendidaEFeedback()
    {
        var ct = TestContext.Current.CancellationToken;
        var rules = new FakeRules();
        var feedback = new FakeFeedback();
        var service = new MappingLearningService(new FakePresets([]), rules, feedback, Normalizer);

        var saved = await service.RememberAsync("fone_res", RuleTargetKind.Template, "TELEFONE", ct);

        saved.IsSuccess.ShouldBeTrue();
        var learned = await service.LearnedRulesAsync(ct);
        learned.Count.ShouldBe(1);
        learned[0].ColumnPattern.ShouldBe(Normalizer.Normalize("fone_res").Compact);
        learned[0].TargetRef.ShouldBe("TELEFONE");
        learned[0].Source.ShouldBe(RuleSource.Learned);
        feedback.Recorded.ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EsquecerRemoveARegraAprendida()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new MappingLearningService(new FakePresets([]), new FakeRules(), new FakeFeedback(), Normalizer);
        await service.RememberAsync("fone_res", RuleTargetKind.Template, "TELEFONE", ct);
        var id = (await service.LearnedRulesAsync(ct))[0].Id;

        await service.ForgetAsync(id, ct);

        (await service.LearnedRulesAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DiagnosticoMarcaNotNullSemBindingComoErro()
    {
        var schema = Schema(Tbl("cliente", Col("id", 1, "integer", nullable: false), Col("nome", 2, "varchar(60)", nullable: false)));
        var bindings = new Dictionary<string, ColumnBinding> { [Path("cliente", "id")] = Binding("cliente", "id", ConfidenceLabel: "High") };

        var found = MappingDiagnostics.Analyze(schema, bindings);

        found.ShouldContain(d => d.Code == MappingDiagnostics.Unmapped && d.Severity == MappingSeverity.Error && d.ObjectPath == Path("cliente", "nome"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DefaultIdentidadeEGeradaNaoSaoNaoMapeadas()
    {
        var schema = Schema(Tbl("cliente",
            Col("id", 1, "integer", nullable: false, identity: new IdentityInfo(IdentityKind.AutoIncrement)),
            Col("criado", 2, "varchar(20)", nullable: false, defaultValue: new DefaultValue("'x'", DefaultKind.Literal)),
            Col("total", 3, "integer", nullable: false, generated: new GeneratedInfo("1+1", Stored: true))));

        MappingDiagnostics.Analyze(schema, new Dictionary<string, ColumnBinding>()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DiagnosticoListaRevisaoOrfaoEConfiancaBaixa()
    {
        var schema = Schema(Tbl("cliente", Col("id", 1, "integer", nullable: false), Col("email", 2, "varchar(200)", nullable: true)));
        var bindings = new Dictionary<string, ColumnBinding>
        {
            [Path("cliente", "id")] = Binding("cliente", "id", ConfidenceLabel: "High"),
            [Path("cliente", "email")] = Binding("cliente", "email", State: BindingState.NeedsReview, ConfidenceLabel: "Low"),
            [Path("cliente", "antigo")] = Binding("cliente", "antigo", State: BindingState.Orphaned),
        };

        var codes = MappingDiagnostics.Analyze(schema, bindings).Select(d => d.Code).ToList();

        codes.ShouldContain(MappingDiagnostics.Review);
        codes.ShouldContain(MappingDiagnostics.Orphan);
        codes.ShouldContain(MappingDiagnostics.LowConfidence);
        codes.ShouldNotContain(MappingDiagnostics.Unmapped);
    }

    private static string Path(string table, string column) => new ColumnRef(new ObjectName("main", table), column).CanonicalId;

    private static MappingLearningService Service(IReadOnlyList<GeneratorPresetInfo>? presets = null) =>
        new(new FakePresets(presets ?? []), new FakeRules(), new FakeFeedback(), Normalizer);

    private static ColumnBinding Binding(string table, string column, BindingState State = BindingState.Active, string? ConfidenceLabel = null) =>
        new(new ColumnRef(new ObjectName("main", table), column),
            new GeneratorSpec("core.random.string", 1, new JsonObject()), 0, BindingOrigin.Auto, "fp", State, ConfidenceLabel);

    private static SchemaModel Schema(params TableModel[] tables) =>
        SchemaModel.Create(DatabaseEngine.Sqlite, "3", "loja",
            [NamespaceModel.Create("main", false, tables.ToList()).Value], DateTimeOffset.UnixEpoch).Value;

    private static TableModel Tbl(string name, params ColumnModel[] columns) =>
        TableModel.Create(name, columns, new PrimaryKeyModel(null, ["id"])).Value;

    private static ColumnModel Col(string name, int ordinal, string type, bool nullable,
        IdentityInfo? identity = null, DefaultValue? defaultValue = null, GeneratedInfo? generated = null) =>
        ColumnModel.Create(name, ordinal, NativeTypeParser.Parse(type), nullable, defaultValue: defaultValue,
            identity: identity, generated: generated).Value;

    private sealed class FakePresets(IReadOnlyList<GeneratorPresetInfo> items) : IGeneratorPresetRepository
    {
        public Task<IReadOnlyList<GeneratorPresetInfo>> ListAsync(CancellationToken ct) => Task.FromResult(items);
        public Task<Result<long>> UpsertAsync(GeneratorPresetInfo preset, CancellationToken ct) => Task.FromResult(Result<long>.Ok(preset.Id));
        public Task<Result<bool>> DeleteAsync(string code, CancellationToken ct) => Task.FromResult(Result<bool>.Ok(true));
    }

    private sealed class FakeRules : IMappingRuleRepository
    {
        private readonly List<MappingRuleInfo> _rules = [];
        private long _next = 1;

        public Task<IReadOnlyList<MappingRuleInfo>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MappingRuleInfo>>(_rules.ToList());

        public Task<Result<long>> UpsertAsync(MappingRuleInfo rule, CancellationToken ct)
        {
            var id = rule.Id == 0 ? _next++ : rule.Id;
            _rules.RemoveAll(r => r.Id == id);
            _rules.Add(rule with { Id = id });
            return Task.FromResult(Result<long>.Ok(id));
        }

        public Task DeleteAsync(long id, CancellationToken ct)
        {
            _rules.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFeedback : IMappingFeedbackRepository
    {
        public int Recorded { get; private set; }

        public Task RecordAsync(string columnCompact, RuleTargetKind targetKind, string targetRef, bool accepted, CancellationToken ct)
        {
            Recorded++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MappingFeedbackInfo>> ListAsync(string columnCompact, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MappingFeedbackInfo>>([]);
    }
}
