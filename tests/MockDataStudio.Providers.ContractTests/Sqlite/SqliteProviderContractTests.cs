using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Providers.Models;
using MockDataStudio.Providers.Sqlite;
using Shouldly;
using Xunit;

namespace MockDataStudio.Providers.ContractTests.Sqlite;

// Herda a suíte abstrata de contrato para SQLite (TASK-02.05) e acrescenta os casos específicos do SQLite.
public sealed class SqliteProviderContractTests : ProviderContractTests
{
    protected override IDatabaseProvider Provider { get; } = new SqliteProvider();

    protected override async Task<ConnectionSettings> ProvisionAsync(string schemaSql, string databaseName)
    {
        var folder = Path.Combine(Path.GetTempPath(), "mds-prv-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        TrackFolder(folder);

        var file = Path.Combine(folder, databaseName);
        await using var connection = new SqliteConnection("Data Source=" + file);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = schemaSql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return new ConnectionSettings(Host: null, Port: null, Database: file, User: null);
    }

    // T-PRV-SQLITE-011: introspecção parcial depende de permissões de catálogo, que o SQLite não tem.
    [Fact(Skip = "N/A no SQLite: não há permissões de catálogo; PRV-INTROSPECT-PARTIAL não ocorre (docs/26 §4, nº 011).")]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-SQLITE-011")]
    public Task Contrato011_IntrospeccaoParcialSemPermissao() => Task.CompletedTask;

    // T-PRV-NNN-009 (variante agregada): Cancel() do Microsoft.Data.Sqlite não interrompe count(*) sobre CTE infinita,
    // e a chamada não retorna (PV-019). Não executado para não travar a suíte; o cancelamento entre linhas é o caso verificado.
    [Fact(Skip = "PV-019: Cancel() não interrompe consulta de uma única etapa no Microsoft.Data.Sqlite; mitigação por CommandTimeout.")]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-SQLITE-009-AGREGADO")]
    public Task Contrato009_CancelamentoDeConsultaAgregadaNaoEhInterrompidoNoSqlite() => Task.CompletedTask;

    // T-PRV-SQLITE-008 (limite de tamanho): SQLite não impõe comprimento de VARCHAR nem em tabelas STRICT.
    [Fact(Skip = "N/A no SQLite: VARCHAR(n) não é imposto pelo engine; a validação de tamanho fica no validador (docs/17).")]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-SQLITE-008-DataTooLong")]
    public Task Contrato008_DataTooLongNaoOcorreNoSqlite() => Task.CompletedTask;

    // Bloqueio de escrita: a segunda transação de escrita falha com SQLITE_BUSY, classificado como Timeout.
    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-SQLITE-008")]
    public async Task Contrato008_BloqueioDeEscritaEhClassificadoComoTimeout()
    {
        var settings = await ProvisionAsync("CREATE TABLE t (id INTEGER PRIMARY KEY);", "busy.db");
        await using var holder = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);
        await using var other = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);

        await using var write = holder.CreateCommand();
        write.CommandText = "BEGIN IMMEDIATE";
        await write.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await using var blocked = other.CreateCommand();
        blocked.CommandText = "BEGIN IMMEDIATE";
        var error = await Assert.ThrowsAnyAsync<Exception>(() => blocked.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Provider.ErrorTranslator.Categorize(error).ShouldBe(DbErrorCategory.Timeout);
        Provider.ErrorTranslator.Translate(error).Code.ShouldBe(MockDataStudio.Core.Common.ErrorCodes.PrvTimeout);
    }

    // AUTOINCREMENT: próxima sequence sem consumir valor = último gerado + 1 (sqlite_sequence).
    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-SQLITE-010")]
    public async Task Contrato010_AutoIncrementInformaProximoValorSemConsumir()
    {
        var settings = await ProvisionAsync("CREATE TABLE pedido (id INTEGER PRIMARY KEY AUTOINCREMENT, nota TEXT);", "auto.db");
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO pedido (nota) VALUES ('x')";
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var inspector = Provider.CreateTargetInspector(connection, "3");
        (await inspector.GetSequenceNextValueAsync("pedido", TestContext.Current.CancellationToken)).ShouldBe(6L);
        (await inspector.CountRowsAsync(new ObjectName("main", "pedido"), TestContext.Current.CancellationToken)).ShouldBe(5L);
    }
}
