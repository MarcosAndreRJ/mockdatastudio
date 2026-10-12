using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MockDataStudio.Catalog.Database;
using MockDataStudio.Catalog.Repositories;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.Core.Templates;
using MockDataStudio.SqlTooling.Classification;
using MockDataStudio.SqlTooling.Splitter;

namespace MockDataStudio.Catalog.Staging;

// Importação de scripts do catálogo via staging isolado (ADR-016, 13 §7).
// A análise executa o script num SQLite temporário com o schema de staging; o merge é uma única transação
// no catálogo, precedida de backup premerge.
public sealed partial class StagingImporter(
    CatalogDatabase database, ISqlScriptSplitter splitter, ICatalogMaintenance maintenance, string workFolder) : IStagingImporter
{
    private const long MaxScriptBytes = 200L * 1024 * 1024;
    private const string UserPackCode = "USER";

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    private sealed record Session(string StagingPath, bool CanMerge, MergeMode Mode, string? TargetPackCode);

    public Task<Result<StagingAnalysis>> AnalyzeAsync(StagingImportRequest request, IProgress<int>? progress, CancellationToken ct)
    {
        if (Encoding.UTF8.GetByteCount(request.SqlScript) > MaxScriptBytes)
        {
            return Task.FromResult(Result<StagingAnalysis>.Fail(ErrorCodes.CatImpSize, "O script excede 200 MB."));
        }

        var statements = splitter.Split(request.SqlScript, SqlDialectFeatures.Sqlite);

        for (var i = 0; i < statements.Count; i++)
        {
            var keyword = FirstKeyword(statements[i].Text);
            if (!AllowedKeywords.Contains(keyword) || statements[i].Kind is not (StatementKind.Insert or StatementKind.Update or StatementKind.Delete))
            {
                return Task.FromResult(Result<StagingAnalysis>.Fail(ErrorCodes.CatStgForbidden,
                    $"Comando '{keyword}' não permitido (comando {i + 1}, linha {statements[i].LineNumber}). Somente INSERT, UPDATE e DELETE."));
            }
        }

        var path = Path.Combine(workFolder, $"mds-staging-{Guid.NewGuid():N}.db");
        var connectionString = CatalogDatabase.ConnectionString(path);

        try
        {
            using (var staging = new SqliteConnection(connectionString))
            {
                staging.Open();
                Exec(staging, StagingSchema());

                using var transaction = staging.BeginTransaction();
                for (var i = 0; i < statements.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        Exec(staging, statements[i].Text, transaction);
                    }
                    catch (SqliteException ex)
                    {
                        transaction.Rollback();
                        return Task.FromResult(Result<StagingAnalysis>.Fail(
                            ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ? ErrorCodes.CatStgForbidden : ErrorCodes.CatStgSqlError,
                            $"Erro no comando {i + 1} (linha {statements[i].LineNumber}): {ex.Message}"));
                    }

                    progress?.Report((i + 1) * 100 / Math.Max(statements.Count, 1));
                }

                transaction.Commit();
            }

            var issues = new List<ImportIssue>();
            var analysis = Analyze(path, request, issues);
            var sessionId = Guid.NewGuid().ToString("N");
            _sessions[sessionId] = new Session(path, analysis.CanMerge, request.Mode, request.TargetPackCode);

            return Task.FromResult(Result<StagingAnalysis>.Ok(analysis with { StagingSessionId = sessionId }));
        }
        catch (SqliteException ex)
        {
            TryDelete(path);
            return Task.FromResult(Result<StagingAnalysis>.Fail(ErrorCodes.CatStgSqlError, ex.Message));
        }
    }

    public async Task<Result<MergeReport>> MergeAsync(string stagingSessionId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(stagingSessionId, out var session))
        {
            return Result<MergeReport>.Fail(ErrorCodes.CatStgSession, "Sessão de importação inexistente ou já finalizada.");
        }

        if (!session.CanMerge)
        {
            return Result<MergeReport>.Fail(ErrorCodes.CatStgSession, "A análise contém erros; corrija o script antes de mesclar.");
        }

        var backupPath = await maintenance.BackupAsync(
            Path.Combine(Path.GetDirectoryName(database.FilePath)!, $"{Path.GetFileNameWithoutExtension(database.FilePath)}_premerge_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.db"), ct);

        try
        {
            using var catalog = database.Open();
            using var staging = new SqliteConnection(CatalogDatabase.ConnectionString(session.StagingPath));
            staging.Open();
            using var transaction = catalog.BeginTransaction();

            var report = MergeInto(catalog, transaction, staging, session.Mode, session.TargetPackCode ?? UserPackCode);
            transaction.Commit();

            _sessions.TryRemove(stagingSessionId, out _);
            TryDelete(session.StagingPath);
            return Result<MergeReport>.Ok(report with { BackupPath = backupPath });
        }
        catch (SqliteException ex)
        {
            return Result<MergeReport>.Fail(ErrorCodes.CatStgSqlError, $"Merge revertido: {ex.Message}");
        }
    }

    public void Discard(string stagingSessionId)
    {
        if (_sessions.TryRemove(stagingSessionId, out var session))
        {
            TryDelete(session.StagingPath);
        }
    }

    // ---------- Análise ----------

    private static readonly HashSet<string> AllowedKeywords = new(StringComparer.Ordinal) { "INSERT", "UPDATE", "DELETE" };

    private StagingAnalysis Analyze(string stagingPath, StagingImportRequest request, List<ImportIssue> issues)
    {
        using var staging = new SqliteConnection(CatalogDatabase.ConnectionString(stagingPath));
        staging.Open();
        using var catalog = database.Open();

        var templates = ReadStagingTemplates(staging);
        var fields = ReadRows(staging, "SELECT template_code, field_code, logical_type, max_length, required FROM template_field",
            r => new StagingField(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.GetInt64(4) == 1));
        var aliases = ReadRows(staging, "SELECT template_code, field_code, alias, weight FROM template_alias",
            r => new StagingAlias(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetDouble(3)));
        var values = ReadRows(staging, "SELECT template_code, value, weight FROM template_value",
            r => new StagingValue(r.GetString(0), r.GetString(1), r.GetDouble(2)));
        var records = ReadRows(staging, "SELECT template_code, record_key, data, weight FROM template_record",
            r => new StagingRecord(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetDouble(3)));
        var rules = ReadRows(staging, "SELECT column_pattern, target_kind, target_ref, priority FROM mapping_rule",
            r => new StagingRule(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        var presets = ReadRows(staging, "SELECT code, generator_id FROM generator_preset",
            r => (Code: r.GetString(0), GeneratorId: r.GetString(1)));

        var catalogTemplates = CatalogTemplates(catalog);
        var catalogFields = CatalogFields(catalog);
        var catalogValues = CatalogSet(catalog, "SELECT t.code, v.value FROM template_value v JOIN template t ON t.id = v.template_id");
        var catalogAliases = CatalogSet3(catalog, "SELECT t.code, ifnull(f.field_code,''), a.alias FROM template_alias a JOIN template t ON t.id = a.template_id LEFT JOIN template_field f ON f.id = a.field_id");
        var catalogRecords = CatalogSet(catalog, "SELECT t.code, r.record_key FROM template_record r JOIN template t ON t.id = r.template_id");
        var catalogPresets = CatalogSet(catalog, "SELECT code, '' FROM generator_preset");

        var stagedCodes = templates.Select(t => t.Code).ToHashSet(StringComparer.Ordinal);
        var duplicates = templates.GroupBy(t => t.Code, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var code in duplicates)
        {
            issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpCode, $"Template '{code}' declarado mais de uma vez no script.", code));
        }

        var templatesNew = 0;
        var templatesChanged = 0;
        foreach (var template in templates.DistinctBy(t => t.Code, StringComparer.Ordinal))
        {
            if (!CodePattern().IsMatch(template.Code))
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpCode, $"Código '{template.Code}' inválido (use ^[A-Z][A-Z0-9_]{{1,63}}$).", template.Code));
            }

            if (template.Kind is not ("scalar" or "composite"))
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpKind, $"kind '{template.Kind}' inválido para '{template.Code}'.", template.Code));
            }
            else if (template.Kind == "scalar" && !LogicalTypes.Contains(template.LogicalType ?? string.Empty))
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpKind, $"Template escalar '{template.Code}' exige logical_type válido.", template.Code));
            }

            if (catalogTemplates.TryGetValue(template.Code, out var existing))
            {
                templatesChanged += existing.Name != template.Name || existing.Description != template.Description ? 1 : 0;

                if (existing.IsBuiltIn && (existing.Kind != template.Kind || existing.LogicalType != NormalizeLogical(template)))
                {
                    issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpBuiltin,
                        $"Template embutido '{template.Code}' não pode ter kind/logical_type alterados.", template.Code));
                }

                if (existing.IsBuiltIn && request.Mode == MergeMode.ReplaceTemplateData)
                {
                    issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpBuiltin,
                        $"ReplaceTemplateData não é permitido para o template embutido '{template.Code}'.", template.Code));
                }
            }
            else
            {
                templatesNew++;
            }
        }

        foreach (var field in fields.Where(f => !stagedCodes.Contains(f.TemplateCode) && !catalogTemplates.ContainsKey(f.TemplateCode)))
        {
            issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpUnknownTemplate, $"Campo '{field.FieldCode}' de template inexistente '{field.TemplateCode}'.", field.TemplateCode));
        }

        var stagedFields = fields.Select(f => (f.TemplateCode, f.FieldCode)).ToHashSet();
        var stagedTemplateKinds = templates.DistinctBy(t => t.Code, StringComparer.Ordinal).ToDictionary(t => t.Code, t => t.Kind, StringComparer.Ordinal);

        string? KindOf(string code) => stagedTemplateKinds.TryGetValue(code, out var kind)
            ? kind
            : catalogTemplates.TryGetValue(code, out var cat) ? cat.Kind : null;

        var aliasesNew = 0;
        foreach (var alias in aliases)
        {
            var kind = KindOf(alias.TemplateCode);
            if (kind is null)
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpUnknownTemplate, $"Alias '{alias.Alias}' referencia template inexistente '{alias.TemplateCode}'.", alias.TemplateCode));
                continue;
            }

            if (alias.FieldCode is { } fieldCode && (kind == "scalar" || !(stagedFields.Contains((alias.TemplateCode, fieldCode))
                || catalogFields.Contains((alias.TemplateCode, fieldCode)))))
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpField, $"Alias '{alias.Alias}' referencia campo inexistente ou template escalar.", alias.TemplateCode));
            }

            if (alias.Weight <= 0 || alias.Weight > 2)
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpWeight, $"Peso do alias '{alias.Alias}' deve estar em (0, 2].", alias.TemplateCode));
            }

            if (!catalogAliases.Contains((alias.TemplateCode, alias.FieldCode ?? string.Empty, alias.Alias)))
            {
                aliasesNew++;
            }
        }

        var valuesNew = 0;
        var valuesDuplicated = 0;
        var seenValues = new HashSet<(string, string)>();
        foreach (var value in values)
        {
            if (KindOf(value.TemplateCode) is null)
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpUnknownTemplate, $"Valor '{value.Value}' referencia template inexistente.", value.TemplateCode));
                continue;
            }

            if (value.Weight <= 0)
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpWeight, $"Peso do valor '{value.Value}' deve ser positivo.", value.TemplateCode));
            }

            if (!seenValues.Add((value.TemplateCode, value.Value)) || catalogValues.Contains((value.TemplateCode, value.Value)))
            {
                valuesDuplicated++;
                issues.Add(Issue(IssueSeverity.Info, ErrorCodes.CatImpDupValue, $"Valor '{value.Value}' duplicado (ignorado).", value.TemplateCode));
            }
            else
            {
                valuesNew++;
            }
        }

        var allFields = fields.Concat(CatalogFieldRows(catalog).Where(c => !stagedCodes.Contains(c.TemplateCode))).ToList();
        var fieldsByTemplate = allFields.GroupBy(f => f.TemplateCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var recordsNew = 0;
        foreach (var record in records)
        {
            if (KindOf(record.TemplateCode) is not { } kind)
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpUnknownTemplate, "Registro referencia template inexistente.", record.TemplateCode));
                continue;
            }

            if (kind == "scalar")
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpKind, "Registro em template escalar.", record.TemplateCode));
                continue;
            }

            ValidateRecord(record, fieldsByTemplate.GetValueOrDefault(record.TemplateCode) ?? [], issues);

            if (!catalogRecords.Contains((record.TemplateCode, record.RecordKey ?? string.Empty)))
            {
                recordsNew++;
            }
        }

        ValidateValueTypes(values, templates, issues);

        foreach (var rule in rules)
        {
            ValidateRule(rule, stagedTemplateKinds, fieldsByTemplate, presets.Select(p => p.Code), catalogTemplates.Keys, catalogFields, catalogPresets, issues);
        }

        var canMerge = issues.All(i => i.Severity != IssueSeverity.Error);
        return new StagingAnalysis(templatesNew, templatesChanged, aliasesNew, valuesNew, valuesDuplicated, recordsNew,
            issues.ToList(), canMerge, string.Empty);
    }

    private static void ValidateRecord(StagingRecord record, List<StagingField> fields, List<ImportIssue> issues)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(record.Data);
        }
        catch (JsonException)
        {
            issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpRecordJson, $"Registro '{record.RecordKey}' não é JSON válido.", record.TemplateCode));
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpRecordJson, $"Registro '{record.RecordKey}' deve ser um objeto JSON.", record.TemplateCode));
                return;
            }

            var names = fields.Select(f => f.FieldCode).ToHashSet(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Contains(property.Name))
                {
                    issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpRecordJson, $"Chave desconhecida '{property.Name}' no registro '{record.RecordKey}'.", record.TemplateCode));
                }
                else if (fields.First(f => f.FieldCode == property.Name) is var field && field.MaxLength is { } max
                    && (property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()!.Length : property.Value.GetRawText().Length) > max)
                {
                    issues.Add(Issue(IssueSeverity.Warning, ErrorCodes.CatImpLength, $"Valor de '{field.FieldCode}' excede {max} caracteres.", record.TemplateCode));
                }
            }

            foreach (var required in fields.Where(f => f.Required && !document.RootElement.TryGetProperty(f.FieldCode, out _)))
            {
                issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpRecordJson,
                    $"Campo obrigatório '{required.FieldCode}' ausente no registro '{record.RecordKey}'.", record.TemplateCode));
            }
        }

        if (record.Weight <= 0)
        {
            issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpWeight, $"Peso do registro '{record.RecordKey}' deve ser positivo.", record.TemplateCode));
        }
    }

    private static void ValidateValueTypes(IEnumerable<StagingValue> values, List<StagingTemplate> templates, List<ImportIssue> issues)
    {
        var scalarTypes = templates.DistinctBy(t => t.Code, StringComparer.Ordinal)
            .Where(t => t.Kind == "scalar" && t.LogicalType is not null)
            .ToDictionary(t => t.Code, t => t.LogicalType!, StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (!scalarTypes.TryGetValue(value.TemplateCode, out var logical) || Convertible(value.Value, logical))
            {
                continue;
            }

            issues.Add(Issue(IssueSeverity.Warning, ErrorCodes.CatImpValueType,
                $"Valor '{value.Value}' não é convertível para {logical}.", value.TemplateCode));
        }

    }

    private static void ValidateRule(StagingRule rule, Dictionary<string, string> templateKinds,
        Dictionary<string, List<StagingField>> fieldsByTemplate, IEnumerable<string> stagedPresets, IEnumerable<string> catalogTemplateCodes,
        HashSet<(string, string)> catalogFields, HashSet<(string, string)> catalogPresets, List<ImportIssue> issues)
    {
        var presets = stagedPresets.ToHashSet(StringComparer.Ordinal);
        var catalogTemplateSet = catalogTemplateCodes.ToHashSet(StringComparer.Ordinal);
        var ok = rule.TargetKind switch
        {
            "template" => templateKinds.ContainsKey(rule.TargetRef) || catalogTemplateSet.Contains(rule.TargetRef),
            "template_field" => ResolveField(rule.TargetRef, templateKinds, fieldsByTemplate, catalogFields),
            "preset" => presets.Contains(rule.TargetRef) || catalogPresets.Contains((rule.TargetRef, string.Empty)),
            "generator" => rule.TargetRef.Contains('.', StringComparison.Ordinal),
            _ => false,
        };

        if (!ok)
        {
            issues.Add(Issue(IssueSeverity.Error, ErrorCodes.CatImpRule,
                $"Regra '{rule.ColumnPattern}' com target_kind '{rule.TargetKind}' ou target_ref '{rule.TargetRef}' não resolvível.", null));
        }
    }

    private static bool ResolveField(string targetRef, Dictionary<string, string> templateKinds,
        Dictionary<string, List<StagingField>> fieldsByTemplate, HashSet<(string, string)> catalogFields)
    {
        var dot = targetRef.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return false;
        }

        var template = targetRef[..dot];
        var field = targetRef[(dot + 1)..];
        return (templateKinds.ContainsKey(template) && fieldsByTemplate.GetValueOrDefault(template)?.Any(f => f.FieldCode == field) == true)
            || catalogFields.Contains((template, field));
    }

    private static bool Convertible(string value, string logical) => logical switch
    {
        "integer" => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
        "decimal" or "float" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        "boolean" => value is "0" or "1" or "true" or "false",
        "date" or "datetime" or "datetimetz" => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "uuid" => Guid.TryParse(value, out _),
        _ => true,
    };

    private static readonly HashSet<string> LogicalTypes = new(StringComparer.Ordinal)
    {
        "boolean", "integer", "decimal", "float", "string", "text", "binary", "date", "time", "datetime", "datetimetz",
        "uuid", "json", "enum", "interval", "other",
    };

    private static string? NormalizeLogical(StagingTemplate template) =>
        template.Kind == "scalar" ? template.LogicalType?.ToLowerInvariant() : null;

    // ---------- Merge ----------

    private static MergeReport MergeInto(SqliteConnection catalog, SqliteTransaction tx, SqliteConnection staging, MergeMode mode, string packCode)
    {
        var inserted = 0;
        var updated = 0;
        var skipped = 0;

        Exec(catalog, tx, "INSERT OR IGNORE INTO template_pack (code, name, version, is_builtin, created_at, updated_at) VALUES ($c, $c, '1.0.0', 0, $now, $now)",
            ("$c", packCode), ("$now", Now()));

        var templates = ReadStagingTemplates(staging).DistinctBy(t => t.Code, StringComparer.Ordinal).ToList();
        var affected = new List<string>();

        foreach (var template in templates)
        {
            affected.Add(template.Code);
            var existingId = FindId(catalog, tx, "template", template.Code);

            if (existingId is null)
            {
                Exec(catalog, tx,
                    """
                    INSERT INTO template (code, name, kind, logical_type, locale, description, tags, pack_id, is_builtin, created_at, updated_at)
                    VALUES ($code, $name, $kind, $logical, $locale, $desc, $tags, (SELECT id FROM template_pack WHERE code = $pack), 0, $now, $now)
                    """,
                    ("$code", template.Code), ("$name", template.Name ?? template.Code), ("$kind", template.Kind),
                    ("$logical", template.Kind == "scalar" ? template.LogicalType : null), ("$locale", template.Locale ?? "pt-BR"),
                    ("$desc", template.Description), ("$tags", template.Tags ?? "[]"), ("$pack", packCode), ("$now", Now()));
                inserted++;
            }
            else if (mode == MergeMode.InsertOnly)
            {
                skipped++;
                continue;
            }
            else
            {
                Exec(catalog, tx, "UPDATE template SET name = $name, description = $desc, tags = $tags, updated_at = $now WHERE code = $code",
                    ("$name", template.Name ?? template.Code), ("$desc", template.Description), ("$tags", template.Tags ?? "[]"),
                    ("$now", Now()), ("$code", template.Code));
                updated++;
            }

            if (mode == MergeMode.ReplaceTemplateData && existingId is not null)
            {
                Exec(catalog, tx, "DELETE FROM template_record WHERE template_id = (SELECT id FROM template WHERE code = $c)", ("$c", template.Code));
                Exec(catalog, tx, "DELETE FROM template_value WHERE template_id = (SELECT id FROM template WHERE code = $c)", ("$c", template.Code));
            }
        }

        foreach (var field in ReadRows(staging, "SELECT template_code, field_code, logical_type, max_length, required, ordinal FROM template_field",
                     r => (T: r.GetString(0), F: r.GetString(1), L: r.GetString(2), M: r.IsDBNull(3) ? (int?)null : r.GetInt32(3), Q: r.GetInt64(4), O: r.IsDBNull(5) ? (int?)null : r.GetInt32(5))))
        {
            Exec(catalog, tx,
                """
                INSERT OR IGNORE INTO template_field (template_id, field_code, logical_type, ordinal, max_length, required)
                VALUES ((SELECT id FROM template WHERE code = $t), $f, $l, $o, $m, $q)
                """,
                ("$t", field.T), ("$f", field.F), ("$l", field.L), ("$o", field.O ?? 1), ("$m", field.M), ("$q", field.Q));
        }

        foreach (var alias in ReadRows(staging, "SELECT template_code, field_code, alias, weight, context_table_pattern FROM template_alias",
                     r => (T: r.GetString(0), F: r.IsDBNull(1) ? null : r.GetString(1), A: r.GetString(2), W: r.GetDouble(3), P: r.IsDBNull(4) ? null : r.GetString(4))))
        {
            Exec(catalog, tx,
                """
                INSERT OR IGNORE INTO template_alias (template_id, field_id, alias, normalized_alias, weight, context_table_pattern, source, created_at)
                VALUES ((SELECT id FROM template WHERE code = $t),
                        (SELECT f.id FROM template_field f JOIN template t ON t.id = f.template_id WHERE t.code = $t AND f.field_code = $f),
                        $a, $n, $w, $p, 'import', $now)
                """,
                ("$t", alias.T), ("$f", alias.F), ("$a", alias.A), ("$n", TemplateLoader.NormalizeCompact(alias.A)),
                ("$w", alias.W), ("$p", alias.P), ("$now", Now()));
        }

        foreach (var value in ReadRows(staging, "SELECT template_code, value, weight FROM template_value",
                     r => (T: r.GetString(0), V: r.GetString(1), W: r.GetDouble(2))))
        {
            Exec(catalog, tx,
                mode == MergeMode.Upsert
                    ? "INSERT INTO template_value (template_id, value, weight) VALUES ((SELECT id FROM template WHERE code = $t), $v, $w) ON CONFLICT(template_id, value) DO UPDATE SET weight = excluded.weight"
                    : "INSERT OR IGNORE INTO template_value (template_id, value, weight) VALUES ((SELECT id FROM template WHERE code = $t), $v, $w)",
                ("$t", value.T), ("$v", value.V), ("$w", value.W));
        }

        foreach (var record in ReadRows(staging, "SELECT template_code, record_key, data, weight FROM template_record",
                     r => (T: r.GetString(0), K: r.IsDBNull(1) ? null : r.GetString(1), D: r.GetString(2), W: r.GetDouble(3))))
        {
            var key = record.K ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.D))).Substring(0, 16).ToLowerInvariant();
            Exec(catalog, tx,
                mode == MergeMode.Upsert
                    ? "INSERT INTO template_record (template_id, record_key, data, weight) VALUES ((SELECT id FROM template WHERE code = $t), $k, $d, $w) ON CONFLICT(template_id, record_key) DO UPDATE SET data = excluded.data, weight = excluded.weight"
                    : "INSERT OR IGNORE INTO template_record (template_id, record_key, data, weight) VALUES ((SELECT id FROM template WHERE code = $t), $k, $d, $w)",
                ("$t", record.T), ("$k", key), ("$d", record.D), ("$w", record.W));
        }

        foreach (var preset in ReadRows(staging, "SELECT code, name, generator_id, generator_version, parameters, description FROM generator_preset",
                     r => (C: r.GetString(0), N: r.IsDBNull(1) ? r.GetString(0) : r.GetString(1), G: r.GetString(2), V: r.IsDBNull(3) ? 1 : r.GetInt32(3),
                           P: r.IsDBNull(4) ? "{}" : r.GetString(4), D: r.IsDBNull(5) ? null : r.GetString(5))))
        {
            Exec(catalog, tx,
                "INSERT OR IGNORE INTO generator_preset (code, name, generator_id, generator_version, parameters, description, is_builtin, created_at, updated_at) VALUES ($c, $n, $g, $v, $p, $d, 0, $now, $now)",
                ("$c", preset.C), ("$n", preset.N), ("$g", preset.G), ("$v", preset.V), ("$p", preset.P), ("$d", preset.D), ("$now", Now()));
        }

        foreach (var rule in ReadRows(staging, "SELECT column_pattern, table_pattern, engine, target_kind, target_ref, parameters, priority FROM mapping_rule",
                     r => (Col: r.GetString(0), Tbl: r.IsDBNull(1) ? null : r.GetString(1), Eng: r.IsDBNull(2) ? null : r.GetString(2),
                           Kind: r.GetString(3), Ref: r.GetString(4), Par: r.IsDBNull(5) ? null : r.GetString(5), Prio: r.GetInt32(6))))
        {
            Exec(catalog, tx,
                """
                INSERT OR IGNORE INTO mapping_rule (column_pattern, table_pattern, engine, target_kind, target_ref, parameters, scope, priority, source, enabled, created_at, updated_at)
                VALUES ($col, $tbl, $eng, $kind, $ref, $par, 'global', $prio, 'user', 1, $now, $now)
                """,
                ("$col", rule.Col), ("$tbl", rule.Tbl), ("$eng", rule.Eng), ("$kind", rule.Kind == "template_field" ? "template_field" : rule.Kind),
                ("$ref", rule.Ref), ("$par", rule.Par), ("$prio", rule.Prio), ("$now", Now()));
        }

        foreach (var code in affected.Distinct(StringComparer.Ordinal))
        {
            var hash = TemplateLoader.ContentHash(catalog, tx, code);
            Exec(catalog, tx, "UPDATE template SET content_hash = $h WHERE code = $c", ("$h", hash), ("$c", code));
        }

        return new MergeReport(inserted, updated, skipped, string.Empty);
    }

    // ---------- Auxiliares ----------

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static string StagingSchema()
    {
        var assembly = typeof(StagingImporter).Assembly;
        using var stream = assembly.GetManifestResourceStream("MockDataStudio.Catalog.Staging.staging-schema.sql")
            ?? throw new InvalidOperationException("Schema de staging não embutido.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string FirstKeyword(string text)
    {
        var trimmed = text.TrimStart();
        var end = 0;
        while (end < trimmed.Length && char.IsLetter(trimmed[end]))
        {
            end++;
        }

        return trimmed[..end].ToUpperInvariant();
    }

    private static ImportIssue Issue(IssueSeverity severity, string code, string message, string? table) =>
        new(severity, code, message, table, null, null);

    private static void Exec(SqliteConnection connection, string sql, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection connection, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static long? FindId(SqliteConnection connection, SqliteTransaction? tx, string table, string code)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $"SELECT id FROM {table} WHERE code = $c";
        command.Parameters.AddWithValue("$c", code);
        return command.ExecuteScalar() is long id ? id : null;
    }

    private static List<T> ReadRows<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
        {
            list.Add(map(reader));
        }

        return list;
    }

    private static List<StagingTemplate> ReadStagingTemplates(SqliteConnection staging) =>
        ReadRows(staging, "SELECT code, name, kind, logical_type, locale, description, tags FROM template",
            r => new StagingTemplate(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));

    private static Dictionary<string, CatalogTemplate> CatalogTemplates(SqliteConnection catalog)
    {
        var rows = ReadRows(catalog, "SELECT code, name, kind, logical_type, description, is_builtin FROM template",
            r => new CatalogTemplate(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5) == 1));
        return rows.ToDictionary(t => t.Code, StringComparer.Ordinal);
    }

    private static List<StagingField> CatalogFieldRows(SqliteConnection catalog) =>
        ReadRows(catalog, "SELECT t.code, f.field_code, f.logical_type, f.max_length, f.required FROM template_field f JOIN template t ON t.id = f.template_id",
            r => new StagingField(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.GetInt64(4) == 1));

    private static HashSet<(string, string)> CatalogFields(SqliteConnection catalog) =>
        ReadRows(catalog, "SELECT t.code, f.field_code FROM template_field f JOIN template t ON t.id = f.template_id", r => (r.GetString(0), r.GetString(1)))
            .ToHashSet();

    private static HashSet<(string, string, string)> CatalogSet3(SqliteConnection catalog, string sql) =>
        ReadRows(catalog, sql, r => (r.GetString(0), r.GetString(1), r.GetString(2))).ToHashSet();

    private static HashSet<(string, string)> CatalogSet(SqliteConnection catalog, string sql) =>
        ReadRows(catalog, sql, r => (r.GetString(0), r.GetString(1))).ToHashSet();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Arquivo ainda em uso: será removido na próxima limpeza da pasta temporária.
        }
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    private sealed record StagingTemplate(string Code, string? Name, string Kind, string? LogicalType, string? Locale, string? Description, string? Tags);
    private sealed record StagingField(string TemplateCode, string FieldCode, string LogicalType, int? MaxLength, bool Required);
    private sealed record StagingAlias(string TemplateCode, string? FieldCode, string Alias, double Weight);
    private sealed record StagingValue(string TemplateCode, string Value, double Weight);
    private sealed record StagingRecord(string TemplateCode, string? RecordKey, string Data, double Weight);
    private sealed record StagingRule(string ColumnPattern, string TargetKind, string TargetRef, int Priority);
    private sealed record CatalogTemplate(string Code, string Name, string Kind, string? LogicalType, string? Description, bool IsBuiltIn);
}
