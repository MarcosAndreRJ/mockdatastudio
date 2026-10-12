using MockDataStudio.Catalog;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Application;

public sealed record TemplateDetails(
    TemplateInfo Info, IReadOnlyList<TemplateFieldInfo> Fields, IReadOnlyList<string> Aliases,
    IReadOnlyList<string> SampleValues, bool IsComposite);

/// <summary>Consulta somente leitura ao catálogo de templates (S06, C3.3).</summary>
public sealed class TemplateBrowser(ITemplateRepository repository, ITemplateDataSource dataSource)
{
    public const int SampleSize = 40;

    public Task<IReadOnlyList<TemplateInfo>> ListAsync(string? text, CancellationToken ct) =>
        repository.ListAsync(new TemplateQuery(Text: string.IsNullOrWhiteSpace(text) ? null : text.Trim()), ct);

    public async Task<TemplateDetails?> GetDetailsAsync(string code, CancellationToken ct)
    {
        var info = await repository.GetAsync(code, ct);
        if (info is null)
        {
            return null;
        }

        var fields = await repository.GetFieldsAsync(code, ct);
        var aliases = (await repository.GetAllAliasesAsync(ct))
            .Where(a => a.TemplateCode == code)
            .Select(a => a.FieldCode is null ? a.Alias : $"{a.Alias} ({a.FieldCode})")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var set = await dataSource.LoadAsync(code, ct);
        var sample = new List<string>();
        for (var i = 0; i < Math.Min(SampleSize, set.Count); i++)
        {
            sample.Add(set.Kind == TemplateKind.Scalar
                ? set.GetScalar(i)
                : string.Join(" | ", set.FieldCodes.Select(f => set.GetField(i, f) ?? "NULL")));
        }

        return new TemplateDetails(info, fields, aliases, sample, set.Kind == TemplateKind.Composite);
    }
}
