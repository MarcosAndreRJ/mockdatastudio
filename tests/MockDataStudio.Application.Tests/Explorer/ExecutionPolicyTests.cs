using MockDataStudio.Application.Explorer;
using MockDataStudio.Core.Connections;
using MockDataStudio.SqlTooling.Classification;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// Matriz de decisão da política (prompt, seção 7). Testes unitários, sem banco.
public sealed class ExecutionPolicyTests
{
    [Theory]
    [InlineData(StatementKind.Select, StatementAccess.Read)]
    [InlineData(StatementKind.Transaction, StatementAccess.Read)]
    [InlineData(StatementKind.Insert, StatementAccess.DataChange)]
    [InlineData(StatementKind.Update, StatementAccess.DataChange)]
    [InlineData(StatementKind.Delete, StatementAccess.DataChange)]
    [InlineData(StatementKind.Merge, StatementAccess.DataChange)]
    [InlineData(StatementKind.Ddl, StatementAccess.Structural)]
    [InlineData(StatementKind.Session, StatementAccess.Administrative)]
    [InlineData(StatementKind.Procedure, StatementAccess.Administrative)]
    [InlineData(StatementKind.Other, StatementAccess.Unknown)]
    [Trait("Category", "Unit")]
    public void Classificacao_MapeiaTipoDeComandoParaNaturezaDeAcesso(StatementKind kind, StatementAccess expected) =>
        ExecutionPolicy.AccessOf(kind).ShouldBe(expected);

    [Fact]
    [Trait("Category", "Unit")]
    public void PerfilSomenteLeitura_BloqueiaQualquerComandoQueNaoSejaLeitura()
    {
        var policy = new ExecutionPolicy(true, ConnectionEnvironment.Development);
        policy.Evaluate(StatementAccess.Read, false, false).Decision.ShouldBe(AccessDecision.Allow);
        policy.Evaluate(StatementAccess.DataChange, false, true).Decision.ShouldBe(AccessDecision.Blocked);
        policy.Evaluate(StatementAccess.Structural, false, true).Decision.ShouldBe(AccessDecision.Blocked);
        policy.Evaluate(StatementAccess.Administrative, false, true).Decision.ShouldBe(AccessDecision.Blocked);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Producao_EstruturaSemAutorizacaoEhBloqueada()
    {
        var policy = ExecutionPolicy.Default(ConnectionEnvironment.Production);
        policy.Evaluate(StatementAccess.Structural, false, false).Decision.ShouldBe(AccessDecision.Blocked);
        policy.Evaluate(StatementAccess.Structural, false, true).Decision.ShouldBe(AccessDecision.RequiresConfirmation);
        policy.Evaluate(StatementAccess.DataChange, false, false).Decision.ShouldBe(AccessDecision.Blocked);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AmbienteDesconhecido_ComandoNaoReconhecidoExigeConfirmacao()
    {
        var unknownEnvironment = ExecutionPolicy.Default(ConnectionEnvironment.Unknown);
        unknownEnvironment.Evaluate(StatementAccess.Unknown, false, false).Decision.ShouldBe(AccessDecision.Blocked);
        var development = ExecutionPolicy.Default(ConnectionEnvironment.Development);
        development.Evaluate(StatementAccess.Unknown, false, false).Decision.ShouldBe(AccessDecision.RequiresConfirmation);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Leitura_SempreEPermitida()
    {
        ExecutionPolicy.Default(ConnectionEnvironment.Production).Evaluate(StatementAccess.Read, false, false)
            .Decision.ShouldBe(AccessDecision.Allow);
    }
}
