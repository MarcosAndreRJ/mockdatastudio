using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Sqlite;

// Inspeção do destino antes da inserção direta (docs/10 §3): máximos, sequências, triggers e chaves existentes.
public sealed class SqliteTargetInspector(DbConnection connection) : ITargetInspector
{
    public async Task<long?> GetMaxIntegerAsync(ObjectName table, string column, CancellationToken ct)
    {
        var value = await ScalarAsync($"SELECT MAX({Quote(column)}) FROM {Quote(table.Name)}", ct);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    // AUTOINCREMENT grava o último valor em sqlite_sequence; o próximo é seq+1. Sem tabela/linha, não há sequence.
    public async Task<long?> GetSequenceNextValueAsync(string sequenceName, CancellationToken ct)
    {
        var exists = await ScalarAsync("SELECT COUNT(*) FROM sqlite_schema WHERE name = 'sqlite_sequence'", ct);
        if (Convert.ToInt64(exists, CultureInfo.InvariantCulture) == 0)
        {
            return null;
        }

        var value = await ScalarAsync("SELECT seq FROM sqlite_sequence WHERE name = $n", ct, ("$n", sequenceName));
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture) + 1;
    }

    public async Task<long> CountRowsAsync(ObjectName table, CancellationToken ct) =>
        Convert.ToInt64(await ScalarAsync($"SELECT COUNT(*) FROM {Quote(table.Name)}", ct), CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<TriggerModel>> GetActiveInsertTriggersAsync(ObjectName table, CancellationToken ct)
    {
        var triggers = new List<TriggerModel>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, sql FROM sqlite_schema WHERE type = 'trigger' AND tbl_name = $t";
        command.AddParameter("$t", table.Name);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var parsed = SqliteDdl.ParseTrigger(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1), table.Namespace);
            if (parsed is not null && (parsed.Events & TriggerEvents.Insert) != 0)
            {
                triggers.Add(parsed);
            }
        }

        return triggers;
    }

    public async Task<bool> AllKeysExistAsync(ObjectName table, IReadOnlyList<string> columns, IReadOnlyList<object?[]> keys, CancellationToken ct)
    {
        foreach (var key in keys)
        {
            var predicate = string.Join(" AND ", columns.Select((c, i) => key[i] is null ? $"{Quote(c)} IS NULL" : $"{Quote(c)} = $k{i}"));
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT 1 FROM {Quote(table.Name)} WHERE {predicate} LIMIT 1";
            for (var i = 0; i < columns.Count; i++)
            {
                if (key[i] is not null)
                {
                    command.AddParameter($"$k{i}", key[i]!);
                }
            }

            if (await command.ExecuteScalarAsync(ct) is null)
            {
                return false;
            }
        }

        return true;
    }

    public async IAsyncEnumerable<object?[]> ReadKeysAsync(ObjectName table, IReadOnlyList<string> columns, long limit, [EnumeratorCancellation] CancellationToken ct)
    {
        var list = string.Join(", ", columns.Select(Quote));
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {list} FROM {Quote(table.Name)} ORDER BY {list} LIMIT $n";
        command.AddParameter("$n", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            yield return row;
        }
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private async Task<object?> ScalarAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.AddParameter(name, value ?? DBNull.Value);
        }

        return await command.ExecuteScalarAsync(ct);
    }
}
