# C06 — Contratos de planejamento e execução (`MockDataStudio.Generation.Planning` / `.Engine`)

> **Localização:** os records de configuração puros (`KeyStrategy`, `ReferenceSourceKind`, `CardinalityMode`, `DistributionKind`, `CycleBreakStrategy`, `OutputKind`, `DistributionSpec`, `CardinalitySpec`, `RelationSettings`, `TableSettings`, `ValidationSettings`, `OutputSettings`, `DirectTransactionMode`, `GenerationProfile`) ficam em **`MockDataStudio.Core.Profiles`**, pois são persistidos por Projects (que referencia apenas Core — AR-02). Os demais tipos deste arquivo ficam em `MockDataStudio.Generation.Planning`.

```csharp
namespace MockDataStudio.Core.Profiles;

public enum KeyStrategy { Explicit, Captured, NaturalKeyLookup, DatabaseDefault }
public enum ReferenceSourceKind { Generated, Existing, Fixed, Dataset, Query }
public enum CardinalityMode { ChildDriven, ParentDriven }
public enum DistributionKind { Uniform, Zipf, Weighted, Range, Poisson }
public enum CycleBreakStrategy { NullThenUpdate, DeferredConstraint, DisableChecks }
public enum OutputKind { SqlScript, DirectInsert, Csv, JsonLines }

public sealed record DistributionSpec(DistributionKind Kind, double? ZipfExponent = null, int? Min = null, int? Max = null,
    double? Lambda = null, IReadOnlyList<double>? Weights = null);

public sealed record CardinalitySpec(CardinalityMode Mode, DistributionSpec Distribution, bool OneToOne = false);

public sealed record RelationSettings(
    string ForeignKeyName, ReferenceSourceKind Source, CardinalitySpec Cardinality,
    IReadOnlyList<JsonArray>? FixedValues = null,   // tuplas para Fixed
    string? DatasetPath = null, string? Query = null,
    long ExistingLimit = 100_000, double NullRate = 0,
    CycleBreakStrategy? CycleBreak = null,          // null = automático
    double RootRatio = 0.1, int MaxDepth = 6);      // somente autorreferência (16 §4)

public sealed record TableSettings(bool Include, long RowCount, KeyStrategy? KeyStrategy = null,
    long? KeyStartOverride = null, IReadOnlyDictionary<string, RelationSettings>? Relations = null);

public sealed record ValidationSettings(bool TrialInsert = false, int TrialRows = 50, bool VerifyReferencesInTarget = true);

public sealed record OutputSettings(
    OutputKind Kind, string? Directory = null, string? FileNamePattern = "{project}_{profile}_{yyyyMMdd_HHmmss}",
    long SegmentMaxBytes = 50L * 1024 * 1024, int RowsPerStatement = 0 /*0 = dialeto*/, int BatchSize = 1000,
    DirectTransactionMode TransactionMode = DirectTransactionMode.PerBatch,
    bool IncludeCleanupScript = true, bool ResyncSequences = true, string Encoding = "utf-8",
    bool EmitGuards = true,                 // comandos/comentários de verificação no script (16 §5.2, §9)
    bool AllowDisableFkChecks = false,      // opt-in para CycleBreakStrategy.DisableChecks (16 §3)
    bool QualifyNamespace = false,          // MySQL: prefixar database (18 §2)
    bool QuoteAllIdentifiers = true, bool IncludeTransaction = true,
    bool WriteBom = false, string LineEnding = "\n", string CsvSeparator = ",");
public enum DirectTransactionMode { PerBatch, Single }

public enum ReferenceInstantMode { Fixed, Now }   // Now → reprodutibilidade TimeBound (15 §4.3)

public sealed record GenerationProfile(
    string Name, ulong Seed, DateTimeOffset ReferenceInstant, ReferenceInstantMode ReferenceInstantMode, string TimeZoneId,
    IReadOnlyDictionary<string, TableSettings> Tables,   // por ObjectName.CanonicalId
    KeyStrategy DefaultKeyStrategy, OutputSettings Output, ValidationSettings Validation,
    long KeyOffset = 0);                                  // folga somada ao MAX(pk) do destino
```

```csharp
namespace MockDataStudio.Generation.Planning;

/// Estado do destino coletado imediatamente antes do planejamento (ou vazio em modo offline).
public sealed record TargetState(
    DatabaseEngine Engine, string ServerVersion, string FingerprintHash, ProviderCapabilities Capabilities,
    IReadOnlyDictionary<string, long?> MaxIntegerKeys,     // "<tableId>.<column>" → MAX
    IReadOnlyDictionary<string, long?> SequenceNext,
    IReadOnlyDictionary<string, IReadOnlyList<TriggerModel>> InsertTriggers,
    bool IsOffline);

public sealed record PlanningInput(SchemaModel Schema, IReadOnlyDictionary<string, ColumnBinding> Bindings,
    GenerationProfile Profile, TargetState Target, string ReferenceFingerprintHash /* banco de onde vêm 'Existing' */);

public enum DiagnosticSeverity { Info, Warning, Error }   // Error bloqueia
public sealed record PlanDiagnostic(DiagnosticSeverity Severity, string Code, string Message, string? ObjectPath, string? Suggestion);

public sealed record KeyColumnPlan(string Column, KeyStrategy Strategy, long? Start, long Step, bool NeedsIdentityOverride, string? SequenceToResync);
public sealed record DeferredFk(string ForeignKeyName, CycleBreakStrategy Strategy);

public sealed record TablePlan(
    ObjectName Table, long RowCount, IReadOnlyList<string> InsertColumns,
    IReadOnlyList<string> EvaluationOrder,                  // ordem topológica intra-linha
    IReadOnlyList<KeyColumnPlan> Keys, IReadOnlyList<DeferredFk> DeferredFks,
    IReadOnlyDictionary<string, RelationSettings> Relations, bool ParentDrivenRowCount);

public abstract record PlanStep(int Order);
public sealed record SessionSetupStep(int Order, IReadOnlyList<string> Statements) : PlanStep(Order);
public sealed record InsertTableStep(int Order, ObjectName Table) : PlanStep(Order);
public sealed record DeferredUpdateStep(int Order, ObjectName Table, string ForeignKeyName) : PlanStep(Order);
public sealed record ResyncSequenceStep(int Order, ObjectName Table, string Column, string? Sequence) : PlanStep(Order);
public sealed record SessionTeardownStep(int Order, IReadOnlyList<string> Statements) : PlanStep(Order);

public sealed record GenerationPlan(
    IReadOnlyList<PlanStep> Steps, IReadOnlyDictionary<ObjectName, TablePlan> Tables,
    IReadOnlyList<PlanDiagnostic> Diagnostics, string PlanHash, long EstimatedRows, long EstimatedBytes);

public sealed record PlanResult(GenerationPlan? Plan, IReadOnlyList<PlanDiagnostic> Diagnostics)
{
    public bool IsBlocked => Plan is null;
}

public interface IGenerationPlanner
{
    PlanResult Plan(PlanningInput input);    // síncrono e puro: todo IO acontece antes (TargetState)
}

public interface IDependencyGraphBuilder
{
    DependencyGraph Build(SchemaModel schema, IReadOnlySet<ObjectName> includedTables, IReadOnlyDictionary<string, ColumnBinding> bindings);
}
public sealed class DependencyGraph
{
    public IReadOnlyList<ObjectName> Nodes { get; }
    public IReadOnlyList<DependencyEdge> Edges { get; }
    public IReadOnlyList<IReadOnlyList<ObjectName>> StronglyConnectedComponents();   // Tarjan, ordem topológica reversa estável
}
public sealed record DependencyEdge(ObjectName Child, ObjectName Parent, string ForeignKeyName, bool IsNullable, bool IsDeferrable, bool IsSelfReference, bool ParentIncluded);
```

```csharp
namespace MockDataStudio.Generation.Engine;

/// Fornece a chave do pai de índice i sem armazenar (Explicit/UUID determinístico) ou a partir do KeyRegistry (Captured).
public interface IKeyOracle
{
    object?[] GetKey(ObjectName table, IReadOnlyList<string> keyColumns, long rowIndex);
    long RowCount(ObjectName table);
    KeyPool? ExistingPool(string foreignKeyName);    // quando Source = Existing/Fixed/Dataset/Query
}

public sealed record RowBatch(ObjectName Table, long FirstRowIndex, IReadOnlyList<string> Columns, object?[][] Rows);
public sealed record DeferredUpdateBatch(ObjectName Table, string ForeignKeyName, IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> FkColumns, object?[][] KeyValues, object?[][] FkValues);

public sealed record RunProgress(string Phase, ObjectName? Table, long RowsDone, long RowsTotal, double RowsPerSecond);

// ESTADO DA IMPLEMENTAÇÃO (auditoria 2026-10-12, DA-052): o código tem uma API intermediária por tabela —
// GenerationEngine.RunAsync(TablePlan, IRowSink), IRowSink só com WriteBatchAsync, RowBatch(Table, StartRow, …).
// Este contrato continua NORMATIVO: a Sprint A1 completa IRowSink/RowBatch como abaixo (StartRow → FirstRowIndex),
// e a Sprint A2 expõe IGenerationEngine.RunAsync(GenerationPlan, …). A execução por tabela vira detalhe interno.

/// Destino das linhas geradas. Definido em Generation; MockDataStudio.Output implementa (IOutputWriter : IRowSink).
public interface IRowSink
{
    Task ExecuteSessionStepAsync(PlanStep step, CancellationToken ct);             // SessionSetup/Teardown/ResyncSequence
    Task BeginTableAsync(TablePlan table, CancellationToken ct);
    /// Retorna chaves capturadas (somente inserção direta com KeyStrategy.Captured); senão null.
    Task<IReadOnlyList<object?[]>?> WriteBatchAsync(RowBatch batch, CancellationToken ct);
    Task EndTableAsync(TableRunResult result, CancellationToken ct);
    Task WriteDeferredUpdatesAsync(DeferredUpdateBatch batch, CancellationToken ct);
}

public interface IGenerationEngine
{
    /// Executa o plano emitindo para o sink. Determinístico para (plano, seed, versões).
    Task<RunOutcome> RunAsync(GenerationPlan plan, GenerationRunContext ctx, IRowSink sink,
        IProgress<RunProgress>? progress, CancellationToken ct);
    /// Gera as primeiras N linhas de uma tabela, para preview, sem writer.
    Task<RowBatch> PreviewAsync(GenerationPlan plan, GenerationRunContext ctx, ObjectName table, int rows, CancellationToken ct);
}

public sealed record GenerationRunContext(
    Guid RunId, SchemaModel Schema, IReadOnlyDictionary<string, ColumnBinding> Bindings, GenerationProfile Profile,
    ITemplateDataSource Templates, IReferenceDataSource? References, IGeneratorRegistry Generators, IExprFunctionRegistry Functions);

public enum RunStatus { Completed, Failed, Cancelled }
public sealed record TableRunResult(ObjectName Table, long RowsWritten, IReadOnlyList<KeyRangeResult> KeyRanges);
public sealed record KeyRangeResult(string Column, long? First, long? Last, IReadOnlyList<object?[]>? CapturedKeys);
public sealed record RunOutcome(RunStatus Status, IReadOnlyList<TableRunResult> Tables, IReadOnlyDictionary<string, int> GeneratorVersions,
    IReadOnlyDictionary<string, string> TemplateHashes, IReadOnlyDictionary<string, string> ReferenceFingerprints, MdsError? Error, TimeSpan Elapsed);
```
