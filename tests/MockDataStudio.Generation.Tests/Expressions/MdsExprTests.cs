using System.Text;
using MockDataStudio.Core.Common;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Expressions;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Generation.Random;
using MockDataStudio.Generation.Tests.TestSupport;
using Shouldly;
using Xunit;
using static MockDataStudio.Generation.Tests.TestSupport.GenerationFixtures;

namespace MockDataStudio.Generation.Tests.Expressions;

// MDS-Expr (docs/15 §9): gramática e precedência (EXPR-001), semântica e funções (EXPR-002), limites (EXPR-003).
public sealed class MdsExprTests
{
    private static readonly ExprEnvironment Env = new(PackTemplates(), Reference, Zone, 99UL, "main.cliente");

    [Theory]
    [InlineData("1 + 2 * 3", 7L)]
    [InlineData("(1 + 2) * 3", 9L)]
    [InlineData("-2 + 5", 3L)]
    [InlineData("7 % 4", 3L)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-001")]
    public void T_GEN_EXPR_001_PrecedenciaAritmetica(string source, long expected) =>
        Evaluate(source, new RowContext(0)).ShouldBe(expected);

    [Theory]
    [InlineData("not true or true", true)]
    [InlineData("1 < 2 and 2 < 3", true)]
    [InlineData("'a' || 'b' = 'ab'", true)]
    [InlineData("3 >= 4", false)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-001")]
    public void T_GEN_EXPR_001_PrecedenciaLogicaEComparacao(string source, bool expected) =>
        Evaluate(source, new RowContext(0)).ShouldBe(expected);

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-001")]
    public void T_GEN_EXPR_001_ModoTemplateSubstituiBlocosEEscapaChaves()
    {
        var compiled = CompiledExpr.Compile("Olá {1 + 1}! Use {{chaves}}.", ExprMode.Template, Env);
        compiled.IsSuccess.ShouldBeTrue();
        var cell = Cell(new RowContext(0));
        compiled.Value.Evaluate(ref cell).ShouldBe("Olá 2! Use {chaves}.");
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("'sem fim")]
    [InlineData("@")]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-001")]
    public void T_GEN_EXPR_001_SintaxeInvalidaRetornaGenExprSyntax(string source)
    {
        var result = CompiledExpr.Compile(source, ExprMode.Expression, Env);
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.GenExprSyntax);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-002")]
    public void T_GEN_EXPR_002_NullPropagaEmAritmeticaEConcatenacaoECoalesceResolve()
    {
        var row = new RowContext(0);
        row.Set("a", null);
        Evaluate("@a + 1", row).ShouldBeNull();
        Evaluate("'x' || @a", row).ShouldBeNull();
        Evaluate("coalesce(@a, 'fallback')", row).ShouldBe("fallback");
        Evaluate("concat('a', @a, 'b')", row).ShouldBe("ab");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-002")]
    public void T_GEN_EXPR_002_TiposDiferentesEDivisaoPorZeroFalhamComGenExprType()
    {
        AssertRuntimeError("1 = 'a'", ErrorCodes.GenExprType);
        AssertRuntimeError("1 / 0", ErrorCodes.GenExprType);
        AssertRuntimeError("'a' + 'b'", ErrorCodes.GenExprType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-002")]
    public void T_GEN_EXPR_002_FuncoesDeTextoEDataSeguemAsRegras()
    {
        Evaluate("lower(' AbC ')", new RowContext(0)).ShouldBe(" abc ");
        Evaluate("slug('Ação Fictícia 2')", new RowContext(0)).ShouldBe("acao.ficticia.2");
        Evaluate("substr('abcdef', 2, 3)", new RowContext(0)).ShouldBe("bcd");
        Evaluate("pad_left('42', 5, '0')", new RowContext(0)).ShouldBe("00042");
        Evaluate("format_date(date_add(ref_now(), 'P1D'), 'yyyy-MM-dd')", new RowContext(0)).ShouldBe("2026-10-13");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-002")]
    public void T_GEN_EXPR_002_RowIndexEColunaDaLinhaAtual()
    {
        var row = new RowContext(7);
        row.Set("nome", "Ana");
        Evaluate("row_index() * 2", row).ShouldBe(14L);
        Evaluate("lower(@nome) || '@teste.dev'", row).ShouldBe("ana@teste.dev");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-003")]
    public void T_GEN_EXPR_003_LimitesDeTamanhoEProfundidadeSaoAplicados()
    {
        var big = new string('1', 4097);
        CompiledExpr.Compile(big, ExprMode.Expression, Env).Error.Code.ShouldBe(ErrorCodes.GenExprLimit);

        var deep = new string('(', 70) + "1" + new string(')', 70);
        CompiledExpr.Compile(deep, ExprMode.Expression, Env).Error.Code.ShouldBe(ErrorCodes.GenExprLimit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-003")]
    public void T_GEN_EXPR_003_FuncaoDeIoEReflexaoNaoExiste()
    {
        CompiledExpr.Compile("read_file('/etc/passwd')", ExprMode.Expression, Env).Error.Code.ShouldBe(ErrorCodes.GenExprUnknownFn);
        CompiledExpr.Compile("lower('a', 'b')", ExprMode.Expression, Env).Error.Code.ShouldBe(ErrorCodes.GenExprSyntax);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-EXPR-003")]
    public void T_GEN_EXPR_003_ColunaDesconhecidaEhRejeitadaQuandoListaInformada()
    {
        var result = CompiledExpr.Compile("@inexistente + 1", ExprMode.Expression, Env, knownColumns: ["nome"]);
        result.Error.Code.ShouldBe(ErrorCodes.GenExprColumn);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-GEN-EXPR-001")]
    public async Task T_GEN_EXPR_001_CoreFormatGeraEmailDerivadoNoMotor()
    {
        var table = Table("cliente", [
            Plan(Col("nome", "varchar(60)", ordinal: 1), "catalog.template", Obj(("template", "NOME"))),
            Plan(Col("email", "varchar(120)", ordinal: 2), "core.format",
                Obj(("template", "{slug(@nome)}.{row_index()}@{template('EMAIL_DOMINIO')}"))),
        ], rows: 50);
        var sink = new MemorySink();

        var result = await new GenerationEngine(Registry()).RunAsync(table, sink, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        var domains = Enumerable.Range(0, PackTemplates()["EMAIL_DOMINIO"].Count)
            .Select(i => PackTemplates()["EMAIL_DOMINIO"].GetScalar(i)).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < sink.Rows.Count; i++)
        {
            var parts = ((string)sink.Rows[i][1]!).Split('@');
            parts.Length.ShouldBe(2);
            parts[0].ShouldEndWith("." + i);
            domains.ShouldContain(parts[1]);
        }
    }

    private static object? Evaluate(string source, RowContext row)
    {
        var compiled = CompiledExpr.Compile(source, ExprMode.Expression, Env);
        compiled.IsSuccess.ShouldBeTrue(compiled.IsSuccess ? string.Empty : compiled.Error.Message);
        var cell = Cell(row);
        return compiled.Value.Evaluate(ref cell);
    }

    private static void AssertRuntimeError(string source, string code)
    {
        var compiled = CompiledExpr.Compile(source, ExprMode.Expression, Env);
        compiled.IsSuccess.ShouldBeTrue();
        var cell = Cell(new RowContext(0));
        try
        {
            compiled.Value.Evaluate(ref cell);
        }
        catch (ExprRuntimeException error)
        {
            error.Code.ShouldBe(code);
            return;
        }

        throw new Xunit.Sdk.XunitException("Esperava erro de avaliação.");
    }

    private static GenerationCell Cell(RowContext row) =>
        new(row.RowIndex, DeterministicRandom.For(StreamKey.Derive(1, "x", "value"), row.RowIndex), row);
}
