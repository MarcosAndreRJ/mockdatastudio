using MockDataStudio.Core.Common;
using MockDataStudio.Generation.Generators;

namespace MockDataStudio.Generation.Expressions;

// core.format e core.expr (docs/15 §6, §9): gerador cujo valor é a avaliação de uma expressão compilada.
internal sealed class ExpressionGenerator(CompiledExpr expression) : IValueGenerator
{
    public static Result<IValueGenerator> Create(string source, ExprMode mode, GeneratorCreateContext ctx)
    {
        var environment = new ExprEnvironment(ctx.Templates, ctx.ReferenceInstant, ctx.TimeZone, ctx.Seed, ctx.Table.CanonicalId);
        var compiled = CompiledExpr.Compile(source, mode, environment);
        return compiled.IsSuccess
            ? Result<IValueGenerator>.Ok(new ExpressionGenerator(compiled.Value))
            : Result<IValueGenerator>.Fail(compiled.Error);
    }

    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns => expression.ReferencedColumns;

    public object? Generate(ref GenerationCell cell) => expression.Evaluate(ref cell);
}
