using MockDataStudio.Application.Explorer;
using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.SqlTooling.Completion;
using MockDataStudio.SqlTooling.Dialect;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// C6.2: catálogo do autocomplete a partir do schema real do SQLite, com o motor sobre ele.
public sealed class CompletionCatalogServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Catalogo_TemTabelasViewsEColunasReais()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bench = await SqliteWorkbench.CreateAsync(ct: ct);
        await using var connection = await bench.OpenAsync(ct);

        var catalog = await new CompletionCatalogService(bench.Provider).BuildAsync(connection, ct);

        catalog.Relations.ShouldContain("cliente");
        catalog.Relations.ShouldContain("pedido");
        catalog.Relations.ShouldContain("vw_pedidos");
        catalog.ColumnsOf("cliente").ShouldBe(["id", "nome", "email", "ativo"]);
        catalog.ColumnsOf("pedido").ShouldContain("cliente_id");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Motor_SobreCatalogoReal_SugereColunaDaTabelaCitada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bench = await SqliteWorkbench.CreateAsync(ct: ct);
        await using var connection = await bench.OpenAsync(ct);
        var catalog = await new CompletionCatalogService(bench.Provider).BuildAsync(connection, ct);
        const string text = "SELECT n FROM pedido p JOIN cliente c ON c.id = p.cliente_id";

        var result = SqlCompletionEngine.Complete(text, "SELECT n".Length, catalog, SqlDialectFeatures.Sqlite);

        // "nome" é coluna de cliente, que entrou por JOIN: a sugestão precisa vir com o tipo Column.
        result.Items.ShouldContain(i => i.Label == "nome" && i.Kind == CompletionKind.Column);
    }
}
