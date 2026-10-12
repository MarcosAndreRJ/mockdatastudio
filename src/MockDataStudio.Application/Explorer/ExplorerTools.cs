using System.Data.Common;
using System.Runtime.CompilerServices;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Output;
using MockDataStudio.Providers;

namespace MockDataStudio.Application.Explorer;

// Filtro/busca da árvore (C5.4, TASK-12.02). Puro: sem UI e sem banco.
public static class ExplorerFilter
{
    public static bool Matches(string candidate, string? query) =>
        string.IsNullOrWhiteSpace(query)
        || candidate.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    // Um objeto aparece se o nome ou o schema contêm o texto buscado.
    public static IReadOnlyList<ObjectName> Filter(IEnumerable<ObjectName> objects, string? query) =>
        objects.Where(o => Matches(o.Name, query) || Matches(o.Namespace, query)).ToList();
}

// Quem referencia uma tabela (C5.4, TASK-12.03 "ver referências"). Lê as FKs de todas as tabelas do banco.
public sealed record IncomingReference(ObjectName Child, string ForeignKeyName, IReadOnlyList<string> Columns, IReadOnlyList<string> ReferencedColumns);

public sealed class ReferenceFinder(IDatabaseProvider provider)
{
    public async Task<IReadOnlyList<IncomingReference>> FindIncomingAsync(DbConnection connection, ObjectName target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var extractor = provider.CreateSchemaExtractor();
        var found = new List<IncomingReference>();
        foreach (var table in (await extractor.ListTablesAsync(connection, ct)).OrderBy(t => t))
        {
            ct.ThrowIfCancellationRequested();
            var described = await extractor.ExtractTableAsync(connection, table, ct);
            if (!described.IsSuccess)
            {
                continue;
            }

            foreach (var fk in described.Value.ForeignKeys.Where(f => f.Referenced == target))
            {
                found.Add(new IncomingReference(table, fk.Name, fk.Columns, fk.ReferencedColumns));
            }
        }

        return found;
    }
}

// Explain (C6, TASK-13): só SELECT/WITH, e nunca com ANALYZE. Assim o comando não executa a consulta.
public sealed record QueryPlan(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);

public static class ExplainService
{
    public static bool IsReadStatement(string sql)
    {
        var first = sql.TrimStart().Split([' ', '\t', '\r', '\n', '('], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return first.Equals("SELECT", StringComparison.OrdinalIgnoreCase) || first.Equals("WITH", StringComparison.OrdinalIgnoreCase);
    }

    // Retorna null quando o SGBD não tem EXPLAIN suportado nesta versão do aplicativo (Firebird).
    public static string? BuildStatement(DatabaseEngine engine, string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var text = sql.Trim().TrimEnd(';').Trim();
        if (text.Contains(';', StringComparison.Ordinal))
        {
            throw new ArgumentException("EXPLAIN aceita um único comando por vez.", nameof(sql));
        }

        if (!IsReadStatement(text))
        {
            throw new ArgumentException("EXPLAIN só é oferecido para SELECT e WITH.", nameof(sql));
        }

        return engine switch
        {
            DatabaseEngine.Sqlite => "EXPLAIN QUERY PLAN " + text,
            DatabaseEngine.PostgreSql or DatabaseEngine.MySql => "EXPLAIN " + text,
            _ => null,
        };
    }

    public static async Task<QueryPlan> ExplainAsync(DbConnection connection, DatabaseEngine engine, string sql, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var statement = BuildStatement(engine, sql)
            ?? throw new NotSupportedException($"EXPLAIN não é suportado para {engine} nesta versão.");

        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return new QueryPlan(columns, rows);
    }
}

// Exportação da grade (C6, TASK-13.04) sobre ResultExporter. Exporta as linhas recebidas, sem reabrir o cursor.
public enum ExportFormat { Csv, Json }

public static class ResultExportService
{
    public static Task<long> ExportAsync(Stream destination, ExportFormat format, IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows, CancellationToken ct, string csvSeparator = ",")
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(rows);
        var source = ToAsync(rows, ct);
        return format == ExportFormat.Csv
            ? ResultExporter.WriteCsvAsync(destination, columns, source, ct, csvSeparator)
            : ResultExporter.WriteJsonAsync(destination, columns, source, ct);
    }

    private static async IAsyncEnumerable<object?[]> ToAsync(IReadOnlyList<object?[]> rows, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return row;
        }
    }
}

// Arquivos de script (C6, Save/Open). UTF-8 sem BOM.
public static class ScriptFileService
{
    public static Task SaveAsync(string path, string text, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.WriteAllTextAsync(path, text ?? string.Empty, new System.Text.UTF8Encoding(false), ct);
    }

    public static Task<string> OpenAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllTextAsync(path, ct);
    }
}

// Barra de estado (C5.4 e C5.3, prompt §11): conexão, ambiente e se a restrição de somente leitura foi aplicada de fato.
public sealed record ConnectionStatusInfo(
    string ConnectionName, string EngineName, string Database, string Environment, bool IsProduction,
    bool ReadOnlyRequested, bool ReadOnlyEnforced, string? Warning, string ReadOnlyLabel);

public static class ConnectionStatus
{
    public static ConnectionStatusInfo Build(string connectionName, DatabaseEngine engine, string database,
        ConnectionEnvironment environment, bool readOnlyRequested, bool readOnlyEnforced)
    {
        var warning = readOnlyRequested && !readOnlyEnforced
            ? "Somente leitura solicitada pelo perfil, mas não aplicada no servidor: a execução não tem essa proteção."
            : null;
        return new ConnectionStatusInfo(
            connectionName,
            engine.ToString(),
            database,
            environment.ToString(),
            IsProduction: environment is ConnectionEnvironment.Production,
            readOnlyRequested,
            readOnlyEnforced,
            warning,
            ReadOnlyLabel: !readOnlyRequested ? "Leitura e escrita"
                : readOnlyEnforced ? "Somente leitura: aplicada no servidor"
                : "Somente leitura: NÃO aplicada no servidor");
    }
}
