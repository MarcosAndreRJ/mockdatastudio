namespace MockDataStudio.App.Resources;

// Textos compostos da tela Templates (a parte gerada está em TemplatesText.Generated.cs).
public static partial class TemplatesText
{
    /// <summary>Resumo da análise de importação; o trecho de problemas só aparece quando há algum.</summary>
    public static string ImportAnalysisOk(int templatesNew, int templatesChanged, int valuesNew, int issues) =>
        ImportAnalysisOkBase(templatesNew, templatesChanged, valuesNew) + (issues > 0 ? ImportIssues(issues) : string.Empty);

    public static string ImportAnalysisBlocked(int issues) =>
        ImportBlockedBase + (issues > 0 ? ImportIssues(issues) : string.Empty);
}
