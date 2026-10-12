using MockDataStudio.Core.Schema;
using MockDataStudio.Generation.Coercion;
using MockDataStudio.Generation.Generators;
using Shouldly;
using Xunit;

namespace MockDataStudio.Generation.Tests.Coercion;

// T-GEN-COERCE-001: regras de coerção de docs/15 §5. Falhas usam GEN-COERCE e nunca truncam em silêncio.
public sealed class CoercionTests
{
    [Theory]
    [InlineData("01310-100", 1310100L)]
    [InlineData(" 42 ", 42L)]
    [InlineData("-7", -7L)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_TextoParaInteiroRemoveNaoDigitos(string input, long expected)
    {
        var result = ValueCoercer.Coerce(input, Type(LogicalType.Integer, "integer"));
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_TextoVazioOuOverflowFalham()
    {
        ValueCoercer.Coerce("   ", Type(LogicalType.Integer, "integer")).IsSuccess.ShouldBeFalse();
        ValueCoercer.Coerce("99999999999999999999999", Type(LogicalType.Integer, "integer")).Error.Code
            .ShouldBe(MockDataStudio.Core.Common.ErrorCodes.GenCoerce);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("sim", true)]
    [InlineData("S", true)]
    [InlineData("0", false)]
    [InlineData("nao", false)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_TextoParaBooleano(string input, bool expected)
    {
        ValueCoercer.Coerce(input, Type(LogicalType.Boolean, "boolean")).Value.ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_DecimalArredondaParaParPorIntegerEDataParte()
    {
        ValueCoercer.Coerce(2.5m, Type(LogicalType.Integer, "integer")).Value.ShouldBe(2L);
        ValueCoercer.Coerce(3.5m, Type(LogicalType.Integer, "integer")).Value.ShouldBe(4L);
        ValueCoercer.Coerce(new DateTime(2026, 10, 12, 8, 30, 0), Type(LogicalType.Date, "date")).Value
            .ShouldBe(new DateOnly(2026, 10, 12));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_InteiroParaTextoComZerosAEsquerdaQuandoPedido()
    {
        var result = ValueCoercer.Coerce(42L, Type(LogicalType.String, "char(8)"), new CoerceOptions(PadLeft: 8));
        result.Value.ShouldBe("00000042");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_TextoMaiorQueComprimentoFalhaSemTruncarSalvoOpcao()
    {
        var column = Type(LogicalType.String, "varchar(5)");
        ValueCoercer.Coerce("abcdefgh", column).IsSuccess.ShouldBeFalse();
        ValueCoercer.Coerce("abcdefgh", column, new CoerceOptions(Truncate: true)).Value.ShouldBe("abcde");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_DataIsoEDataHoraEmFormatoInvalidoFalha()
    {
        ValueCoercer.Coerce("2026-10-12", Type(LogicalType.Date, "date")).Value.ShouldBe(new DateOnly(2026, 10, 12));
        ValueCoercer.Coerce("12/10/2026", Type(LogicalType.Date, "date")).IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData("P1D", 0, 0, 1)]
    [InlineData("-P80Y", -80, 0, 0)]
    [InlineData("P1M15D", 0, 1, 15)]
    [InlineData("PT2H30M", 0, 0, 0)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_DuracaoIsoAplicaComponentesEmOrdemDeCalendario(string text, int years, int months, int days)
    {
        var parsed = IsoDuration.Parse(text);
        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.Years.ShouldBe(Math.Abs(years));
        parsed.Value.Months.ShouldBe(months);
        parsed.Value.Days.ShouldBe(days);
        parsed.Value.Sign.ShouldBe(years < 0 ? -1 : 1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_DuracaoNegativaRecuaDaReferencia()
    {
        var reference = new DateTime(2026, 10, 12, 0, 0, 0);
        IsoDuration.Parse("-P18Y").Value.Apply(reference).ShouldBe(new DateTime(2008, 10, 12));
        IsoDuration.Parse("P1M15D").Value.Apply(new DateTime(2026, 1, 1)).ShouldBe(new DateTime(2026, 2, 16));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-GEN-COERCE-001")]
    public void T_GEN_COERCE_001_DuracaoMalformadaEhRejeitada()
    {
        IsoDuration.Parse("P").IsSuccess.ShouldBeFalse();
        IsoDuration.Parse("30D").IsSuccess.ShouldBeFalse();
    }

    private static MockDataStudio.Core.Schema.DataTypeInfo Type(LogicalType logical, string native) =>
        MockDataStudio.Core.Types.NativeTypeParser.Parse(native) with { LogicalType = logical };
}
