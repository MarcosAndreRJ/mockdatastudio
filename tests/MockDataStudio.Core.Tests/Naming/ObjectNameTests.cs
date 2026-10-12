using MockDataStudio.Core.Naming;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Naming;

public sealed class ObjectNameTests
{
    private static readonly char[] Alphabet =
        ['a', 'Z', '0', '_', '$', ' ', '.', '"', 'ç', 'é', '日', '-', 'x', 'Q', '9'];

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-NAM-001")]
    public void T_NAM_001_ObjectName_RoundTrip_10000CasosGerados()
    {
        var random = new Random(20261012);

        for (var i = 0; i < 10_000; i++)
        {
            var original = new ObjectName(RandomPart(random), RandomPart(random));
            ObjectName.ParseCanonical(original.CanonicalId).ShouldBe(original, $"Caso {i}: '{original.CanonicalId}'");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-NAM-002")]
    public void T_NAM_002_ColumnRef_RoundTrip_10000CasosGerados()
    {
        var random = new Random(7);

        for (var i = 0; i < 10_000; i++)
        {
            var original = new ColumnRef(new ObjectName(RandomPart(random), RandomPart(random)), RandomPart(random));
            ColumnRef.ParseCanonical(original.CanonicalId).ShouldBe(original, $"Caso {i}: '{original.CanonicalId}'");
        }
    }

    [Theory]
    [InlineData("public.cliente", "public", "cliente")]
    [InlineData("PUBLIC.CLIENTE", "PUBLIC", "CLIENTE")]
    [InlineData("main.\"item pedido\"", "main", "item pedido")]
    [InlineData("\"a.b\".\"c\"\"d\"", "a.b", "c\"d")]
    public void ParseCanonical_AceitaFormasComAspas(string canonical, string ns, string name)
    {
        ObjectName.ParseCanonical(canonical).ShouldBe(new ObjectName(ns, name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Formatacao_CitaApenasQuandoNecessario()
    {
        new ObjectName("public", "cliente").CanonicalId.ShouldBe("public.cliente");
        new ObjectName("main", "item pedido").CanonicalId.ShouldBe("main.\"item pedido\"");
        new ObjectName("main", "a\"b").CanonicalId.ShouldBe("main.\"a\"\"b\"");
    }

    [Theory]
    [InlineData("public")]
    [InlineData("a.b.c.d")]
    [InlineData("\"sem fechamento")]
    [InlineData("a..b")]
    public void ParseCanonical_RejeitaFormatoInvalido(string canonical)
    {
        Should.Throw<FormatException>(() => ObjectName.ParseCanonical(canonical));
    }

    private static string RandomPart(Random random)
    {
        var length = random.Next(1, 13);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        return new string(chars);
    }
}
