using MockDataStudio.Core.Connections;

namespace MockDataStudio.Application.Explorer;

public enum ConfirmationLevel
{
    None,
    Simple,
    Typed,
}

// O que a UI deve exigir antes de executar: nenhuma confirmação, confirmação simples ou digitação da palavra (docs/23 §5.2).
public sealed record ConfirmationRequest(ConfirmationLevel Level, string Reason, string? TypedWord);

// Componente único da decisão de confirmação (C5.1). Recebe o veredito de ExecutionPolicy e decide o nível de confirmação.
// Não consulta o banco nem abre diálogos: a UI só apresenta o que este componente pede.
public static class ExecutionGuard
{
    // Palavra que o usuário digita em produção para operações destrutivas, DDL e administrativas.
    public const string TypedWord = "EXECUTAR";

    public static ConfirmationRequest RequestFor(StatementAccess access, AccessVerdict verdict, ConnectionEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        if (verdict.Decision != AccessDecision.RequiresConfirmation)
        {
            return new ConfirmationRequest(ConfirmationLevel.None, verdict.Reason, null);
        }

        var production = environment is ConnectionEnvironment.Production or ConnectionEnvironment.Unknown;
        var destructive = verdict.Reason == ExecutionPolicy.DestructiveDataChangeReason;
        var structural = access is StatementAccess.Structural or StatementAccess.Administrative or StatementAccess.Unknown;

        return production && (destructive || structural)
            ? new ConfirmationRequest(ConfirmationLevel.Typed, verdict.Reason, TypedWord)
            : new ConfirmationRequest(ConfirmationLevel.Simple, verdict.Reason, null);
    }
}
