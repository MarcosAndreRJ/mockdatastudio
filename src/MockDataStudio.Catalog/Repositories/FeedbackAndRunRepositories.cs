using System.Globalization;
using Microsoft.Data.Sqlite;
using MockDataStudio.Catalog.Database;

namespace MockDataStudio.Catalog.Repositories;

// Feedback de mapeamento e execuções de geração (TASK-04.02).
public sealed class MappingFeedbackRepository(CatalogDatabase database) : IMappingFeedbackRepository
{
    public Task RecordAsync(string columnCompact, RuleTargetKind targetKind, string targetRef, bool accepted, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mapping_feedback (column_compact, target_kind, target_ref, accepted_count, rejected_count, last_at)
            VALUES ($col, $kind, $ref, $acc, $rej, $at)
            ON CONFLICT(column_compact, target_kind, target_ref) DO UPDATE SET
                accepted_count = accepted_count + excluded.accepted_count,
                rejected_count = rejected_count + excluded.rejected_count,
                last_at = excluded.last_at
            """;
        command.Parameters.AddWithValue("$col", columnCompact);
        command.Parameters.AddWithValue("$kind", ToDb(targetKind));
        command.Parameters.AddWithValue("$ref", targetRef);
        command.Parameters.AddWithValue("$acc", accepted ? 1 : 0);
        command.Parameters.AddWithValue("$rej", accepted ? 0 : 1);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MappingFeedbackInfo>> ListAsync(string columnCompact, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_compact, target_kind, target_ref, accepted_count, rejected_count, last_at FROM mapping_feedback WHERE column_compact = $col ORDER BY target_kind, target_ref";
        command.Parameters.AddWithValue("$col", columnCompact);
        var list = new List<MappingFeedbackInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MappingFeedbackInfo(reader.GetString(0), FromDb(reader.GetString(1)), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt32(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.None)));
        }

        return Task.FromResult<IReadOnlyList<MappingFeedbackInfo>>(list);
    }

    private static string ToDb(RuleTargetKind kind) => kind switch
    {
        RuleTargetKind.TemplateField => "template_field",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static RuleTargetKind FromDb(string text) => text switch
    {
        "template_field" => RuleTargetKind.TemplateField,
        _ => Enum.Parse<RuleTargetKind>(text, ignoreCase: true),
    };
}

public sealed class GenerationRunRepository(CatalogDatabase database) : IGenerationRunRepository
{
    public Task StartAsync(GenerationRunInfo run, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO generation_run (id, project_id, project_path, profile_name, started_at, status, seed, plan_hash, structure_hash,
                output_kind, target_profile_id, target_fingerprint, rows_total, report, cleanup_path)
            VALUES ($id, $proj, $path, $profile, $started, $status, $seed, $plan, $structure, $output, $target, $fp, $rows, $report, $cleanup)
            """;
        command.Parameters.AddWithValue("$id", run.RunId);
        command.Parameters.AddWithValue("$proj", run.ProjectId);
        command.Parameters.AddWithValue("$path", (object?)run.ProjectPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$profile", run.ProfileName);
        command.Parameters.AddWithValue("$started", run.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$status", run.Status);
        command.Parameters.AddWithValue("$seed", run.Seed);
        command.Parameters.AddWithValue("$plan", run.PlanHash);
        command.Parameters.AddWithValue("$structure", run.StructureHash);
        command.Parameters.AddWithValue("$output", run.OutputKind);
        command.Parameters.AddWithValue("$target", (object?)run.TargetProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$fp", (object?)run.TargetFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$rows", run.RowsTotal);
        command.Parameters.AddWithValue("$report", (object?)run.ReportJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$cleanup", (object?)run.CleanupPath ?? DBNull.Value);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task FinishAsync(string runId, string status, DateTimeOffset finishedAt, long rowsTotal, string? reportJson, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE generation_run SET status = $status, finished_at = $fin, rows_total = $rows, report = $report WHERE id = $id";
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$fin", finishedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows", rowsTotal);
        command.Parameters.AddWithValue("$report", (object?)reportJson ?? DBNull.Value);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GenerationRunInfo>> ListAsync(string projectId, int limit, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, project_id, project_path, profile_name, started_at, finished_at, status, seed, plan_hash, structure_hash,
                output_kind, target_profile_id, target_fingerprint, rows_total, report, cleanup_path
            FROM generation_run WHERE project_id = $proj ORDER BY started_at DESC LIMIT $n
            """;
        command.Parameters.AddWithValue("$proj", projectId);
        command.Parameters.AddWithValue("$n", limit);
        var list = new List<GenerationRunInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new GenerationRunInfo(
                reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.None),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.None),
                reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetInt64(13), reader.IsDBNull(14) ? null : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15)));
        }

        return Task.FromResult<IReadOnlyList<GenerationRunInfo>>(list);
    }
}
