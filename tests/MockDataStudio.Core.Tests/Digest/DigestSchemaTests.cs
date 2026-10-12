using System.Reflection;
using System.Text.Json.Nodes;
using Json.Schema;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Digest;

// Validação de referência (apenas testes, DA-017): o JSON Schema publicado é a fonte externa de verdade.
public sealed class DigestSchemaTests
{
    private static JsonSchema ReferenceSchema()
    {
        using var stream = typeof(DigestSerializer).Assembly
            .GetManifestResourceStream("MockDataStudio.Core.Digest.digest-1.0.schema.json")
            ?? throw new InvalidOperationException("Schema embutido não encontrado.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    private static bool ConformsToSchema(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            // JSON sintaticamente inválido não está em conformidade com o schema.
            return false;
        }

        var result = ReferenceSchema().Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.Flag });
        return result.IsValid;
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-001")]
    [InlineData("sqlite")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("firebird")]
    public void T_DIG_001_SaidaDoSerializador_ValidaPeloJsonSchemaDeReferencia(string engine)
    {
        var serializer = DigestFixtures.NewSerializer();
        var output = serializer.Serialize(DigestFixtures.LoadExample(engine), new DigestWriteOptions(Indented: true, IncludeComments: true));

        ConformsToSchema(output).ShouldBeTrue("A saída do serializador deve obedecer ao digest-1.0.schema.json.");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-010")]
    [InlineData("sqlite")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("firebird")]
    public void T_DIG_010_Exemplos_TemMesmoVereditoNoValidadorCodificadoEReferencia(string engine)
    {
        var json = RepoPaths.ReadExample(engine);
        var codified = new DigestValidator(new StructureHasher()).Validate(json)
            .Where(e => e.Severity == DigestSeverity.Error).ToList();

        codified.ShouldBeEmpty();
        ConformsToSchema(json).ShouldBeTrue();
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-010")]
    [InlineData("{ \"format\": \"mds.digest\"")]
    [InlineData("{\"format\": \"outro\", \"version\": \"1.0\"}")]
    public void T_DIG_010_ErrosEstruturais_TambemFalhamNaReferencia(string json)
    {
        var codified = new DigestValidator(new StructureHasher()).Validate(json)
            .Any(e => e.Severity == DigestSeverity.Error);

        codified.ShouldBeTrue();
        ConformsToSchema(json).ShouldBeFalse();
    }
}
