using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MockDataStudio.Catalog.Database;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog.Repositories;

// Repositório de templates (C03, TASK-04.02). Aplica as invariantes de 09 §3 antes de gravar.
public sealed partial class TemplateRepository(CatalogDatabase database) : ITemplateRepository
{
    private const string UserPackCode = "USER";

    public Task<IReadOnlyList<TemplateInfo>> ListAsync(TemplateQuery query, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.code, t.name, t.kind, t.logical_type, t.locale, t.tags, t.description, p.code, t.is_builtin,
                   (SELECT COUNT(*) FROM template_value v WHERE v.template_id = t.id),
                   (SELECT COUNT(*) FROM template_record r WHERE r.template_id = t.id)
            FROM template t JOIN template_pack p ON p.id = t.pack_id
            WHERE ($text IS NULL OR t.code LIKE '%' || $text || '%' OR t.name LIKE '%' || $text || '%')
              AND ($kind IS NULL OR t.kind = $kind)
              AND ($pack IS NULL OR p.code = $pack)
            ORDER BY t.code
            """;
        command.Parameters.AddWithValue("$text", (object?)query.Text ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", query.Kind is { } k ? (object)k.ToString().ToLowerInvariant() : DBNull.Value);
        command.Parameters.AddWithValue("$pack", (object?)query.PackCode ?? DBNull.Value);

        var list = new List<TemplateInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var code = reader.GetString(1);
            list.Add(new TemplateInfo(
                reader.GetInt64(0), code, reader.GetString(2),
                Enum.Parse<TemplateKind>(reader.GetString(3), ignoreCase: true),
                reader.IsDBNull(4) ? null : Enum.Parse<LogicalType>(reader.GetString(4), ignoreCase: true),
                reader.GetString(5), Tags(reader.GetString(6)), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8), reader.GetInt64(9) == 1, reader.GetInt64(10),
                TemplateLoader.ContentHash(connection, null, code)));
        }

        return Task.FromResult<IReadOnlyList<TemplateInfo>>(list);
    }

    public Task<TemplateInfo?> GetAsync(string code, CancellationToken ct)
    {
        var all = ListAsync(new TemplateQuery(), ct).GetAwaiter().GetResult();
        return Task.FromResult(all.FirstOrDefault(t => t.Code == code));
    }

    public Task<IReadOnlyList<TemplateFieldInfo>> GetFieldsAsync(string code, CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.field_code, f.logical_type, f.ordinal, f.max_length, f.required
            FROM template_field f JOIN template t ON t.id = f.template_id
            WHERE t.code = $code ORDER BY f.ordinal
            """;
        command.Parameters.AddWithValue("$code", code);

        var list = new List<TemplateFieldInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TemplateFieldInfo(reader.GetString(0),
                Enum.Parse<LogicalType>(reader.GetString(1), ignoreCase: true),
                reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetInt64(4) == 1));
        }

        return Task.FromResult<IReadOnlyList<TemplateFieldInfo>>(list);
    }

    public Task<IReadOnlyList<TemplateAliasInfo>> GetAllAliasesAsync(CancellationToken ct)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, t.code, f.field_code, a.alias, a.normalized_alias, a.weight, a.context_table_pattern
            FROM template_alias a
            JOIN template t ON t.id = a.template_id
            LEFT JOIN template_field f ON f.id = a.field_id
            ORDER BY t.code, a.alias
            """;

        var list = new List<TemplateAliasInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TemplateAliasInfo(reader.GetInt64(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetDouble(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return Task.FromResult<IReadOnlyList<TemplateAliasInfo>>(list);
    }

    public Task<Result<long>> SaveAsync(TemplateDraft draft, CancellationToken ct)
    {
        var validation = Validate(draft);
        if (!validation.IsSuccess)
        {
            return Task.FromResult(Result<long>.Fail(validation.Error));
        }

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var existing = FindTemplate(connection, transaction, draft.Code);
        long templateId;

        if (existing is { } current)
        {
            if (current.IsBuiltIn && (!string.Equals(current.Kind, draft.Kind.ToString(), StringComparison.OrdinalIgnoreCase) || current.LogicalType != draft.LogicalType?.ToString().ToLowerInvariant()))
            {
                return Task.FromResult(Result<long>.Fail(ErrorCodes.CatBuiltinReadOnly,
                    $"Template embutido '{draft.Code}' não pode ter tipo ou kind alterados."));
            }

            templateId = current.Id;
            Exec(connection, transaction, "UPDATE template SET name = $name, description = $desc, tags = $tags, updated_at = $now WHERE id = $id",
                ("$name", draft.Name), ("$desc", (object?)draft.Description ?? DBNull.Value),
                ("$tags", JsonSerializer.Serialize(draft.Tags)), ("$now", Now()), ("$id", templateId));

            if (!current.IsBuiltIn)
            {
                // Substitui o conteúdo do template pelo rascunho (campos, aliases, valores e registros).
                foreach (var table in new[] { "template_record", "template_value", "template_alias", "template_field" })
                {
                    Exec(connection, transaction, $"DELETE FROM {table} WHERE template_id = $id", ("$id", templateId));
                }
            }
        }
        else
        {
            EnsureUserPack(connection, transaction);
            templateId = InsertTemplate(connection, transaction, draft);
        }

        InsertContent(connection, transaction, templateId, draft, ignoreExisting: existing is { IsBuiltIn: true });

        Exec(connection, transaction, "UPDATE template SET content_hash = $hash WHERE id = $id",
            ("$hash", TemplateLoader.ContentHash(connection, transaction, draft.Code)), ("$id", templateId));

        transaction.Commit();
        return Task.FromResult(Result<long>.Ok(templateId));
    }

    public Task<Result<bool>> DeleteAsync(string code, CancellationToken ct)
    {
        using var connection = database.Open();
        var existing = FindTemplate(connection, null, code);
        if (existing is null)
        {
            return Task.FromResult(Result<bool>.Ok(false));
        }

        if (existing.Value.IsBuiltIn)
        {
            return Task.FromResult(Result<bool>.Fail(ErrorCodes.CatBuiltinReadOnly,
                $"Template embutido '{code}' não pode ser excluído."));
        }

        Exec(connection, null, "DELETE FROM template WHERE id = $id", ("$id", existing.Value.Id));
        return Task.FromResult(Result<bool>.Ok(true));
    }

    public Task<Result<string>> CloneAsync(string code, string newCode, CancellationToken ct)
    {
        using var connection = database.Open();
        var draft = ReadDraft(connection, code);
        if (draft is null)
        {
            return Task.FromResult(Result<string>.Fail(ErrorCodes.CatImpUnknownTemplate, $"Template '{code}' não existe."));
        }

        var copy = draft with { Code = newCode, Name = draft.Name + " (cópia)" };
        var saved = SaveAsync(copy, ct).GetAwaiter().GetResult();
        return Task.FromResult(saved.IsSuccess
            ? Result<string>.Ok(newCode)
            : Result<string>.Fail(saved.Error));
    }

    // ---------- Validação ----------

    private static Result<bool> Validate(TemplateDraft draft)
    {
        if (!CodePattern().IsMatch(draft.Code))
        {
            return Result<bool>.Fail(ErrorCodes.CatImpCode, $"Código '{draft.Code}' inválido: use maiúsculas, dígitos e '_' (2 a 64).");
        }

        if (draft.Kind == TemplateKind.Scalar && draft.LogicalType is null)
        {
            return Result<bool>.Fail(ErrorCodes.CatImpKind, "Template escalar exige logical_type.");
        }

        if (draft.Kind == TemplateKind.Composite && draft.Fields.Count == 0)
        {
            return Result<bool>.Fail(ErrorCodes.CatImpField, "Template composto exige ao menos um campo.");
        }

        var fieldCodes = draft.Fields.Select(f => f.FieldCode).ToHashSet(StringComparer.Ordinal);
        foreach (var alias in draft.Aliases)
        {
            if (alias.FieldCode is null)
            {
                continue;
            }

            if (draft.Kind == TemplateKind.Scalar || !fieldCodes.Contains(alias.FieldCode))
            {
                return Result<bool>.Fail(ErrorCodes.CatImpField, $"Alias '{alias.Alias}' referencia campo inexistente ou template escalar.");
            }
        }

        foreach (var record in draft.Records)
        {
            if (draft.Kind == TemplateKind.Scalar)
            {
                return Result<bool>.Fail(ErrorCodes.CatImpKind, "Template escalar não aceita registros.");
            }

            var unknown = record.Data.Keys.Where(k => !fieldCodes.Contains(k)).ToList();
            var missing = draft.Fields.Where(f => f.Required && !record.Data.ContainsKey(f.FieldCode)).Select(f => f.FieldCode).ToList();
            if (unknown.Count > 0 || missing.Count > 0)
            {
                return Result<bool>.Fail(ErrorCodes.CatImpRecordJson,
                    $"Registro '{record.RecordKey}' com campos desconhecidos ({string.Join(',', unknown)}) ou obrigatórios ausentes ({string.Join(',', missing)}).");
            }
        }

        return Result<bool>.Ok(true);
    }

    // ---------- Persistência ----------

    private static void InsertContent(SqliteConnection connection, SqliteTransaction transaction, long templateId, TemplateDraft draft, bool ignoreExisting)
    {
        var verb = ignoreExisting ? "INSERT OR IGNORE" : "INSERT";
        var fieldIds = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var field in draft.Fields)
        {
            Exec(connection, transaction,
                $"{verb} INTO template_field (template_id, field_code, logical_type, ordinal, max_length, required) VALUES ($t, $c, $l, $o, $m, $r)",
                ("$t", templateId), ("$c", field.FieldCode), ("$l", field.LogicalType.ToString().ToLowerInvariant()),
                ("$o", field.Ordinal), ("$m", (object?)field.MaxLength ?? DBNull.Value), ("$r", field.Required ? 1 : 0));
            fieldIds[field.FieldCode] = LastRowId(connection, transaction);
        }

        foreach (var alias in draft.Aliases)
        {
            var fieldId = alias.FieldCode is null ? (object)DBNull.Value : fieldIds[alias.FieldCode];
            Exec(connection, transaction,
                $"{verb} INTO template_alias (template_id, field_id, alias, normalized_alias, weight, context_table_pattern, source, created_at) VALUES ($t, $f, $a, $n, $w, $p, 'user', $now)",
                ("$t", templateId), ("$f", fieldId), ("$a", alias.Alias), ("$n", TemplateLoader.NormalizeCompact(alias.Alias)),
                ("$w", alias.Weight), ("$p", (object?)alias.ContextTablePattern ?? DBNull.Value), ("$now", Now()));
        }

        foreach (var value in draft.Values)
        {
            Exec(connection, transaction, $"{verb} INTO template_value (template_id, value, weight) VALUES ($t, $v, $w)",
                ("$t", templateId), ("$v", value.Value), ("$w", value.Weight));
        }

        foreach (var record in draft.Records)
        {
            var data = JsonSerializer.Serialize(record.Data.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal));
            Exec(connection, transaction, $"{verb} INTO template_record (template_id, record_key, data, weight) VALUES ($t, $k, $d, $w)",
                ("$t", templateId), ("$k", record.RecordKey), ("$d", data), ("$w", record.Weight));
        }
    }

    private static long InsertTemplate(SqliteConnection connection, SqliteTransaction transaction, TemplateDraft draft)
    {
        Exec(connection, transaction,
            """
            INSERT INTO template (code, name, kind, logical_type, locale, description, tags, pack_id, is_builtin, created_at, updated_at)
            VALUES ($code, $name, $kind, $logical, 'pt-BR', $desc, $tags,
                    (SELECT id FROM template_pack WHERE code = $pack), 0, $now, $now)
            """,
            ("$code", draft.Code), ("$name", draft.Name), ("$kind", draft.Kind.ToString().ToLowerInvariant()),
            ("$logical", draft.LogicalType is null ? DBNull.Value : draft.LogicalType.Value.ToString().ToLowerInvariant()),
            ("$desc", (object?)draft.Description ?? DBNull.Value), ("$tags", JsonSerializer.Serialize(draft.Tags)),
            ("$pack", UserPackCode), ("$now", Now()));
        return LastRowId(connection, transaction);
    }

    private static void EnsureUserPack(SqliteConnection connection, SqliteTransaction transaction)
    {
        Exec(connection, transaction,
            "INSERT OR IGNORE INTO template_pack (code, name, version, is_builtin, created_at, updated_at) VALUES ($c, 'Templates do usuário', '1.0.0', 0, $now, $now)",
            ("$c", UserPackCode), ("$now", Now()));
    }

    private static (long Id, string Kind, string? LogicalType, bool IsBuiltIn)? FindTemplate(SqliteConnection connection, SqliteTransaction? transaction, string code)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, kind, logical_type, is_builtin FROM template WHERE code = $code";
        command.Parameters.AddWithValue("$code", code);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return (reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3) == 1);
    }

    private static TemplateDraft? ReadDraft(SqliteConnection connection, string code)
    {
        var header = FindTemplate(connection, null, code);
        if (header is null)
        {
            return null;
        }

        var kind = Enum.Parse<TemplateKind>(header.Value.Kind, ignoreCase: true);
        var (name, description, tags) = ReadHeader(connection, code);

        var fields = new List<DraftField>();
        var aliases = new List<DraftAlias>();
        var values = new List<TemplateValueRow>();
        var records = new List<TemplateRecordRow>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT field_code, logical_type, ordinal, max_length, required FROM template_field WHERE template_id = $id ORDER BY ordinal";
            command.Parameters.AddWithValue("$id", header.Value.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                fields.Add(new DraftField(reader.GetString(0), Enum.Parse<LogicalType>(reader.GetString(1), ignoreCase: true),
                    reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetInt64(4) == 1));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT a.alias, f.field_code, a.weight, a.context_table_pattern FROM template_alias a
                LEFT JOIN template_field f ON f.id = a.field_id WHERE a.template_id = $id
                """;
            command.Parameters.AddWithValue("$id", header.Value.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                aliases.Add(new DraftAlias(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT value, weight FROM template_value WHERE template_id = $id";
            command.Parameters.AddWithValue("$id", header.Value.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                values.Add(new TemplateValueRow(reader.GetString(0), reader.GetDouble(1)));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT record_key, data, weight FROM template_record WHERE template_id = $id";
            command.Parameters.AddWithValue("$id", header.Value.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(reader.GetString(1)) ?? [];
                records.Add(new TemplateRecordRow(reader.GetString(0), data, reader.GetDouble(2)));
            }
        }

        var logical = header.Value.LogicalType is null ? (LogicalType?)null : Enum.Parse<LogicalType>(header.Value.LogicalType, ignoreCase: true);
        return new TemplateDraft(code, name, kind, logical, description, tags, fields, aliases, values, records);
    }

    private static (string Name, string? Description, IReadOnlyList<string> Tags) ReadHeader(SqliteConnection connection, string code)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, description, tags FROM template WHERE code = $code";
        command.Parameters.AddWithValue("$code", code);
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), Tags(reader.GetString(2)));
    }

    private static List<string> Tags(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static void Exec(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static long LastRowId(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
