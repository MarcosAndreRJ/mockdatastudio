using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Mapping;

// Motor de mapeamento semântico (docs/14 §4–§7). Determinístico: mesma entrada, mesma saída (T-MAP-004).
public sealed partial class MappingEngine(IStructuralBinder binder) : IMappingEngine
{
    private const double AmbiguityThreshold = 60;
    private const double MinimumCandidateScore = 40;

    public MappingSuggestion SuggestColumn(MappingContext ctx, ObjectName table, ColumnModel column)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var tableModel = ctx.Schema.FindTable(table)
            ?? throw new ArgumentException($"Tabela '{table.CanonicalId}' não existe no schema.", nameof(table));
        var columnRef = new ColumnRef(table, column.Name);

        if (ctx.ExistingBindings.TryGetValue(columnRef.CanonicalId, out var existing) && existing.Origin == BindingOrigin.User)
        {
            return Single(columnRef, existing.Generator, EvidenceKind.RuleMatch, 100, "Vínculo definido pelo usuário", Confidence.Confirmed);
        }

        var structural = binder.TryBind(ctx.Schema, table, tableModel, column);
        if (structural is not null)
        {
            var kind = structural.Generator.GeneratorId switch
            {
                "core.skip" => EvidenceKind.StructuralGenerated,
                "rel.fk" => EvidenceKind.StructuralFk,
                "rel.key" when column.Identity is not null => EvidenceKind.StructuralIdentity,
                _ => EvidenceKind.StructuralPk,
            };
            return Single(columnRef, structural.Generator, kind, 100, "Binding estrutural do schema", Confidence.Confirmed);
        }

        var norm = ctx.Knowledge.Normalizer.Normalize(column.Name, table.Name);
        var candidates = new CandidateSet();

        CollectRules(ctx, table, norm, candidates);
        CollectTemplateCodes(ctx, norm, candidates);
        CollectAliases(ctx, table, norm, candidates);

        var scored = candidates.Build(ctx, table, tableModel, column, norm);
        ApplyFallbacks(scored, column);

        var ordered = scored
            .Where(c => c.Score >= MinimumCandidateScore || c.Forced)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Rank)
            .ThenByDescending(c => c.ValueCount)
            .ThenBy(c => c.Target.Ref, StringComparer.Ordinal)
            .ThenBy(c => c.Target.FieldCode ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var result = new List<MappingCandidate>(ordered.Count);
        for (var i = 0; i < ordered.Count && i < ctx.Options.MaxCandidates; i++)
        {
            var candidate = ordered[i];
            var margin = i == 0
                ? (ordered.Count > 1 ? candidate.Score - ordered[1].Score : double.PositiveInfinity)
                : candidate.Score - ordered[0].Score;
            var confidence = candidate.ForcedConfidence ?? Band(candidate, margin, ctx.Options);
            result.Add(new MappingCandidate(candidate.Target, candidate.Score, confidence, candidate.Evidence));
        }

        var ambiguous = IsAmbiguous(ordered);
        return new MappingSuggestion(columnRef, result, ambiguous);
    }

    public IReadOnlyList<MappingSuggestion> SuggestTable(MappingContext ctx, ObjectName table)
    {
        var tableModel = ctx.Schema.FindTable(table)
            ?? throw new ArgumentException($"Tabela '{table.CanonicalId}' não existe no schema.", nameof(table));
        return tableModel.Columns.Select(c => SuggestColumn(ctx, table, c)).ToList();
    }

    public IReadOnlyList<ColumnBinding> AutoBind(MappingContext ctx, ObjectName table)
    {
        var tableModel = ctx.Schema.FindTable(table)
            ?? throw new ArgumentException($"Tabela '{table.CanonicalId}' não existe no schema.", nameof(table));
        var bindings = new List<ColumnBinding>();

        foreach (var column in tableModel.Columns)
        {
            var columnRef = new ColumnRef(table, column.Name);
            if (ctx.ExistingBindings.TryGetValue(columnRef.CanonicalId, out var existing) && existing.Origin == BindingOrigin.User)
            {
                continue;
            }

            var suggestion = SuggestColumn(ctx, table, column);
            var best = suggestion.Best;
            if (best is null || suggestion.IsAmbiguous)
            {
                continue;
            }

            if (best.Confidence is not (Confidence.High or Confidence.Confirmed))
            {
                continue;
            }

            var origin = best.Confidence == Confidence.Confirmed && best.Evidence.Any(e => e.Kind is EvidenceKind.StructuralPk or EvidenceKind.StructuralFk
                or EvidenceKind.StructuralIdentity or EvidenceKind.StructuralGenerated)
                ? BindingOrigin.Structural
                : BindingOrigin.Auto;
            bindings.Add(new ColumnBinding(columnRef, best.Target.Spec, NullRate: 0, Origin: origin,
                ColumnFingerprint: StructuralBinder.Fingerprint(column), ConfidenceLabel: best.Confidence.ToString()));
        }

        return bindings;
    }

    private static MappingSuggestion Single(ColumnRef column, GeneratorSpec spec, EvidenceKind kind, double score, string description, Confidence confidence)
    {
        var target = new MappingTarget(MappingTargetKind.Generator, spec.GeneratorId, null, spec);
        var candidate = new MappingCandidate(target, score, confidence, [new Evidence(kind, score, description)]);
        return new MappingSuggestion(column, [candidate], IsAmbiguous: false);
    }

    private static void CollectRules(MappingContext ctx, ObjectName table, NormalizedName norm, CandidateSet set)
    {
        foreach (var rule in ctx.Knowledge.Rules)
        {
            if (rule.Engine is { } engine && engine != ctx.Schema.Engine)
            {
                continue;
            }

            if (rule.TablePattern is not null && !Glob(rule.TablePattern, table.Name))
            {
                continue;
            }

            if (!Glob(rule.ColumnPattern, norm.Compact) && !Glob(rule.ColumnPattern, norm.ExpandedCompact))
            {
                continue;
            }

            var user = string.Equals(rule.Source, "user", StringComparison.OrdinalIgnoreCase);
            var learned = string.Equals(rule.Source, "learned", StringComparison.OrdinalIgnoreCase);
            var score = learned ? 92 : Math.Clamp(95 + (rule.Priority - 100) / 20.0, 90, 99);
            var target = RuleTarget(rule);
            set.Add(target, score, EvidenceKind.RuleMatch, $"Regra '{rule.ColumnPattern}' (prioridade {rule.Priority})", Rank: 0,
                Forced: user ? Confidence.Confirmed : null);
        }
    }

    private static void CollectTemplateCodes(MappingContext ctx, NormalizedName norm, CandidateSet set)
    {
        foreach (var template in ctx.Knowledge.Templates)
        {
            var name = ctx.Knowledge.TemplateName(template.Code);
            var matches = norm.Compact == name.Compact || norm.ExpandedCompact == name.ExpandedCompact
                || (norm.CoreTokens.Count == 1 && norm.CoreTokens[0] == name.Compact);
            if (!matches)
            {
                continue;
            }

            set.Add(TemplateTarget(template.Code), 90, EvidenceKind.ExactTemplateCode, $"Código do template '{template.Code}'", Rank: 1);
        }
    }

    private static void CollectAliases(MappingContext ctx, ObjectName table, NormalizedName norm, CandidateSet set)
    {
        var seen = new HashSet<IndexedAlias>();
        var pool = new List<IndexedAlias>();
        pool.AddRange(ctx.Knowledge.ByCompact(norm.Compact));
        pool.AddRange(ctx.Knowledge.ByExpanded(norm.ExpandedCompact));
        foreach (var token in norm.CoreTokens.Distinct(StringComparer.Ordinal))
        {
            pool.AddRange(ctx.Knowledge.ByCoreToken(token));
        }

        foreach (var entry in pool)
        {
            if (!seen.Add(entry))
            {
                continue;
            }

            var alias = entry.Alias;
            var target = AliasTarget(alias);
            var weight = alias.Weight;

            if (entry.Normalized.Compact == norm.Compact)
            {
                set.Add(target, Math.Min(95, 88 * weight), EvidenceKind.ExactAlias, $"Alias '{alias.Alias}' exato", Rank: 2, AliasInfo: alias);
            }
            else if (entry.Normalized.ExpandedCompact == norm.ExpandedCompact)
            {
                set.Add(target, 82 * weight, EvidenceKind.NormalizedAlias, $"Alias '{alias.Alias}' normalizado", Rank: 2, AliasInfo: alias);
            }
            else if (norm.CoreTokens.Contains(entry.Normalized.ExpandedCompact, StringComparer.Ordinal))
            {
                set.Add(target, 70 * weight, EvidenceKind.CoreTokenAlias, $"Alias '{alias.Alias}' coincide com token de núcleo", Rank: 2, AliasInfo: alias);
            }

            var overlap = Jaccard(norm.CoreTokens, entry.Normalized.CoreTokens);
            if (overlap >= 0.5)
            {
                set.Add(target, 40 + 30 * overlap, EvidenceKind.TokenOverlap, $"Sobreposição de tokens com '{alias.Alias}' ({overlap:0.00})", Rank: 3, AliasInfo: alias);
            }
        }
    }

    private static void ApplyFallbacks(List<ScoredCandidate> scored, ColumnModel column)
    {
        if (scored.Any(c => c.Score >= AmbiguityThreshold))
        {
            return;
        }

        if (column.Default is { Kind: DefaultKind.Function } defaultValue && !column.IsNullable
            && IsTimeFunction(defaultValue.RawExpression))
        {
            scored.Add(ScoredCandidate.Fallback(
                new MappingTarget(MappingTargetKind.Generator, "core.date.relative", null,
                    new GeneratorSpec("core.date.relative", 1, new JsonObject { ["from"] = "-30d", ["to"] = "0d" })),
                85, Confidence.High, EvidenceKind.TypeFallback, "Default de função de tempo em coluna NOT NULL"));
            return;
        }

        if (column.Type.LogicalType == LogicalType.Boolean)
        {
            scored.Add(ScoredCandidate.Fallback(
                new MappingTarget(MappingTargetKind.Generator, "core.bool", null,
                    new GeneratorSpec("core.bool", 1, new JsonObject { ["p"] = 0.5 })),
                60, Confidence.Medium, EvidenceKind.TypeFallback, "Tipo booleano sem candidato semântico"));
            return;
        }

        var fallback = FallbackSpec(column.Type.LogicalType);
        if (fallback is not null)
        {
            scored.Add(ScoredCandidate.Fallback(
                new MappingTarget(MappingTargetKind.Generator, fallback.GeneratorId, null, fallback),
                MinimumCandidateScore, Confidence.Low, EvidenceKind.TypeFallback, $"Fallback por tipo lógico {column.Type.LogicalType}"));
        }
    }

    // Fallback por tipo lógico (docs/14 §7). Other sem gerador: None.
    private static GeneratorSpec? FallbackSpec(LogicalType type) => type switch
    {
        LogicalType.Integer => new GeneratorSpec("core.random.int", 1, new JsonObject { ["min"] = 1, ["max"] = 100000 }),
        LogicalType.Decimal => new GeneratorSpec("core.random.decimal", 1, new JsonObject { ["min"] = 0, ["max"] = 100000 }),
        LogicalType.String => new GeneratorSpec("core.format", 1, new JsonObject { ["pattern"] = "{lorem(1..3)}" }),
        LogicalType.Text => new GeneratorSpec("core.lorem.paragraph", 1, new JsonObject()),
        LogicalType.Date or LogicalType.DateTime => new GeneratorSpec("core.date.range", 1, new JsonObject { ["from"] = "-5y", ["to"] = "0d" }),
        LogicalType.Uuid => new GeneratorSpec("core.uuid", 1, new JsonObject { ["version"] = 4 }),
        LogicalType.Json => new GeneratorSpec("core.fixed", 1, new JsonObject { ["value"] = "{}" }),
        LogicalType.Enum => new GeneratorSpec("core.list.random", 1, new JsonObject()),
        LogicalType.Binary => new GeneratorSpec("core.random.bytes", 1, new JsonObject { ["length"] = 16 }),
        _ => null,
    };

    private static bool IsTimeFunction(string raw)
    {
        var upper = raw.ToUpperInvariant();
        return upper.Contains("CURRENT_", StringComparison.Ordinal) || upper.Contains("NOW(", StringComparison.Ordinal);
    }

    private static Confidence Band(ScoredCandidate candidate, double margin, MappingOptions options)
    {
        if (candidate.Score >= options.AutoApplyMinScore && margin >= options.AmbiguityMargin)
        {
            return Confidence.High;
        }

        if (candidate.Score >= AmbiguityThreshold && (candidate.Score < options.AutoApplyMinScore || margin < options.AmbiguityMargin))
        {
            return Confidence.Medium;
        }

        return candidate.Score >= 40 ? Confidence.Low : Confidence.None;
    }

    private static bool IsAmbiguous(List<ScoredCandidate> ordered)
    {
        var strong = ordered.Where(c => c.Score >= AmbiguityThreshold).Take(2).ToList();
        return strong.Count == 2 && strong[0].Score - strong[1].Score < 10;
    }

    private static double Jaccard(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var sa = new HashSet<string>(a, StringComparer.Ordinal);
        var sb = new HashSet<string>(b, StringComparer.Ordinal);
        if (sa.Count == 0 || sb.Count == 0)
        {
            return 0;
        }

        var inter = sa.Intersect(sb, StringComparer.Ordinal).Count();
        var union = sa.Union(sb, StringComparer.Ordinal).Count();
        return (double)inter / union;
    }

    private static bool Glob(string pattern, string text)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static MappingTarget TemplateTarget(string code) =>
        new(MappingTargetKind.Template, code, null, new GeneratorSpec("core.template", 1, new JsonObject { ["template"] = code }));

    internal static MappingTarget AliasTarget(KnowledgeAlias alias) =>
        alias.FieldCode is null
            ? TemplateTarget(alias.TemplateCode)
            : new MappingTarget(MappingTargetKind.TemplateField, alias.TemplateCode, alias.FieldCode,
                new GeneratorSpec("core.template.field", 1, new JsonObject { ["template"] = alias.TemplateCode, ["field"] = alias.FieldCode }));

    private static MappingTarget RuleTarget(KnowledgeRule rule)
    {
        switch (rule.TargetKind)
        {
            case MappingTargetKind.Template:
                return TemplateTarget(rule.TargetRef);
            case MappingTargetKind.TemplateField:
                var parts = rule.TargetRef.Split('.', 2);
                var template = parts[0];
                var field = parts.Length > 1 ? parts[1] : string.Empty;
                return new MappingTarget(MappingTargetKind.TemplateField, template, field,
                    new GeneratorSpec("core.template.field", 1, new JsonObject { ["template"] = template, ["field"] = field }));
            case MappingTargetKind.Preset:
                return new MappingTarget(MappingTargetKind.Preset, rule.TargetRef, null,
                    new GeneratorSpec("core.preset", 1, new JsonObject { ["preset"] = rule.TargetRef }));
            default:
                var parameters = string.IsNullOrWhiteSpace(rule.ParametersJson)
                    ? new JsonObject()
                    : (JsonNode.Parse(rule.ParametersJson) as JsonObject ?? new JsonObject());
                return new MappingTarget(MappingTargetKind.Generator, rule.TargetRef, null,
                    new GeneratorSpec(rule.TargetRef, 1, parameters));
        }
    }
}
