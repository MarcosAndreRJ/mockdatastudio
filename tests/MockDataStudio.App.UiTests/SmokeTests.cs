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
        // Prefixo: a contagem depende dos perfis cadastrados (antes vinha do dado de exemplo, "(12)").
        ("Connections", "Saved Connections ("),
        ("Explorer", "Database Explorer"),
        // Sem perfis salvos, a aba inicial do editor pede a conexão (cada aba escolhe a sua).
        ("SQL Editor", "Selecione a conexão e o database desta aba."),
        ("Schema Digest", "Database Structure"),
        ("Templates", "Template Catalog"),
        ("Mapping", "Field Mapping"),
        ("Generation", "Em desenvolvimento. Esta tela será liberada em uma próxima etapa."),
        ("History", "Em desenvolvimento. Esta tela será liberada em uma próxima etapa."),
        ("Settings", "Em desenvolvimento. Esta tela será liberada em uma próxima etapa."),
    ];

    [Fact]
    [Trait("Category", "UI")]
    [Trait("Id", "T-UI-SMOKE-001")]
    public void T_UI_SMOKE_001_NavegaTodasAsTelas_SemErroNaInterface()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "MockDataStudio.App.exe");
        var dataDir = Path.Combine(Path.GetTempPath(), "mds-ui-" + Guid.NewGuid().ToString("N"));
        File.Exists(exePath).ShouldBeTrue($"Executável do app não encontrado em {exePath}.");

        using var automation = new UIA3Automation();
        var startInfo = new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = false };
        startInfo.Environment["MDS_DATA_DIR"] = dataDir;
        using var app = FlaUI.Core.Application.Launch(startInfo);

        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30));
            window.ShouldNotBeNull("A janela principal do app não apareceu.");

            foreach (var (navigation, marker) in Screens)
            {
                var item = window.FindFirstDescendant(cf => cf.ByName(navigation));
                item.ShouldNotBeNull($"Item de navegação '{navigation}' não encontrado.");

                item.Click();

                // Espera ativa pelo marcador (até 10 s): o app pode demorar sob carga, e um sleep fixo deixava o teste instável.
                // Marcadores com contagem dinâmica são comparados por prefixo: o nome UIA do TextBlock é o texto inteiro.
                // Filtra por tipo (Text) para não enumerar a árvore inteira: a enumeração completa estoura timeout de COM.
                var shown = false;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!shown && DateTime.UtcNow < deadline)
                {
                    shown = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
                        .Any(e => (e.Name ?? string.Empty).StartsWith(marker, StringComparison.Ordinal));
                    if (!shown)
                    {
                        Thread.Sleep(250);
                    }
                }

                shown.ShouldBeTrue($"A tela '{navigation}' não exibiu o texto esperado '{marker}'.");

                // Uma única janela do processo: se a interface abrir um diálogo de erro, ele aparece como janela extra.
                var processWindows = automation.GetDesktop()
                    .FindAllChildren(new PropertyCondition(automation.PropertyLibrary.Element.ProcessId, app.ProcessId));
                processWindows.Length.ShouldBe(1, $"A tela '{navigation}' abriu uma janela inesperada (provável erro de interface).");
            }
        }
        finally
        {
            // Dispose do FlaUI não fecha o processo: encerra explicitamente para não deixar o app vivo.
            app.Close();
            if (!app.HasExited)
            {
                app.Kill();
            }
        }
    }
}
