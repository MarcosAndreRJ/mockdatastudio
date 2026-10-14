using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Editing;
using MockDataStudio.Core.Templates;
using Shouldly;
using Xunit;

namespace MockDataStudio.Catalog.Tests;

// Exemplos da tela Templates (ajuda para criar templates) e normalização do código digitado.
// Cada exemplo é salvo ou importado no catálogo real, para garantir que o que a tela oferece passa pelas validações.
public sealed class TemplateExamplesTests : IDisposable
{
    private readonly CatalogTestEnv _env = new();

    public void Dispose() => _env.Dispose();

    [Theory]
    [Trait("Category", "Contract")]
    [InlineData("cliente br", "CLIENTE_BR")]
    [InlineData("  pedido-2024 ", "PEDIDO_2024")]
    [InlineData("ja_OK", "JA_OK")]
    [InlineData("", "")]
    public void NormalizeCode_TransformaDigitacaoNoCodigoDoCatalogo(string typed, string expected) =>
        TemplateEditor.NormalizeCode(typed).ShouldBe(expected);

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExemploEscalarSalvaComCincoValores()
    {
        var ct = TestContext.Current.CancellationToken;

        var saved = await new TemplateEditor(_env.Templates, _env.DataSource).SaveScalarAsync(TemplateExamples.Scalar(), ct);

        saved.IsSuccess.ShouldBeTrue(saved.IsSuccess ? string.Empty : saved.Error.Message);
        (await _env.Templates.GetAsync(TemplateExamples.ScalarCode, ct))!.ValueCount.ShouldBe(5);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExemploCompostoSalvaERecarregaComRegistroSemUf()
    {
        var ct = TestContext.Current.CancellationToken;
        var fields = CompositeTemplateEditor.ParseFields(TemplateExamples.CompositeFields);
        fields.IsSuccess.ShouldBeTrue(fields.IsSuccess ? string.Empty : fields.Error.Message);
        var records = CompositeTemplateEditor.ParseRecords(TemplateExamples.CompositeRecords, fields.Value!);
        records.IsSuccess.ShouldBeTrue(records.IsSuccess ? string.Empty : records.Error.Message);

        var editor = new CompositeTemplateEditor(_env.Templates, _env.DataSource);
        var saved = await editor.SaveAsync(new CompositeTemplateForm(
            TemplateExamples.CompositeCode, TemplateExamples.CompositeName, TemplateExamples.CompositeDescription,
            fields.Value!, TemplateEditor.ParseLines(TemplateExamples.CompositeAliases), records.Value!), ct);
        saved.IsSuccess.ShouldBeTrue(saved.IsSuccess ? string.Empty : saved.Error.Message);

        var loaded = await editor.LoadAsync(TemplateExamples.CompositeCode, ct);
        loaded.IsSuccess.ShouldBeTrue(loaded.IsSuccess ? string.Empty : loaded.Error.Message);
        loaded.Value!.Fields.Count.ShouldBe(3);
        loaded.Value.Records.Count.ShouldBe(3);
        // "CLI003 | Carla Dias |" deixa a UF vazia: vira nulo no catálogo.
        (loaded.Value.Records[2]["uf"] ?? string.Empty).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExemploDeScriptSqlAnalisaEImporta()
    {
        var ct = TestContext.Current.CancellationToken;
        await _env.Pack.InstallAsync(ct);
        var service = new TemplateImportService(_env.Importer);

        var analysis = await service.AnalyzeScriptAsync(TemplateExamples.Script, MergeMode.Upsert, ct);

        analysis.IsSuccess.ShouldBeTrue(analysis.IsSuccess ? string.Empty : analysis.Error.Message);
        analysis.Value!.CanMerge.ShouldBeTrue();
        var merged = await service.MergeAsync(analysis.Value.StagingSessionId, ct);
        merged.IsSuccess.ShouldBeTrue(merged.IsSuccess ? string.Empty : merged.Error.Message);
        (await _env.Templates.GetAsync(TemplateExamples.ScriptCode, ct)).ShouldNotBeNull();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExemploDePackJsonAnalisaEImportaComposotoEEscalar()
    {
        var ct = TestContext.Current.CancellationToken;
        await _env.Pack.InstallAsync(ct);
        var service = new TemplateImportService(_env.Importer);

        var analysis = await service.AnalyzePackJsonAsync(TemplateExamples.PackJson, MergeMode.Upsert, ct);

        analysis.IsSuccess.ShouldBeTrue(analysis.IsSuccess ? string.Empty : analysis.Error.Message);
        analysis.Value!.CanMerge.ShouldBeTrue();
        var merged = await service.MergeAsync(analysis.Value.StagingSessionId, ct);
        merged.IsSuccess.ShouldBeTrue(merged.IsSuccess ? string.Empty : merged.Error.Message);

        var produto = await _env.Templates.GetAsync("EXEMPLO_PRODUTO", ct);
        produto!.Kind.ShouldBe(TemplateKind.Composite);
        (await _env.Templates.GetFieldsAsync("EXEMPLO_PRODUTO", ct)).Count.ShouldBe(3);
        (await _env.Templates.GetAsync("EXEMPLO_PRIORIDADE", ct))!.ValueCount.ShouldBe(3);
    }
}
