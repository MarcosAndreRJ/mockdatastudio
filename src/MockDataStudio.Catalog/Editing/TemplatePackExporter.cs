using System.Text;
using System.Text.Json;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog.Editing;

// Exporta templates do usuário como pack JSON (formato "template-pack-1.0", o mesmo de pack-core-ptbr.json).
// O pack exportado pode ser importado de volta pela tela Templates (TemplateImportService.AnalyzePackJsonAsync).
// Templates embutidos não são exportados: eles já vêm no pacote do aplicativo.
public sealed class TemplatePackExporter(ITemplateRepository templates, ITemplateDataSource dataSource)
{
    public const string Format = "template-pack-1.0";

    public async Task<Result<string>> ExportAsync(string packCode, string packName, string author,
        IReadOnlyList<string>? templateCodes, CancellationToken ct, string? license = "MIT", string? version = "1.0.0")
    {
        if (string.IsNullOrWhiteSpace(packCode) || !System.Text.RegularExpressions.Regex.IsMatch(packCode, "^[A-Z0-9_]{2,64}$"))
        {
            return Result<string>.Fail(ErrorCodes.CatImpCode, "Código do pack inválido: use maiúsculas, dígitos e '_' (2 a 64).");
        }

        var wanted = templateCodes is null ? null : new HashSet<string>(templateCodes, StringComparer.Ordinal);
        var selected = (await templates.ListAsync(new TemplateQuery(), ct))
            .Where(t => !t.IsBuiltIn && (wanted is null || wanted.Contains(t.Code)))
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

        var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteString("code", packCode);
            writer.WriteString("name", string.IsNullOrWhiteSpace(packName) ? packCode : packName);
            writer.WriteString("version", string.IsNullOrWhiteSpace(version) ? "1.0.0" : version);
            writer.WriteString("author", author ?? string.Empty);
            writer.WriteString("license", string.IsNullOrWhiteSpace(license) ? "MIT" : license);
            writer.WriteStartArray("templates");
            foreach (var info in selected)
            {
                ct.ThrowIfCancellationRequested();
                await WriteTemplateAsync(writer, info, ct);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Result<string>.Ok(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private async Task WriteTemplateAsync(Utf8JsonWriter writer, TemplateInfo info, CancellationToken ct)
    {
        writer.WriteStartObject();
        writer.WriteString("code", info.Code);
        writer.WriteString("name", info.Name);
        writer.WriteString("kind", info.Kind == TemplateKind.Composite ? "composite" : "scalar");
        if (info.LogicalType is { } logical)
        {
            writer.WriteString("logicalType", logical.ToString().ToLowerInvariant());
        }

        writer.WritePropertyName("tags");
        writer.WriteStartArray();
        foreach (var tag in info.Tags)
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();

        if (!string.IsNullOrEmpty(info.Description))
        {
            writer.WriteString("description", info.Description);
        }

        var aliases = (await templates.GetAllAliasesAsync(ct)).Where(a => a.TemplateCode == info.Code).ToList();
        writer.WriteStartArray("aliases");
        foreach (var alias in aliases.OrderBy(a => a.Alias, StringComparer.Ordinal))
        {
            if (alias.FieldCode is null)
            {
                writer.WriteStringValue(alias.Alias);
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString("alias", alias.Alias);
                writer.WriteString("field", alias.FieldCode);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();

        var set = await dataSource.LoadAsync(info.Code, ct);
        if (info.Kind == TemplateKind.Composite)
        {
            await WriteCompositeAsync(writer, info, set, ct);
        }
        else
        {
            writer.WriteStartArray("values");
            for (var i = 0; i < set.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("value", set.GetScalar(i));
                writer.WriteNumber("weight", set.GetWeight(i));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private async Task WriteCompositeAsync(Utf8JsonWriter writer, TemplateInfo info, TemplateDataSet set, CancellationToken ct)
    {
        var fields = (await templates.GetFieldsAsync(info.Code, ct)).OrderBy(f => f.Ordinal).ToList();
        writer.WriteStartArray("fields");
        foreach (var field in fields)
        {
            writer.WriteStartObject();
            writer.WriteString("code", field.FieldCode);
            writer.WriteString("logicalType", field.LogicalType.ToString().ToLowerInvariant());
            writer.WriteNumber("ordinal", field.Ordinal);
            if (field.MaxLength is { } max)
            {
                writer.WriteNumber("maxLength", max);
            }

            writer.WriteBoolean("required", field.Required);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("records");
        for (var i = 0; i < set.Count; i++)
        {
            writer.WriteStartObject();
            writer.WriteString("key", (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteNumber("weight", set.GetWeight(i));
            writer.WritePropertyName("data");
            writer.WriteStartObject();
            foreach (var field in fields)
            {
                var value = set.GetField(i, field.FieldCode);
                if (string.IsNullOrEmpty(value))
                {
                    writer.WriteNull(field.FieldCode);
                }
                else
                {
                    writer.WriteString(field.FieldCode, value);
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
