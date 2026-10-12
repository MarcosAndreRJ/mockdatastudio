# C07 — Contratos de validação

```csharp
namespace MockDataStudio.Generation.Validation;

public enum IssueLevel { Info, Warning, Error }   // Error bloqueia a execução
public enum IssueCategory { Mapping, Type, Length, Precision, Range, Nullability, Uniqueness, ForeignKey, Check, Trigger, Cycle, Capacity, Reproducibility, Target, Security }

public sealed record ValidationIssue(
    IssueLevel Level, IssueCategory Category, string Code, string Message,
    string? ObjectPath,                  // ex. "public.cliente.email"
    string? Suggestion,
    IReadOnlyDictionary<string, string>? Data = null);

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Level == IssueLevel.Error);
}

public interface IPreflightValidator
{
    /// Validações estáticas (sem banco): tipos, tamanhos, faixas, nulabilidade, capacidade, CHECKs interpretáveis.
    ValidationReport Validate(GenerationPlan plan, GenerationRunContext ctx, ProviderCapabilities? targetCapabilities);
}

/// Regras individuais (plugáveis internamente). Ordem de execução por Order.
public interface IValidationRule
{
    string Code { get; } int Order { get; }
    IEnumerable<ValidationIssue> Evaluate(ValidationRuleContext ctx);
}
public sealed record ValidationRuleContext(GenerationPlan Plan, GenerationRunContext Run, ProviderCapabilities? Target,
    IReadOnlyDictionary<string, IValueGenerator> Generators /* por ColumnRef.CanonicalId */);

/// Interpretação limitada de CHECK (17 §4).
public interface ICheckConstraintInterpreter
{
    CheckInterpretation Interpret(CheckConstraintModel check, TableModel table, DatabaseEngine engine);
}
public enum CheckInterpretationKind { Range, InList, NotEmpty, MinLength, MaxLength, Comparison, Regex, Unsupported }
public sealed record CheckInterpretation(CheckInterpretationKind Kind, string? Column, JsonObject? Parameters, string? Reason);
```

```csharp
namespace MockDataStudio.Application.Validation;

/// Inserção de teste em transação revertida no destino (17 §6). Só permitido em ambientes != Production.
public interface ITrialInsertRunner
{
    Task<TrialInsertReport> RunAsync(GenerationPlan plan, GenerationRunContext ctx, Guid targetProfileId, int rowsPerTable, CancellationToken ct);
}
public sealed record TrialInsertReport(bool Success, IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<string> TablesTested, bool SequencesMayHaveAdvanced);
```
