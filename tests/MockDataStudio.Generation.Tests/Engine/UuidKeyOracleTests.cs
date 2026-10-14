using MockDataStudio.Core.Naming;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Random;
using Shouldly;
using Xunit;

namespace MockDataStudio.Generation.Tests.Engine;

// A2r.1 (chave UUID, docs/16 §5.3): key(i) é função pura de (seed, tabela, coluna, i).
public sealed class UuidKeyOracleTests
{
    private static readonly ObjectName Cliente = new("public", "cliente");

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUUID-001")]
    public void MesmaLinhaProduzMesmaChaveIndependenteDaOrdemDeChamada()
    {
        var oracle = Oracle(seed: 42UL, rows: 10);

        var forward = Enumerable.Range(0, 10).Select(r => (Guid)oracle.GetKey(Cliente, ["id"], r)[0]!).ToList();
        var backward = Enumerable.Range(0, 10).Reverse().Select(r => (Guid)oracle.GetKey(Cliente, ["id"], r)[0]!).Reverse().ToList();

        backward.ShouldBe(forward);
        ((Guid)oracle.GetKey(Cliente, ["id"], 3)[0]!).ShouldBe(forward[3]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUUID-002")]
    public void ChavesTemVersaoQuatroEVarianteRfc4122()
    {
        var oracle = Oracle(seed: 7UL, rows: 50);

        foreach (var r in Enumerable.Range(0, 50))
        {
            var bytes = ((Guid)oracle.GetKey(Cliente, ["id"], r)[0]!).ToByteArray();
            (bytes[7] >> 4).ShouldBe(4);          // versão 4 no nibble alto do terceiro grupo (ordem little-endian do Guid)
            (bytes[8] & 0xC0).ShouldBe(0x80);     // variante RFC 4122
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUUID-003")]
    public void ChavesSaoUnicasEntreLinhasESensiveisASemente()
    {
        var oracle = Oracle(seed: 1UL, rows: 2000);
        var other = Oracle(seed: 2UL, rows: 2000);

        var keys = Enumerable.Range(0, 2000).Select(r => (Guid)oracle.GetKey(Cliente, ["id"], r)[0]!).ToList();

        keys.Distinct().Count().ShouldBe(2000);
        ((Guid)other.GetKey(Cliente, ["id"], 0)[0]!).ShouldNotBe(keys[0]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUUID-004")]
    public void ColunaErradaTabelaNaoRegistradaEIndiceForaFalham()
    {
        var oracle = Oracle(seed: 9UL, rows: 5);

        Should.Throw<NotSupportedException>(() => oracle.GetKey(Cliente, ["outra"], 0));
        Should.Throw<NotSupportedException>(() => oracle.GetKey(new ObjectName("public", "pedido"), ["id"], 0));
        Should.Throw<ArgumentOutOfRangeException>(() => oracle.GetKey(Cliente, ["id"], 5));
        oracle.RowCount(Cliente).ShouldBe(5);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-KEYUUID-005")]
    public void HelperCompartilhadoReproduzAMesmaRegraDoGerador()
    {
        // O gerador core.uuid e o oracle usam UuidV4.Create: a mesma sequência de RNG gera o mesmo Guid.
        var key = StreamKey.Derive(5UL, "public.cliente.id", "key");
        var a = DeterministicRandom.For(key, 4);
        var b = DeterministicRandom.For(key, 4);

        UuidV4.Create(ref a).ShouldBe(UuidV4.Create(ref b));
    }

    private static UuidKeyOracle Oracle(ulong seed, long rows)
    {
        var oracle = new UuidKeyOracle(seed);
        oracle.Register(Cliente, "id", rows);
        return oracle;
    }
}
