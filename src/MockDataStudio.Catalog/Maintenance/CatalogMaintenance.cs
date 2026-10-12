using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MockDataStudio.Catalog.Database;
using MockDataStudio.Catalog.Repositories;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Catalog.Maintenance;

// Backup, restauração e verificação de integridade do catálogo (09 §8, TASK-04.05).
public sealed class CatalogMaintenance(CatalogDatabase database, string backupFolder) : ICatalogMaintenance
{
    public Task<string> BackupAsync(string? destinationPath, CancellationToken ct)
    {
        var target = destinationPath ?? Path.Combine(backupFolder,
            $"catalog_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.db");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        // VACUUM INTO produz cópia consistente mesmo com WAL. O caminho é literal escapado (não há parâmetro).
        command.CommandText = $"VACUUM INTO '{target.Replace("'", "''", StringComparison.Ordinal)}'";
        command.ExecuteNonQuery();

        return Task.FromResult(target);
    }

    public Task<Result<bool>> RestoreAsync(string backupPath, CancellationToken ct)
    {
        if (!File.Exists(backupPath))
        {
            return Task.FromResult(Result<bool>.Fail(ErrorCodes.CatBackupFailed, "Arquivo de backup não encontrado."));
        }

        using (var check = new SqliteConnection(CatalogDatabase.ConnectionString(backupPath)))
        {
            check.Open();
            using var command = check.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Result<bool>.Fail(ErrorCodes.CatBackupFailed, "O backup está corrompido."));
            }
        }

        // O catálogo atual é preservado como *_replaced_<ts>.db antes de ser substituído.
        if (database.Exists)
        {
            var preserved = Path.Combine(Path.GetDirectoryName(database.FilePath)!,
                $"{Path.GetFileNameWithoutExtension(database.FilePath)}_replaced_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.db");
            File.Move(database.FilePath, preserved);
        }

        File.Copy(backupPath, database.FilePath, overwrite: true);
        return Task.FromResult(Result<bool>.Ok(true));
    }

    public Task<CatalogHealth> CheckIntegrityAsync(CancellationToken ct)
    {
        var messages = new List<string>();
        using var connection = database.Open();

        var integrity = Scalar(connection, "PRAGMA integrity_check");
        var integrityOk = string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase);
        if (!integrityOk)
        {
            messages.Add($"integrity_check: {integrity}");
        }

        using (var fk = connection.CreateCommand())
        {
            fk.CommandText = "PRAGMA foreign_key_check";
            using var reader = fk.ExecuteReader();
            if (reader.Read())
            {
                messages.Add("foreign_key_check encontrou violações.");
                integrityOk = false;
            }
        }

        var mismatched = Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(*) FROM template_alias a JOIN template_field f ON f.id = a.field_id JOIN template t ON t.id = a.template_id WHERE f.template_id <> t.id"),
            CultureInfo.InvariantCulture);
        if (mismatched > 0)
        {
            messages.Add($"{mismatched} alias(es) de campo apontam para outro template.");
            integrityOk = false;
        }

        foreach (var record in InvalidRecords(connection))
        {
            messages.Add(record);
            integrityOk = false;
        }

        DisableBrokenRules(connection, messages);

        var version = MigratorVersion(connection);
        return Task.FromResult(new CatalogHealth(integrityOk, version, messages));
    }

    // Regras cujo target não resolve são desabilitadas, não excluídas (09 §8).
    private static void DisableBrokenRules(SqliteConnection connection, List<string> messages)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, target_kind, target_ref FROM mapping_rule WHERE enabled = 1
            """;
        var broken = new List<long>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var kind = reader.GetString(1);
                var target = reader.GetString(2);
                if (!Resolves(connection, kind, target))
                {
                    broken.Add(id);
                }
            }
        }

        foreach (var id in broken)
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE mapping_rule SET enabled = 0 WHERE id = $id";
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
            messages.Add($"Regra {id} desabilitada: target_ref não resolvível.");
        }
    }

    private static bool Resolves(SqliteConnection connection, string kind, string target)
    {
        if (kind == "generator")
        {
            return target.Contains('.', StringComparison.Ordinal);
        }

        if (kind == "preset")
        {
            return Scalar(connection, "SELECT COUNT(*) FROM generator_preset WHERE code = $c", ("$c", target)) != "0";
        }

        if (kind == "template")
        {
            return Scalar(connection, "SELECT COUNT(*) FROM template WHERE code = $c", ("$c", target)) != "0";
        }

        var dot = target.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && Scalar(connection,
            "SELECT COUNT(*) FROM template_field f JOIN template t ON t.id = f.template_id WHERE t.code = $t AND f.field_code = $f",
            ("$t", target[..dot]), ("$f", target[(dot + 1)..])) != "0";
    }

    // Registros que não respeitam os campos do template (campo obrigatório ausente ou chave desconhecida).
    private static List<string> InvalidRecords(SqliteConnection connection)
    {
        var problems = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.code, r.record_key, r.data FROM template_record r JOIN template t ON t.id = r.template_id
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<(string Code, string Key, string Data)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        reader.Close();

        foreach (var (code, key, data) in rows)
        {
            var fields = FieldsOf(connection, code);
            using var document = JsonDocument.Parse(data);
            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var unknown = names.Where(n => !fields.ContainsKey(n)).ToList();
            var missing = fields.Where(f => f.Value && !names.Contains(f.Key)).Select(f => f.Key).ToList();
            if (unknown.Count > 0 || missing.Count > 0)
            {
                problems.Add($"Registro '{key}' de '{code}' inválido (desconhecidos: {string.Join(',', unknown)}; ausentes: {string.Join(',', missing)}).");
            }
        }

        return problems;
    }

    // Todos os campos do template; valor = obrigatório.
    private static Dictionary<string, bool> FieldsOf(SqliteConnection connection, string code)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT f.field_code, f.required FROM template_field f JOIN template t ON t.id = f.template_id WHERE t.code = $c";
        command.Parameters.AddWithValue("$c", code);
        var fields = new Dictionary<string, bool>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            fields[reader.GetString(0)] = reader.GetInt64(1) == 1;
        }

        return fields;
    }

    private static int MigratorVersion(SqliteConnection connection) => CatalogMigrator.CurrentVersion(connection);

    private static string Scalar(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }
}
