using System.Data.Common;
using MockDataStudio.Application.Explorer;
using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// Execução SQL (prompt, seções 6 a 8 e 13): paginação, confirmação, políticas, erros, cancelamento e sessão somente leitura.
public sealed class QueryServiceTests
{
    private const string ReadOnlySession = "PRAGMA query_only = ON;";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Consulta_RetornaColunasReaisPrimeiraPaginaEMarcaSeHaMais()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var run = await service.ExecuteAsync(connection,
            Request("SELECT id, nome, email FROM cliente ORDER BY id", pageSize: 2), TestContext.Current.CancellationToken);

        var statement = run.Statements.Single();
        statement.Status.ShouldBe(StatementStatus.Succeeded);
        statement.Columns.Select(c => c.Name).ShouldBe(["id", "nome", "email"]);
        statement.FirstPage.Count.ShouldBe(2);
        statement.HasMore.ShouldBeTrue();
        statement.FirstPage[0][1].ShouldBe("Ana");
        statement.FirstPage[1][2].ShouldBeNull("NULL do banco deve chegar como null, não como texto.");

        var rest = await statement.Cursor!.NextPageAsync(2, TestContext.Current.CancellationToken);
        rest.Rows.Count.ShouldBe(1);
        rest.HasMore.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Paginacao_PercorreGrandeVolumeEmPaginasSemCarregarTudo()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        await SqliteWorkbench.ScalarAsync(connection,
            "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n WHERE x < 1200) " +
            "INSERT INTO pedido (id, cliente_id, total) SELECT x + 100, 1, x FROM n", TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var run = await service.ExecuteAsync(connection,
            Request("SELECT id FROM pedido WHERE id >= 100 ORDER BY id", pageSize: 500), TestContext.Current.CancellationToken);
        var statement = run.Statements.Single();
        var total = statement.FirstPage.Count;
        var pages = 1;
        var page = new ResultPage(statement.FirstPage, statement.HasMore);
        while (page.HasMore)
        {
            page = await statement.Cursor!.NextPageAsync(500, TestContext.Current.CancellationToken);
            total += page.Rows.Count;
            pages++;
        }

        total.ShouldBe(1200);
        pages.ShouldBe(3);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dml_EmDesenvolvimentoExigeConfirmacaoAntesDeAlterar()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var pending = await service.ExecuteAsync(connection,
            Request("UPDATE cliente SET ativo = 0 WHERE id = 1"), TestContext.Current.CancellationToken);
        pending.Statements.Single().Status.ShouldBe(StatementStatus.RequiresConfirmation);
        (await SqliteWorkbench.ScalarAsync(connection, "SELECT ativo FROM cliente WHERE id = 1", TestContext.Current.CancellationToken))
            .ShouldBe(1L);

        await using var confirmed = await service.ExecuteAsync(connection,
            Request("UPDATE cliente SET ativo = 0 WHERE id = 1", confirmed: true), TestContext.Current.CancellationToken);
        var statement = confirmed.Statements.Single();
        statement.Status.ShouldBe(StatementStatus.Succeeded);
        statement.RecordsAffected.ShouldBe(1L);
        statement.Messages.Single().Text.ShouldContain("1 linha(s) afetada(s)");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dml_SemWhereEhDestrutivoEExigeConfirmacao()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var run = await service.ExecuteAsync(connection, Request("DELETE FROM pedido"), TestContext.Current.CancellationToken);

        run.Statements.Single().Status.ShouldBe(StatementStatus.RequiresConfirmation);
        run.Statements.Single().Messages.Single().Text.ShouldContain("todas as linhas");
        (await SqliteWorkbench.ScalarAsync(connection, "SELECT count(*) FROM pedido", TestContext.Current.CancellationToken))
            .ShouldBe(3L);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Producao_AlteracaoSemAutorizacaoEhBloqueadaMesmoConfirmada()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var run = await service.ExecuteAsync(connection,
            Request("UPDATE cliente SET ativo = 0 WHERE id = 1", confirmed: true, environment: ConnectionEnvironment.Production),
            TestContext.Current.CancellationToken);

        run.Statements.Single().Status.ShouldBe(StatementStatus.Blocked);
        (await SqliteWorkbench.ScalarAsync(connection, "SELECT ativo FROM cliente WHERE id = 1", TestContext.Current.CancellationToken))
            .ShouldBe(1L);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Producao_ComAutorizacaoExplicitaExecuta()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var run = await service.ExecuteAsync(connection,
            Request("UPDATE cliente SET ativo = 0 WHERE id = 1", confirmed: true, authorized: true, environment: ConnectionEnvironment.Production),
            TestContext.Current.CancellationToken);

        run.Statements.Single().Status.ShouldBe(StatementStatus.Succeeded);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PerfilSomenteLeitura_BloqueiaEscritaEAplicaRestricaoDeSessao()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);
        var policy = new ExecutionPolicy(ProfileReadOnly: true, Environment: ConnectionEnvironment.Development, SessionRestriction: ReadOnlySession);

        await using var write = await service.ExecuteAsync(connection,
            new ExecutionRequest("UPDATE cliente SET ativo = 0 WHERE id = 1", SqliteWorkbench.Dialect, policy, Confirmed: true),
            TestContext.Current.CancellationToken);
        write.Statements.Single().Status.ShouldBe(StatementStatus.Blocked);

        await using var read = await service.ExecuteAsync(connection,
            new ExecutionRequest("SELECT count(*) FROM cliente", SqliteWorkbench.Dialect, policy),
            TestContext.Current.CancellationToken);
        read.Statements.Single().Status.ShouldBe(StatementStatus.Succeeded);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PerfilSomenteLeitura_SemRestricaoEfetivaNaoExecutaNada()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);
        var policy = new ExecutionPolicy(ProfileReadOnly: true, Environment: ConnectionEnvironment.Development, SessionRestriction: null);

        await using var run = await service.ExecuteAsync(connection,
            new ExecutionRequest("SELECT 1", SqliteWorkbench.Dialect, policy), TestContext.Current.CancellationToken);

        run.Statements.ShouldBeEmpty();
        run.Messages.Single().Text.ShouldContain("sem restrição efetiva");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ErroDeSintaxe_InformaLinhaECodigoEInterrompeOScript()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var run = await service.ExecuteAsync(connection,
            // "SELEC" não é reconhecido pelo classificador: a política exige confirmação antes de executar.
            Request("SELECT 1;\nSELEC 2;\nSELECT 3;", confirmed: true), TestContext.Current.CancellationToken);

        run.Statements.Count.ShouldBe(2);
        var failed = run.Statements[1];
        failed.Status.ShouldBe(StatementStatus.Failed);
        failed.Line.ShouldBe(2);
        failed.Error!.Code.ShouldBe(ErrorCodes.PrvSqlSyntax);
        failed.Messages.Single().Text.ShouldStartWith("Erro na linha 2");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Script_ExecutaComandosEmSequenciaComResultadosPorComando()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var run = await service.ExecuteAsync(connection,
            Request("INSERT INTO pedido (id, cliente_id, total) VALUES (20, 2, 5);\nSELECT count(*) FROM pedido;", confirmed: true),
            TestContext.Current.CancellationToken);

        run.Statements.Count.ShouldBe(2);
        run.Statements[0].RecordsAffected.ShouldBe(1L);
        run.Statements[1].FirstPage[0][0].ShouldBe(4L);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancelamento_InterrompeConsultaLongaComStatusCancelado()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);
        // Consulta que produz linhas continuamente: o cancelamento atua entre linhas. Uma consulta que agrega numa única
        // etapa (count(*) sobre CTE infinita) não é interrompida pelo Cancel() do Microsoft.Data.Sqlite (ver PV-019).
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await using var run = await service.ExecuteAsync(connection,
            Request("WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c) SELECT x FROM c",
                pageSize: 1_000_000), cts.Token);

        run.Statements.Single().Status.ShouldBe(StatementStatus.Cancelled);
        run.Statements.Single().Messages.Single().Text.ShouldContain("cancelada");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Ddl_EmDesenvolvimentoExigeConfirmacao()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var service = Service(bench);

        await using var pending = await service.ExecuteAsync(connection,
            Request("CREATE TABLE teste_ddl (a INTEGER)"), TestContext.Current.CancellationToken);
        pending.Statements.Single().Status.ShouldBe(StatementStatus.RequiresConfirmation);

        await using var confirmed = await service.ExecuteAsync(connection,
            Request("CREATE TABLE teste_ddl (a INTEGER)", confirmed: true), TestContext.Current.CancellationToken);
        confirmed.Statements.Single().Status.ShouldBe(StatementStatus.Succeeded);
    }

    private static QueryService Service(SqliteWorkbench bench) => new(SqliteWorkbench.Splitter, bench.Provider.ErrorTranslator);

    private static ExecutionRequest Request(string script, bool confirmed = false, bool authorized = false,
        ConnectionEnvironment environment = ConnectionEnvironment.Development, int pageSize = 500) =>
        new(script, SqliteWorkbench.Dialect, ExecutionPolicy.Default(environment), confirmed, authorized, pageSize);
}
