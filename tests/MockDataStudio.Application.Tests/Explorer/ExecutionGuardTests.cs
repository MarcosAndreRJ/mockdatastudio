using MockDataStudio.Application.Explorer;
using MockDataStudio.Core.Connections;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// T-SQL-GUARD-001: matriz de confirmação do ExecutionGuard (docs/23 §5.2), no ponto de decisão do editor.
public sealed class ExecutionGuardTests
{
    [Theory]
    [InlineData(ConnectionEnvironment.Development, StatementAccess.Structural, ConfirmationLevel.Simple)]
    [InlineData(ConnectionEnvironment.Test, StatementAccess.Structural, ConfirmationLevel.Simple)]
    [InlineData(ConnectionEnvironment.Production, StatementAccess.Structural, ConfirmationLevel.Typed)]
    [InlineData(ConnectionEnvironment.Unknown, StatementAccess.Administrative, ConfirmationLevel.Typed)]
    public void DdlOuAdministrativo_ExigeDigitacaoSoEmProducaoOuDesconhecido(
        ConnectionEnvironment environment, StatementAccess access, ConfirmationLevel expected)
    {
        var verdict = new AccessVerdict(AccessDecision.RequiresConfirmation, "Comando estrutural ou administrativo: confirmação necessária.");

        ExecutionGuard.RequestFor(access, verdict, environment).Level.ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-GUARD-001")]
    public void AlteracaoDestrutivaEmProducao_PedeDigitacaoDaPalavra()
    {
        var verdict = new AccessVerdict(AccessDecision.RequiresConfirmation, ExecutionPolicy.DestructiveDataChangeReason);

        var request = ExecutionGuard.RequestFor(StatementAccess.DataChange, verdict, ConnectionEnvironment.Production);

        request.Level.ShouldBe(ConfirmationLevel.Typed);
        request.TypedWord.ShouldBe(ExecutionGuard.TypedWord);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-GUARD-001")]
    public void AlteracaoComWhereEmProducao_PedeSoConfirmacaoSimples()
    {
        var verdict = new AccessVerdict(AccessDecision.RequiresConfirmation, "Alteração de dados: confirmação necessária.");

        var request = ExecutionGuard.RequestFor(StatementAccess.DataChange, verdict, ConnectionEnvironment.Production);

        request.Level.ShouldBe(ConfirmationLevel.Simple);
        request.TypedWord.ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-GUARD-001")]
    public void Bloqueado_NaoPedeConfirmacaoNenhuma()
    {
        var verdict = new AccessVerdict(AccessDecision.Blocked, "Perfil somente leitura: o comando altera dados, estrutura ou sessão.");

        ExecutionGuard.RequestFor(StatementAccess.DataChange, verdict, ConnectionEnvironment.Production).Level
            .ShouldBe(ConfirmationLevel.None);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-GUARD-001")]
    public void Consulta_NaoPedeConfirmacao()
    {
        var verdict = new AccessVerdict(AccessDecision.Allow, "Consulta de leitura.");

        ExecutionGuard.RequestFor(StatementAccess.Read, verdict, ConnectionEnvironment.Production).Level
            .ShouldBe(ConfirmationLevel.None);
    }
}
