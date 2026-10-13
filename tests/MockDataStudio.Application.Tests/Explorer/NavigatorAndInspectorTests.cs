using MockDataStudio.Application.Explorer;
using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.Core.Naming;
using MockDataStudio.Providers;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// Navegação sob demanda e Object Inspector sobre SQLite real (prompt, seções 4, 5 e 12).
public sealed class NavigatorAndInspectorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Navegacao_ListaEsquemasTabelasViewsETriggersReais()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);
        var ct = TestContext.Current.CancellationToken;

        (await navigator.ListSchemasAsync(connection, ct)).ShouldBe(["main"]);
        (await navigator.ListTablesAsync(connection, "main", ct)).Select(o => o.Name).ShouldBe(["cliente", "pedido"]);
        (await navigator.ListViewsAsync(connection, "main", ct)).Select(o => o.Name).ShouldBe(["vw_pedidos"]);
        (await navigator.ListTriggersAsync(connection, "main", ct)).Select(t => t.Name).ShouldBe(["trg_pedido"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Catalogo_AgrupaTodosOsObjetosPorSchemaNumaPassada()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);

        var catalog = await navigator.LoadCatalogAsync(connection, TestContext.Current.CancellationToken);

        var main = catalog.ShouldHaveSingleItem();
        main.Schema.ShouldBe("main");
        main.Tables.Select(o => o.Name).ShouldBe(["cliente", "pedido"]);
        main.Views.Select(o => o.Name).ShouldBe(["vw_pedidos"]);
        main.Triggers.Select(t => t.Name).ShouldBe(["trg_pedido"]);
        main.Routines.ShouldBeEmpty();
        main.Sequences.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Navegacao_SchemaInexistenteNaoRetornaObjetos()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);

        (await navigator.ListTablesAsync(connection, "inexistente", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Navegacao_DescricaoDeTabelaEhCacheadaAteRefresh()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);
        var ct = TestContext.Current.CancellationToken;
        var table = new ObjectName("main", "pedido");

        var first = (await navigator.DescribeTableAsync(connection, table, ct)).Value;
        var second = (await navigator.DescribeTableAsync(connection, table, ct)).Value;
        ReferenceEquals(first, second).ShouldBeTrue("A segunda leitura deve vir do cache da sessão.");

        navigator.Refresh();
        var third = (await navigator.DescribeTableAsync(connection, table, ct)).Value;
        ReferenceEquals(first, third).ShouldBeFalse("Refresh deve descartar o cache.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Navegacao_TabelaInexistenteRetornaFalhaSemLancar()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);

        var result = await navigator.DescribeTableAsync(connection, new ObjectName("main", "nao_existe"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Inspector_ColunasChavesEIdentidadeVemDoBancoReal()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);
        var model = (await navigator.DescribeTableAsync(connection, new ObjectName("main", "cliente"), TestContext.Current.CancellationToken)).Value;

        var inspection = ObjectInspector.Inspect(new ObjectName("main", "cliente"), model);

        var id = inspection.Columns.Single(c => c.Name == "id");
        id.IsPrimaryKey.ShouldBeTrue();
        id.IsIdentity.ShouldBeTrue();
        id.Nullable.ShouldBeFalse();

        var nome = inspection.Columns.Single(c => c.Name == "nome");
        nome.NativeType.ShouldBe("text");
        nome.Nullable.ShouldBeFalse();

        var email = inspection.Columns.Single(c => c.Name == "email");
        email.IsUnique.ShouldBeTrue();
        email.Nullable.ShouldBeTrue();

        inspection.Columns.Single(c => c.Name == "ativo").DefaultValue.ShouldBe("1");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Inspector_IndicesEForeignKeysVemDoBancoReal()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);
        var model = (await navigator.DescribeTableAsync(connection, new ObjectName("main", "pedido"), TestContext.Current.CancellationToken)).Value;

        var inspection = ObjectInspector.Inspect(new ObjectName("main", "pedido"), model);

        inspection.Indexes.Single(i => i.Name == "ix_pedido_cliente").Columns.ShouldBe(["cliente_id"]);

        var fk = inspection.ForeignKeys.Single();
        fk.Columns.ShouldBe(["cliente_id"]);
        fk.Referenced.Name.ShouldBe("cliente");
        fk.ReferencedColumns.ShouldBe(["id"]);
        fk.OnDelete.ShouldBe("CASCADE");
        fk.OnUpdate.ShouldBe("NO ACTION");
        inspection.Columns.Single(c => c.Name == "cliente_id").IsForeignKey.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Preview_RetornaRegistrosReaisRespeitandoLimite()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var navigator = new DatabaseNavigator(bench.Provider);
        var table = new ObjectName("main", "cliente");
        var model = (await navigator.DescribeTableAsync(connection, table, TestContext.Current.CancellationToken)).Value;

        var sql = TablePreview.BuildSql(table, model, name => "\"" + name + "\"", limit: 2);
        sql.ShouldBe("SELECT \"id\", \"nome\", \"email\", \"ativo\" FROM \"cliente\" LIMIT 2");

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = 0;
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows++;
        }

        rows.ShouldBe(2);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 50)]
    [InlineData(5000, TablePreview.MaxRows)]
    [Trait("Category", "Unit")]
    public void Preview_LimiteEhLimitadoAoIntervaloPermitido(int requested, int expected) =>
        TablePreview.ClampLimit(requested).ShouldBe(expected);

    [Fact]
    [Trait("Category", "Unit")]
    public void Preview_NuncaGeraSelectSemLimite()
    {
        var model = MockDataStudio.Core.Schema.TableModel.Create("t",
            [MockDataStudio.Core.Schema.ColumnModel.Create("a", 1,
                MockDataStudio.Core.Types.NativeTypeParser.Parse("integer"), true).Value]).Value;
        TablePreview.BuildSql(new ObjectName("main", "t"), model, n => n, 10).ShouldContain("LIMIT 10");
    }
}
