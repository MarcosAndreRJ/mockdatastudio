using MockDataStudio.Application;
using MockDataStudio.Application.Templates;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests;

public sealed class TemplateDraftValidatorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ValidaCodigoInvalidoERecusa()
    {
        var draft = new TemplateDraft("invalido", "Nome", TemplateKind.Scalar, LogicalType.String, null, [], [], [], [new TemplateValueRow("v", 1.0)], []);
        var res = TemplateDraftValidator.Validate(draft);

        res.IsValid.ShouldBeFalse();
        res.Messages.Any(m => m.PropertyName == nameof(draft.Code)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidaEscalarSemValores()
    {
        var draft = new TemplateDraft("TST_ESCALAR", "Nome", TemplateKind.Scalar, LogicalType.String, null, [], [], [], [], []);
        var res = TemplateDraftValidator.Validate(draft);

        res.IsValid.ShouldBeFalse();
        res.Messages.Any(m => m.PropertyName == nameof(draft.Values)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidaCompostoSemCampos()
    {
        var draft = new TemplateDraft("TST_COMP", "Nome", TemplateKind.Composite, null, null, [], [], [], [], []);
        var res = TemplateDraftValidator.Validate(draft);

        res.IsValid.ShouldBeFalse();
        res.Messages.Any(m => m.PropertyName == nameof(draft.Fields)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidaCamposDuplicadosEmComposto()
    {
        var fields = new List<DraftField>
        {
            new("codigo", LogicalType.String, 1, 10, true),
            new("codigo", LogicalType.Integer, 2, null, true)
        };
        var draft = new TemplateDraft("TST_COMP_DUP", "Nome", TemplateKind.Composite, null, null, [], fields, [], [], []);
        var res = TemplateDraftValidator.Validate(draft);

        res.IsValid.ShouldBeFalse();
        res.Messages.Any(m => m.PropertyName == nameof(draft.Fields) && m.Message.Contains("duplicado")).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidaCamposObrigatoriosVaziosEmRegistros()
    {
        var fields = new List<DraftField>
        {
            new("obrigatorio", LogicalType.String, 1, 10, true)
        };
        var records = new List<TemplateRecordRow>
        {
            new("1", new Dictionary<string, string?> { ["obrigatorio"] = "" }, 1.0)
        };
        var draft = new TemplateDraft("TST_COMP_REG", "Nome", TemplateKind.Composite, null, null, [], fields, [], [], records);
        var res = TemplateDraftValidator.Validate(draft);

        res.IsValid.ShouldBeFalse();
        res.Messages.Any(m => m.PropertyName == nameof(draft.Records) && m.Message.Contains("obrigatório")).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TemplateBrowser_CalculaImpactoDeExclusao()
    {
        var repo = new FakeTemplateRepo();
        var browser = new TemplateBrowser(repo, null!);

        var impact = await browser.GetDeletionImpactAsync("TST_IMPACTO", CancellationToken.None);
        impact.ShouldNotBeNull();
        impact.Code.ShouldBe("TST_IMPACTO");
        impact.IsBuiltIn.ShouldBeFalse();
        impact.CanDelete.ShouldBeTrue();
        impact.ValueOrRecordCount.ShouldBe(20);
        impact.AliasCount.ShouldBe(2);
    }

    private sealed class FakeTemplateRepo : ITemplateRepository
    {
        public Task<IReadOnlyList<TemplateInfo>> ListAsync(TemplateQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<TemplateInfo?> GetAsync(string code, CancellationToken ct) =>
            Task.FromResult<TemplateInfo?>(new TemplateInfo(1, "TST_IMPACTO", "Impacto", TemplateKind.Scalar, LogicalType.String, "pt-BR", [], null, "USER", false, 20, "hash"));
        public Task<TemplateDraft?> GetDraftAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<TemplateFieldInfo>> GetFieldsAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<TemplateAliasInfo>> GetAllAliasesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TemplateAliasInfo>>(
            [
                new(1, "TST_IMPACTO", null, "alias1", "alias1", 1.0, null),
                new(2, "TST_IMPACTO", null, "alias2", "alias2", 1.0, null),
                new(3, "OUTRO", null, "alias3", "alias3", 1.0, null)
            ]);
        public Task<Result<long>> SaveAsync(TemplateDraft draft, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<bool>> DeleteAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<Result<string>> CloneAsync(string code, string newCode, CancellationToken ct) => throw new NotImplementedException();
    }
}
