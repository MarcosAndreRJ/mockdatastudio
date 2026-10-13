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

// A2r.1 (FKs compostas e sobrepostas, docs/16 §7): registro das FKs da tabela e PLN-FK-OVERLAP.
public sealed class ForeignKeyPlanningTests
{
    private static readonly ObjectName Pedido = new("public", "pedido");
    private static readonly ObjectName Cliente = new("public", "cliente");
    private static readonly ObjectName ItemPedido = new("public", "item_pedido");
    private static readonly ObjectName Entrega = new("public", "entrega");

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-FKCOMP-001")]
    public void FkCompostaEhRegistradaComoComposta()
    {
        var plan = new GenerationPlanner().Plan(Input(Schema(
            Tbl("pedido", ["id"], Col("id", 1)),
            Tbl("item_pedido", ["pedido_id", "item_seq"], Col("pedido_id", 1), Col("item_seq", 2),
                Fk("fk_item_pedido", ["pedido_id"], Pedido, ["id"])),
            Tbl("entrega", ["id"], Col("id", 1), Col("pedido_id", 2), Col("item_seq", 3),
                Fk("fk_entrega_item", ["pedido_id", "item_seq"], ItemPedido, ["pedido_id", "item_seq"])))));

        plan.IsBlocked.ShouldBeFalse();
        var fk = plan.Plan!.Tables[Entrega].ForeignKeys!.Single();
        fk.Composite.ShouldBeTrue();
        fk.ChildColumns.ShouldBe(["pedido_id", "item_seq"]);
        fk.Parent.ShouldBe(ItemPedido);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-FKOVL-001")]
    public void SobreposicaoImpliadaPelaTabelaPaiNaoBloqueia()
    {
        // entrega.pedido_id aparece na FK composta (→ item_pedido) e na FK simples (→ pedido).
        // item_pedido já tem FK (pedido_id) → pedido(id): a FK simples é implicada pela composta.
        var plan = new GenerationPlanner().Plan(Input(Schema(
            Tbl("pedido", ["id"], Col("id", 1)),
            Tbl("item_pedido", ["pedido_id", "item_seq"], Col("pedido_id", 1), Col("item_seq", 2),
                Fk("fk_item_pedido", ["pedido_id"], Pedido, ["id"])),
            Tbl("entrega", ["id"], Col("id", 1), Col("pedido_id", 2), Col("item_seq", 3),
                Fk("fk_entrega_item", ["pedido_id", "item_seq"], ItemPedido, ["pedido_id", "item_seq"]),
                Fk("fk_entrega_pedido", ["pedido_id"], Pedido, ["id"])))));

        plan.IsBlocked.ShouldBeFalse();
        plan.Diagnostics.ShouldNotContain(d => d.Code == "PLN-FK-OVERLAP");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-FKOVL-002")]
    public void SobreposicaoNaoImpliadaBloqueiaComPlnFkOverlap()
    {
        // entrega.pedido_id vai para item_pedido (composta) e para cliente(id): nada na estrutura liga as duas.
        var plan = new GenerationPlanner().Plan(Input(Schema(
            Tbl("cliente", ["id"], Col("id", 1)),
            Tbl("pedido", ["id"], Col("id", 1)),
            Tbl("item_pedido", ["pedido_id", "item_seq"], Col("pedido_id", 1), Col("item_seq", 2),
                Fk("fk_item_pedido", ["pedido_id"], Pedido, ["id"])),
            Tbl("entrega", ["id"], Col("id", 1), Col("pedido_id", 2), Col("item_seq", 3),
                Fk("fk_entrega_item", ["pedido_id", "item_seq"], ItemPedido, ["pedido_id", "item_seq"]),
                Fk("fk_entrega_cliente", ["pedido_id"], Cliente, ["id"])))));

        plan.IsBlocked.ShouldBeTrue();
        plan.Diagnostics.ShouldContain(d => d.Code == "PLN-FK-OVERLAP" && d.Severity == DiagnosticSeverity.Error);
    }

    private static PlanningInput Input(SchemaModel schema)
    {
        var tables = schema.Namespaces.SelectMany(n => n.Tables).Select(t => t.Name).ToDictionary(
            n => new ObjectName("public", n).CanonicalId, _ => new TableSettings(true, 10));
        var profile = new GenerationProfile("fk", 1UL, DateTimeOffset.UnixEpoch, ReferenceInstantMode.Fixed, "UTC", tables,
            KeyStrategy.Explicit, new OutputSettings(OutputKind.SqlScript), new ValidationSettings());
        var target = new TargetState(DatabaseEngine.PostgreSql, "17", "fp",
            new ProviderCapabilities(true, true, true, true, false, true, true, true, 65535, 63),
            new Dictionary<string, long?>(), new Dictionary<string, long?>(),
            new Dictionary<string, IReadOnlyList<TriggerModel>>(), IsOffline: false);
        return new PlanningInput(schema, new Dictionary<string, ColumnBinding>(), profile, target, "ref");
    }

    private static SchemaModel Schema(params TableModel[] tables) =>
        SchemaModel.Create(DatabaseEngine.PostgreSql, "17", "loja",
            [NamespaceModel.Create("public", false, tables.ToList()).Value], DateTimeOffset.UnixEpoch).Value;

    private static TableModel Tbl(string name, string[] pk, params object[] parts)
    {
        var columns = parts.OfType<ColumnModel>().ToList();
        var fks = parts.OfType<ForeignKeyModel>().ToList();
        return TableModel.Create(name, columns, new PrimaryKeyModel(null, pk), fks).Value;
    }

    private static ColumnModel Col(string name, int ordinal) =>
        ColumnModel.Create(name, ordinal, NativeTypeParser.Parse("integer"), isNullable: false).Value;

    private static ForeignKeyModel Fk(string name, string[] columns, ObjectName referenced, string[] referencedColumns) =>
        new(name, columns, referenced, referencedColumns);
}
