using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Database;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Catalog.Packs;

// Instala o pacote embutido CORE_PTBR (TASK-04.06). Não há caminho especial: o pacote vira um script de
// staging e passa pela mesma análise e merge de qualquer importação (dogfooding, 09 §6).
public sealed class BuiltInPackInstaller(CatalogDatabase database, IStagingImporter importer)
{
    public const string PackCode = "CORE_PTBR";
    private const string ResourceName = "MockDataStudio.Catalog.Seeds.pack-core-ptbr.json";

    public async Task<Result<PackInstallReport>> InstallAsync(CancellationToken ct)
    {
        var json = ReadResource();
        var script = BuildScript(json);

        var analysis = await importer.AnalyzeAsync(
            new StagingImportRequest(script, MergeMode.Upsert, PackCode), null, ct);
        if (!analysis.IsSuccess)
        {
            return Result<PackInstallReport>.Fail(analysis.Error);
        }

        if (!analysis.Value.CanMerge)
        {
            var first = analysis.Value.Issues.First(i => i.Severity == IssueSeverity.Error);
            return Result<PackInstallReport>.Fail(first.Code, first.Message);
        }

        var merged = await importer.MergeAsync(analysis.Value.StagingSessionId, ct);
        if (!merged.IsSuccess)
        {
            return Result<PackInstallReport>.Fail(merged.Error);
        }

        MarkBuiltIn(json);
        return Result<PackInstallReport>.Ok(new PackInstallReport(analysis.Value.TemplatesNew, analysis.Value.TemplatesChanged,
            analysis.Value.AliasesNew, analysis.Value.ValuesNew, analysis.Value.RecordsNew, merged.Value.BackupPath));
    }

    // Converte o pacote em SQL de staging (literais escapados). Somente INSERT, conforme 09 §4.
    public static string BuildScript(string packJson)
    {
        using var document = JsonDocument.Parse(packJson);
        var sb = new StringBuilder();

        foreach (var template in document.RootElement.GetProperty("templates").EnumerateArray())
        {
            var code = template.GetProperty("code").GetString()!;
            var kind = template.GetProperty("kind").GetString()!;
            var logical = template.TryGetProperty("logicalType", out var lt) ? lt.GetString() : null;
            var name = template.TryGetProperty("name", out var nm) ? nm.GetString()! : code;
            var description = template.TryGetProperty("description", out var ds) ? ds.GetString() : null;
            var tags = template.TryGetProperty("tags", out var tg) ? tg.GetRawText() : "[]";

            sb.Append("INSERT INTO template (code, name, kind, logical_type, description, tags) VALUES (")
              .Append(Lit(code)).Append(", ").Append(Lit(name)).Append(", ").Append(Lit(kind)).Append(", ")
              .Append(logical is null ? "NULL" : Lit(logical)).Append(", ")
              .Append(description is null ? "NULL" : Lit(description)).Append(", ").Append(Lit(tags)).Append(");\n");

            if (template.TryGetProperty("fields", out var fields))
            {
                foreach (var field in fields.EnumerateArray())
                {
                    var req = !field.TryGetProperty("required", out var rq) || rq.GetBoolean() ? 1 : 0;
                    sb.Append("INSERT INTO template_field (template_code, field_code, logical_type, ordinal, max_length, required) VALUES (")
                      .Append(Lit(code)).Append(", ").Append(Lit(field.GetProperty("code").GetString()!)).Append(", ")
                      .Append(Lit(field.GetProperty("logicalType").GetString()!)).Append(", ")
                      .Append(field.GetProperty("ordinal").GetInt32().ToString(CultureInfo.InvariantCulture)).Append(", ")
                      .Append(field.TryGetProperty("maxLength", out var ml) ? ml.GetInt32().ToString(CultureInfo.InvariantCulture) : "NULL").Append(", ")
                      .Append(req.ToString(CultureInfo.InvariantCulture)).Append(");\n");
                }
            }

            if (template.TryGetProperty("aliases", out var aliases))
            {
                foreach (var alias in aliases.EnumerateArray())
                {
                    var (text, field) = alias.ValueKind == JsonValueKind.String
                        ? (alias.GetString()!, (string?)null)
                        : (alias.GetProperty("alias").GetString()!, alias.TryGetProperty("field", out var f) ? f.GetString() : null);

                    sb.Append("INSERT INTO template_alias (template_code, field_code, alias) VALUES (")
                      .Append(Lit(code)).Append(", ").Append(field is null ? "NULL" : Lit(field)).Append(", ").Append(Lit(text)).Append(");\n");
                }
            }

            if (template.TryGetProperty("values", out var values))
            {
                foreach (var value in values.EnumerateArray())
                {
                    var weight = value.TryGetProperty("weight", out var w) ? w.GetDouble() : 1.0;
                    sb.Append("INSERT INTO template_value (template_code, value, weight) VALUES (")
                      .Append(Lit(code)).Append(", ").Append(Lit(value.GetProperty("value").GetString()!)).Append(", ")
                      .Append(weight.ToString(CultureInfo.InvariantCulture)).Append(");\n");
                }
            }

            if (template.TryGetProperty("records", out var records))
            {
                foreach (var record in records.EnumerateArray())
                {
                    var weight = record.TryGetProperty("weight", out var rw) ? rw.GetDouble() : 1.0;
                    sb.Append("INSERT INTO template_record (template_code, record_key, data, weight) VALUES (")
                      .Append(Lit(code)).Append(", ").Append(Lit(record.GetProperty("key").GetString()!)).Append(", ")
                      .Append(Lit(record.GetProperty("data").GetRawText())).Append(", ")
                      .Append(weight.ToString(CultureInfo.InvariantCulture)).Append(");\n");
                }
            }
        }

        return sb.ToString();
    }

    // Marca os templates do pacote como embutidos (somente o instalador faz isso; o trigger protege depois).
    private void MarkBuiltIn(string packJson)
    {
        using var document = JsonDocument.Parse(packJson);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        using (var pack = connection.CreateCommand())
        {
            pack.Transaction = transaction;
            pack.CommandText = """
                INSERT INTO template_pack (code, name, version, author, license, is_builtin, created_at, updated_at)
                VALUES ($code, $name, $version, $author, $license, 1, $now, $now)
                ON CONFLICT(code) DO UPDATE SET is_builtin = 1, name = excluded.name, version = excluded.version,
                    author = excluded.author, license = excluded.license, updated_at = excluded.updated_at
                """;
            pack.Parameters.AddWithValue("$code", PackCode);
            pack.Parameters.AddWithValue("$name", document.RootElement.GetProperty("name").GetString()!);
            pack.Parameters.AddWithValue("$version", document.RootElement.GetProperty("version").GetString()!);
            pack.Parameters.AddWithValue("$author", document.RootElement.GetProperty("author").GetString()!);
            pack.Parameters.AddWithValue("$license", document.RootElement.GetProperty("license").GetString()!);
            pack.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            pack.ExecuteNonQuery();
        }

        foreach (var template in document.RootElement.GetProperty("templates").EnumerateArray())
        {
            using var mark = connection.CreateCommand();
            mark.Transaction = transaction;
            mark.CommandText = """
                UPDATE template SET is_builtin = 1, pack_id = (SELECT id FROM template_pack WHERE code = $pack)
                WHERE code = $code
                """;
            mark.Parameters.AddWithValue("$pack", PackCode);
            mark.Parameters.AddWithValue("$code", template.GetProperty("code").GetString()!);
            mark.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static string ReadResource()
    {
        using var stream = typeof(BuiltInPackInstaller).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Pacote CORE_PTBR não embutido.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Lit(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

public sealed record PackInstallReport(int TemplatesNew, int TemplatesChanged, int AliasesNew, int ValuesNew, int RecordsNew, string BackupPath);
