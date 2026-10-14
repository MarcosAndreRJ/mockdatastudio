using MockDataStudio.Catalog.Data;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog.Editing;

// Formulário de template escalar (C8a.1, S07/S08): nome, descrição, tipo lógico, valores e aliases.
public sealed record ScalarTemplateForm(
    string Code, string Name, string? Description, LogicalType LogicalType,
    IReadOnlyList<string> Values, IReadOnlyList<string> Aliases,
    TemplateDraft? OriginalDraft = null);

// Edição de templates sobre o repositório do catálogo. As regras (código, tipo, embutidos somente leitura)
// continuam no repositório; aqui ficam a montagem do rascunho a partir do formulário e a leitura de texto.
public sealed class TemplateEditor(ITemplateRepository templates, ITemplateDataSource dataSource)
{
    public static IReadOnlyList<string> ParseLines(string text) =>
        (text ?? string.Empty)
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    // Código no formato do catálogo (maiúsculas, dígitos e "_"): o que o usuário digita vira o código válido.
    // Ex.: "cliente br" → "CLIENTE_BR". Caracteres fora de A-Z/0-9 viram "_"; o repositório ainda valida o tamanho.
    public static string NormalizeCode(string text) =>
        new string((text ?? string.Empty).Trim().ToUpperInvariant()
            .Select(ch => ch is (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? ch : '_')
            .ToArray());

    public Task<Result<long>> SaveScalarAsync(ScalarTemplateForm form, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.Values.Count == 0)
        {
            return Task.FromResult(Result<long>.Fail(ErrorCodes.CatImpCode, "Informe ao menos um valor para o template."));
        }

        var original = form.OriginalDraft;
        var tags = original?.Tags ?? [];

        // Aliases: preserva aliases por campo e atributos de aliases preservados (peso e padrão de tabela)
        var aliases = new List<DraftAlias>();
        if (original is not null)
        {
            aliases.AddRange(original.Aliases.Where(a => a.FieldCode is not null));
        }

        var origTemplateAliases = original?.Aliases
            .Where(a => a.FieldCode is null)
            .GroupBy(a => a.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            ?? [];

        foreach (var aliasText in form.Aliases)
        {
            if (origTemplateAliases.TryGetValue(aliasText, out var origAlias))
            {
                aliases.Add(origAlias);
            }
            else
            {
                aliases.Add(new DraftAlias(aliasText));
            }
        }

        // Valores: preserva pesos existentes quando o valor não mudou
        var origWeights = original?.Values
            .GroupBy(v => v.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Weight, StringComparer.Ordinal)
            ?? [];

        var values = form.Values.Select(value =>
            new TemplateValueRow(value, origWeights.TryGetValue(value, out var w) ? w : 1.0)).ToList();

        var draft = new TemplateDraft(
            form.Code,
            form.Name,
            TemplateKind.Scalar,
            form.LogicalType,
            string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
            tags,
            original?.Fields ?? [],
            aliases,
            values,
            []);
        return templates.SaveAsync(draft, ct);
    }

    public Task<Result<long>> SaveDraftAsync(TemplateDraft draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return templates.SaveAsync(draft, ct);
    }

    public Task<Result<string>> CloneAsync(string code, string newCode, CancellationToken ct) =>
        templates.CloneAsync(code, newCode, ct);

    public Task<Result<bool>> DeleteAsync(string code, CancellationToken ct) =>
        templates.DeleteAsync(code, ct);

    // Carrega um template escalar do usuário para o formulário (todos os valores, não só a prévia). Embutidos são somente leitura.
    public async Task<Result<ScalarTemplateForm>> LoadScalarAsync(string code, CancellationToken ct)
    {
        var info = await templates.GetAsync(code, ct);
        if (info is null)
        {
            return Result<ScalarTemplateForm>.Fail(ErrorCodes.CatImpUnknownTemplate, $"Template '{code}' não existe.");
        }

        if (info.IsBuiltIn)
        {
            return Result<ScalarTemplateForm>.Fail(ErrorCodes.CatBuiltinReadOnly, $"Template embutido '{code}' é somente leitura.");
        }

        if (info.Kind != TemplateKind.Scalar)
        {
            return Result<ScalarTemplateForm>.Fail(ErrorCodes.CatImpCode, "Edição de templates compostos ainda não está disponível.");
        }

        var draft = await templates.GetDraftAsync(code, ct);
        List<string> values;
        List<string> aliases;

        if (draft is not null)
        {
            values = draft.Values.Select(v => v.Value).Distinct(StringComparer.Ordinal).ToList();
            aliases = draft.Aliases
                .Where(a => a.FieldCode is null)
                .Select(a => a.Alias)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            var set = await dataSource.LoadAsync(code, ct);
            values = Enumerable.Range(0, set.Count).Select(set.GetScalar).Distinct(StringComparer.Ordinal).ToList();
            aliases = (await templates.GetAllAliasesAsync(ct))
                .Where(a => a.TemplateCode == code && a.FieldCode is null)
                .Select(a => a.Alias)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return Result<ScalarTemplateForm>.Ok(new ScalarTemplateForm(
            info.Code, info.Name, info.Description, info.LogicalType ?? LogicalType.String, values, aliases, draft));
    }

    public async Task<Result<TemplateDraft>> LoadDraftAsync(string code, CancellationToken ct)
    {
        var info = await templates.GetAsync(code, ct);
        if (info is null)
        {
            return Result<TemplateDraft>.Fail(ErrorCodes.CatImpUnknownTemplate, $"Template '{code}' não existe.");
        }

        if (info.IsBuiltIn)
        {
            return Result<TemplateDraft>.Fail(ErrorCodes.CatBuiltinReadOnly, $"Template embutido '{code}' é somente leitura.");
        }

        var draft = await templates.GetDraftAsync(code, ct);
        return draft is not null
            ? Result<TemplateDraft>.Ok(draft)
            : Result<TemplateDraft>.Fail(ErrorCodes.CatImpUnknownTemplate, $"Template '{code}' não existe.");
    }
}
