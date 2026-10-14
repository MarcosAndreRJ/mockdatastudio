using MockDataStudio.App.Services;
using MockDataStudio.Application;
using Shouldly;
using Xunit;

namespace MockDataStudio.App.Tests;

// T-TPL-DEL-001..: regra de confirmação da exclusão (TC-202, D-06).
public sealed class TemplateDeletePolicyTests
{
    private static TemplateDeletionImpact Impact(int items, bool builtIn = false) =>
        new("MEU_TPL", "Meu template", builtIn, items, 2, CanDelete: !builtIn);

    [Theory]
    [Trait("Id", "T-TPL-DEL-001")]
    [InlineData(0, false)]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(5000, true)]
    public void DigitarCodigoSoAPartirDoLimiar(int items, bool typed) =>
        TemplateDeletePolicy.RequiresTypedConfirmation(Impact(items)).ShouldBe(typed);

    [Fact]
    [Trait("Id", "T-TPL-DEL-002")]
    public void MensagemMostraNomeCodigoEImpacto()
    {
        var message = TemplateDeletePolicy.Message(Impact(12));

        message.ShouldContain("Meu template");
        message.ShouldContain("MEU_TPL");
        message.ShouldContain("12");
        message.ShouldContain("2");
    }
}
