using System.Text.RegularExpressions;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Mapping;

// Candidato pontuado (14 §5). Score é a base do maior componente de casamento mais os ajustes somados.
internal sealed class ScoredCandidate
{
    public required MappingTarget Target { get; init; }

    public double Score { get; set; }

    public int Rank { get; init; }

    public int ValueCount { get; init; }

    public List<Evidence> Evidence { get; init; } = [];

    public bool Forced { get; init; }

    public Confidence? ForcedConfidence { get; init; }

    public static ScoredCandidate Fallback(MappingTarget target, double score, Confidence confidence, EvidenceKind kind, string description) =>
        new()
        {
            Target = target,
            Score = score,
            Rank = 9,
            Forced = true,
            ForcedConfidence = confidence,
            Evidence = [new Evidence(kind, score, description)],
        };
}

// Acumula as evidências de casamento por alvo e calcula os ajustes (14 §5) com o conhecimento carregado.
internal sealed class CandidateSet
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public required MappingTarget Target { get; init; }

        public double Base { get; set; }

        public EvidenceKind BaseKind { get; set; }

        public string BaseDescription { get; set; } = string.Empty;

        public int Rank { get; set; }

        public KnowledgeAlias? Alias { get; set; }

        public Confidence? Forced { get; set; }
    }

    // Casamento de uma evidência base. Para cada alvo vale o maior componente (14 §5: base = máximo).
    public void Add(MappingTarget target, double score, EvidenceKind kind, string description, int Rank,
        Confidence? Forced = null, KnowledgeAlias? AliasInfo = null)
    {
        var key = target.Kind + "|" + target.Ref + "|" + (target.FieldCode ?? string.Empty);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry { Target = target };
            _entries[key] = entry;
        }

        if (entry.Forced is null && Forced is not null)
        {
            entry.Forced = Forced;
        }

        if (score > entry.Base || entry.BaseDescription.Length == 0)
        {
            entry.Base = score;
            entry.BaseKind = kind;
            entry.BaseDescription = description;
            entry.Rank = Rank;
            entry.Alias = AliasInfo;
        }
    }

    public List<ScoredCandidate> Build(MappingContext ctx, ObjectName table, TableModel tableModel, ColumnModel column, NormalizedName norm)
    {
        var result = new List<ScoredCandidate>(_entries.Count);
        var tableNorm = ctx.Knowledge.Normalizer.Normalize(table.Name);

        foreach (var entry in _entries.Values)
        {
            var target = entry.Target;
            var adjustments = new List<Evidence>();
            var valueCount = 0;
            int? maxLength = null;
            int? p95 = null;
            LogicalType? logical = null;
            var composite = false;
            KnowledgeTemplate? template = null;

            if (target.Kind is MappingTargetKind.Template or MappingTargetKind.TemplateField
                && ctx.Knowledge.TryGetTemplate(target.Ref, out var tmpl))
            {
                template = tmpl;
                valueCount = tmpl.ValueCount;
                composite = tmpl.IsComposite;

                if (target.Kind == MappingTargetKind.TemplateField)
                {
                    var field = tmpl.Fields.FirstOrDefault(f => string.Equals(f.FieldCode, target.FieldCode, StringComparison.Ordinal));
                    logical = field?.LogicalType;
                    maxLength = field?.MaxLength;
                    p95 = field?.P95ValueLength;
                }
                else
                {
                    logical = tmpl.LogicalType;
                    maxLength = tmpl.MaxValueLength;
                    p95 = tmpl.P95ValueLength;
                }
            }

            // Tipo (14 §6): compatível +5, coercível -5, incompatível -40. Composto não tem tipo próprio.
            if (!composite && logical is { } templateType)
            {
                var mark = TypeMark(templateType, column.Type.LogicalType);
                switch (mark)
                {
                    case '+':
                        adjustments.Add(new Evidence(EvidenceKind.TypeCompatible, 5, $"Tipo {templateType} compatível com {column.Type.LogicalType}"));
                        break;
                    case '~':
                        adjustments.Add(new Evidence(EvidenceKind.TypeCoercible, -5, $"Tipo {templateType} coercível em {column.Type.LogicalType}"));
                        break;
                    case '-':
                        adjustments.Add(new Evidence(EvidenceKind.TypeIncompatible, -40, $"Tipo {templateType} incompatível com {column.Type.LogicalType}"));
                        break;
                }
            }

            // Comprimento: caber no alvo +3; coluna menor que o percentil 95 dos valores -25 (truncaria).
            if (column.Type.Length is { } columnLength)
            {
                if (maxLength is { } max && max <= columnLength)
                {
                    adjustments.Add(new Evidence(EvidenceKind.LengthFits, 3, $"Valores de até {max} caracteres cabem em {columnLength}"));
                }

                if (p95 is { } percentile && columnLength < percentile)
                {
                    adjustments.Add(new Evidence(EvidenceKind.LengthTooSmall, -25, $"Coluna de {columnLength} caracteres menor que o P95 ({percentile})"));
                }
            }

            // Contexto de tabela (14 §5): alias com contexto que casa +8; que não casa -15;
            // campo de template composto cujo nome bate com a tabela +8.
            if (entry.Alias?.ContextTablePattern is { } pattern)
            {
                adjustments.Add(Glob(pattern, table.Name)
                    ? new Evidence(EvidenceKind.TableContext, 8, $"Contexto '{pattern}' casa a tabela")
                    : new Evidence(EvidenceKind.TableContextMismatch, -15, $"Contexto '{pattern}' não casa a tabela"));
            }

            if (target.Kind == MappingTargetKind.TemplateField && composite && TableNamesTemplate(tableNorm, target.Ref, ctx))
            {
                adjustments.Add(new Evidence(EvidenceKind.TableContext, 8, $"Tabela corresponde ao template composto '{target.Ref}'"));
            }

            var score = Math.Clamp(entry.Base + adjustments.Sum(e => e.Points), 0, 100);
            var evidence = new List<Evidence> { new(entry.BaseKind, entry.Base, entry.BaseDescription) };
            evidence.AddRange(adjustments);

            result.Add(new ScoredCandidate
            {
                Target = target,
                Score = score,
                Rank = entry.Rank,
                ValueCount = valueCount,
                Evidence = evidence,
                ForcedConfidence = entry.Forced,
                Forced = entry.Forced is not null,
            });
        }

        return result;
    }

    // Matriz de compatibilidade de tipos (14 §6): linha = tipo do template; coluna = tipo lógico da coluna.
    // '+' compatível, '~' coercível, '-' incompatível, null = sem regra (ex.: Binary, Interval, Other).
    internal static char? TypeMark(LogicalType templateType, LogicalType columnType)
    {
        var row = templateType switch
        {
            LogicalType.Boolean => 0,
            LogicalType.Integer => 1,
            LogicalType.Decimal or LogicalType.Float => 2,
            LogicalType.String or LogicalType.Text or LogicalType.Json or LogicalType.Uuid or LogicalType.Enum => 3,
            LogicalType.Date or LogicalType.DateTime or LogicalType.DateTimeTz or LogicalType.Time => 4,
            _ => -1,
        };
        var col = columnType switch
        {
            LogicalType.Boolean => 0,
            LogicalType.Integer => 1,
            LogicalType.Decimal => 2,
            LogicalType.Float => 3,
            LogicalType.String or LogicalType.Enum => 4,
            LogicalType.Text => 5,
            LogicalType.Date => 6,
            LogicalType.DateTime or LogicalType.DateTimeTz or LogicalType.Time => 7,
            LogicalType.Uuid => 8,
            LogicalType.Json => 9,
            _ => -1,
        };
        if (row < 0 || col < 0)
        {
            return null;
        }

        return Matrix[row, col];
    }

    //                       Bool Int  Dec  Flt  Str  Txt  Date DT   Uuid Json
    private static readonly char[,] Matrix =
    {
        /* boolean */ { '+', '~', '-', '-', '~', '~', '-', '-', '-', '-' },
        /* integer */ { '~', '+', '+', '+', '~', '~', '-', '-', '-', '-' },
        /* decimal */ { '-', '~', '+', '+', '~', '~', '-', '-', '-', '-' },
        /* string  */ { '~', '~', '~', '~', '+', '+', '~', '~', '~', '~' },
        /* date    */ { '-', '-', '-', '-', '~', '~', '+', '+', '-', '-' },
    };

    private static bool TableNamesTemplate(NormalizedName tableNorm, string templateCode, MappingContext ctx)
    {
        var name = ctx.Knowledge.TemplateName(templateCode);
        return tableNorm.Compact == name.Compact || tableNorm.ExpandedCompact == name.ExpandedCompact
            || tableNorm.CoreTokens.Contains(name.Compact, StringComparer.Ordinal);
    }

    private static bool Glob(string pattern, string text)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
