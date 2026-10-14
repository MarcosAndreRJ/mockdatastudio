using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Planning;
using TablePlan = MockDataStudio.Generation.Planning.TablePlan;
using Shouldly;
using Xunit;

namespace MockDataStudio.Generation.Tests.Engine;

// A2r.1 (KeyOracle de chaves compostas, docs/16 §5.3): item_pedido = (pedido_id do grupo, item_seq dentro do grupo).
public sealed class CompositeKeyOracleTests
{
    private static readonly ObjectName Pedido = new("public", "pedido");
    private static readonly ObjectName ItemPedido = new("public", "item_pedido");

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYCOMP-001")]
    public void IndiceDeGruposResolveGrupoESequencia()
    {
        // Pedido 0 tem 2 itens, pedido 1 nenhum, pedido 2 tem 3 itens.
        var index = new ChildSequenceIndex([2, 0, 3]);

        index.RowCount.ShouldBe(5);
        Enumerable.Range(0, 5).Select(r => index.GroupOf(r)).ShouldBe([0, 0, 2, 2, 2]);
        Enumerable.Range(0, 5).Select(r => index.SeqOf(r)).ShouldBe([1L, 2L, 1L, 2L, 3L]);
        Should.Throw<ArgumentOutOfRangeException>(() => index.GroupOf(5));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYCOMP-002")]
    public void ChaveCompostaCombinaChaveDoPaiESequenciaDoGrupo()
    {
        var pedidoPlan = new KeyColumnPlan("id", KeyStrategy.Explicit, 10, 1, false, null);
        var pedidos = new ExplicitKeyOracle(Plan(Pedido, pedidoPlan, rows: 3));
        var grouping = new ChildSequenceIndex([2, 0, 3]);
        var oracle = new CompositeKeyOracle();
        oracle.Register(ItemPedido,
        [
            new ParentKeyComponent("pedido_id", pedidos, Pedido, "id", grouping),
            new SequenceComponent("item_seq", grouping),
        ], grouping.RowCount);

        var keys = Enumerable.Range(0, 5).Select(r => oracle.GetKey(ItemPedido, ["pedido_id", "item_seq"], r)).ToList();

        keys.Select(k => (k[0], k[1])).ShouldBe([(10L, 1L), (10L, 2L), (12L, 1L), (12L, 2L), (12L, 3L)]);
        oracle.RowCount(ItemPedido).ShouldBe(5);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYCOMP-003")]
    public void ColunasErradasOuTabelaSemChaveCompostaFalham()
    {
        var grouping = new ChildSequenceIndex([1, 1]);
        var oracle = new CompositeKeyOracle();
        oracle.Register(ItemPedido, [new SequenceComponent("item_seq", grouping), new SequenceComponent("linha", grouping)], 2);

        Should.Throw<NotSupportedException>(() => oracle.GetKey(ItemPedido, ["item_seq"], 0));
        Should.Throw<NotSupportedException>(() => oracle.GetKey(Pedido, ["id"], 0));
        Should.Throw<ArgumentException>(() => oracle.Register(Pedido, [new SequenceComponent("a", grouping)], 1));
    }

    private static GenerationPlan Plan(ObjectName table, KeyColumnPlan key, long rows)
    {
        var tablePlan = new TablePlan(table, rows, ["id"], ["id"], [key], [], new Dictionary<string, RelationSettings>(), false);
        return new GenerationPlan([], new Dictionary<ObjectName, TablePlan> { [table] = tablePlan }, [], "sha256:test", rows, 0);
    }
}
