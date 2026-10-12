using MockDataStudio.Core.Connections;
using MockDataStudio.SqlTooling.Classification;

namespace MockDataStudio.Application.Explorer;

// Política de execução do Explorer e do editor SQL (docs/23 §5; prompt do módulo, seção 7).
// A decisão é tomada antes de qualquer comando chegar ao banco. Esta política não substitui a restrição efetiva
// de sessão (ExecutionPolicy.SessionRestriction), que é aplicada pelo provider quando suportada.

public enum StatementAccess
{
    Read,
    DataChange,
    Structural,
    Administrative,
    Unknown,
}

public enum AccessDecision
{
    Allow,
    RequiresConfirmation,
    Blocked,
}

public sealed record AccessVerdict(AccessDecision Decision, string Reason);

public sealed record ExecutionPolicy(
    bool ProfileReadOnly,
    ConnectionEnvironment Environment,
    string? SessionRestriction = null)
{
    // Restrição efetiva de sessão para perfis somente leitura, em SQL do próprio provider (ex.: "PRAGMA query_only = ON;").
    // Fornecida pela composição; nulo quando o provider não oferece restrição de sessão.
    public static ExecutionPolicy Default(ConnectionEnvironment environment) => new(false, environment);

    public bool IsProduction => Environment is ConnectionEnvironment.Production or ConnectionEnvironment.Unknown;

    // Mapeia a classificação do SqlTooling para a natureza do acesso.
    public static StatementAccess AccessOf(StatementKind kind) => kind switch
    {
        StatementKind.Select => StatementAccess.Read,
        StatementKind.Transaction => StatementAccess.Read,
        StatementKind.Insert or StatementKind.Update or StatementKind.Delete or StatementKind.Merge => StatementAccess.DataChange,
        StatementKind.Ddl => StatementAccess.Structural,
        StatementKind.Session => StatementAccess.Administrative,
        StatementKind.Procedure => StatementAccess.Administrative,
        _ => StatementAccess.Unknown,
    };

    // Avalia um comando. "Blocked" nunca é liberado pela confirmação do usuário.
    public AccessVerdict Evaluate(StatementAccess access, bool isDestructive, bool authorizedProduction)
    {
        if (ProfileReadOnly && access is not StatementAccess.Read)
        {
            return new AccessVerdict(AccessDecision.Blocked, "Perfil somente leitura: o comando altera dados, estrutura ou sessão.");
        }

        switch (access)
        {
            case StatementAccess.Read:
                return new AccessVerdict(AccessDecision.Allow, "Consulta de leitura.");

            case StatementAccess.Unknown:
                return authorizedProduction || !IsProduction
                    ? new AccessVerdict(AccessDecision.RequiresConfirmation, "Comando não reconhecido: confirmação necessária.")
                    : new AccessVerdict(AccessDecision.Blocked, "Comando não reconhecido em ambiente de produção.");

            case StatementAccess.DataChange when isDestructive:
                return new AccessVerdict(AccessDecision.RequiresConfirmation,
                    "Alteração de dados sem cláusula WHERE: afetará todas as linhas da tabela.");

            case StatementAccess.DataChange when IsProduction && !authorizedProduction:
                return new AccessVerdict(AccessDecision.Blocked, "Alteração de dados em produção exige autorização explícita.");

            case StatementAccess.DataChange:
                return new AccessVerdict(AccessDecision.RequiresConfirmation, "Alteração de dados: confirmação necessária.");

            case StatementAccess.Structural or StatementAccess.Administrative when IsProduction && !authorizedProduction:
                return new AccessVerdict(AccessDecision.Blocked, "Comando estrutural ou administrativo em produção exige autorização explícita.");

            default:
                return new AccessVerdict(AccessDecision.RequiresConfirmation, "Comando estrutural ou administrativo: confirmação necessária.");
        }
    }
}
