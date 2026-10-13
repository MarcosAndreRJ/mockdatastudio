using MockDataStudio.Application.Explorer;
using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.Core.Connections;
using MockDataStudio.SqlTooling.Dialect;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// C6r.1 e C6r.4: sessão de consulta com conexão dedicada e modos de transação; histórico de execuções.
// Verificado em SQLite real: uma segunda conexão enxerga só o que foi confirmado.
public sealed class QuerySessionTests
{
    private static readonly ExecutionPolicy Policy = new(false, ConnectionEnvironment.Development);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ModoManual_RollbackDesfazOInsert()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bench = await SqliteWorkbench.CreateAsync(ct: ct);
        await using var session = new QuerySession(token => bench.OpenAsync(token));
        session.SetMode(TransactionMode.Manual);

        await session.RunAsync(new QueryService(SqliteWorkbench.Splitter, bench.Provider.ErrorTranslator), Insert(), ct);
        session.HasOpenTransaction.ShouldBeTrue();
        await session.RollbackAsync(ct);

        await using var observer = await bench.OpenAsync(ct);
        (await Count(observer, ct)).ShouldBe(3L);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ModoManual_CommitTornaOInsertVisivel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bench = await SqliteWorkbench.CreateAsync(ct: ct);
        await using var session = new QuerySession(token => bench.OpenAsync(token));
        session.SetMode(TransactionMode.Manual);

        await session.RunAsync(new QueryService(SqliteWorkbench.Splitter, bench.Provider.ErrorTranslator), Insert(), ct);
        await session.CommitAsync(ct);

        await using var observer = await bench.OpenAsync(ct);
        (await Count(observer, ct)).ShouldBe(4L);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ModoAutoCommit_InsertFicaVisivelDeImediato()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bench = await SqliteWorkbench.CreateAsync(ct: ct);
        await using var session = new QuerySession(token => bench.OpenAsync(token));

        await session.RunAsync(new QueryService(SqliteWorkbench.Splitter, bench.Provider.ErrorTranslator), Insert(), ct);

        session.HasOpenTransaction.ShouldBeFalse();
        await using var observer = await bench.OpenAsync(ct);
        (await Count(observer, ct)).ShouldBe(4L);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TrocarModo_ComTransacaoAberta_Falha()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bench = await SqliteWorkbench.CreateAsync(ct: ct);
        await using var session = new QuerySession(token => bench.OpenAsync(token));
        session.SetMode(TransactionMode.Manual);
        await session.RunAsync(new QueryService(SqliteWorkbench.Splitter, bench.Provider.ErrorTranslator), Insert(), ct);

        Should.Throw<InvalidOperationException>(() => session.SetMode(TransactionMode.AutoCommit));

        await session.RollbackAsync(ct);
        session.SetMode(TransactionMode.AutoCommit);
        session.Mode.ShouldBe(TransactionMode.AutoCommit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Historico_MaisRecentePrimeiroELimitaCapacidade()
    {
        var history = new ExecutionHistory(capacity: 2);
        var now = DateTimeOffset.UnixEpoch;

        history.Add(new HistoryEntry(now, "a", StatementStatus.Succeeded, 1, TimeSpan.Zero));
        history.Add(new HistoryEntry(now, "b", StatementStatus.Failed, null, TimeSpan.Zero));
        history.Add(new HistoryEntry(now, "c", StatementStatus.Succeeded, 3, TimeSpan.Zero));

        history.Entries.Select(e => e.Text).ShouldBe(["c", "b"]);
        history.Clear();
        history.Entries.ShouldBeEmpty();
    }

    private static ExecutionRequest Insert() =>
        new("insert into cliente (nome, ativo) values ('Zoe', 1);", SqlDialectFeatures.Sqlite, Policy, Confirmed: true);

    private static async Task<long> Count(System.Data.Common.DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from cliente";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }
}
