using MockDataStudio.Application.Explorer;
using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.Core.Naming;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// C5.4 (menu de contexto): scripts gerados a partir do TableModel real, sobre SQLite.
public sealed class ObjectScriptsTests
{
    private static readonly ObjectName Cliente = new("main", "cliente");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTable_GeraDdlComChaveENotNull()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);
        var model = (await new DatabaseNavigator(bench.Provider).DescribeTableAsync(connection, Cliente, TestContext.Current.CancellationToken)).Value!;

        var ddl = ObjectScripts.CreateTable(Cliente, model, bench.Provider.GetDialect("3"));

        // O dialeto SQLite omite o namespace virtual "main" (docs/10 §3).
        ddl.ShouldStartWith("CREATE TABLE \"cliente\" (");
        ddl.ShouldContain("\"nome\"");
        ddl.ShouldContain("NOT NULL");
        ddl.ShouldContain("PRIMARY KEY (\"id\")");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertExample_ExecutaNoSqliteEDesfazNaTransacao()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var bench = await SqliteWorkbench.CreateAsync(ct: ct);
        await using var connection = await bench.OpenAsync(ct);
        var model = (await new DatabaseNavigator(bench.Provider).DescribeTableAsync(connection, Cliente, ct)).Value!;
        var insert = ObjectScripts.InsertExample(Cliente, model, bench.Provider.GetDialect("3"));

        await Execute(connection, "BEGIN", ct);
        try
        {
            await Execute(connection, insert, ct);
            var count = await Scalar(connection, "SELECT count(*) FROM cliente", ct);

            count.ShouldBe(4L);
        }
        finally
        {
            await Execute(connection, "ROLLBACK", ct);
        }

        (await Scalar(connection, "SELECT count(*) FROM cliente", ct)).ShouldBe(3L);
    }

    private static async Task Execute(System.Data.Common.DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> Scalar(System.Data.Common.DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct);
    }
}
