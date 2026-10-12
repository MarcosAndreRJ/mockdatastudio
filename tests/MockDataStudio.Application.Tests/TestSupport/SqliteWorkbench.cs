using System.Data.Common;
using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Sqlite;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Splitter;
using MockDataStudio.Core.Common;
using MockDataStudio.Application.Explorer;

namespace MockDataStudio.Application.Tests.TestSupport;

// Banco SQLite real em pasta temporária, com o provider de produção (SqliteProvider) para abrir conexões.
internal sealed class SqliteWorkbench : IAsyncDisposable
{
    public const string Schema = """
        CREATE TABLE cliente (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            nome TEXT NOT NULL,
            email TEXT UNIQUE,
            ativo BOOLEAN NOT NULL DEFAULT 1
        );
        CREATE TABLE pedido (
            id INTEGER PRIMARY KEY,
            cliente_id INTEGER NOT NULL REFERENCES cliente(id) ON DELETE CASCADE,
            total NUMERIC(12,2)
        );
        CREATE INDEX ix_pedido_cliente ON pedido(cliente_id);
        CREATE VIEW vw_pedidos AS SELECT cliente_id, count(*) AS n FROM pedido GROUP BY cliente_id;
        CREATE TRIGGER trg_pedido AFTER INSERT ON pedido BEGIN UPDATE cliente SET ativo = 1 WHERE id = NEW.cliente_id; END;
        INSERT INTO cliente (id, nome, email, ativo) VALUES (1, 'Ana', 'ana@exemplo.test', 1), (2, 'Bruno', NULL, 0), (3, 'Carla', 'carla@exemplo.test', 1);
        INSERT INTO pedido (id, cliente_id, total) VALUES (10, 1, 99.90), (11, 1, NULL), (12, 3, 15.00);
        """;

    private SqliteWorkbench(string folder, string file, SqliteProvider provider)
    {
        Folder = folder;
        File = file;
        Provider = provider;
        Settings = new ConnectionSettings(Host: null, Port: null, Database: file, User: null);
    }

    public string Folder { get; }

    public string File { get; }

    public SqliteProvider Provider { get; }

    public ConnectionSettings Settings { get; }

    public static ISqlScriptSplitter Splitter { get; } = new SqlScriptSplitter();

    public static SqlDialectFeatures Dialect => SqlDialectFeatures.Sqlite;

    public static async Task<SqliteWorkbench> CreateAsync(string schema = Schema, CancellationToken ct = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "mds-explorer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "loja.db");
        await using (var connection = new SqliteConnection("Data Source=" + file))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = schema;
            await command.ExecuteNonQueryAsync(ct);
        }

        return new SqliteWorkbench(folder, file, new SqliteProvider());
    }

    public Task<DbConnection> OpenAsync(CancellationToken ct) => Provider.OpenConnectionAsync(Settings, null, ct);

    public static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Arquivo ainda aberto pelo driver: a pasta temporária é limpa pelo sistema.
        }

        await ValueTask.CompletedTask;
    }
}
