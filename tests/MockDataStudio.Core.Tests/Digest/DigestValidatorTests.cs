using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Digest;

// T-DIG-007: cada código DIG-* é provocado por ao menos uma fixture derivada do exemplo SQLite.
public sealed class DigestValidatorTests
{
    private static readonly DigestValidator Validator = new(new StructureHasher());

    public static TheoryData<string, string, DigestSeverity> Cenarios()
    {
        var baseText = RepoPaths.ReadExample("sqlite");
        var data = new TheoryData<string, string, DigestSeverity>
        {
            { ErrorCodes.DigJson, "{ isto nao e json", DigestSeverity.Error },
            { ErrorCodes.DigFormat, Replace(baseText, "\"format\": \"mds.digest\"", "\"format\": \"outro\""), DigestSeverity.Error },
            { ErrorCodes.DigVersion, Replace(baseText, "\"version\": \"1.0\"", "\"version\": \"1.x\""), DigestSeverity.Error },
            { ErrorCodes.DigMajorUnsupported, Replace(baseText, "\"version\": \"1.0\"", "\"version\": \"2.0\""), DigestSeverity.Error },
            { ErrorCodes.DigMinorNewer, Replace(baseText, "\"version\": \"1.0\"", "\"version\": \"1.3\""), DigestSeverity.Warning },
            { ErrorCodes.DigSchema, Replace(baseText, "\"format\": \"mds.digest\",", "\"format\": \"mds.digest\", \"extra\": 1,"), DigestSeverity.Error },
            { ErrorCodes.DigDup, Replace(baseText, "\"name\": \"produto\"", "\"name\": \"categoria\""), DigestSeverity.Error },
            { ErrorCodes.DigPkCol, Replace(baseText, "\"pk\": [\"id\"]", "\"pk\": [\"inexistente\"]"), DigestSeverity.Error },
            { ErrorCodes.DigFkTarget, Replace(baseText, "\"r\": \"main.categoria\"", "\"r\": \"main.nao_existe\""), DigestSeverity.Error },
            {
                ErrorCodes.DigFkExternal,
                Replace(Replace(baseText, "\"r\": \"main.categoria\"", "\"r\": \"main.nao_existe\""), "\"options\": {", "\"options\": { \"include\": [\"loja\"],"),
                DigestSeverity.Warning
            },
            { ErrorCodes.DigFkArity, Replace(baseText, "\"rc\": [\"id\"], \"od\": \"setnull\"", "\"rc\": [\"id\", \"nome\"], \"od\": \"setnull\""), DigestSeverity.Error },
            { ErrorCodes.DigFlags, Replace(baseText, "[\"id\", \"integer\", \"nn ai\"]", "[\"id\", \"integer\", \"nn ai gs\"]"), DigestSeverity.Error },
            { ErrorCodes.DigHashMismatch, Regex.Replace(baseText, "\"hash\": \"sha256:[0-9a-f]{64}\"", "\"hash\": \"sha256:" + new string('0', 64) + "\""), DigestSeverity.Warning },
        };

        return data;
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-007")]
    [MemberData(nameof(Cenarios))]
    public void T_DIG_007_CadaCodigo_ProvocadoPorFixtureInvalida(string code, string json, DigestSeverity severity)
    {
        var errors = Validator.Validate(json);

        errors.ShouldContain(e => e.Code == code && e.Severity == severity,
            customMessage: $"Esperado {code} ({severity}). Obtido: {string.Join("; ", errors.Select(e => e.Code))}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-007")]
    public void Exemplo_Valido_NaoProduzErrosNemAvisosDeEstrutura()
    {
        var errors = Validator.Validate(RepoPaths.ReadExample("sqlite"));

        errors.Where(e => e.Severity == DigestSeverity.Error).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_ComErro_DevolveCodigoDoPrimeiroErro()
    {
        var result = DigestFixtures_NewSerializer().Deserialize("{ isto nao e json");

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.DigJson);
    }

    private static IDigestSerializer DigestFixtures_NewSerializer() => DigestFixtures.NewSerializer();

    private static string Replace(string text, string from, string to)
    {
        text.Contains(from, StringComparison.Ordinal).ShouldBeTrue($"Trecho não encontrado no exemplo: {from}");
        return text.Replace(from, to, StringComparison.Ordinal);
    }
}
