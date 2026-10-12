using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Catalog.Database;

// Uma migração de schema (09 §7, M-01). O checksum é o SHA-256 do texto normalizado (LF).
public sealed record Migration(int Version, string Name, string Sql)
{
    public string Checksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql.Replace("\r\n", "\n", StringComparison.Ordinal))))
        .ToLowerInvariant();

    // Lê as migrações embutidas no assembly (Migrations/VNNNN__descricao.sql), em ordem de versão.
    public static IReadOnlyList<Migration> LoadEmbedded()
    {
        var assembly = typeof(Migration).Assembly;
        var result = new List<Migration>();

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.Contains(".Migrations.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var fileName = resource[(resource.IndexOf(".Migrations.", StringComparison.Ordinal) + ".Migrations.".Length)..];
            var (version, name) = ParseFileName(fileName);
            result.Add(new Migration(version, name, reader.ReadToEnd()));
        }

        return result.OrderBy(m => m.Version).ToList();
    }

    private static (int Version, string Name) ParseFileName(string fileName)
    {
        // "V0001__init.sql" -> (1, "init")
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var separator = withoutExtension.IndexOf("__", StringComparison.Ordinal);
        if (!withoutExtension.StartsWith('V') || separator < 2)
        {
            throw new FormatException($"Nome de migração inválido: {fileName}");
        }

        return (int.Parse(withoutExtension[1..separator], System.Globalization.CultureInfo.InvariantCulture),
            withoutExtension[(separator + 2)..]);
    }
}

// Aplica migrações pendentes, cada uma em transação própria (M-02). Falha → rollback e catálogo na versão anterior.
public sealed class CatalogMigrator(CatalogDatabase database, IReadOnlyList<Migration> migrations, Func<string>? premigrateBackup = null)
{
    public Result<int> Migrate()
    {
        var target = migrations.Count == 0 ? 0 : migrations.Max(m => m.Version);

        using var connection = database.Open();
        var current = CurrentVersion(connection);

        if (current > target)
        {
            return Result<int>.Fail(ErrorCodes.CatNewerSchema,
                $"O catálogo está na versão {current}, mais nova que a suportada ({target}). Abra somente leitura.");
        }

        var applied = AppliedChecksums(connection);
        foreach (var (version, checksum) in applied)
        {
            var migration = migrations.FirstOrDefault(m => m.Version == version);
            if (migration is not null && migration.Checksum != checksum)
            {
                return Result<int>.Fail(ErrorCodes.CatMigChecksum,
                    $"A migração V{version:D4} foi alterada após ser aplicada (checksum divergente).");
            }
        }

        var pending = migrations.Where(m => m.Version > current).OrderBy(m => m.Version).ToList();
        if (pending.Count == 0)
        {
            return Result<int>.Ok(current);
        }

        if (current > 0 && premigrateBackup is not null)
        {
            premigrateBackup();
        }

        foreach (var migration in pending)
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = migration.Sql;
                    command.ExecuteNonQuery();
                }

                using (var record = connection.CreateCommand())
                {
                    record.Transaction = transaction;
                    record.CommandText = """
                        INSERT INTO schema_version (version, script_name, checksum, applied_at)
                        VALUES ($version, $name, $checksum, $at)
                        """;
                    record.Parameters.AddWithValue("$version", migration.Version);
                    record.Parameters.AddWithValue("$name", migration.Name);
                    record.Parameters.AddWithValue("$checksum", migration.Checksum);
                    record.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                    record.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (SqliteException ex)
            {
                transaction.Rollback();
                return Result<int>.Fail(ErrorCodes.CatMigFailed,
                    $"Falha na migração V{migration.Version:D4}: {ex.Message}");
            }

            current = migration.Version;
        }

        return Result<int>.Ok(current);
    }

    public static int CurrentVersion(SqliteConnection connection)
    {
        if (!TableExists(connection, "schema_version"))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static List<(int Version, string Checksum)> AppliedChecksums(SqliteConnection connection)
    {
        var list = new List<(int, string)>();
        if (!TableExists(connection, "schema_version"))
        {
            return list;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, checksum FROM schema_version";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return list;
    }
}
