using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Generation.Expressions;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Generators;

// Geradores compostos (docs/15 §6): core.weighted e core.switch.
internal sealed class WeightedCompositeGenerator : IValueGenerator
{
    private readonly IReadOnlyList<IValueGenerator> _children;
    private readonly WeightedIndex _weightedIndex;

    public WeightedCompositeGenerator(IReadOnlyList<IValueGenerator> children, WeightedIndex weightedIndex)
    {
        _children = children;
        _weightedIndex = weightedIndex;
        Domain = new ValueDomain(null, false);
        DependsOnColumns = children.SelectMany(c => c.DependsOnColumns).Distinct(StringComparer.Ordinal).ToList();
    }

    public ValueDomain Domain { get; }

    public IReadOnlyList<string> DependsOnColumns { get; }

    public object? Generate(ref GenerationCell cell)
    {
        var index = cell.Rng.NextWeightedIndex(_weightedIndex);
        return _children[index].Generate(ref cell);
    }
}

internal sealed class SwitchCompositeGenerator : IValueGenerator
{
    private readonly IReadOnlyList<(CompiledExpr When, IValueGenerator Generator)> _cases;
    private readonly IValueGenerator _elseGenerator;

    public SwitchCompositeGenerator(IReadOnlyList<(CompiledExpr When, IValueGenerator Generator)> cases, IValueGenerator elseGenerator)
    {
        _cases = cases;
        _elseGenerator = elseGenerator;
        Domain = new ValueDomain(null, false);
        DependsOnColumns = cases
            .SelectMany(c => c.When.ReferencedColumns.Concat(c.Generator.DependsOnColumns))
            .Concat(elseGenerator.DependsOnColumns)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public ValueDomain Domain { get; }

    public IReadOnlyList<string> DependsOnColumns { get; }

    public object? Generate(ref GenerationCell cell)
    {
        for (var i = 0; i < _cases.Count; i++)
        {
            var (when, generator) = _cases[i];
            var cond = when.Evaluate(ref cell);
            if (IsTrue(cond))
            {
                return generator.Generate(ref cell);
            }
        }

        return _elseGenerator.Generate(ref cell);
    }

    private static bool IsTrue(object? value) => value switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        double d => d != 0.0,
        decimal dec => dec != 0m,
        string s => s.Length > 0 && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };
}

internal sealed class WeightedCompositeFactory : IGeneratorFactory
{
    public GeneratorDescriptor Descriptor { get; } = new(
        "core.weighted",
        "Composto ponderado",
        GeneratorCategory.Composite,
        CurrentVersion: 1,
        SupportedVersions: [1],
        OutputTypes: [LogicalType.Other, LogicalType.String, LogicalType.Integer, LogicalType.Decimal, LogicalType.DateTime],
        Parameters: [GeneratorFactory.Optional("weights", "doubleList", "Pesos", null)],
        Reproducibility: Reproducibility.Stable,
        Description: "Seleciona um gerador filho conforme distribuição de pesos.");

    public Result<IValueGenerator> Create(GeneratorSpec spec, GeneratorCreateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(ctx);

        if (ctx.Registry is null)
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenUnsupported, "Registry não disponível no contexto.");
        }

        var childSpecs = ExtractChildren(spec);
        if (childSpecs.Count == 0)
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenParamValue, "core.weighted requer ao menos um gerador filho.");
        }

        var weights = ExtractWeights(spec, childSpecs.Count);
        if (weights.Count != childSpecs.Count)
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenParamValue, "weights deve ter o mesmo tamanho que children.");
        }

        if (weights.Any(w => w <= 0))
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenParamValue, "pesos devem ser positivos.");
        }

        var children = new List<IValueGenerator>(childSpecs.Count);
        foreach (var childSpec in childSpecs)
        {
            if (!ctx.Registry.TryGet(childSpec.GeneratorId, out var factory))
            {
                return Result<IValueGenerator>.Fail(ErrorCodes.GenUnknownGenerator,
                    $"Gerador filho '{childSpec.GeneratorId}' não está registrado.");
            }

            var created = factory.Create(childSpec, ctx);
            if (!created.IsSuccess)
            {
                return created;
            }

            children.Add(created.Value);
        }

        var weightedIndex = new WeightedIndex(weights);
        return Result<IValueGenerator>.Ok(new WeightedCompositeGenerator(children, weightedIndex));
    }

    private static IReadOnlyList<GeneratorSpec> ExtractChildren(GeneratorSpec spec)
    {
        if (spec.Children is { Count: > 0 })
        {
            return spec.Children;
        }

        if (spec.Parameters.TryGetPropertyValue("children", out var node) && node is JsonArray arr)
        {
            var list = new List<GeneratorSpec>();
            foreach (var item in arr)
            {
                if (item is JsonObject obj && obj.TryGetPropertyValue("generatorId", out var idNode) && idNode is not null)
                {
                    var id = idNode.GetValue<string>();
                    var v = obj.TryGetPropertyValue("version", out var vn) && vn is not null ? vn.GetValue<int>() : 1;
                    var p = obj.TryGetPropertyValue("parameters", out var pn) && pn is JsonObject pObj ? pObj : new JsonObject();
                    list.Add(new GeneratorSpec(id, v, p));
                }
            }

            return list;
        }

        return [];
    }

    private static List<double> ExtractWeights(GeneratorSpec spec, int childCount)
    {
        if (spec.Parameters.TryGetPropertyValue("weights", out var node) && node is JsonArray arr)
        {
            var list = new List<double>();
            foreach (var item in arr)
            {
                if (item is JsonValue v && v.TryGetValue<double>(out var d))
                {
                    list.Add(d);
                }
                else if (item is JsonValue vi && vi.TryGetValue<long>(out var l))
                {
                    list.Add(l);
                }
            }

            return list;
        }

        return Enumerable.Repeat(1.0, childCount).ToList();
    }
}

internal sealed class SwitchCompositeFactory : IGeneratorFactory
{
    public GeneratorDescriptor Descriptor { get; } = new(
        "core.switch",
        "Composto condicional (switch)",
        GeneratorCategory.Composite,
        CurrentVersion: 1,
        SupportedVersions: [1],
        OutputTypes: [LogicalType.Other, LogicalType.String, LogicalType.Integer, LogicalType.Decimal, LogicalType.DateTime],
        Parameters: [],
        Reproducibility: Reproducibility.Stable,
        Description: "Avalia expressões 'when' na ordem e despacha para o gerador correspondente.");

    public Result<IValueGenerator> Create(GeneratorSpec spec, GeneratorCreateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(ctx);

        if (ctx.Registry is null)
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenUnsupported, "Registry não disponível no contexto.");
        }

        if (!spec.Parameters.TryGetPropertyValue("cases", out var casesNode) || casesNode is not JsonArray casesArr || casesArr.Count == 0)
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenParamValue, "core.switch requer ao menos um caso em 'cases'.");
        }

        if (!spec.Parameters.TryGetPropertyValue("else", out var elseNode) || elseNode is not JsonObject elseObj)
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenParamValue, "core.switch requer especificação em 'else'.");
        }

        var elseSpec = ParseSpec(elseObj);
        if (elseSpec is null || !ctx.Registry.TryGet(elseSpec.GeneratorId, out var elseFactory))
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenUnknownGenerator,
                $"Gerador 'else' ('{elseSpec?.GeneratorId}') não está registrado.");
        }

        var elseCreated = elseFactory.Create(elseSpec, ctx);
        if (!elseCreated.IsSuccess)
        {
            return elseCreated;
        }

        var env = new ExprEnvironment(ctx.Templates, ctx.ReferenceInstant, ctx.TimeZone, ctx.Seed, ctx.Table.CanonicalId);
        var cases = new List<(CompiledExpr When, IValueGenerator Generator)>(casesArr.Count);

        foreach (var item in casesArr)
        {
            if (item is not JsonObject caseObj)
            {
                return Result<IValueGenerator>.Fail(ErrorCodes.GenParamValue, "Cada item de 'cases' deve ser um objeto.");
            }

            var whenStr = caseObj.TryGetPropertyValue("when", out var wn) && wn is not null ? wn.GetValue<string>() : null;
            if (string.IsNullOrWhiteSpace(whenStr))
            {
                return Result<IValueGenerator>.Fail(ErrorCodes.GenParamValue, "Item de 'cases' requer expressão 'when'.");
            }

            var compiledWhen = CompiledExpr.Compile(whenStr, ExprMode.Expression, env);
            if (!compiledWhen.IsSuccess)
            {
                return Result<IValueGenerator>.Fail(compiledWhen.Error);
            }

            JsonObject? childObj = null;
            if (caseObj.TryGetPropertyValue("spec", out var sn) && sn is JsonObject so)
            {
                childObj = so;
            }
            else if (caseObj.TryGetPropertyValue("generator", out var gn) && gn is JsonObject go)
            {
                childObj = go;
            }

            var childSpec = childObj is not null ? ParseSpec(childObj) : null;
            if (childSpec is null || !ctx.Registry.TryGet(childSpec.GeneratorId, out var caseFactory))
            {
                return Result<IValueGenerator>.Fail(ErrorCodes.GenUnknownGenerator,
                    $"Gerador de caso ('{childSpec?.GeneratorId}') não está registrado.");
            }

            var caseCreated = caseFactory.Create(childSpec, ctx);
            if (!caseCreated.IsSuccess)
            {
                return caseCreated;
            }

            cases.Add((compiledWhen.Value, caseCreated.Value));
        }

        return Result<IValueGenerator>.Ok(new SwitchCompositeGenerator(cases, elseCreated.Value));
    }

    private static GeneratorSpec? ParseSpec(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("generatorId", out var idNode) || idNode is null)
        {
            return null;
        }

        var id = idNode.GetValue<string>();
        var v = obj.TryGetPropertyValue("version", out var vn) && vn is not null ? vn.GetValue<int>() : 1;
        var p = obj.TryGetPropertyValue("parameters", out var pn) && pn is JsonObject pObj ? pObj : new JsonObject();
        return new GeneratorSpec(id, v, p);
    }
}
