using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Generation.Planning;
using Shouldly;
using Xunit;

namespace MockDataStudio.Generation.Tests.Planning;

// T-REL-* (planejamento relacional, docs/16) e grafo de dependências. Sem banco: todo o estado do destino é sintético.
public sealed class PlanningTests
{
    private static readonly ObjectName Categoria = new("public", "categoria");
    private static readonly ObjectName Cliente = new("public", "cliente");
    private static readonly ObjectName Pedido = new("public", "pedido");
    private static readonly ObjectName Item = new("public", "item");

    // ---------- grafo e Tarjan (16 §2) ----------

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-GRAPHUNIT-001")]
    public void Componentes_CadeiaSaiComPaisAntesDosFilhos()
    {
        var graph = new DependencyGraph([Categoria, Cliente, Pedido, Item],
        [
            Edge(Item, Pedido), Edge(Pedido, Cliente), Edge(Pedido, Categoria),
        ]);

        var order = graph.StronglyConnectedComponents().Select(c => c.Single().Name).ToList();

        order.IndexOf("cliente").ShouldBeLessThan(order.IndexOf("pedido"));
        order.IndexOf("categoria").ShouldBeLessThan(order.IndexOf("pedido"));
        order.IndexOf("pedido").ShouldBeLessThan(order.IndexOf("item"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-GRAPHUNIT-002")]
    public void Componentes_CicloFormaUmaUnicaComponente()
    {
        var a = new ObjectName("public", "a");
        var b = new ObjectName("public", "b");
        var graph = new DependencyGraph([a, b], [Edge(a, b), Edge(b, a)]);

        var components = graph.StronglyConnectedComponents();

        components.Count.ShouldBe(1);
        components[0].Count.ShouldBe(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-GRAPHUNIT-003")]
    public void Componentes_AutorreferenciaNaoFormaCicloMultiTabela()
    {
        var graph = new DependencyGraph([Categoria], [Edge(Categoria, Categoria, self: true)]);

        var components = graph.StronglyConnectedComponents();

        components.Count.ShouldBe(1);
        components[0].Count.ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-GRAPHUNIT-004")]
    public void Componentes_CadeiaLongaNaoEstouraPilha()
    {
        const int size = 20_000;
        var nodes = Enumerable.Range(0, size).Select(i => new ObjectName("public", "t" + i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var edges = Enumerable.Range(1, size - 1).Select(i => Edge(nodes[i], nodes[i - 1])).ToList();

        var components = new DependencyGraph(nodes, edges).StronglyConnectedComponents();

        components.Count.ShouldBe(size);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-GRAPHUNIT-005")]
    public void Componentes_OrdemEhDeterministicaIndependenteDaEntrada()
    {
        var first = new DependencyGraph([Categoria, Cliente, Pedido], [Edge(Pedido, Cliente)]).StronglyConnectedComponents();
        var second = new DependencyGraph([Pedido, Cliente, Categoria], [Edge(Pedido, Cliente)]).StronglyConnectedComponents();

        first.Select(c => c.Single()).ToList().ShouldBe(second.Select(c => c.Single()).ToList());
    }

    // ---------- faixa de chaves (16 §5.2) ----------

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUNIT-001")]
    public void FaixaExplicita_UsaMaiorEntreMaxSequenceEOffset()
    {
        var (plan, diagnostics) = KeyPlanner.PlanExplicitRange("public.cliente", "id", Int("integer"), 10,
            maxExisting: 1000, sequenceNext: 500, startOverride: null, keyOffset: 5, offline: false,
            needsIdentityOverride: false, sequence: null);

        plan.Start.ShouldBe(1006);
        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUNIT-002")]
    public void FaixaExplicita_OfflineEmiteAvisoEComecaEmUm()
    {
        var (plan, diagnostics) = KeyPlanner.PlanExplicitRange("public.cliente", "id", Int("integer"), 10,
            null, null, null, 0, offline: true, needsIdentityOverride: false, sequence: null);

        plan.Start.ShouldBe(1);
        diagnostics.ShouldContain(d => d.Code == "PLN-KEY-OFFLINE" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUNIT-003")]
    public void FaixaExplicita_EstouraSmallintEmitePlnKeyOverflow()
    {
        var (_, diagnostics) = KeyPlanner.PlanExplicitRange("public.item", "id", Int("smallint"), 20,
            maxExisting: 32760, sequenceNext: null, startOverride: null, keyOffset: 0, offline: false,
            needsIdentityOverride: false, sequence: null);

        diagnostics.ShouldContain(d => d.Code == "PLN-KEY-OVERFLOW" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUNIT-004")]
    public void FaixaExplicita_SobrescritaTemPrecedenciaSobreODestino()
    {
        var (plan, _) = KeyPlanner.PlanExplicitRange("public.cliente", "id", Int("integer"), 10,
            maxExisting: 1000, sequenceNext: null, startOverride: 100, keyOffset: 0, offline: false,
            needsIdentityOverride: false, sequence: null);

        plan.Start.ShouldBe(100);
    }

    // ---------- ciclos (16 §3) ----------

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-CYCUNIT-001")]
    public void Ciclo_ComFkAnulavelEhResolvidoPorNullThenUpdate()
    {
        var a = new ObjectName("public", "a");
        var b = new ObjectName("public", "b");
        var edges = new[] { Edge(a, b, nullable: true), Edge(b, a, nullable: false) };

        var resolution = CycleResolver.Resolve([a, b], edges, allowDeferred: false, allowDisableChecks: false);

        resolution.Breakable.ShouldBeTrue();
        resolution.Deferred.Count.ShouldBe(1);
        resolution.Deferred[0].Strategy.ShouldBe(CycleBreakStrategy.NullThenUpdate);
        resolution.Deferred[0].Edge.Child.ShouldBe(a);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-CYCUNIT-002")]
    public void Ciclo_NotNullSemDeferralNaoEhQuebravel()
    {
        var a = new ObjectName("public", "a");
        var b = new ObjectName("public", "b");
        var edges = new[] { Edge(a, b, nullable: false), Edge(b, a, nullable: false) };

        var resolution = CycleResolver.Resolve([a, b], edges, allowDeferred: false, allowDisableChecks: false);

        resolution.Breakable.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-CYCUNIT-003")]
    public void Ciclo_NotNullDeferrableUsaDeferredConstraint()
    {
        var a = new ObjectName("public", "a");
        var b = new ObjectName("public", "b");
        var edges = new[] { Edge(a, b, nullable: false, deferrable: true), Edge(b, a, nullable: false) };

        var resolution = CycleResolver.Resolve([a, b], edges, allowDeferred: true, allowDisableChecks: false);

        resolution.Breakable.ShouldBeTrue();
        resolution.Deferred.Single().Strategy.ShouldBe(CycleBreakStrategy.DeferredConstraint);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-CYCUNIT-004")]
    public void Ciclo_MinimoEscolheUmaUnicaArestaEntreTres()
    {
        var a = new ObjectName("public", "a");
        var b = new ObjectName("public", "b");
        var c = new ObjectName("public", "c");
        var edges = new[]
        {
            Edge(a, b, nullable: false), Edge(b, c, nullable: false), Edge(c, a, nullable: true),
        };

        var resolution = CycleResolver.Resolve([a, b, c], edges, allowDeferred: false, allowDisableChecks: false);

        resolution.Breakable.ShouldBeTrue();
        resolution.Deferred.Count.ShouldBe(1);
        resolution.Deferred[0].Edge.Child.ShouldBe(c);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-CYCUNIT-005")]
    public void Ciclo_AnelGrandeUsaBuscaGulosaEAdiaUmaAresta()
    {
        // 13 arestas anuláveis em anel: acima do limite da busca exaustiva, então a heurística gulosa decide.
        var nodes = Enumerable.Range(0, 13).Select(i => new ObjectName("public", "n" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var edges = Enumerable.Range(0, 13).Select(i => Edge(nodes[i], nodes[(i + 1) % 13], nullable: true)).ToList();

        var resolution = CycleResolver.Resolve(nodes, edges, allowDeferred: false, allowDisableChecks: false);

        resolution.Breakable.ShouldBeTrue();
        resolution.Deferred.Count.ShouldBe(1);
    }

    // ---------- planejador (C06, 16 §10) ----------

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-PLNUNIT-001")]
    public void Planejador_LojaSimplesOrdenaPaisAntesDosFilhosEHashEstavel()
    {
        var input = LojaInput();

        var first = new GenerationPlanner().Plan(input);
        var second = new GenerationPlanner().Plan(input);

        first.IsBlocked.ShouldBeFalse();
        var insertOrder = first.Plan!.Steps.OfType<InsertTableStep>().Select(s => s.Table.Name).ToList();
        insertOrder.IndexOf("cliente").ShouldBeLessThan(insertOrder.IndexOf("pedido"));
        insertOrder.IndexOf("pedido").ShouldBeLessThan(insertOrder.IndexOf("item"));
        first.Plan.PlanHash.ShouldBe(second.Plan!.PlanHash);
        first.Plan.PlanHash.ShouldStartWith("sha256:");
        first.Plan.EstimatedRows.ShouldBe(20 + 1000 + 5000 + 15000);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-PLNUNIT-002")]
    public void Planejador_FkParaPaiExcluidoBloqueiaComPlnFkParentExcluded()
    {
        var input = LojaInput(excludeCliente: true);

        var result = new GenerationPlanner().Plan(input);

        result.IsBlocked.ShouldBeTrue();
        result.Diagnostics.ShouldContain(d => d.Code == "PLN-FK-PARENT-EXCLUDED" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-PLNUNIT-003")]
    public void Planejador_CicloSemAdiamentoBloqueiaComPlnCycleUnbreakable()
    {
        var a = Tbl("a", [Col("id", 1), Col("b_id", 2)], ["id"], Fk("fk_a_b", ["b_id"], new ObjectName("public", "b"), ["id"], nullable: false));
        var b = Tbl("b", [Col("id", 1), Col("a_id", 2)], ["id"], Fk("fk_b_a", ["a_id"], new ObjectName("public", "a"), ["id"], nullable: false));
        var input = new PlanningInput(Schema(a, b), new Dictionary<string, ColumnBinding>(),
            Profile(new Dictionary<string, TableSettings>
            {
                ["public.a"] = new(true, 10),
                ["public.b"] = new(true, 10),
            }), Target(), "ref");

        var result = new GenerationPlanner().Plan(input);

        result.IsBlocked.ShouldBeTrue();
        result.Diagnostics.ShouldContain(d => d.Code == "PLN-CYCLE-UNBREAKABLE");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-PLNUNIT-004")]
    public void Planejador_CicloAnulavelGeraDeferredUpdateEAvisoInfo()
    {
        var a = Tbl("a", [Col("id", 1), Col("b_id", 2, nullable: true)], ["id"], Fk("fk_a_b", ["b_id"], new ObjectName("public", "b"), ["id"], nullable: true));
        var b = Tbl("b", [Col("id", 1), Col("a_id", 2)], ["id"], Fk("fk_b_a", ["a_id"], new ObjectName("public", "a"), ["id"], nullable: false));
        var input = new PlanningInput(Schema(a, b), new Dictionary<string, ColumnBinding>(),
            Profile(new Dictionary<string, TableSettings>
            {
                ["public.a"] = new(true, 10),
                ["public.b"] = new(true, 10),
            }), Target(), "ref");

        var result = new GenerationPlanner().Plan(input);

        result.IsBlocked.ShouldBeFalse();
        result.Plan!.Steps.OfType<DeferredUpdateStep>().Single().ForeignKeyName.ShouldBe("fk_a_b");
        result.Diagnostics.ShouldContain(d => d.Code == "PLN-CYCLE-NULLUPDATE" && d.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-PLNUNIT-005")]
    public void Planejador_ChaveSerialGeraResyncDeSequence()
    {
        var table = Tbl("cliente", [Col("id", 1, identity: new IdentityInfo(IdentityKind.Serial, SequenceName: "public.cliente_id_seq"))], ["id"]);
        var input = new PlanningInput(Schema(table), new Dictionary<string, ColumnBinding>(),
            Profile(new Dictionary<string, TableSettings> { ["public.cliente"] = new(true, 10) }),
            Target(seq: new Dictionary<string, long?> { ["public.cliente_id_seq"] = 42 }), "ref");

        var result = new GenerationPlanner().Plan(input);

        result.Plan!.Steps.OfType<ResyncSequenceStep>().Single().Sequence.ShouldBe("public.cliente_id_seq");
        result.Plan.Tables[Cliente].Keys.Single().Start.ShouldBe(43);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-PLNUNIT-006")]
    public void Planejador_TabelaComZeroLinhasEmiteInfo()
    {
        var table = Tbl("cliente", [Col("id", 1)], ["id"]);
        var input = new PlanningInput(Schema(table), new Dictionary<string, ColumnBinding>(),
            Profile(new Dictionary<string, TableSettings> { ["public.cliente"] = new(true, 0) }), Target(), "ref");

        var result = new GenerationPlanner().Plan(input);

        result.Diagnostics.ShouldContain(d => d.Code == "PLN-NO-ROWS" && d.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-PLNUNIT-007")]
    public void Planejador_MudancaDeLinhasAlteraOHash()
    {
        var baseline = new GenerationPlanner().Plan(LojaInput()).Plan!.PlanHash;
        var changed = new GenerationPlanner().Plan(LojaInput(clienteRows: 2000)).Plan!.PlanHash;

        changed.ShouldNotBe(baseline);
    }

    // ---------- fixtures ----------

    private static PlanningInput LojaInput(bool excludeCliente = false, long clienteRows = 1000)
    {
        var categoria = Tbl("categoria", [Col("id", 1), Col("pai_id", 2, nullable: true)], ["id"],
            Fk("fk_categoria_pai", ["pai_id"], Categoria, ["id"], nullable: true));
        var cliente = Tbl("cliente", [Col("id", 1)], ["id"]);
        var pedido = Tbl("pedido", [Col("id", 1), Col("cliente_id", 2)], ["id"],
            Fk("fk_pedido_cliente", ["cliente_id"], Cliente, ["id"], nullable: false));
        var item = Tbl("item", [Col("id", 1), Col("pedido_id", 2)], ["id"],
            Fk("fk_item_pedido", ["pedido_id"], Pedido, ["id"], nullable: false));

        var tables = new Dictionary<string, TableSettings>
        {
            ["public.categoria"] = new(true, 20),
            ["public.pedido"] = new(true, 5000),
            ["public.item"] = new(true, 15000),
        };
        if (!excludeCliente)
        {
            tables["public.cliente"] = new(true, clienteRows);
        }

        return new PlanningInput(Schema(categoria, cliente, pedido, item), new Dictionary<string, ColumnBinding>(),
            Profile(tables), Target(), "ref");
    }

    private static SchemaModel Schema(params TableModel[] tables) =>
        SchemaModel.Create(DatabaseEngine.PostgreSql, "17", "loja",
            [NamespaceModel.Create("public", false, tables.ToList()).Value], DateTimeOffset.UnixEpoch).Value;

    private static TableModel Tbl(string name, ColumnModel[] columns, string[] pk, params ForeignKeyModel[] fks) =>
        TableModel.Create(name, columns, new PrimaryKeyModel(null, pk), fks.ToList()).Value;

    private static ColumnModel Col(string name, int ordinal, bool nullable = false, IdentityInfo? identity = null) =>
        ColumnModel.Create(name, ordinal, NativeTypeParser.Parse("integer"), nullable, identity: identity).Value;

    private static DataTypeInfo Int(string type) => NativeTypeParser.Parse(type);

    private static ForeignKeyModel Fk(string name, string[] columns, ObjectName referenced, string[] referencedColumns, bool nullable) =>
        new(name, columns, referenced, referencedColumns);

    private static DependencyEdge Edge(ObjectName child, ObjectName parent, bool self = false, bool nullable = false, bool deferrable = false) =>
        new(child, parent, child.Name + "_fk_" + parent.Name, nullable, deferrable, self || child == parent, ParentIncluded: true);

    private static GenerationProfile Profile(Dictionary<string, TableSettings> tables) =>
        new("loja", 1UL, DateTimeOffset.UnixEpoch, ReferenceInstantMode.Fixed, "UTC", tables,
            KeyStrategy.Explicit, new OutputSettings(OutputKind.SqlScript), new ValidationSettings());

    private static TargetState Target(IReadOnlyDictionary<string, long?>? seq = null) =>
        new(DatabaseEngine.PostgreSql, "17", "fp",
            new ProviderCapabilities(true, true, true, true, false, true, true, true, 65535, 63),
            new Dictionary<string, long?>(), seq ?? new Dictionary<string, long?>(),
            new Dictionary<string, IReadOnlyList<TriggerModel>>(), IsOffline: false);
}
