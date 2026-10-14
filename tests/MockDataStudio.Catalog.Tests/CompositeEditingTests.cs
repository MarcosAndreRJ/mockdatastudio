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

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CompostoPreservaTagsAliasesCamposEPesosEmRoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var editor = new CompositeTemplateEditor(_env.Templates, _env.DataSource);

        var originalDraft = new TemplateDraft(
            "TST_COMP_RT",
            "Composto Round Trip",
            TemplateKind.Composite,
            null,
            "Descricao completa",
            ["localizacao", "geo"],
            [
                new DraftField("cep", LogicalType.String, 1, 9, true),
                new DraftField("cidade", LogicalType.String, 2, 60, true),
                new DraftField("uf", LogicalType.String, 3, 2, false)
            ],
            [
                new DraftAlias("endereco", null, 1.5, "cli_*"),
                new DraftAlias("cod_postal", "cep", 1.9, null)
            ],
            [],
            [
                new TemplateRecordRow("rec_sp", new Dictionary<string, string?> { ["cep"] = "01001-000", ["cidade"] = "São Paulo", ["uf"] = "SP" }, 3.5),
                new TemplateRecordRow("rec_rj", new Dictionary<string, string?> { ["cep"] = "20040-002", ["cidade"] = "Rio de Janeiro", ["uf"] = "RJ" }, 2.0)
            ]);

        var saveInit = await _env.Templates.SaveAsync(originalDraft, ct);
        saveInit.IsSuccess.ShouldBeTrue();

        var infoInitial = await _env.Templates.GetAsync("TST_COMP_RT", ct);
        infoInitial.ShouldNotBeNull();
        var initialHash = infoInitial.ContentHash;

        // Carrega via editor
        var formResult = await editor.LoadAsync("TST_COMP_RT", ct);
        formResult.IsSuccess.ShouldBeTrue();
        var form = formResult.Value!;

        // Salva novamente sem alterações
        var saveAgain = await editor.SaveAsync(form, ct);
        saveAgain.IsSuccess.ShouldBeTrue();

        var reloadedDraft = await _env.Templates.GetDraftAsync("TST_COMP_RT", ct);
        reloadedDraft.ShouldNotBeNull();
        reloadedDraft.Tags.ShouldBe(["localizacao", "geo"]);

        // Preserva alias por campo e alias de template com seus pesos
        reloadedDraft.Aliases.Count.ShouldBe(2);
        var aliasEnd = reloadedDraft.Aliases.Single(a => a.Alias == "endereco");
        aliasEnd.FieldCode.ShouldBeNull();
        aliasEnd.Weight.ShouldBe(1.5);
        aliasEnd.ContextTablePattern.ShouldBe("cli_*");

        var aliasCep = reloadedDraft.Aliases.Single(a => a.Alias == "cod_postal");
        aliasCep.FieldCode.ShouldBe("cep");
        aliasCep.Weight.ShouldBe(1.9);

        // Preserva registros com chaves e pesos
        reloadedDraft.Records.Count.ShouldBe(2);
        var recSp = reloadedDraft.Records[0];
        recSp.RecordKey.ShouldBe("rec_sp");
        recSp.Weight.ShouldBe(3.5);
        recSp.Data["cidade"].ShouldBe("São Paulo");

        var recRj = reloadedDraft.Records[1];
        recRj.RecordKey.ShouldBe("rec_rj");
        recRj.Weight.ShouldBe(2.0);

        var infoAgain = await _env.Templates.GetAsync("TST_COMP_RT", ct);
        infoAgain!.ContentHash.ShouldBe(initialHash, "Hash deve ser idêntico ao re-salvar composto sem mudanças");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CompostoContaRegistrosEmListAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fields = CompositeTemplateEditor.ParseFields("c1 | string | 10 | sim\nc2 | string | 10 | sim").Value!;
        var records = CompositeTemplateEditor.ParseRecords("a | b\nc | d\ne | f", fields).Value!;
        var editor = new CompositeTemplateEditor(_env.Templates, _env.DataSource);

        var saved = await editor.SaveAsync(new CompositeTemplateForm("TST_CONTAGEM_COMP", "Contagem", null,
            fields, [], records), ct);
        saved.IsSuccess.ShouldBeTrue();

        var info = await _env.Templates.GetAsync("TST_CONTAGEM_COMP", ct);
        info.ShouldNotBeNull();
        info.ValueCount.ShouldBe(3, "ValueCount deve refletir a contagem de registros para templates compostos");
    }
}
