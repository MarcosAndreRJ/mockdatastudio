using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog.Editing;

// Formulário de template composto (C8a.1): campos e registros. Texto de edição:
//   campos:    uma linha por campo: codigo | tipo | tamanho máximo (opcional) | obrigatório (sim/não, padrão sim)
//   registros: uma linha por registro: valores na ordem dos campos, separados por " | ". Valor vazio = NULL.
public sealed record CompositeTemplateForm(
    string Code, string Name, string? Description,
    IReadOnlyList<DraftField> Fields, IReadOnlyList<string> Aliases,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Records);

public sealed class CompositeTemplateEditor(ITemplateRepository templates, ITemplateDataSource dataSource)
{
    private const char Separator = '|';

    // ---------- texto <-> modelo ----------

    public static Result<IReadOnlyList<DraftField>> ParseFields(string text)
    {
        var fields = new List<DraftField>();
        var lines = Lines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            var parts = lines[i].Split(Separator).Select(p => p.Trim()).ToArray();
            if (parts.Length < 2 || parts.Length > 4 || parts[0].Length == 0)
            {
                return Result<IReadOnlyList<DraftField>>.Fail(ErrorCodes.CatImpCode,
                    $"Campo, linha {i + 1}: use 'codigo | tipo | tamanho | obrigatório'.");
            }

            if (!Enum.TryParse<LogicalType>(parts[1], ignoreCase: true, out var logicalType))
            {
                return Result<IReadOnlyList<DraftField>>.Fail(ErrorCodes.CatImpCode,
                    $"Campo '{parts[0]}': tipo '{parts[1]}' inválido (ex.: string, integer, date).");
            }

            int? maxLength = null;
            if (parts.Length >= 3 && parts[2].Length > 0)
            {
                if (!int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length) || length <= 0)
                {
                    return Result<IReadOnlyList<DraftField>>.Fail(ErrorCodes.CatImpCode,
                        $"Campo '{parts[0]}': tamanho máximo deve ser um inteiro positivo.");
                }

                maxLength = length;
            }

            // Obrigatório por padrão; só "não"/"nao" desmarca.
            var answer = parts.Length >= 4 ? parts[3] : string.Empty;
            var required = !(answer.Equals("não", StringComparison.OrdinalIgnoreCase) || answer.Equals("nao", StringComparison.OrdinalIgnoreCase));
            fields.Add(new DraftField(parts[0], logicalType, i + 1, maxLength, required));
        }

        return Result<IReadOnlyList<DraftField>>.Ok(fields);
    }

    public static string FormatFields(IReadOnlyList<DraftField> fields) =>
        string.Join(Environment.NewLine, fields
            .OrderBy(f => f.Ordinal)
            .Select(f => $"{f.FieldCode} | {f.LogicalType.ToString().ToLowerInvariant()}"
                + (f.MaxLength is { } max ? $" | {max}" : " | ")
                + (f.Required ? " | sim" : " | não")));

    public static Result<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ParseRecords(string text, IReadOnlyList<DraftField> fields)
    {
        var records = new List<IReadOnlyDictionary<string, string?>>();
        var lines = Lines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            var values = lines[i].Split(Separator).Select(v => v.Trim()).ToArray();
            if (values.Length != fields.Count)
            {
                return Result<IReadOnlyList<IReadOnlyDictionary<string, string?>>>.Fail(ErrorCodes.CatImpCode,
                    $"Registro, linha {i + 1}: esperados {fields.Count} valor(es) separados por '|', encontrados {values.Length}.");
            }

            var row = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var f = 0; f < fields.Count; f++)
            {
                row[fields[f].FieldCode] = values[f].Length == 0 ? null : values[f];
            }

            records.Add(row);
        }

        return Result<IReadOnlyList<IReadOnlyDictionary<string, string?>>>.Ok(records);
    }

    public static string FormatRecords(IReadOnlyList<IReadOnlyDictionary<string, string?>> records, IReadOnlyList<DraftField> fields) =>
        string.Join(Environment.NewLine, records.Select(r =>
            string.Join($" {Separator} ", fields.OrderBy(f => f.Ordinal).Select(f => r.TryGetValue(f.FieldCode, out var v) ? v ?? string.Empty : string.Empty))));

    private static List<string> Lines(string text) =>
        (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    // ---------- catálogo ----------

    public Task<Result<long>> SaveAsync(CompositeTemplateForm form, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.Fields.Count == 0)
        {
            return Task.FromResult(Result<long>.Fail(ErrorCodes.CatImpCode, "Informe ao menos um campo para o template composto."));
        }

        var draft = new TemplateDraft(
            form.Code, form.Name, TemplateKind.Composite, null,
            string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
            [],
            form.Fields,
            form.Aliases.Select(alias => new DraftAlias(alias)).ToList(),
            [],
            // Chave por posição: registros com a mesma chave colidiriam no catálogo.
            form.Records.Select((r, i) => new TemplateRecordRow((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), r, 1.0)).ToList());
        return templates.SaveAsync(draft, ct);
    }

    // Carrega um composto do usuário para o formulário. Embutidos são somente leitura; escalares não passam por aqui.
    public async Task<Result<CompositeTemplateForm>> LoadAsync(string code, CancellationToken ct)
    {
        var info = await templates.GetAsync(code, ct);
        if (info is null)
        {
            return Result<CompositeTemplateForm>.Fail(ErrorCodes.CatImpUnknownTemplate, $"Template '{code}' não existe.");
        }

        if (info.IsBuiltIn)
        {
            return Result<CompositeTemplateForm>.Fail(ErrorCodes.CatBuiltinReadOnly, $"Template embutido '{code}' é somente leitura.");
        }

        if (info.Kind != TemplateKind.Composite)
        {
            return Result<CompositeTemplateForm>.Fail(ErrorCodes.CatImpCode, $"Template '{code}' não é composto.");
        }

        var fields = (await templates.GetFieldsAsync(code, ct))
            .OrderBy(f => f.Ordinal)
            .Select(f => new DraftField(f.FieldCode, f.LogicalType, f.Ordinal, f.MaxLength, f.Required))
            .ToList();

        var set = await dataSource.LoadAsync(code, ct);
        var records = new List<IReadOnlyDictionary<string, string?>>();
        for (var i = 0; i < set.Count; i++)
        {
            var row = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                row[field.FieldCode] = set.GetField(i, field.FieldCode);
            }

            records.Add(row);
        }

        var aliases = (await templates.GetAllAliasesAsync(ct))
            .Where(a => a.TemplateCode == code && a.FieldCode is null)
            .Select(a => a.Alias)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Result<CompositeTemplateForm>.Ok(new CompositeTemplateForm(
            info.Code, info.Name, info.Description, fields, aliases, records));
    }
}
