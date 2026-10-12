using System.Data.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Sqlite;

// Inserção direta com INSERT preparado e parametrizado dentro da transação informada (docs/10 §3).
// Valores passam por SqliteValues, o mesmo mapeamento dos literais de script.
public sealed class SqliteBulkInserter(DbConnection connection, string dateStorage) : IBulkInserter
{
    private ObjectName? _table;
    private List<ColumnModel> _columns = [];
    private InsertOptions _options = new(false, null);

    public BulkInsertMethod Method => BulkInsertMethod.PreparedStatement;

    public Task PrepareAsync(ObjectName table, IReadOnlyList<ColumnModel> columns, InsertOptions options, CancellationToken ct)
    {
        _table = table;
        _columns = columns.Where(c => c.IsInsertable).ToList();
        _options = options;
        return Task.CompletedTask;
    }

    public async Task<BulkInsertResult> InsertAsync(IReadOnlyList<object?[]> rows, DbTransaction transaction, CancellationToken ct)
    {
        if (_table is null)
        {
            throw new InvalidOperationException("PrepareAsync deve ser chamado antes de InsertAsync.");
        }

        var table = _table;
        var names = string.Join(",", _columns.Select(c => Quote(c.Name)));
        var parameters = string.Join(",", _columns.Select((_, i) => "$p" + i));

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {Quote(table.Name)} ({names}) VALUES ({parameters})";
        for (var i = 0; i < _columns.Count; i++)
        {
            insert.Parameters.Add(insert.CreateParameter());
        }

        await insert.PrepareAsync(ct);

        var captured = _options.CaptureColumns is { Count: > 0 } ? new List<object?[]>() : null;
        long inserted = 0;
        foreach (var row in rows)
        {
            for (var i = 0; i < _columns.Count; i++)
            {
                var p = insert.Parameters[i];
                p.ParameterName = "$p" + i;
                p.Value = SqliteValues.ToStorage(row[i], _columns[i].Type, dateStorage) ?? DBNull.Value;
            }

            inserted += await insert.ExecuteNonQueryAsync(ct);

            if (captured is not null && _options.CaptureColumns is { } captureColumns)
            {
                captured.Add(await ReadCapturedAsync(transaction, table, captureColumns, ct));
            }
        }

        return new BulkInsertResult(inserted, captured);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<object?[]> ReadCapturedAsync(DbTransaction transaction, ObjectName table, IReadOnlyList<string> columns, CancellationToken ct)
    {
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = $"SELECT {string.Join(",", columns.Select(Quote))} FROM {Quote(table.Name)} WHERE rowid = last_insert_rowid()";
        await using var reader = await read.ExecuteReaderAsync(ct);
        var values = new object?[columns.Count];
        if (await reader.ReadAsync(ct))
        {
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
        }

        return values;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
