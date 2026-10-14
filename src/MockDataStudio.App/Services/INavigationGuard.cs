namespace MockDataStudio.App.Services;

/// <summary>
/// Implementada pela página atual do Shell que pode ter alterações não salvas (TC-104). Antes de sair da página, o Shell
/// pergunta ao usuário e chama <see cref="SaveChangesAsync"/> ou <see cref="DiscardChanges"/> conforme a escolha.
/// </summary>
public interface INavigationGuard
{
    /// <summary>Há alterações que seriam perdidas ao sair da página.</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>Texto curto (já traduzido) que diz o que está pendente, mostrado no diálogo.</summary>
    string UnsavedChangesDescription { get; }

    /// <summary>Grava o que está pendente. Retorna falso se algo não pôde ser salvo (a navegação fica bloqueada).</summary>
    Task<bool> SaveChangesAsync();

    /// <summary>Descarta o que está pendente.</summary>
    void DiscardChanges();
}

/// <summary>Escolha do usuário no diálogo de alterações não salvas.</summary>
public enum LeaveDecision
{
    Save,
    Discard,
    Stay,
}

/// <summary>Pergunta ao usuário o que fazer com as alterações pendentes (implementação visual no App, falsa nos testes).</summary>
public interface IUnsavedChangesPrompt
{
    Task<LeaveDecision> AskAsync(string description);
}
