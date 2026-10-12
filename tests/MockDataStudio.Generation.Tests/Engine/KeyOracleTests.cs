using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Planning;
using TablePlan = MockDataStudio.Generation.Planning.TablePlan;
using Shouldly;
using Xunit;

namespace MockDataStudio.Generation.Tests.Engine;

// T-REL-ORACLE: chave Explicit inteira calculada por índice (16 §5.3).
public sealed class KeyOracleTests
{
    private static readonly ObjectName Cliente = new("public", "cliente");

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-ORACLEUNIT-001")]
    public void ChaveExplicita_CalculaInicioMaisPasso()
    {
        var oracle = new ExplicitKeyOracle(Plan(start: 1006, rows: 10));

        oracle.GetKey(Cliente, ["id"], 0)[0].ShouldBe(1006L);
        oracle.GetKey(Cliente, ["id"], 9)[0].ShouldBe(1015L);
        oracle.RowCount(Cliente).ShouldBe(10);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-ORACLEUNIT-002")]
    public void ChaveExplicita_IndiceForaDaFaixaFalha()
    {
        var oracle = new ExplicitKeyOracle(Plan(start: 1, rows: 3));

        Should.Throw<ArgumentOutOfRangeException>(() => oracle.GetKey(Cliente, ["id"], 3));
        Should.Throw<ArgumentOutOfRangeException>(() => oracle.GetKey(Cliente, ["id"], -1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-REL-ORACLEUNIT-003")]
    public void ChaveNaoSuportada_ColunaErradaOuComposta_LancaNotSupported()
    {
        var oracle = new ExplicitKeyOracle(Plan(start: 1, rows: 3));

        Should.Throw<NotSupportedException>(() => oracle.GetKey(Cliente, ["outra"], 0));
        Should.Throw<NotSupportedException>(() => oracle.GetKey(Cliente, ["id", "x"], 0));
    }

    private static GenerationPlan Plan(long start, long rows)
    {
        var key = new KeyColumnPlan("id", KeyStrategy.Explicit, start, 1, false, null);
        var table = new TablePlan(Cliente, rows, ["id"], ["id"], [key], [], new Dictionary<string, RelationSettings>(), false);
        return new GenerationPlan([], new Dictionary<ObjectName, TablePlan> { [Cliente] = table }, [], "sha256:test", rows, 0);
    }
}
