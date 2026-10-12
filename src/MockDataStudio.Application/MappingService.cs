using MockDataStudio.Catalog;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Mapping;
using MockDataStudio.Projects;

namespace MockDataStudio.Application;

/// <summary>Mapeamento semântico coluna → gerador sobre o projeto aberto (C09 §2, docs/14).</summary>
public interface IMappingService
{
    /// <summary>
    /// Monta o contexto: schema do snapshot ativo, conhecimento do catálogo (templates, aliases, regras globais e do projeto)
    /// e vínculos existentes. Falha com PRJ-SCHEMA se o projeto não tem snapshot ativo.
    /// </summary>
    Task<Result<MappingContext>> BuildContextAsync(MdsProject project, CancellationToken ct);

    IReadOnlyList<MappingSuggestion> Suggest(MappingContext ctx, ObjectName table);

    /// <summary>Aplica as sugestões de confiança alta de todas as tabelas. Vínculos de origem User nunca são sobrescritos.</summary>
    MdsProject AutoBindAll(MdsProject project, MappingContext ctx);

    /// <summary>Grava o vínculo escolhido pelo usuário. Com <paramref name="learnAsRule"/>, registra o aprendizado no catálogo.</summary>
    Task<MdsProject> SetBindingAsync(MdsProject project, ColumnBinding binding, bool learnAsRule, CancellationToken ct);
}

public sealed class MappingService(
    ITemplateRepository templates,
    IMappingRuleRepository rules,
    IMappingFeedbackRepository feedback,
    IMappingEngine engine,
    INameNormalizer normalizer,
    IDigestSerializer digestSerializer,
    IClock clock) : IMappingService
{
    public async Task<Result<MappingContext>> BuildContextAsync(MdsProject project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        var snapshot = project.Snapshots.FirstOrDefault(s => s.Id == project.ActiveSnapshotId);
        if (snapshot is null)
        {
            return Result<MappingContext>.Fail(ErrorCodes.PrjSchema, "O projeto não tem snapshot ativo: capture ou importe a estrutura do banco primeiro.");
        }

        var schema = digestSerializer.Deserialize(snapshot.DigestJson);
        if (!schema.IsSuccess)
        {
            return Result<MappingContext>.Fail(schema.Error);
        }

        var knowledge = await LoadKnowledgeAsync(project, ct);
        return Result<MappingContext>.Ok(new MappingContext(schema.Value, knowledge, project.Bindings, new MappingOptions()));
    }

    public IReadOnlyList<MappingSuggestion> Suggest(MappingContext ctx, ObjectName table) => engine.SuggestTable(ctx, table);

    public MdsProject AutoBindAll(MdsProject project, MappingContext ctx)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ctx);

        var bindings = new Dictionary<string, ColumnBinding>(project.Bindings, StringComparer.Ordinal);
        foreach (var ns in ctx.Schema.Namespaces)
        {
            foreach (var table in ns.Tables)
            {
                foreach (var binding in engine.AutoBind(ctx, new ObjectName(ns.Name, table.Name)))
                {
                    var key = binding.Column.CanonicalId;
                    if (bindings.TryGetValue(key, out var existing) && existing.Origin == BindingOrigin.User)
                    {
                        continue;
                    }

                    bindings[key] = binding;
                }
            }
        }

        return project with { Bindings = bindings, ModifiedAt = clock.UtcNow };
    }

    public async Task<MdsProject> SetBindingAsync(MdsProject project, ColumnBinding binding, bool learnAsRule, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(binding);

        var chosen = binding with { Origin = BindingOrigin.User, State = BindingState.Active, ConfidenceLabel = "confirmed" };
        var bindings = new Dictionary<string, ColumnBinding>(project.Bindings, StringComparer.Ordinal)
        {
            [chosen.Column.CanonicalId] = chosen,
        };

        if (learnAsRule)
        {
            await LearnAsync(project, chosen, ct);
        }

        return project with { Bindings = bindings, ModifiedAt = clock.UtcNow };
    }

    // Aceite do usuário: reforça o par (coluna normalizada → alvo) e, para templates e geradores, grava uma regra aprendida.
    private async Task LearnAsync(MdsProject project, ColumnBinding binding, CancellationToken ct)
    {
        var (kind, targetRef) = TargetOf(binding.Generator);
        var compact = normalizer.Normalize(binding.Column.Column, binding.Column.Table.Name).Compact;

        await feedback.RecordAsync(compact, kind, targetRef, accepted: true, ct);

        var existing = project.Bindings.TryGetValue(binding.Column.CanonicalId, out var previous) ? previous : null;
        if (existing is { Origin: BindingOrigin.Auto or BindingOrigin.Rule } && TargetOf(existing.Generator) != (kind, targetRef))
        {
            // O usuário trocou a sugestão automática: registra a rejeição do alvo anterior.
            var (oldKind, oldRef) = TargetOf(existing.Generator);
            await feedback.RecordAsync(compact, oldKind, oldRef, accepted: false, ct);
        }

        var parameters = kind == RuleTargetKind.Generator ? binding.Generator.Parameters.ToJsonString() : null;
        await rules.UpsertAsync(new MappingRuleInfo(
            Id: 0, ColumnPattern: binding.Column.Column, TablePattern: null, Engine: null, TargetKind: kind, TargetRef: targetRef,
            ParametersJson: parameters, Scope: RuleScope.Global, Priority: 100, Source: RuleSource.Learned, Enabled: true), ct);
    }

    private static (RuleTargetKind Kind, string Ref) TargetOf(GeneratorSpec spec)
    {
        var parameters = spec.Parameters;
        return spec.GeneratorId switch
        {
            "catalog.template" => (RuleTargetKind.Template, parameters["template"]?.GetValue<string>() ?? spec.GeneratorId),
            "catalog.template.field" => (RuleTargetKind.TemplateField,
                $"{parameters["template"]?.GetValue<string>()}.{parameters["field"]?.GetValue<string>()}"),
            _ => (RuleTargetKind.Generator, spec.GeneratorId),
        };
    }

    private async Task<MappingKnowledge> LoadKnowledgeAsync(MdsProject project, CancellationToken ct)
    {
        var infos = await templates.ListAsync(new TemplateQuery(), ct);
        var knowledgeTemplates = new List<KnowledgeTemplate>(infos.Count);
        foreach (var info in infos)
        {
            var fields = await templates.GetFieldsAsync(info.Code, ct);
            knowledgeTemplates.Add(new KnowledgeTemplate(
                info.Code, info.Kind == MockDataStudio.Core.Templates.TemplateKind.Composite, info.LogicalType,
                fields.Select(f => new KnowledgeField(f.FieldCode, f.LogicalType, f.MaxLength, null)).ToList(),
                (int)Math.Min(info.ValueCount, int.MaxValue), null, null));
        }

        var aliases = (await templates.GetAllAliasesAsync(ct))
            .Select(a => new KnowledgeAlias(a.TemplateCode, a.FieldCode, a.Alias, a.Weight, a.ContextTablePattern))
            .ToList();

        var knowledgeRules = (await rules.ListAsync(ct))
            .Where(r => r.Enabled)
            .Select(r => new KnowledgeRule(r.Id, r.ColumnPattern, r.TablePattern, r.Engine, ToMapping(r.TargetKind),
                r.TargetRef, r.ParametersJson, r.Priority, r.Source.ToString()))
            .ToList();

        // Regras do próprio projeto (docs/22 §1): maior prioridade que as globais de mesmo padrão.
        var projectRules = project.ProjectRules ?? [];
        for (var i = 0; i < projectRules.Count; i++)
        {
            var rule = projectRules[i];
            if (Enum.TryParse<MappingTargetKind>(rule.TargetKind, ignoreCase: true, out var kind))
            {
                knowledgeRules.Add(new KnowledgeRule(-(i + 1), rule.ColumnPattern, rule.TablePattern, null, kind, rule.TargetRef,
                    rule.Parameters?.ToJsonString(), rule.Priority ?? 100, "Project"));
            }
        }

        return new MappingKnowledge(knowledgeTemplates, aliases, knowledgeRules, normalizer);
    }

    private static MappingTargetKind ToMapping(RuleTargetKind kind) => kind switch
    {
        RuleTargetKind.Template => MappingTargetKind.Template,
        RuleTargetKind.TemplateField => MappingTargetKind.TemplateField,
        RuleTargetKind.Preset => MappingTargetKind.Preset,
        _ => MappingTargetKind.Generator,
    };
}
