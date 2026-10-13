using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Editing;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using Shouldly;
using Xunit;

namespace MockDataStudio.Catalog.Tests;

// C8a.1 (edição de templates compostos): campos, registros e a gravação no catálogo real.
public sealed class CompositeEditingTests : IDisposable
{
    private readonly CatalogTestEnv _env = new();

    public void Dispose() => _env.Dispose();

    private const string FieldsText = "rua | string | 120 | sim\nnumero | integer | | não";

    [Fact]
    [Trait("Category", "Unit")]
    public void CamposSaoLidosEFormatadosDeVolta()
    {
        var parsed = CompositeTemplateEditor.ParseFields(FieldsText);

        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value!.Select(f => f.FieldCode).ShouldBe(["rua", "numero"]);
        parsed.Value[0].MaxLength.ShouldBe(120);
        parsed.Value[1].Required.ShouldBeFalse();

        var again = CompositeTemplateEditor.ParseFields(CompositeTemplateEditor.FormatFields(parsed.Value));
        again.Value!.Select(f => (f.FieldCode, f.LogicalType, f.MaxLength, f.Required))
            .ShouldBe(parsed.Value.Select(f => (f.FieldCode, f.LogicalType, f.MaxLength, f.Required)));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CampoComTipoInvalidoEhRecusado()
    {
        CompositeTemplateEditor.ParseFields("rua | texto").IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegistroComQuantidadeDeValoresErradaEhRecusadoComALinha()
    {
        var fields = CompositeTemplateEditor.ParseFields(FieldsText).Value!;

        var parsed = CompositeTemplateEditor.ParseRecords("Rua A | 10\nSó uma coluna", fields);

        parsed.IsSuccess.ShouldBeFalse();
        parsed.Error.Message.ShouldContain("linha 2");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CompostoSalvoEhCarregadoComOsMesmosRegistros()
    {
        var ct = TestContext.Current.CancellationToken;
        var fields = CompositeTemplateEditor.ParseFields(FieldsText).Value!;
        var records = CompositeTemplateEditor.ParseRecords("Rua A | 10\nRua B |", fields).Value!;
        var editor = new CompositeTemplateEditor(_env.Templates, _env.DataSource);

        var saved = await editor.SaveAsync(new CompositeTemplateForm("TST_LOGRADOURO", "Logradouro de teste", null,
            fields, ["via"], records), ct);

        saved.IsSuccess.ShouldBeTrue(saved.IsSuccess ? string.Empty : saved.Error.Message);
        var loaded = await editor.LoadAsync("TST_LOGRADOURO", ct);
        loaded.IsSuccess.ShouldBeTrue(loaded.IsSuccess ? string.Empty : loaded.Error.Message);
        loaded.Value!.Records.Count.ShouldBe(2);
        loaded.Value.Records.Select(r => r["rua"]).OrderBy(v => v, StringComparer.Ordinal).ShouldBe(["Rua A", "Rua B"]);
        // O carregador do catálogo devolve NULL como texto vazio (campos de composto não são nulos na geração).
        loaded.Value.Records.Single(r => r["rua"] == "Rua B")["numero"].ShouldBeNullOrWhiteSpace();
        loaded.Value.Aliases.ShouldBe(["via"]);
        CompositeTemplateEditor.FormatRecords(loaded.Value.Records, loaded.Value.Fields).Split(Environment.NewLine).Length.ShouldBe(2);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CompostoEmbutidoNaoAbreParaEdicao()
    {
        var ct = TestContext.Current.CancellationToken;
        await _env.Pack.InstallAsync(ct);

        var loaded = await new CompositeTemplateEditor(_env.Templates, _env.DataSource).LoadAsync("ENDERECO", ct);

        loaded.IsSuccess.ShouldBeFalse();
        loaded.Error.Code.ShouldBe(ErrorCodes.CatBuiltinReadOnly);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CompostoSemCamposEhRecusado()
    {
        var saved = await new CompositeTemplateEditor(_env.Templates, _env.DataSource).SaveAsync(
            new CompositeTemplateForm("TST_SEMCAMPO", "Sem campo", null, [], [], []), TestContext.Current.CancellationToken);

        saved.IsSuccess.ShouldBeFalse();
    }
}
