using System.Text.Json.Nodes;
using MockDataStudio.Application;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests;

// C7a.2 (migração de bindings ao trocar de snapshot): mantido, renomeado, revisar e órfão. Sem banco.
public sealed class BindingMigratorTests
{
    private static readonly ObjectName Cliente = new("main", "cliente");
    private static readonly StructureHasher Hasher = new();

    [Fact]
    [Trait("Category", "Unit")]
    public void SchemaIgual_MantemTodosOsVinculosAtivos()
    {
        var before = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("nome", 2, "varchar(60)")));
        var bindings = Bindings(before, "nome");

        var result = BindingMigrator.Migrate(bindings, before, Hasher);

        result.Entries.ShouldAllBe(e => e.Outcome == MigrationOutcome.Kept);
        result.Bindings["main.cliente.nome"].State.ShouldBe(BindingState.Active);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ColunaRenomeadaComMesmaEstrutura_MigraParaONovoNome()
    {
        var before = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("nome", 2, "varchar(60)")));
        var after = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("nome_completo", 2, "varchar(60)")));
        var bindings = Bindings(before, "nome");

        var result = BindingMigrator.Migrate(bindings, after, Hasher);

        result.Entries.Single(e => e.FromId == "main.cliente.nome").Outcome.ShouldBe(MigrationOutcome.Renamed);
        result.Bindings.ContainsKey("main.cliente.nome_completo").ShouldBeTrue();
        result.Bindings["main.cliente.nome_completo"].State.ShouldBe(BindingState.Active);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EstruturaMudou_VinculoVaiParaRevisao()
    {
        var before = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("nome", 2, "varchar(60)")));
        var after = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("nome", 2, "varchar(120)")));
        var bindings = Bindings(before, "nome");

        var result = BindingMigrator.Migrate(bindings, after, Hasher);

        result.Entries.Single().Outcome.ShouldBe(MigrationOutcome.NeedsReview);
        result.Bindings["main.cliente.nome"].State.ShouldBe(BindingState.NeedsReview);
        result.Bindings["main.cliente.nome"].Note.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ColunaRemovidaSemCandidato_FicaOrfa()
    {
        var before = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("email", 2, "varchar(200)")));
        var after = Schema(Tbl("cliente", Col("id", 1, "integer")));
        var bindings = Bindings(before, "email");

        var result = BindingMigrator.Migrate(bindings, after, Hasher);

        result.Entries.Single().Outcome.ShouldBe(MigrationOutcome.Orphaned);
        result.Bindings["main.cliente.email"].State.ShouldBe(BindingState.Orphaned);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TabelaRemovida_FicaOrfa()
    {
        var before = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("nome", 2, "varchar(60)")));
        var after = Schema(Tbl("pedido", Col("id", 1, "integer")));
        var bindings = Bindings(before, "nome");

        var result = BindingMigrator.Migrate(bindings, after, Hasher);

        result.Bindings["main.cliente.nome"].State.ShouldBe(BindingState.Orphaned);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenomeacaoAmbigua_NaoAdivinha_FicaOrfa()
    {
        // Duas colunas novas com a mesma estrutura: não há como saber qual é a renomeada.
        var before = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("nome", 2, "varchar(60)")));
        var after = Schema(Tbl("cliente", Col("id", 1, "integer"), Col("primeiro", 2, "varchar(60)"), Col("segundo", 3, "varchar(60)")));
        var bindings = Bindings(before, "nome");

        var result = BindingMigrator.Migrate(bindings, after, Hasher);

        result.Entries.Single().Outcome.ShouldBe(MigrationOutcome.Orphaned);
    }

    private static Dictionary<string, ColumnBinding> Bindings(SchemaModel schema, params string[] columns)
    {
        var table = schema.FindTable(Cliente)!;
        var result = new Dictionary<string, ColumnBinding>(StringComparer.Ordinal);
        foreach (var name in columns)
        {
            var column = table.FindColumn(name)!;
            var reference = new ColumnRef(Cliente, name);
            result[reference.CanonicalId] = new ColumnBinding(
                reference,
                new GeneratorSpec("core.random.string", 1, new JsonObject()),
                NullRate: 0,
                Origin: BindingOrigin.Auto,
                ColumnFingerprint: Hasher.ComputeColumnFingerprint(table, column));
        }

        return result;
    }

    private static SchemaModel Schema(params TableModel[] tables) =>
        SchemaModel.Create(DatabaseEngine.Sqlite, "3", "loja",
            [NamespaceModel.Create("main", false, tables.ToList()).Value], DateTimeOffset.UnixEpoch).Value;

    private static TableModel Tbl(string name, params ColumnModel[] columns) =>
        TableModel.Create(name, columns, new PrimaryKeyModel(null, ["id"])).Value;

    private static ColumnModel Col(string name, int ordinal, string type) =>
        ColumnModel.Create(name, ordinal, NativeTypeParser.Parse(type), isNullable: false).Value;
}
