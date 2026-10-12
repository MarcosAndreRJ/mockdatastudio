using MockDataStudio.Core.Hashing;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Hashing;

public sealed class ExpressionNormalizerTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("status IN ('A','B')", "status in('A','B')")]
    [InlineData("  STATUS   in ( 'A' , 'B' )  ", "status in('A','B')")]
    [InlineData("(preco >= 0)", "preco >= 0")]
    [InlineData("(a) + (b)", "(a)+(b)")]
    [InlineData("nome = 'Joao  da  Silva'", "nome = 'Joao  da  Silva'")]
    [InlineData("\"Coluna Mista\" = 1", "\"Coluna Mista\" = 1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_ColapsaEspacosELowercasePreservandoLiteraisEIdentificadoresCitados(string? input, string expected)
    {
        ExpressionNormalizer.Normalize(input).ShouldBe(expected);
    }
}
