using System.Reflection;
using System.Text.RegularExpressions;
using MockDataStudio.App.Resources;
using Shouldly;
using Xunit;

namespace MockDataStudio.App.Tests;

// T-TPL-I18N-001/002: os textos da tela Templates (TC-105) vêm só de Strings.resx e as duas listas não divergem.
public sealed partial class TemplatesTextTests
{
    [GeneratedRegex(@"name=""(Templates[.][A-Za-z0-9]+)""")]
    private static partial Regex ResxKey();

    [GeneratedRegex(@"(?:Get|Format)[(]""(Templates[.][A-Za-z0-9]+)""")]
    private static partial Regex CodeKey();

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MockDataStudio.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src", "MockDataStudio.App", "Resources");
    }

    [Fact]
    [Trait("Id", "T-TPL-I18N-001")]
    public void TodaPropriedadeDeTextoResolveNoResx()
    {
        var properties = typeof(TemplatesText).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string));

        foreach (var property in properties)
        {
            var value = (string)property.GetValue(null)!;
            value.ShouldNotBeNullOrWhiteSpace(property.Name);
            value.ShouldNotStartWith("Templates.", customMessage: property.Name);
        }
    }

    [Fact]
    [Trait("Id", "T-TPL-I18N-002")]
    public void ChavesDoResxEDoCodigoSaoAsMesmas()
    {
        var resx = ResxKey().Matches(File.ReadAllText(Path.Combine(AppDir(), "Strings.resx"))).Select(m => m.Groups[1].Value).ToHashSet();
        var code = CodeKey().Matches(File.ReadAllText(Path.Combine(AppDir(), "TemplatesText.Generated.cs"))).Select(m => m.Groups[1].Value).ToHashSet();

        resx.Except(code).ShouldBeEmpty("chaves no resx sem texto em TemplatesText");
        code.Except(resx).ShouldBeEmpty("chaves usadas em TemplatesText que não existem no resx");
    }

    [Fact]
    [Trait("Id", "T-TPL-I18N-003")]
    public void TextosCompostosDaAnaliseDeImportacao()
    {
        TemplatesText.ImportAnalysisOk(1, 2, 3, 0).ShouldNotContain(TemplatesText.ImportIssues(0));
        TemplatesText.ImportAnalysisBlocked(2).ShouldStartWith(TemplatesText.ImportBlockedBase);
    }
}
