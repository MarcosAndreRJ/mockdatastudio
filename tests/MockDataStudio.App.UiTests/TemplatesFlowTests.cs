using Shouldly;
using Xunit;

namespace MockDataStudio.App.UiTests;

// T-UI-FLOW-002 (C8a.1 e C8a.2): edição de um template escalar e importação por script, pela tela Templates.
// Não cobre a cópia para a área de transferência (instruções para IA) nem a exportação de pack (arquivo).
public sealed class TemplatesFlowTests
{
    private const string Script = """
        INSERT INTO template (code, name, kind, logical_type, locale, description, tags) VALUES ('UI_IMPORTADO', 'Importado pela UI', 'scalar', 'string', 'pt-BR', NULL, NULL);
        INSERT INTO template_value (template_code, value, weight) VALUES ('UI_IMPORTADO', 'Alfa', 1.0), ('UI_IMPORTADO', 'Beta', 1.0);
        """;

    [Fact]
    [Trait("Category", "UI")]
    [Trait("Id", "T-UI-FLOW-002")]
    public void T_UI_FLOW_002_EditaTemplateEImportaScript()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "mds-ui-tpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        using var session = new AppSession(dataDir);
        session.Navigate("Templates");
        session.WaitForText("Template Catalog", TimeSpan.FromSeconds(60));

        // Edição: cria um template escalar pelo formulário e confirma que ele aparece na lista.
        session.SetText("Código do template", "UI_EDITADO");
        session.SetText("Nome do template", "Editado pela UI");
        session.SetText("Valores do template", "Um\nDois");
        session.ClickButton("Salvar template");
        session.WaitForText("Template 'UI_EDITADO' salvo", TimeSpan.FromSeconds(60));
        session.WaitForText("Editado pela UI", TimeSpan.FromSeconds(60));

        // Edição do que já foi salvo: altera o nome e salva de novo (atualização no lugar).
        session.SetText("Nome do template", "Editado de novo");
        session.ClickButton("Salvar template");
        session.WaitForText("Editado de novo", TimeSpan.FromSeconds(60));

        // Código digitado em minúsculas e com espaço vira o código válido.
        session.SetText("Código do template", "ui teste");
        session.ClickButton("Salvar template");
        session.WaitForText("Template 'UI_TESTE' salvo", TimeSpan.FromSeconds(60));

        // Exemplo escalar: preenche o formulário e salva.
        session.ClickButton("Exemplo escalar (lista de valores)");
        session.WaitForText("Exemplo escalar carregado", TimeSpan.FromSeconds(60));
        session.ClickButton("Salvar template");
        session.WaitForText("Template 'EXEMPLO_STATUS_PEDIDO' salvo", TimeSpan.FromSeconds(60));

        // Duplicar (TC-202): a cópia recebe código livre derivado do original e aparece na lista.
        session.ClickButton("Duplicar template selecionado");
        session.WaitForText("Cópia 'EXEMPLO_STATUS_PEDIDO_COPIA' criada", TimeSpan.FromSeconds(60));

        // Importação (aba Importar/Exportar): cola o script, analisa e importa; depois volta à aba Templates.
        session.ClickButton("Aba Importar/Exportar");
        session.SetText("Script ou pack a importar", Script);
        session.ClickButton("Analisar");
        session.WaitForText("Análise ok", TimeSpan.FromSeconds(60));
        session.ClickButton("Importar");
        session.WaitForText("Importado:", TimeSpan.FromSeconds(60));
        session.ClickButton("Aba Templates");
        session.WaitForText("Importado pela UI", TimeSpan.FromSeconds(60));
    }
}
