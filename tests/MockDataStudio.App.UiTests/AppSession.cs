using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Shouldly;

namespace MockDataStudio.App.UiTests;

// Sessão de teste: abre o app com uma pasta de dados descartável (MDS_DATA_DIR), para não tocar nos dados do usuário.
internal sealed class AppSession : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly FlaUI.Core.Application _app;

    public AppSession(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);

        var exePath = Path.Combine(AppContext.BaseDirectory, "MockDataStudio.App.exe");
        File.Exists(exePath).ShouldBeTrue($"Executável do app não encontrado em {exePath}.");

        var info = new ProcessStartInfo(exePath) { UseShellExecute = false };
        info.Environment["MDS_DATA_DIR"] = dataDir;
        _app = FlaUI.Core.Application.Launch(info);
        Window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(30)) ?? throw new InvalidOperationException("A janela principal do app não apareceu.");
    }

    public string DataDir { get; }

    public Window Window { get; }

    public AutomationElement Navigate(string item)
    {
        var nav = Window.FindFirstDescendant(cf => cf.ByName(item));
        nav.ShouldNotBeNull($"Item de navegação '{item}' não encontrado.");
        nav.Activate();
        return nav;
    }

    public AutomationElement Find(string name, TimeSpan? timeout = null)
    {
        AutomationElement? found = null;
        SpinWait.SpinUntil(() =>
        {
            found = Window.FindFirstDescendant(cf => cf.ByName(name));
            return found is not null;
        }, timeout ?? TimeSpan.FromSeconds(15));
        found.ShouldNotBeNull($"Elemento '{name}' não encontrado.");
        return found;
    }

    // Invoca pelo padrão de automação (não depende de o botão estar visível na janela); cai para o clique do mouse sem o padrão.
    public void ClickButton(string name) => Find(name).Activate();

    // Digita o texto num campo identificado pelo nome de automação (AutomationProperties.Name).
    public void SetText(string name, string text) => Find(name).AsTextBox().Enter(text);

    /// <summary>Espera o elemento ficar habilitado (por exemplo, um botão que depende de uma seleção carregada em segundo plano).</summary>
    public void WaitEnabled(string name, TimeSpan? timeout = null)
    {
        var enabled = SpinWait.SpinUntil(() => Find(name).IsEnabled, timeout ?? TimeSpan.FromSeconds(30));
        enabled.ShouldBeTrue($"O elemento '{name}' não foi habilitado.");
    }

    /// <summary>Espera até existir um texto na tela cujo nome contenha o trecho informado (somente elementos de texto).</summary>
    public string WaitForText(string fragment, TimeSpan? timeout = null)
    {
        string? match = null;
        SpinWait.SpinUntil(() =>
        {
            match = TextNames()
                .Select(n => n ?? string.Empty)
                .FirstOrDefault(n => n.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            return match is not null;
        }, timeout ?? TimeSpan.FromSeconds(30));
        match.ShouldNotBeNull($"O texto '{fragment}' não apareceu na tela. Textos visíveis: " + VisibleTexts());
        return match;
    }

    private string VisibleTexts() =>
        string.Join(" | ", TextNames().Where(n => !string.IsNullOrWhiteSpace(n)).Take(60));

    // Nome UIA de cada texto. Alguns elementos não suportam a propriedade Name; nesse caso o nome vira nulo, sem derrubar a espera.
    private IEnumerable<string?> TextNames() =>
        Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(e =>
        {
            try
            {
                return e.Name;
            }
            catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
            {
                return null;
            }
        });

    public void Dispose()
    {
        // Dispose do FlaUI não fecha o processo: encerra explicitamente para não deixar o app vivo.
        try
        {
            _app.Close();
            if (!_app.HasExited)
            {
                _app.Kill();
            }
        }
        finally
        {
            _automation.Dispose();
            try
            {
                Directory.Delete(DataDir, recursive: true);
            }
            catch (IOException)
            {
                // Arquivo ainda aberto pelo SQLite: a pasta temporária é limpa pelo sistema.
            }
        }
    }
}

internal static class ElementActivation
{
    // Ativa pelo padrão de automação (não depende de a janela estar na frente nem do ponteiro do mouse); sem padrão, clica.
    public static void Activate(this AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
        }
        else if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
        }
        else
        {
            element.Click();
        }
    }
}
