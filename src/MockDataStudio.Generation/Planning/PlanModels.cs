using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Generation.Planning;

// Modelos do planejamento relacional (C06). Puros: todo IO acontece antes, na coleta do TargetState.

/// Estado do destino coletado imediatamente antes do planejamento (ou vazio em modo offline).
public sealed record TargetState(
    DatabaseEngine Engine, string ServerVersion, string FingerprintHash, ProviderCapabilities Capabilities,
    IReadOnlyDictionary<string, long?> MaxIntegerKeys,      // "<tableId>.<column>" → MAX
    IReadOnlyDictionary<string, long?> SequenceNext,        // nome da sequence → próximo valor
    IReadOnlyDictionary<string, IReadOnlyList<TriggerModel>> InsertTriggers,
    bool IsOffline);

public sealed record PlanningInput(
    SchemaModel Schema,
    IReadOnlyDictionary<string, ColumnBinding> Bindings,
    GenerationProfile Profile,
    TargetState Target,
    string ReferenceFingerprintHash);

public enum DiagnosticSeverity { Info, Warning, Error }   // Error bloqueia

public sealed record PlanDiagnostic(DiagnosticSeverity Severity, string Code, string Message, string? ObjectPath, string? Suggestion);

public sealed record KeyColumnPlan(
    string Column, KeyStrategy Strategy, long? Start, long Step, bool NeedsIdentityOverride, string? SequenceToResync);

public sealed record DeferredFk(string ForeignKeyName, CycleBreakStrategy Strategy);

public sealed record TablePlan(
    ObjectName Table, long RowCount, IReadOnlyList<string> InsertColumns,
    IReadOnlyList<string> EvaluationOrder,
    IReadOnlyList<KeyColumnPlan> Keys, IReadOnlyList<DeferredFk> DeferredFks,
    IReadOnlyDictionary<string, RelationSettings> Relations, bool ParentDrivenRowCount);

public sealed record GenerationPlan(
    IReadOnlyList<PlanStep> Steps, IReadOnlyDictionary<ObjectName, TablePlan> Tables,
    IReadOnlyList<PlanDiagnostic> Diagnostics, string PlanHash, long EstimatedRows, long EstimatedBytes);

public sealed record PlanResult(GenerationPlan? Plan, IReadOnlyList<PlanDiagnostic> Diagnostics)
{
    public bool IsBlocked => Plan is null;
}

public interface IGenerationPlanner
{
    PlanResult Plan(PlanningInput input);
}
