using MockDataStudio.Core.Naming;

namespace MockDataStudio.Generation.Planning;

// Etapas do plano de execução relacional (C06).
public abstract record PlanStep(int Order);

public sealed record SessionSetupStep(int Order, IReadOnlyList<string> Statements) : PlanStep(Order);

public sealed record InsertTableStep(int Order, ObjectName Table) : PlanStep(Order);

public sealed record DeferredUpdateStep(int Order, ObjectName Table, string ForeignKeyName) : PlanStep(Order);

public sealed record ResyncSequenceStep(int Order, ObjectName Table, string Column, string? Sequence) : PlanStep(Order);

public sealed record SessionTeardownStep(int Order, IReadOnlyList<string> Statements) : PlanStep(Order);
