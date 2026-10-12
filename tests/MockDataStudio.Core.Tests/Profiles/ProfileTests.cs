using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Profiles;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Profiles;

public sealed class ProfileTests
{
    [Fact]
    public void DistributionSpec_Uniform_ValidaComSucesso()
    {
        var result = DistributionSpec.Create(DistributionKind.Uniform);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe(DistributionKind.Uniform);
    }

    [Fact]
    public void DistributionSpec_Zipf_SemExpoente_Falha()
    {
        var result = DistributionSpec.Create(DistributionKind.Zipf, zipfExponent: 0);
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.GenParamRange);
    }

    [Fact]
    public void DistributionSpec_Range_MinMaiorQueMax_Falha()
    {
        var result = DistributionSpec.Create(DistributionKind.Range, min: 10, max: 5);
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.GenParamRange);
    }

    [Fact]
    public void DistributionSpec_Poisson_LambdaInvalido_Falha()
    {
        var result = DistributionSpec.Create(DistributionKind.Poisson, lambda: -1);
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.GenParamRange);
    }

    [Fact]
    public void DistributionSpec_Weighted_PesosInvalidos_Falha()
    {
        var result = DistributionSpec.Create(DistributionKind.Weighted, weights: new List<double> { -1, 0 });
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.GenParamRange);
    }

    [Fact]
    public void RelationSettings_NullRateForaDaFaixa_Falha()
    {
        var dist = DistributionSpec.Create(DistributionKind.Uniform).Value;
        var card = CardinalitySpec.Create(CardinalityMode.ChildDriven, dist).Value;

        var result = RelationSettings.Create("fk_pedido_cliente", ReferenceSourceKind.Generated, card, nullRate: 1.5);
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.GenParamRange);
    }

    [Fact]
    public void TableSettings_RowCountNegativo_Falha()
    {
        var result = TableSettings.Create(include: true, rowCount: -10);
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.GenParamRange);
    }

    [Fact]
    public void OutputSettings_ValidaValoresPadraoEInvariantes()
    {
        var result = OutputSettings.Create(OutputKind.SqlScript);
        result.IsSuccess.ShouldBeTrue();
        result.Value.BatchSize.ShouldBe(1000);
        result.Value.SegmentMaxBytes.ShouldBe(50L * 1024 * 1024);

        var failResult = OutputSettings.Create(OutputKind.SqlScript, batchSize: 0);
        failResult.IsSuccess.ShouldBeFalse();
        failResult.Error!.Code.ShouldBe(ErrorCodes.GenParamRange);
    }

    [Fact]
    public void GenerationProfile_ValidaInvariantesECriaCorretamente()
    {
        var dist = DistributionSpec.Create(DistributionKind.Uniform).Value;
        var card = CardinalitySpec.Create(CardinalityMode.ChildDriven, dist).Value;
        var rel = RelationSettings.Create("fk_item_pedido", ReferenceSourceKind.Generated, card).Value;
        var table = TableSettings.Create(true, 1000, KeyStrategy.Explicit, relations: new Dictionary<string, RelationSettings> { ["fk"] = rel }).Value;

        var output = OutputSettings.Create(OutputKind.SqlScript).Value;
        var validation = ValidationSettings.Create(false, 10).Value;

        var profileResult = GenerationProfile.Create(
            name: "DefaultProfile",
            seed: 42UL,
            referenceInstant: DateTimeOffset.UtcNow,
            referenceInstantMode: ReferenceInstantMode.Fixed,
            timeZoneId: "UTC",
            tables: new Dictionary<string, TableSettings> { ["public.item"] = table },
            defaultKeyStrategy: KeyStrategy.Explicit,
            output: output,
            validation: validation,
            keyOffset: 0);

        profileResult.IsSuccess.ShouldBeTrue();
        profileResult.Value.Name.ShouldBe("DefaultProfile");
        profileResult.Value.Seed.ShouldBe(42UL);
        profileResult.Value.Tables.Count.ShouldBe(1);

        var failName = GenerationProfile.Create(
            name: "",
            seed: 42UL,
            referenceInstant: DateTimeOffset.UtcNow,
            referenceInstantMode: ReferenceInstantMode.Fixed,
            timeZoneId: "UTC",
            tables: new Dictionary<string, TableSettings>(),
            defaultKeyStrategy: KeyStrategy.Explicit,
            output: output,
            validation: validation);

        failName.IsSuccess.ShouldBeFalse();
        failName.Error!.Code.ShouldBe(ErrorCodes.GenParamRequired);
    }
}
