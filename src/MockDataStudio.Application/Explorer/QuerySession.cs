using System.Data.Common;

namespace MockDataStudio.Application.Explorer;

public enum TransactionMode
{
    // Cada comando confirma sozinho (comportamento padrão do SGBD).
    AutoCommit,

    // A sessão abre uma transação e só confirma com Commit (ou desfaz com Rollback).
    Manual,
}

// Sessão de consulta do editor (C6r.1): conexão dedicada, separada da conexão de navegação do Explorer,
// e controle de transação. Início, COMMIT e ROLLBACK são enviados como SQL simples (BEGIN, COMMIT, ROLLBACK),
// o que vale para SQLite, PostgreSQL e MySQL. O Firebird usa outra sintaxe e não é coberto por este serviço.
public sealed class QuerySession : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<DbConnection>>? _openConnection;
    private readonly bool _ownsConnection;
    private DbConnection? _connection;
    private bool _transactionOpen;

    // Sessão que abre e descarta a própria conexão.
    public QuerySession(Func<CancellationToken, Task<DbConnection>> openConnection)
    {
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        _ownsConnection = true;
    }

    // Sessão sobre uma conexão emprestada (ex.: a da aba do editor): não a fecha ao descartar, só desfaz transação aberta.
    public QuerySession(DbConnection borrowed)
    {
        _connection = borrowed ?? throw new ArgumentNullException(nameof(borrowed));
        _ownsConnection = false;
    }

    public TransactionMode Mode { get; private set; } = TransactionMode.AutoCommit;

    public bool HasOpenTransaction => _transactionOpen;

    // Troca de modo não pode deixar transação aberta para trás: confirme ou desfaça antes.
    public void SetMode(TransactionMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        if (_transactionOpen)
        {
            throw new InvalidOperationException("Há uma transação aberta: confirme (Commit) ou desfaça (Rollback) antes de trocar o modo.");
        }

        Mode = mode;
    }

    public async Task<DbConnection> ConnectionAsync(CancellationToken ct) =>
        _connection ??= await _openConnection!(ct);

    // Executa um script na conexão da sessão. No modo Manual, abre a transação antes do primeiro comando.
    public async Task<QueryRun> RunAsync(QueryService service, ExecutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        var connection = await ConnectionAsync(ct);
        if (Mode == TransactionMode.Manual && !_transactionOpen)
        {
            await ExecuteSqlAsync(connection, "BEGIN", ct);
            _transactionOpen = true;
        }

        return await service.ExecuteAsync(connection, request, ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (!_transactionOpen)
        {
            return;
        }

        await ExecuteSqlAsync(await ConnectionAsync(ct), "COMMIT", ct);
        _transactionOpen = false;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (!_transactionOpen)
        {
            return;
        }

        await ExecuteSqlAsync(await ConnectionAsync(ct), "ROLLBACK", ct);
        _transactionOpen = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            if (_transactionOpen)
            {
                try
                {
                    await RollbackAsync(CancellationToken.None);
                }
                catch (DbException)
                {
                    // Conexão já caída: o SGBD desfaz a transação ao encerrar a sessão.
                }
            }

            if (_ownsConnection)
            {
                await _connection.DisposeAsync();
            }

            _connection = null;
        }
    }

    private static async Task ExecuteSqlAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
