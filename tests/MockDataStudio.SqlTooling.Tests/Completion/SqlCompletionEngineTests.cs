using MockDataStudio.SqlTooling.Completion;
using MockDataStudio.SqlTooling.Dialect;
using Shouldly;
using Xunit;

namespace MockDataStudio.SqlTooling.Tests.Completion;

// C6.2 (docs/20): autocomplete básico sobre um catálogo capturado. Sem banco.
public sealed class SqlCompletionEngineTests
{
    private static readonly CompletionCatalog Catalog = new(
        ["cliente", "pedido", "vw_pedidos"],
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["cliente"] = ["id", "nome", "email"],
            ["pedido"] = ["id", "cliente_id", "total"],
        });

    [Fact]
    [Trait("Category", "Unit")]
    public void InicioDeConsulta_SugereKeywordsERelacoes()
    {
        var result = SqlCompletionEngine.Complete("", 0, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.ShouldContain(i => i.Label == "SELECT" && i.Kind == CompletionKind.Keyword);
        result.Items.ShouldContain(i => i.Label == "cliente" && i.Kind == CompletionKind.Relation);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PrefixoDeRelacao_FiltraSemDistinguirCaixa()
    {
        const string text = "SELECT * FROM CL";

        var result = SqlCompletionEngine.Complete(text, text.Length, Catalog, SqlDialectFeatures.Sqlite);

        result.ReplaceStart.ShouldBe(text.Length - 2);
        result.Items.Select(i => i.Label).ShouldBe(["cliente"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DepoisDeFrom_SugereSoRelacoes()
    {
        const string text = "SELECT * FROM ";

        var result = SqlCompletionEngine.Complete(text, text.Length, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.ShouldAllBe(i => i.Kind == CompletionKind.Relation);
        result.Items.Select(i => i.Label).ShouldContain("pedido");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QualificadorComApelido_SugereColunasDaRelacaoResolvida()
    {
        const string text = "SELECT c. FROM cliente c";
        var caret = text.IndexOf('.', StringComparison.Ordinal) + 1;

        var result = SqlCompletionEngine.Complete(text, caret, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.Select(i => i.Label).ShouldBe(["email", "id", "nome"]);
        result.Items.ShouldAllBe(i => i.Kind == CompletionKind.Column);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QualificadorComNomeDaRelacao_SugereColunas()
    {
        const string text = "SELECT pedido.to FROM pedido";
        var caret = text.IndexOf("to", StringComparison.Ordinal) + 2;

        var result = SqlCompletionEngine.Complete(text, caret, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.Select(i => i.Label).ShouldBe(["total"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ColunasDasRelacoesCitadas_AparecemNoMeioDaConsulta()
    {
        const string text = "SELECT n FROM cliente";

        var result = SqlCompletionEngine.Complete(text, "SELECT n".Length, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.ShouldContain(i => i.Label == "nome" && i.Kind == CompletionKind.Column);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DentroDeLiteral_NaoSugereNada()
    {
        const string text = "SELECT * FROM cliente WHERE nome = 'ana";

        var result = SqlCompletionEngine.Complete(text, text.Length, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DentroDeComentario_NaoSugereNada()
    {
        const string text = "-- comentário com cli";

        var result = SqlCompletionEngine.Complete(text, text.Length, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QualificadorDesconhecido_NaoSugereNada()
    {
        const string text = "SELECT x. FROM cliente c";
        var caret = text.IndexOf('.', StringComparison.Ordinal) + 1;

        var result = SqlCompletionEngine.Complete(text, caret, Catalog, SqlDialectFeatures.Sqlite);

        result.Items.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resultado_EhDeterministicoEOrdenado()
    {
        const string text = "SELECT * FROM cliente c, pedido p WHERE ";

        var first = SqlCompletionEngine.Complete(text, text.Length, Catalog, SqlDialectFeatures.Sqlite);
        var second = SqlCompletionEngine.Complete(text, text.Length, Catalog, SqlDialectFeatures.Sqlite);

        first.Items.ShouldBe(second.Items);
        first.Items.Where(i => i.Kind == CompletionKind.Keyword).Select(i => i.Label)
            .ShouldBe(first.Items.Where(i => i.Kind == CompletionKind.Keyword).Select(i => i.Label).OrderBy(l => l, StringComparer.OrdinalIgnoreCase));
    }
}
