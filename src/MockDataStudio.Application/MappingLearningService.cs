using MockDataStudio.Catalog;
using MockDataStudio.Core.Common;
using MockDataStudio.Mapping;

namespace MockDataStudio.Application;

// Presets de gerador e regras aprendidas (C8a.4, FR-059 e FR-064).
// Presets: sugestão quando o nome da coluna, normalizado, é igual ao código do preset (ex.: "cpf" → CPF).
// Regras aprendidas: "Lembrar para outros projetos" grava uma regra global pela chave compacta da coluna.
public sealed class MappingLearningService(
    IGeneratorPresetRepository presets,
    IMappingRuleRepository rules,
    IMappingFeedbackRepository feedback,
    NameNormalizer normalizer)
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
}
