using MockDataStudio.Catalog;
using MockDataStudio.Core.Common;
using MockDataStudio.Mapping;

namespace MockDataStudio.Application;

// Presets de gerador e regras aprendidas (C8a.4, FR-059 e FR-064).
// Presets: sugestão quando o nome da coluna, normalizado, é igual ao código do preset (ex.: "cpf" → CPF).
// Regras aprendidas: "Lembrar para outros projetos" grava uma regra global pela chave compacta da coluna.
public sealed partial class MappingLearningService(
    IGeneratorPresetRepository presets,
    IMappingRuleRepository rules,
    IMappingFeedbackRepository feedback,
    INameNormalizer normalizer)
{
    public async Task<IReadOnlyList<GeneratorPresetInfo>> SuggestPresetsAsync(string columnName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        var compact = normalizer.Normalize(columnName).Compact;
        return (await presets.ListAsync(ct))
            .Where(p => normalizer.Normalize(p.Code).Compact == compact)
            .OrderBy(p => p.Code, StringComparer.Ordinal)
            .ToList();
    }

    // Grava a aceitação como regra global (origem Learned) e como feedback, para o motor ponderar a decisão.
    public async Task<Result<long>> RememberAsync(string columnName, RuleTargetKind targetKind, string targetRef, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRef);
        var compact = normalizer.Normalize(columnName).Compact;

        var saved = await rules.UpsertAsync(new MappingRuleInfo(
            0, compact, null, null, targetKind, targetRef, null, RuleScope.Global, 100, RuleSource.Learned, true), ct);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        await feedback.RecordAsync(compact, targetKind, targetRef, accepted: true, ct);
        return saved;
    }

    public async Task<IReadOnlyList<MappingRuleInfo>> LearnedRulesAsync(CancellationToken ct) =>
        (await rules.ListAsync(ct))
        .Where(r => r.Source == RuleSource.Learned)
        .OrderBy(r => r.ColumnPattern, StringComparer.Ordinal)
        .ToList();

    public Task ForgetAsync(long ruleId, CancellationToken ct) => rules.DeleteAsync(ruleId, ct);

    // ---------- presets (FR-059) ----------

    public Task<IReadOnlyList<GeneratorPresetInfo>> ListPresetsAsync(CancellationToken ct) => presets.ListAsync(ct);

    // Salva um preset do usuário. Embutidos são somente leitura; código, gerador e parâmetros são validados antes de gravar.
    public async Task<Result<long>> SavePresetAsync(GeneratorPresetInfo preset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!PresetCode().IsMatch(preset.Code))
        {
            return Result<long>.Fail(ErrorCodes.CatImpCode, $"Código '{preset.Code}' inválido: use maiúsculas, dígitos e '_' (2 a 64).");
        }

        if (string.IsNullOrWhiteSpace(preset.Name) || string.IsNullOrWhiteSpace(preset.GeneratorId))
        {
            return Result<long>.Fail(ErrorCodes.CatImpCode, "Nome e gerador do preset são obrigatórios.");
        }

        if (preset.GeneratorVersion < 1)
        {
            return Result<long>.Fail(ErrorCodes.CatImpCode, "Versão do gerador deve ser 1 ou maior.");
        }

        if (!IsJsonObject(preset.ParametersJson))
        {
            return Result<long>.Fail(ErrorCodes.CatImpCode, "Parâmetros do preset devem ser um objeto JSON, ex.: {}.");
        }

        var existing = (await presets.ListAsync(ct)).FirstOrDefault(p => p.Code == preset.Code);
        if (existing is { IsBuiltIn: true })
        {
            return Result<long>.Fail(ErrorCodes.CatBuiltinReadOnly, $"Preset embutido '{preset.Code}' é somente leitura.");
        }

        return await presets.UpsertAsync(preset with { IsBuiltIn = false }, ct);
    }

    public async Task<Result<bool>> DeletePresetAsync(string code, CancellationToken ct)
    {
        var existing = (await presets.ListAsync(ct)).FirstOrDefault(p => p.Code == code);
        if (existing is { IsBuiltIn: true })
        {
            return Result<bool>.Fail(ErrorCodes.CatBuiltinReadOnly, $"Preset embutido '{code}' não pode ser excluído.");
        }

        return await presets.DeleteAsync(code, ct);
    }

    private static bool IsJsonObject(string? json)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) is System.Text.Json.Nodes.JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z0-9_]{2,64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PresetCode();
}
