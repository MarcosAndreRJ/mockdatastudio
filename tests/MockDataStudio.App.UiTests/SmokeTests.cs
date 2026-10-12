using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;
using Shouldly;
using Xunit;

namespace MockDataStudio.App.UiTests;

// Teste de fumaça (Sprint 1, etapa 1.8): abre o app, percorre todas as telas e verifica
// (a) que a tela certa foi exibida (texto exclusivo da tela) e (b) que nenhuma janela extra apareceu.
public sealed class SmokeTests
{
    private static readonly (string Navigation, string ScreenMarker)[] Screens =
    [
        ("Home", "Workspace Overview"),
        ("Connections", "Saved Connections (12)"),
        ("Explorer", "Database Explorer"),
        ("SQL Editor", "Query - shopdb"),
        ("Schema Digest", "Database Structure"),
        ("Templates", "Template Catalog"),
        ("Mapping", "Field Mapping"),
        ("Generation", "Tela sem mockup nesta etapa. Será criada quando o mockup for fornecido."),
        ("History", "Tela sem mockup nesta etapa. Será criada quando o mockup for fornecido."),
        ("Settings", "Tela sem mockup nesta etapa. Será criada quando o mockup for fornecido."),
    ];

    [Fact]
    [Trait("Category", "UI")]
    [Trait("Id", "T-UI-SMOKE-001")]
    public void T_UI_SMOKE_001_NavegaTodasAsTelas_SemErroNaInterface()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "MockDataStudio.App.exe");
        File.Exists(exePath).ShouldBeTrue($"Executável do app não encontrado em {exePath}.");

        using var automation = new UIA3Automation();
        using var app = FlaUI.Core.Application.Launch(exePath);

        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30));
        window.ShouldNotBeNull("A janela principal do app não apareceu.");

        foreach (var (navigation, marker) in Screens)
        {
            var item = window.FindFirstDescendant(cf => cf.ByName(navigation));
            item.ShouldNotBeNull($"Item de navegação '{navigation}' não encontrado.");

            item.Click();
            Thread.Sleep(1200);

            window.FindFirstDescendant(cf => cf.ByName(marker))
                .ShouldNotBeNull($"A tela '{navigation}' não exibiu o texto esperado '{marker}'.");

            // Uma única janela do processo: se a interface abrir um diálogo de erro, ele aparece como janela extra.
            var processWindows = automation.GetDesktop()
                .FindAllChildren(new PropertyCondition(automation.PropertyLibrary.Element.ProcessId, app.ProcessId));
            processWindows.Length.ShouldBe(1, $"A tela '{navigation}' abriu uma janela inesperada (provável erro de interface).");
        }

        app.Close();
    }
}
