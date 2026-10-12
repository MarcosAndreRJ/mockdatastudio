using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Coercion;
using MockDataStudio.Generation.Expressions;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Generation.Random;
using MockDataStudio.Generation.Unique;

namespace MockDataStudio.Generation.Engine;

// Motor de geração em lotes (docs/15 §2–§3). Não conhece SQL: emite valores canônicos para um IRowSink.
// Lotes saem em ordem crescente de índice; a saída não depende do grau de paralelismo (T-GEN-DET-001).

public interface IRowSink
{
    Task WriteBatchAsync(RowBatch batch, CancellationToken ct);
}

public sealed record RowBatch(ObjectName Table, long StartRow, IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);

// Erro de geração com contexto (tabela, coluna, linha). O código é um GEN-* do catálogo de erros.
public sealed class GenerationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// Coluna do plano: coluna do modelo, gerador já resolvido, taxa de nulos e exigência de unicidade.
public sealed record ColumnPlan(ColumnModel Column, GeneratorSpec Generator, double NullRate, bool Unique);

public sealed record TablePlan(
    ObjectName Table, IReadOnlyList<ColumnPlan> Columns, long RowCount,
    ulong Seed, DateTimeOffset ReferenceInstant, TimeZoneInfo TimeZone,
    IReadOnlyDictionary<string, TemplateDataSet> Templates,
    int BatchSize = 1000, int MaxDegreeOfParallelism = 1);

public sealed class GenerationEngine(IGeneratorRegistry registry)
{
    // Limite de linhas para unicidade por tentativas (15 §8.2, estratégia 5).
    public const long MaxAttemptRows = 5_000_000;

    // Tentativas máximas por linha na estratégia 5 (15 §8.2).
    public const int MaxAttempts = 16;

    public async Task<Result<long>> RunAsync(TablePlan plan, IRowSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);

        var prepared = Prepare(plan);
        if (!prepared.IsSuccess)
        {
            return Result<long>.Fail(prepared.Error);
        }

        var table = prepared.Value;
        var columnNames = table.Insert.Select(s => s.Model.Name).ToList();
        var sequential = table.Steps.Any(s => s.Attempts);
        long written = 0;

        try
        {
            for (long start = 0; start < plan.RowCount;)
            {
                ct.ThrowIfCancellationRequested();
                var count = (int)Math.Min(plan.BatchSize, plan.RowCount - start);
                var rows = new object?[count][];
                if (sequential || plan.MaxDegreeOfParallelism <= 1)
                {
                    for (var i = 0; i < count; i++)
                    {
                        rows[i] = table.EvaluateRow(start + i);
                    }
                }
                else
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = plan.MaxDegreeOfParallelism, CancellationToken = ct };
                    var first = start;
                    Parallel.For(0, count, options, i => rows[i] = table.EvaluateRow(first + i));
                }

                await sink.WriteBatchAsync(new RowBatch(plan.Table, start, columnNames, rows), ct);
                start += count;
                written += count;
            }
        }
        catch (GenerationException ex)
        {
            return Result<long>.Fail(ex.Code, ex.Message);
        }
        catch (AggregateException ex) when (ex.InnerException is GenerationException inner)
        {
            return Result<long>.Fail(inner.Code, inner.Message);
        }

        return Result<long>.Ok(written);
    }

    // Monta geradores, ordem de avaliação, colunas do INSERT e estratégia de unicidade por coluna.
    private Result<PreparedTable> Prepare(TablePlan plan)
    {
        var steps = new List<Step>(plan.Columns.Count);
        foreach (var column in plan.Columns)
        {
            if (!column.Column.IsNullable && column.NullRate > 0)
            {
                return Result<PreparedTable>.Fail(ErrorCodes.ValNullNotNull,
                    $"Coluna '{column.Column.Name}' é NOT NULL e não pode ter taxa de nulos {column.NullRate:0.###}.");
            }

            if (!registry.TryGet(column.Generator.GeneratorId, out var factory))
            {
                return Result<PreparedTable>.Fail(ErrorCodes.GenUnknownGenerator,
                    $"Gerador '{column.Generator.GeneratorId}' não está registrado.");
            }

            var ctx = new GeneratorCreateContext(plan.Seed, plan.Table, column.Column, plan.Templates, plan.ReferenceInstant, plan.TimeZone);
            var created = factory.Create(column.Generator, ctx);
            if (!created.IsSuccess)
            {
                return Result<PreparedTable>.Fail(created.Error);
            }

            steps.Add(new Step(column.Column, column.NullRate, column.Unique, column.Generator.GeneratorId, created.Value));
        }

        var ordered = EvaluationOrder(steps);
        if (!ordered.IsSuccess)
        {
            return Result<PreparedTable>.Fail(ordered.Error);
        }

        var planned = new List<Step>(ordered.Value.Count);
        foreach (var step in ordered.Value)
        {
            var uniqueness = PlanUniqueness(step, plan);
            if (!uniqueness.IsSuccess)
            {
                return Result<PreparedTable>.Fail(uniqueness.Error);
            }

            planned.Add(uniqueness.Value);
        }

        var insert = planned
            .Where(s => s.Generator is not SkipGenerator)
            .OrderBy(s => s.Model.Ordinal)
            .ToList();
        return Result<PreparedTable>.Ok(new PreparedTable(plan, planned, insert));
    }

    // Ordem topológica por DependsOnColumns, com desempate pelo ordinal da coluna (15 §2.1).
    private static Result<IReadOnlyList<Step>> EvaluationOrder(List<Step> steps)
    {
        var names = steps.Select(s => s.Model.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            foreach (var dependency in step.Generator.DependsOnColumns)
            {
                if (!names.Contains(dependency))
                {
                    return Result<IReadOnlyList<Step>>.Fail(ErrorCodes.GenExprColumn,
                        $"Coluna '{step.Model.Name}' depende de '{dependency}', que não existe na tabela.");
                }
            }
        }

        var remaining = steps.OrderBy(s => s.Model.Ordinal).ToList();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<Step>(steps.Count);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(s => s.Generator.DependsOnColumns.All(done.Contains));
            if (next is null)
            {
                return Result<IReadOnlyList<Step>>.Fail(ErrorCodes.GenExprCycle, "Dependência circular entre colunas geradas.");
            }

            remaining.Remove(next);
            done.Add(next.Model.Name);
            ordered.Add(next);
        }

        return Result<IReadOnlyList<Step>>.Ok(ordered);
    }

    // Unicidade (15 §8.2): permutação bijetiva quando o domínio cabe nas linhas; tentativas caso contrário.
    private static Result<Step> PlanUniqueness(Step step, TablePlan plan)
    {
        if (!step.Unique)
        {
            return Result<Step>.Ok(step);
        }

        if (step.Generator is IIndexableGenerator indexable && indexable.Domain.SupportsIndexing)
        {
            var cardinality = indexable.Domain.Cardinality;
            if (cardinality is null)
            {
                // Domínio ilimitado indexável (ex.: sequência): único por construção, valor = índice da linha.
                return Result<Step>.Ok(step with { Indexable = indexable });
            }

            if (cardinality < plan.RowCount)
            {
                return Result<Step>.Fail(ErrorCodes.PlnUniqueCapacity,
                    $"Coluna '{step.Model.Name}' tem {cardinality} valores distintos para {plan.RowCount} linhas.");
            }

            var key = StreamKey.Derive(plan.Seed, ColumnScope(plan.Table, step.Model.Name), "perm");
            return Result<Step>.Ok(step with { Indexable = indexable, Permutation = new FeistelPermutation((ulong)plan.RowCount, key.Value) });
        }

        if (plan.RowCount > MaxAttemptRows)
        {
            return Result<Step>.Fail(ErrorCodes.GenUniqueExhausted,
                $"Unicidade por tentativas limitada a {MaxAttemptRows} linhas por coluna.");
        }

        return Result<Step>.Ok(step with { Attempts = true });
    }

    private static string ColumnScope(ObjectName table, string column) => table.CanonicalId + "." + column;

    private sealed record Step(ColumnModel Model, double NullRate, bool Unique, string GeneratorId, IValueGenerator Generator)
    {
        public IIndexableGenerator? Indexable { get; init; }

        public FeistelPermutation? Permutation { get; init; }

        public bool Attempts { get; init; }
    }

    private sealed record PreparedTable(TablePlan Plan, IReadOnlyList<Step> Steps, IReadOnlyList<Step> Insert)
    {
        // Valores já vistos por coluna na estratégia de tentativas (executada sequencialmente).
        private readonly Dictionary<string, HashSet<object?>> _seen = new(StringComparer.Ordinal);

        public object?[] EvaluateRow(long row)
        {
            var context = new RowContext(row);
            foreach (var step in Steps)
            {
                context.Set(step.Model.Name, EvaluateCell(step, row, context));
            }

            var values = new object?[Insert.Count];
            for (var i = 0; i < values.Length; i++)
            {
                context.TryGet(Insert[i].Model.Name, out values[i]);
            }

            return values;
        }

        private object? EvaluateCell(Step step, long row, RowContext context)
        {
            if (step.NullRate > 0)
            {
                var nullRng = DeterministicRandom.For(StreamKey.Derive(Plan.Seed, ColumnScope(Plan.Table, step.Model.Name), "null"), row);
                if (nullRng.NextDouble() < step.NullRate)
                {
                    return null;
                }
            }

            if (step.Attempts)
            {
                return EvaluateWithAttempts(step, row, context);
            }

            var value = Produce(step, row, context, attempt: 0);
            return Coerce(step, row, value);
        }

        // Estratégia 5 (15 §8.2): tentativas com retry:<k> até obter valor inédito; limite de 16 por linha.
        private object? EvaluateWithAttempts(Step step, long row, RowContext context)
        {
            if (!_seen.TryGetValue(step.Model.Name, out var seen))
            {
                seen = new HashSet<object?>();
                _seen[step.Model.Name] = seen;
            }

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var value = Coerce(step, row, Produce(step, row, context, attempt));
                if (seen.Add(value))
                {
                    return value;
                }
            }

            throw new GenerationException(ErrorCodes.GenUniqueExhausted,
                $"Esgotadas {MaxAttempts} tentativas de valor único (tabela {Plan.Table.CanonicalId}, coluna {step.Model.Name}, linha {row}). Altere o gerador.");
        }

        private object? Produce(Step step, long row, RowContext context, int attempt)
        {
            var scope = ColumnScope(Plan.Table, step.Model.Name);
            try
            {
                if (step.Permutation is { } permutation && step.Indexable is { } indexable)
                {
                    return indexable.ValueAt((long)permutation.Map((ulong)row));
                }

                if (step.Indexable is { } unbounded && unbounded.Domain.Cardinality is null)
                {
                    return unbounded.ValueAt(row);
                }

                var purpose = attempt == 0 ? "value" : "retry:" + attempt;
                var rng = DeterministicRandom.For(StreamKey.Derive(Plan.Seed, scope, purpose), row);
                var cell = new GenerationCell(row, rng, context);
                return step.Generator.Generate(ref cell);
            }
            catch (ExprRuntimeException ex)
            {
                throw new GenerationException(ex.Code, Where(step, row, ex.Message));
            }
        }

        private object? Coerce(Step step, long row, object? value)
        {
            var coerced = ValueCoercer.Coerce(value, step.Model.Type);
            if (!coerced.IsSuccess)
            {
                throw new GenerationException(coerced.Error.Code, Where(step, row, coerced.Error.Message));
            }

            return coerced.Value;
        }

        private string Where(Step step, long row, string message) =>
            $"{message} (tabela {Plan.Table.CanonicalId}, coluna {step.Model.Name}, linha {row})";
    }
}
