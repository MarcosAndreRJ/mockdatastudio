using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Editing;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using Shouldly;
using Xunit;

namespace MockDataStudio.Catalog.Tests;

// C8a.1 (edição de templates) e C8a.2/C8a.3 (importação por script e por pack JSON), sobre o catálogo real.
public sealed class EditingTests : IDisposable
{
    private readonly CatalogTestEnv _env = new();

    public void Dispose() => _env.Dispose();

    private const string ScriptValid = """
        INSERT INTO template (code, name, kind, logical_type, locale, description, tags) VALUES ('TST_CIDADE', 'Cidade de teste', 'scalar', 'string', 'pt-BR', NULL, NULL);
        INSERT INTO template_value (template_code, value, weight) VALUES ('TST_CIDADE', 'Recife', 1.0), ('TST_CIDADE', 'Natal', 1.0);
        """;

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EditorSalvaTemplateEscalarERecuperaValores()
    {
        var editor = new TemplateEditor(_env.Templates, _env.DataSource);
        var form = new ScalarTemplateForm("TST_UF", "UF de teste", "Sigla", LogicalType.String,
            TemplateEditor.ParseLines("SP\nRJ\nSP"), ["estado"]);

        var saved = await editor.SaveScalarAsync(form, TestContext.Current.CancellationToken);

        saved.IsSuccess.ShouldBeTrue(saved.IsSuccess ? string.Empty : saved.Error.Message);
        var info = await _env.Templates.GetAsync("TST_UF", TestContext.Current.CancellationToken);
        info!.Kind.ShouldBe(TemplateKind.Scalar);
        info.ValueCount.ShouldBe(2, "valores repetidos são removidos ao montar o formulário");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EditorRecusaTemplateSemValores()
    {
        var editor = new TemplateEditor(_env.Templates, _env.DataSource);

        var saved = await editor.SaveScalarAsync(
            new ScalarTemplateForm("TST_VAZIO", "Vazio", null, LogicalType.String, [], []), TestContext.Current.CancellationToken);

        saved.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EditorClonaEExcluiCopiaUsuario()
    {
        var ct = TestContext.Current.CancellationToken;
        var editor = new TemplateEditor(_env.Templates, _env.DataSource);
        await editor.SaveScalarAsync(new ScalarTemplateForm("TST_BASE", "Base", null, LogicalType.String, ["x"], []), ct);

        var clone = await editor.CloneAsync("TST_BASE", "TST_COPIA", ct);
        clone.IsSuccess.ShouldBeTrue();
        (await _env.Templates.GetAsync("TST_COPIA", ct)).ShouldNotBeNull();

        (await editor.DeleteAsync("TST_COPIA", ct)).Value.ShouldBeTrue();
        (await _env.Templates.GetAsync("TST_COPIA", ct)).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EditorNaoExcluiTemplateEmbutido()
    {
        var ct = TestContext.Current.CancellationToken;
        await _env.Pack.InstallAsync(ct);

        var deleted = await new TemplateEditor(_env.Templates, _env.DataSource).DeleteAsync("NOME", ct);

        deleted.IsSuccess.ShouldBeFalse();
        deleted.Error.Code.ShouldBe(MockDataStudio.Core.Common.ErrorCodes.CatBuiltinReadOnly);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ImportacaoPorScript_AnalisaEMescla()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TemplateImportService(_env.Importer);

        var analysis = await service.AnalyzeScriptAsync(ScriptValid, MergeMode.Upsert, ct);

        analysis.IsSuccess.ShouldBeTrue(analysis.IsSuccess ? string.Empty : analysis.Error.Message);
        analysis.Value!.CanMerge.ShouldBeTrue();
        analysis.Value.TemplatesNew.ShouldBe(1);
        analysis.Value.ValuesNew.ShouldBe(2);

        var merged = await service.MergeAsync(analysis.Value.StagingSessionId, ct);
        merged.IsSuccess.ShouldBeTrue();
        (await _env.Templates.GetAsync("TST_CIDADE", ct)).ShouldNotBeNull();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ImportacaoPorScript_ScriptInvalidoNaoPodeSerMesclado()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TemplateImportService(_env.Importer);

        var analysis = await service.AnalyzeScriptAsync("INSERT INTO tabela_que_nao_existe VALUES (1);", MergeMode.Upsert, ct);

        (!analysis.IsSuccess || !analysis.Value!.CanMerge).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task PackJson_MalformadoDevolveErroSemExcecao()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TemplateImportService(_env.Importer);

        var analysis = await service.AnalyzePackJsonAsync("{ \"sem_templates\": true }", MergeMode.Upsert, ct);

        analysis.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EditorCarregaOsMesmosValoresSalvos()
    {
        var ct = TestContext.Current.CancellationToken;
        var editor = new TemplateEditor(_env.Templates, _env.DataSource);
        await editor.SaveScalarAsync(new ScalarTemplateForm("TST_CARGA", "Carga", "desc", LogicalType.String,
            ["alfa", "beta", "gama"], ["apelido"]), ct);

        var loaded = await editor.LoadScalarAsync("TST_CARGA", ct);

        loaded.IsSuccess.ShouldBeTrue(loaded.IsSuccess ? string.Empty : loaded.Error.Message);
        loaded.Value!.Values.OrderBy(v => v, StringComparer.Ordinal).ShouldBe(["alfa", "beta", "gama"]);
        loaded.Value.Aliases.ShouldBe(["apelido"]);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EditorNaoAbreTemplateEmbutidoParaEdicao()
    {
        var ct = TestContext.Current.CancellationToken;
        await _env.Pack.InstallAsync(ct);

        var loaded = await new TemplateEditor(_env.Templates, _env.DataSource).LoadScalarAsync("NOME", ct);

        loaded.IsSuccess.ShouldBeFalse();
        loaded.Error.Code.ShouldBe(MockDataStudio.Core.Common.ErrorCodes.CatBuiltinReadOnly);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Instrucoes_DescrevemAsTabelasDeStaging()
    {
        TemplateInstructions.ForAi.ShouldContain("template_value");
        TemplateInstructions.ForAi.ShouldContain("template_record");
        TemplateInstructions.ForAi.ShouldContain("INSERT");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EditorPreservaTagsPesosEAliasesEmRoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var editor = new TemplateEditor(_env.Templates, _env.DataSource);

        var originalDraft = new TemplateDraft(
            "TST_FISCAL",
            "Template Fiscal",
            TemplateKind.Scalar,
            LogicalType.String,
            "Descrição fiscal",
            ["tributario", "brasil"],
            [],
            [new DraftAlias("imposto", null, 1.8, "cad_*")],
            [new TemplateValueRow("ICMS", 5.0), new TemplateValueRow("ISS", 2.5)],
            []);

        var saveInit = await _env.Templates.SaveAsync(originalDraft, ct);
        saveInit.IsSuccess.ShouldBeTrue();

        var infoInitial = await _env.Templates.GetAsync("TST_FISCAL", ct);
        infoInitial.ShouldNotBeNull();
        var initialHash = infoInitial.ContentHash;

        // Carrega via editor
        var formResult = await editor.LoadScalarAsync("TST_FISCAL", ct);
        formResult.IsSuccess.ShouldBeTrue();
        var form = formResult.Value!;

        // Salva novamente sem alterações no formulário
        var saveAgain = await editor.SaveScalarAsync(form, ct);
        saveAgain.IsSuccess.ShouldBeTrue();

        var reloadedDraft = await _env.Templates.GetDraftAsync("TST_FISCAL", ct);
        reloadedDraft.ShouldNotBeNull();
        reloadedDraft.Tags.ShouldBe(["tributario", "brasil"]);
        reloadedDraft.Aliases.Count.ShouldBe(1);
        reloadedDraft.Aliases[0].Alias.ShouldBe("imposto");
        reloadedDraft.Aliases[0].Weight.ShouldBe(1.8);
        reloadedDraft.Aliases[0].ContextTablePattern.ShouldBe("cad_*");

        var valIcms = reloadedDraft.Values.Single(v => v.Value == "ICMS");
        valIcms.Weight.ShouldBe(5.0);
        var valIss = reloadedDraft.Values.Single(v => v.Value == "ISS");
        valIss.Weight.ShouldBe(2.5);

        var infoAgain = await _env.Templates.GetAsync("TST_FISCAL", ct);
        infoAgain!.ContentHash.ShouldBe(initialHash, "Hash deve ser idêntico ao re-salvar sem mudanças");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ListAsync_BuscaPorTagEAlias()
    {
        var ct = TestContext.Current.CancellationToken;
        var draft = new TemplateDraft(
            "TST_BUSCA",
            "Template Para Busca",
            TemplateKind.Scalar,
            LogicalType.String,
            null,
            ["tag_exclusiva"],
            [],
            [new DraftAlias("alias_exclusivo")],
            [new TemplateValueRow("valor1", 1.0)],
            []);

        (await _env.Templates.SaveAsync(draft, ct)).IsSuccess.ShouldBeTrue();

        var byTag = await _env.Templates.ListAsync(new TemplateQuery(Text: "tag_exclusiva"), ct);
        byTag.Any(t => t.Code == "TST_BUSCA").ShouldBeTrue("deve encontrar pela tag");

        var byAlias = await _env.Templates.ListAsync(new TemplateQuery(Text: "alias_exclusivo"), ct);
        byAlias.Any(t => t.Code == "TST_BUSCA").ShouldBeTrue("deve encontrar pelo alias");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseLines_RemoveVaziosEDuplicados()
    {
        TemplateEditor.ParseLines("  a \n\n b\r\na").ShouldBe(["a", "b"]);
    }
}
