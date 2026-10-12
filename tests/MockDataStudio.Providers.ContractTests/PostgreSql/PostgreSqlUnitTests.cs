using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Providers.Models;
using MockDataStudio.Providers.PostgreSql;
using Shouldly;
using Xunit;

namespace MockDataStudio.Providers.ContractTests.PostgreSql;

// Testes puros do provider PostgreSQL (mapeamento de tipos, literais, INSERT e sessão). Não exigem servidor.
public sealed class PostgreSqlUnitTests
{
    [Theory]
    [InlineData("character varying(60)", "varchar(60)")]
    [InlineData("bpchar(11)", "char(11)")]
    [InlineData("int4", "integer")]
    [InlineData("int8", "bigint")]
    [InlineData("int2", "smallint")]
    [InlineData("bool", "boolean")]
    [InlineData("timestamp without time zone", "timestamp")]
    [InlineData("timestamp with time zone", "timestamptz")]
    [InlineData("numeric(12,2)", "numeric(12,2)")]
    [InlineData("float8", "double precision")]
    public void Canonical_NormalizaNomesDoCatalogoPostgreSql(string raw, string expected)
    {
        PostgreSqlTypeMapper.Canonical(raw).ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRV-PG-TYPES-001")]
    public void Map_PreservaComprimentoDeVarchar()
    {
        var type = new PostgreSqlTypeMapper().Map(new NativeTypeDescriptor("character varying(60)", null, null, null, null, null, null));

        type.Length.ShouldBe(60);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRV-PG-LIT-001")]
    public void Literais_EscapamAspasERespeitamTiposBasicos()
    {
        var formatter = new PostgreSqlLiteralFormatter();
        var text = NativeTypeParser.Parse("varchar(20)");

        formatter.Format(null, text).ShouldBe("NULL");
        formatter.Format("O'Brien", text).ShouldBe("'O''Brien'");
        formatter.Format(true, text).ShouldBe("TRUE");
        formatter.Format(12, text).ShouldBe("12");
        formatter.Format(1.50m, text).ShouldBe("1.50");
        formatter.Format(new byte[] { 0xAB, 0x01 }, text).ShouldBe("'\\xab01'");
        formatter.Format(double.NaN, text).ShouldBe("'NaN'::double precision");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRV-PG-LIT-002")]
    public void Literais_RecusamNulEmTexto()
    {
        var formatter = new PostgreSqlLiteralFormatter();

        Should.Throw<ArgumentOutOfRangeException>(() => formatter.Format("a\0b", NativeTypeParser.Parse("text")));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRV-PG-DIA-001")]
    public void QuoteIdentifier_DuplicaAspasDuplas()
    {
        new PostgreSqlDialect().QuoteIdentifier("a\"b").ShouldBe("\"a\"\"b\"");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRV-PG-DIA-002")]
    public void BuildInsert_UsaSchemaQualificadoEOverrideAntesDoValues()
    {
        var sql = new PostgreSqlDialect().BuildInsert(
            new ObjectName("public", "cliente"),
            ["id", "nome"],
            [["1", "'x'"], ["2", "'y'"]],
            "OVERRIDING SYSTEM VALUE");

        sql.ShouldStartWith("INSERT INTO \"public\".\"cliente\" (\"id\", \"nome\") OVERRIDING SYSTEM VALUE VALUES");
        sql.ShouldEndWith("(2, 'y');");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRV-PG-DIA-003")]
    public void IdentityOverride_SoParaGeneratedAlways()
    {
        var dialect = new PostgreSqlDialect();
        var always = Column(new IdentityInfo(IdentityKind.IdentityAlways));
        var byDefault = Column(new IdentityInfo(IdentityKind.IdentityByDefault));

        dialect.IdentityOverrideClause(always).ShouldBe("OVERRIDING SYSTEM VALUE");
        dialect.IdentityOverrideClause(byDefault).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-PRV-PG-DIA-004")]
    public void ResyncSequence_GeraSetvalComIsCalledFalse()
    {
        var statements = new PostgreSqlDialect().ResyncSequenceStatements(
            new ObjectName("public", "cliente"), Column(null), "public.cliente_id_seq", 42);

        statements.ShouldBe(["SELECT setval('public.cliente_id_seq', 42, false);"]);
    }

    // T-PRV-PG-001..011 (contrato contra servidor real). Não executado: Docker indisponível nesta máquina (B1).
    [Fact(Skip = "B1/B2: Docker indisponível nesta máquina; contrato PostgreSQL contra servidor real não executado.")]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-PG-001")]
    public void Contrato_PostgreSqlReal_NaoExecutado()
    {
    }

    private static ColumnModel Column(IdentityInfo? identity) =>
        ColumnModel.Create("id", 1, NativeTypeParser.Parse("integer"), isNullable: false, identity: identity).Value;
}
