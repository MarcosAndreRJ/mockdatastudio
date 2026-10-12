# C04 — Contratos de mapeamento (`MockDataStudio.Mapping`)

```csharp
namespace MockDataStudio.Mapping;

public enum Confidence { None = 0, Low = 1, Medium = 2, High = 3, Confirmed = 4 }

public sealed record NormalizedName(string Original, string Compact, string ExpandedCompact,
    IReadOnlyList<string> Tokens, IReadOnlyList<string> CoreTokens, int? Ordinal);
// "nro_cep": Compact "NROCEP"; ExpandedCompact "NUMCEP"; Tokens ["NUM","CEP"]; CoreTokens ["CEP"]; Ordinal null
// Algoritmo exato em 14 §2.

public interface INameNormalizer
{
    /// tableName opcional: usado para remover o nome da tabela dos CoreTokens (14 §2 passo 6).
    NormalizedName Normalize(string identifier, string? tableName = null);
}

public enum EvidenceKind { ExactTemplateCode, ExactAlias, NormalizedAlias, CoreTokenAlias, TokenOverlap,
                           TableContext, TableContextMismatch, TypeCompatible, TypeCoercible, TypeIncompatible,
                           LengthFits, LengthTooSmall, UniqueCapacity, RuleMatch, HistoryMatch,
                           StructuralPk, StructuralFk, StructuralIdentity, StructuralGenerated, CheckConstraintHint, TypeFallback }
public sealed record Evidence(EvidenceKind Kind, double Points, string Description);

public enum MappingTargetKind { Template, TemplateField, Preset, Generator }
public sealed record MappingTarget(MappingTargetKind Kind, string Ref, string? FieldCode, GeneratorSpec Spec);

public sealed record MappingCandidate(MappingTarget Target, double Score, Confidence Confidence, IReadOnlyList<Evidence> Evidence);

public sealed record MappingSuggestion(ColumnRef Column, IReadOnlyList<MappingCandidate> Candidates, bool IsAmbiguous)
{
    public MappingCandidate? Best => Candidates.Count > 0 ? Candidates[0] : null;
}

public sealed record MappingContext(
    SchemaModel Schema, MappingKnowledge Knowledge,
    IReadOnlyDictionary<string, ColumnBinding> ExistingBindings, // por ColumnRef.CanonicalId
    MappingOptions Options);

public sealed record MappingOptions(int MaxCandidates = 5, double AutoApplyMinScore = 80, double AmbiguityMargin = 10);

/// Snapshot imutável do conhecimento do catálogo (aliases, templates, regras), carregado uma vez.
/// Construído pela Application a partir dos repositórios do Catalog (Mapping NÃO referencia Catalog — AR-02).
public sealed class MappingKnowledge
{
    public MappingKnowledge(IEnumerable<KnowledgeTemplate> templates, IEnumerable<KnowledgeAlias> aliases,
                            IEnumerable<KnowledgeRule> rules, INameNormalizer normalizer);
    // índices internos: por NormalizedAlias (Compact), por CoreToken, por TemplateCode
}
public sealed record KnowledgeTemplate(string Code, bool IsComposite, LogicalType? LogicalType,
    IReadOnlyList<KnowledgeField> Fields, int ValueCount, int? MaxValueLength, int? P95ValueLength);
public sealed record KnowledgeField(string FieldCode, LogicalType LogicalType, int? MaxLength, int? P95ValueLength);
// MaxValueLength/P95ValueLength: comprimento (caracteres) máximo e percentil 95 dos valores — usados em LengthFits/LengthTooSmall (14 §5).
public sealed record KnowledgeAlias(string TemplateCode, string? FieldCode, string Alias, double Weight, string? ContextTablePattern);
public sealed record KnowledgeRule(long Id, string ColumnPattern, string? TablePattern, DatabaseEngine? Engine,
    MappingTargetKind TargetKind, string TargetRef, string? ParametersJson, int Priority, string Source);

public interface IMappingEngine
{
    MappingSuggestion SuggestColumn(MappingContext ctx, ObjectName table, ColumnModel column);
    IReadOnlyList<MappingSuggestion> SuggestTable(MappingContext ctx, ObjectName table);
    /// Aplica a melhor sugestão somente quando Confidence >= High e !IsAmbiguous; nunca sobrescreve Origin=User.
    IReadOnlyList<ColumnBinding> AutoBind(MappingContext ctx, ObjectName table);
}

public interface IStructuralBinder
{
    /// Retorna binding obrigatório para PK identity, colunas geradas, FKs; null se a coluna é semântica.
    ColumnBinding? TryBind(SchemaModel schema, ObjectName table, TableModel tableModel, ColumnModel column);
}
```

> **Nota de dependência:** `ColumnBinding` e `GeneratorSpec` são definidos em **`MockDataStudio.Core.Bindings`** (contrato em [C05 §1](C05-generation.md)), pois são consumidos por Mapping, Generation e Projects — que só podem referenciar Core (AR-02).
