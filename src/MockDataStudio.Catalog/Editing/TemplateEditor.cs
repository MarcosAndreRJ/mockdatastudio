using MockDataStudio.Catalog.Data;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog.Editing;

// Formulário de template escalar (C8a.1, S07/S08): nome, descrição, tipo lógico, valores e aliases.
public sealed record ScalarTemplateForm(
    string Code, string Name, string? Description, LogicalType LogicalType,
    IReadOnlyList<string> Values, IReadOnlyList<string> Aliases);

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

    public Task<Result<long>> SaveScalarAsync(ScalarTemplateForm form, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.Values.Count == 0)
        {
            return Task.FromResult(Result<long>.Fail(ErrorCodes.CatImpCode, "Informe ao menos um valor para o template."));
        }

        var draft = new TemplateDraft(
            form.Code,
            form.Name,
            TemplateKind.Scalar,
            form.LogicalType,
            string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
            [],
            [],
            form.Aliases.Select(alias => new DraftAlias(alias)).ToList(),
            form.Values.Select(value => new TemplateValueRow(value, 1.0)).ToList(),
            []);
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

        var set = await dataSource.LoadAsync(code, ct);
        var values = Enumerable.Range(0, set.Count).Select(set.GetScalar).Distinct(StringComparer.Ordinal).ToList();
        var aliases = (await templates.GetAllAliasesAsync(ct))
            .Where(a => a.TemplateCode == code && a.FieldCode is null)
            .Select(a => a.Alias)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return Result<ScalarTemplateForm>.Ok(new ScalarTemplateForm(
            info.Code, info.Name, info.Description, info.LogicalType ?? LogicalType.String, values, aliases));
    }
}
