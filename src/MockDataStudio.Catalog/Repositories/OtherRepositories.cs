using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MockDataStudio.Catalog.Database;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Catalog.Repositories;

// Regras de mapeamento, presets, perfis de conexão sem segredo e histórico (TASK-04.02).
public sealed class MappingRuleRepository(CatalogDatabase database) : IMappingRuleRepository
{
    public Task<IReadOnlyList<MappingRuleInfo>> ListAsync(CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, column_pattern, table_pattern, engine, target_kind, target_ref, parameters, scope, priority, source, enabled
            FROM mapping_rule ORDER BY priority DESC, id
            """;
        var list = new List<MappingRuleInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MappingRuleInfo(
                reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : Enum.Parse<DatabaseEngine>(reader.GetString(3), ignoreCase: true),
                ParseTarget(reader.GetString(4)), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                Enum.Parse<RuleScope>(reader.GetString(7), ignoreCase: true), reader.GetInt32(8),
                ParseSource(reader.GetString(9)), reader.GetInt64(10) == 1));
        }

        return Task.FromResult<IReadOnlyList<MappingRuleInfo>>(list);
    }

    public Task<Result<long>> UpsertAsync(MappingRuleInfo rule, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mapping_rule (column_pattern, table_pattern, engine, target_kind, target_ref, parameters, scope, priority, source, enabled, created_at, updated_at)
            VALUES ($col, $tbl, $eng, $kind, $ref, $params, $scope, $prio, $src, $en, $now, $now)
            ON CONFLICT DO UPDATE SET parameters = excluded.parameters, priority = excluded.priority, enabled = excluded.enabled, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$col", rule.ColumnPattern);
        command.Parameters.AddWithValue("$tbl", (object?)rule.TablePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("$eng", rule.Engine is { } e ? (object)e.ToString().ToLowerInvariant() : DBNull.Value);
        command.Parameters.AddWithValue("$kind", ToDb(rule.TargetKind));
        command.Parameters.AddWithValue("$ref", rule.TargetRef);
        command.Parameters.AddWithValue("$params", (object?)rule.ParametersJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$scope", rule.Scope.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$prio", rule.Priority);
        command.Parameters.AddWithValue("$src", rule.Source.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$en", rule.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();

        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT id FROM mapping_rule WHERE column_pattern = $col AND ifnull(table_pattern,'') = ifnull($tbl,'') AND ifnull(engine,'') = ifnull($eng,'') AND target_kind = $kind AND target_ref = $ref";
        idCommand.Parameters.AddWithValue("$col", rule.ColumnPattern);
        idCommand.Parameters.AddWithValue("$tbl", (object?)rule.TablePattern ?? DBNull.Value);
        idCommand.Parameters.AddWithValue("$eng", rule.Engine is { } e2 ? (object)e2.ToString().ToLowerInvariant() : DBNull.Value);
        idCommand.Parameters.AddWithValue("$kind", ToDb(rule.TargetKind));
        idCommand.Parameters.AddWithValue("$ref", rule.TargetRef);
        return Task.FromResult(Result<long>.Ok(Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture)));
    }

    public Task DeleteAsync(long id, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mapping_rule WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private static string ToDb(RuleTargetKind kind) => kind switch
    {
        RuleTargetKind.TemplateField => "template_field",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static RuleTargetKind ParseTarget(string text) => text switch
    {
        "template_field" => RuleTargetKind.TemplateField,
        _ => Enum.Parse<RuleTargetKind>(text, ignoreCase: true),
    };

    private static RuleSource ParseSource(string text) => text switch
    {
        "builtin" => RuleSource.BuiltIn,
        _ => Enum.Parse<RuleSource>(text, ignoreCase: true),
    };
}

public sealed class GeneratorPresetRepository(CatalogDatabase database) : IGeneratorPresetRepository
{
    public Task<IReadOnlyList<GeneratorPresetInfo>> ListAsync(CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, code, name, generator_id, generator_version, parameters, description, is_builtin FROM generator_preset ORDER BY code";
        var list = new List<GeneratorPresetInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new GeneratorPresetInfo(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt64(7) == 1));
        }

        return Task.FromResult<IReadOnlyList<GeneratorPresetInfo>>(list);
    }

    public Task<Result<long>> UpsertAsync(GeneratorPresetInfo preset, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO generator_preset (code, name, generator_id, generator_version, parameters, description, is_builtin, created_at, updated_at)
            VALUES ($code, $name, $gid, $gver, $params, $desc, $builtin, $now, $now)
            ON CONFLICT(code) DO UPDATE SET name = excluded.name, generator_id = excluded.generator_id,
                generator_version = excluded.generator_version, parameters = excluded.parameters,
                description = excluded.description, updated_at = excluded.updated_at
            WHERE generator_preset.is_builtin = 0
            """;
        command.Parameters.AddWithValue("$code", preset.Code);
        command.Parameters.AddWithValue("$name", preset.Name);
        command.Parameters.AddWithValue("$gid", preset.GeneratorId);
        command.Parameters.AddWithValue("$gver", preset.GeneratorVersion);
        command.Parameters.AddWithValue("$params", preset.ParametersJson);
        command.Parameters.AddWithValue("$desc", (object?)preset.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$builtin", preset.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();

        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT id FROM generator_preset WHERE code = $code";
        idCommand.Parameters.AddWithValue("$code", preset.Code);
        return Task.FromResult(Result<long>.Ok(Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture)));
    }

    public Task<Result<bool>> DeleteAsync(string code, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM generator_preset WHERE code = $code AND is_builtin = 0";
        command.Parameters.AddWithValue("$code", code);
        return Task.FromResult(Result<bool>.Ok(command.ExecuteNonQuery() > 0));
    }
}

public sealed class ConnectionProfileRepository(CatalogDatabase database) : IConnectionProfileRepository
{
    private static readonly string[] SecretKeys = ["password", "senha", "pwd", "secret", "token", "apikey"];

    public Task<IReadOnlyList<ConnectionProfileInfo>> ListAsync(CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, engine, settings, environment, is_readonly, notes FROM connection_profile ORDER BY name";
        var list = new List<ConnectionProfileInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ConnectionProfileInfo(reader.GetString(0), reader.GetString(1),
                Enum.Parse<DatabaseEngine>(reader.GetString(2), ignoreCase: true), reader.GetString(3),
                reader.GetString(4), reader.GetInt64(5) == 1, reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return Task.FromResult<IReadOnlyList<ConnectionProfileInfo>>(list);
    }

    public Task<Result<bool>> SaveAsync(ConnectionProfileInfo profile, CancellationToken ct)
    {
        if (ContainsSecret(profile.SettingsJson))
        {
            return Task.FromResult(Result<bool>.Fail(ErrorCodes.CatProfileSecret, "Configurações do perfil contêm campo de senha."));
        }

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connection_profile (id, name, engine, settings, environment, is_readonly, secret, prompt_password, notes, created_at, updated_at)
            VALUES ($id, $name, $engine, $settings, $env, $ro, NULL, 0, $notes, $now, $now)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, engine = excluded.engine, settings = excluded.settings,
                environment = excluded.environment, is_readonly = excluded.is_readonly, notes = excluded.notes, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$engine", profile.Engine.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$settings", profile.SettingsJson);
        command.Parameters.AddWithValue("$env", profile.Environment);
        command.Parameters.AddWithValue("$ro", profile.IsReadOnly ? 1 : 0);
        command.Parameters.AddWithValue("$notes", (object?)profile.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        return Task.FromResult(Result<bool>.Ok(true));
    }

    private static bool ContainsSecret(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .Any(p => SecretKeys.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class QueryHistoryRepository(CatalogDatabase database) : IQueryHistoryRepository
{
    private const int MaxSqlBytes = 64 * 1024;

    public Task AddAsync(HistoryEntry entry, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO query_history (profile_id, executed_at, sql_text, statement_kind, duration_ms, rows_affected, success, error_code)
            VALUES ($p, $at, $sql, $kind, $ms, $rows, $ok, $err)
            """;
        command.Parameters.AddWithValue("$p", (object?)entry.ProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", entry.ExecutedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sql", Truncate(entry.SqlText));
        command.Parameters.AddWithValue("$kind", entry.StatementKind);
        command.Parameters.AddWithValue("$ms", entry.DurationMs);
        command.Parameters.AddWithValue("$rows", (object?)entry.RowsAffected ?? DBNull.Value);
        command.Parameters.AddWithValue("$ok", entry.Success ? 1 : 0);
        command.Parameters.AddWithValue("$err", (object?)entry.ErrorCode ?? DBNull.Value);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryEntry>> ListAsync(int limit, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT profile_id, executed_at, sql_text, statement_kind, duration_ms, rows_affected, success, error_code FROM query_history ORDER BY executed_at DESC LIMIT $n";
        command.Parameters.AddWithValue("$n", limit);
        var list = new List<HistoryEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new HistoryEntry(reader.IsDBNull(0) ? null : reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.None),
                reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5), reader.GetInt64(6) == 1,
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return Task.FromResult<IReadOnlyList<HistoryEntry>>(list);
    }

    private static string Truncate(string sql)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(sql);
        return bytes.Length <= MaxSqlBytes ? sql : System.Text.Encoding.UTF8.GetString(bytes, 0, MaxSqlBytes);
    }
}
