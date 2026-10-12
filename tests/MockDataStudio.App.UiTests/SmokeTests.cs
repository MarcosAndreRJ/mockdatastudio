using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Shouldly;
using Xunit;

namespace MockDataStudio.App.UiTests;

// Teste de fumaça (Sprint 1, etapa 1.8): abre o app, percorre todas as telas e falha se aparecer erro.
public sealed class SmokeTests
{
    private static readonly string[] NavigationTitles =
    [
        "Home",
        "Connections",
        "Explorer",
        "SQL Editor",
        "Schema Digest",
        "Templates",
        "Mapping",
        "Generation",
        "History",
        "Settings",
    ];

    [Fact]
    [Trait("Category", "UI")]
    [Trait("Id", "T-UI-SMOKE-001")]
    public void T_UI_SMOKE_001_NavegaTodasAsTelas_SemErroNaInterface()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "MockDataStudio.App.exe");
        File.Exists(exePath).ShouldBeTrue($"Executável do app não encontrado em {exePath}.");

        using var automation = new UIA3Automation();
        using var app = Application.Launch(exePath);

        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30));
        window.ShouldNotBeNull("A janela principal do app não apareceu.");

        foreach (var title in NavigationTitles)
        {
            var item = window.FindFirstDescendant(cf => cf.ByName(title));
            item.ShouldNotBeNull($"Item de navegação '{title}' não encontrado.");

            item.Click();
            Thread.Sleep(600);

            // A caixa de erro do app tem o mesmo título da janela principal: conta-se a quantidade de janelas.
            var topLevelWindows = app.GetAllTopLevelWindows(automation);
            Console.WriteLine($"DBG {title}: {topLevelWindows.Length} -> " + string.Join(" | ", topLevelWindows.Select(w => w.Title + "[" + w.ClassName + "]")));
            topLevelWindows.Length.ShouldBe(1, $"Tela '{title}' abriu uma janela inesperada (provável erro de interface).");
        }

        app.Close();
    }
}
