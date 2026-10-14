using MockDataStudio.App.Controls;
using MockDataStudio.App.Resources;
using MockDataStudio.Application;

namespace MockDataStudio.App.Services;

/// <summary>Confirma a exclusão de um template mostrando o impacto (TC-202). Implementação visual no App, falsa nos testes.</summary>
public interface ITemplateDeleteConfirmation
{
    bool Confirm(TemplateDeletionImpact impact);
}

/// <summary>Regra de confirmação (D-06): a partir do limiar de itens é preciso digitar o código do template.</summary>
public static class TemplateDeletePolicy
{
    public const int TypedConfirmationThreshold = 100;

    public static bool RequiresTypedConfirmation(TemplateDeletionImpact impact) =>
        impact.ValueOrRecordCount >= TypedConfirmationThreshold;

    public static string Message(TemplateDeletionImpact impact) =>
        TemplatesText.DeleteMessage(impact.Name, impact.Code, impact.ValueOrRecordCount, impact.AliasCount);
}

public sealed class TemplateDeleteConfirmation : ITemplateDeleteConfirmation
{
    public bool Confirm(TemplateDeletionImpact impact)
    {
        var owner = System.Windows.Application.Current.MainWindow;
        var message = TemplateDeletePolicy.Message(impact);
        return TemplateDeletePolicy.RequiresTypedConfirmation(impact)
            ? TypedConfirmDialog.Ask(owner, TemplatesText.DeleteTitle, message, impact.Code, TemplatesText.DeleteButton)
            : ConfirmDialog.Ask(owner, TemplatesText.DeleteTitle, message, TemplatesText.DeleteButton);
    }
}
