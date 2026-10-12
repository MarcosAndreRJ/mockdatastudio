using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog.Repositories;

// Lê um template do catálogo como TemplateDataSet ordenado (C05 §2). Usado pelo repositório (ContentHash)
// e pela fonte de dados de geração (CatalogTemplateDataSource).
public static class TemplateLoader
{
    public static Result<TemplateDataSet> Load(SqliteConnection connection, string code, SqliteTransaction? transaction = null)
    {
        using var header = Command(connection, transaction);
        header.CommandText = "SELECT id, kind FROM template WHERE code = $code";
        header.Parameters.AddWithValue("$code", code);
        using var reader = header.ExecuteReader();
        if (!reader.Read())
        {
            reader.Close();
            return Result<TemplateDataSet>.Fail(ErrorCodes.CatImpUnknownTemplate, $"Template '{code}' não existe no catálogo.");
        }

        var templateId = reader.GetInt64(0);
        var kind = Enum.Parse<TemplateKind>(reader.GetString(1), ignoreCase: true);
        reader.Close();

        return kind == TemplateKind.Scalar
            ? LoadScalar(connection, transaction, code, templateId)
            : LoadComposite(connection, transaction, code, templateId);
    }

    private static Result<TemplateDataSet> LoadScalar(SqliteConnection connection, SqliteTransaction? transaction, string code, long templateId)
    {
        var items = new List<(string Key, string[]? Fields, string? Scalar, double Weight)>();
        using var command = Command(connection, transaction);
        command.CommandText = "SELECT value, weight FROM template_value WHERE template_id = $id ORDER BY value";
        command.Parameters.AddWithValue("$id", templateId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = reader.GetString(0);
            items.Add((value, null, value, reader.GetDouble(1)));
        }

        return TemplateDataSet.Create(code, TemplateKind.Scalar, [], items);
    }

    private static Result<TemplateDataSet> LoadComposite(SqliteConnection connection, SqliteTransaction? transaction, string code, long templateId)
    {
        var fields = new List<string>();
        using (var fieldCommand = Command(connection, transaction))
        {
            fieldCommand.CommandText = "SELECT field_code FROM template_field WHERE template_id = $id ORDER BY ordinal";
            fieldCommand.Parameters.AddWithValue("$id", templateId);
            using var fieldReader = fieldCommand.ExecuteReader();
            while (fieldReader.Read())
            {
                fields.Add(fieldReader.GetString(0));
            }
        }

        var items = new List<(string Key, string[]? Fields, string? Scalar, double Weight)>();
        using (var recordCommand = Command(connection, transaction))
        {
            recordCommand.CommandText = "SELECT record_key, data, weight FROM template_record WHERE template_id = $id ORDER BY record_key";
            recordCommand.Parameters.AddWithValue("$id", templateId);
            using var recordReader = recordCommand.ExecuteReader();
            while (recordReader.Read())
            {
                var key = recordReader.GetString(0);
                using var document = JsonDocument.Parse(recordReader.GetString(1));
                var values = fields.Select(f => ReadField(document.RootElement, f)).ToArray();
                items.Add((key, values, null, recordReader.GetDouble(2)));
            }
        }

        return TemplateDataSet.Create(code, TemplateKind.Composite, fields, items);
    }

    private static string ReadField(JsonElement record, string field)
    {
        if (!record.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : value.GetRawText();
    }

    // Hash do conteúdo do template (vazio quando não carregável).
    public static string ContentHash(SqliteConnection connection, SqliteTransaction? transaction, string code)
    {
        var loaded = Load(connection, code, transaction);
        return loaded.IsSuccess ? loaded.Value.ContentHash : string.Empty;
    }

    // Normalização compacta de nomes (14 §2): maiúsculas, sem separadores nem caracteres não alfanuméricos.
    public static string NormalizeCompact(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    public static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
