namespace MockDataStudio.App.Services;

/// <summary>
/// Decide se o Shell pode sair da página atual (TC-104). Sem pendências, libera; com pendências, pergunta ao usuário:
/// Salvar libera só se a gravação tiver sucesso, Descartar libera e descarta, Continuar editando bloqueia.
/// </summary>
public sealed class NavigationGate(IUnsavedChangesPrompt prompt)
{
    public async Task<bool> CanLeaveAsync(object? currentPage)
    {
        if (currentPage is not INavigationGuard guard || !guard.HasUnsavedChanges)
        {
            return true;
        }

        switch (await prompt.AskAsync(guard.UnsavedChangesDescription))
        {
            case LeaveDecision.Save:
                return await guard.SaveChangesAsync();
            case LeaveDecision.Discard:
                guard.DiscardChanges();
                return true;
            default:
                return false;
        }
    }
}
