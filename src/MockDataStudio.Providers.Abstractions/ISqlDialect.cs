using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface ISqlDialect
{
    DatabaseEngine Engine { get; }
    string QuoteIdentifier(string identifier);
    string QualifiedName(ObjectName name);
    ISqlLiteralFormatter Literals { get; }
    string StatementTerminator { get; }
    string? BeginTransaction { get; }
    string Commit { get; }
    IReadOnlyList<string> SessionPreamble(ScriptOptions options);
    string? DisableFkChecks();
    string? EnableFkChecks();
    string? IdentityOverrideClause(ColumnModel identityColumn);
    IReadOnlyList<string> ResyncSequenceStatements(ObjectName table, ColumnModel column, string? sequenceName, long nextValue);
    string BuildInsert(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalRows, string? overrideClause);
    int RecommendedRowsPerInsert { get; }

    /// <summary>
    /// Comando que falha se a faixa reservada de chaves estiver ocupada (EmitGuards, 16 §5.2, 18 §1).
    /// Nulo = dialeto sem suporte: o writer emite só o comentário de verificação.
    /// </summary>
    string? BuildKeyRangeGuard(ObjectName table, string column, long first, long last) => null;

    /// <summary>Linhas entre commits periódicos no script (docs/18 §5). Padrão 50.000; MySQL 10.000 e Firebird 5.000 na sua implementação.</summary>
    long RecommendedCommitRows => 50_000;

    /// <summary>Comandos que adiam a verificação das FKs até o fim da transação (ciclos com DeferredConstraint). Vazio = sem suporte.</summary>
    IReadOnlyList<string> DeferConstraintStatements() => [];

    /// <summary>Tuplas por condição de limpeza (docs/18 §7). Padrão 1.000; Firebird usa 200.</summary>
    int MaxTuplesPerPredicate => 1000;

    /// <summary>
    /// Condição WHERE que casa qualquer uma das tuplas de chave (limpeza). Padrão: <c>(c1, c2) IN ((..),(..))</c>, ou
    /// <c>c IN (..)</c> para chave simples. Dialetos sem comparação de tuplas usam <c>(c1 = x AND c2 = y) OR ...</c>.
    /// </summary>
    string BuildTuplePredicate(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> literalTuples)
    {
        if (columns.Count == 1)
        {
            return QuoteIdentifier(columns[0]) + " IN (" + string.Join(", ", literalTuples.Select(t => t[0])) + ")";
        }

        return "(" + string.Join(", ", columns.Select(QuoteIdentifier)) + ") IN ("
            + string.Join(", ", literalTuples.Select(t => "(" + string.Join(", ", t) + ")")) + ")";
    }
}
