using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Digest;

public sealed class DigestRoundTripTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-002")]
    [InlineData("sqlite")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("firebird")]
    public void T_DIG_002_RoundTrip_ModeloDigestModelo_PreservaEstrutura(string engine)
    {
        var serializer = DigestFixtures.NewSerializer();

        var first = serializer.Deserialize(RepoPaths.ReadExample(engine));
        first.IsSuccess.ShouldBeTrue(first.IsSuccess ? string.Empty : $"{first.Error.Code}: {first.Error.Message}");

        var serialized = serializer.Serialize(first.Value, new DigestWriteOptions());
        var second = serializer.Deserialize(serialized);
        second.IsSuccess.ShouldBeTrue(second.IsSuccess ? string.Empty : $"{second.Error.Code}: {second.Error.Message}");

        // Igualdade estrutural: a forma serializada de ambos os modelos é idêntica.
        serializer.Serialize(second.Value, new DigestWriteOptions()).ShouldBe(serialized);
        second.Value.Namespaces.Sum(n => n.Tables.Count).ShouldBe(first.Value.Namespaces.Sum(n => n.Tables.Count));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-002")]
    [InlineData("sqlite")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("firebird")]
    public void T_DIG_002_RoundTrip_ModelosDosExemplos_SaoIdempotentes(string engine)
    {
        var serializer = DigestFixtures.NewSerializer();
        var model = DigestFixtures.LoadExample(engine);

        var once = serializer.Serialize(model, new DigestWriteOptions());
        var twice = serializer.Serialize(serializer.Deserialize(once).Value, new DigestWriteOptions());

        twice.ShouldBe(once);
    }
}
