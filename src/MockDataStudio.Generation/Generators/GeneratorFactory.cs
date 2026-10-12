using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Generation.Generators;

// Fábrica genérica: valida os parâmetros contra o descritor e chama o construtor do gerador.
internal sealed class GeneratorFactory(GeneratorDescriptor descriptor,
    Func<ParameterSet, GeneratorCreateContext, Result<IValueGenerator>> create) : IGeneratorFactory
{
    public GeneratorDescriptor Descriptor { get; } = descriptor;

    public Result<IValueGenerator> Create(GeneratorSpec spec, GeneratorCreateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.GeneratorId != Descriptor.GeneratorId)
        {
            return Result<IValueGenerator>.Fail(ErrorCodes.GenParamType,
                $"Fábrica '{Descriptor.GeneratorId}' recebeu especificação de '{spec.GeneratorId}'.");
        }

        var bound = ParameterSet.Bind(spec.Parameters, Descriptor.Parameters);
        return bound.IsSuccess
            ? create(bound.Value, ctx)
            : Result<IValueGenerator>.Fail(bound.Error);
    }

    // Constrói um descritor a partir de nomes e tipos (C05 §4).
    public static GeneratorDescriptor Describe(string id, string name, GeneratorCategory category, IReadOnlyList<LogicalType> outputs,
        string description, params GeneratorParameterDefinition[] parameters) =>
        new(id, name, category, CurrentVersion: 1, SupportedVersions: [1], outputs, parameters, Reproducibility.Stable, description);

    public static GeneratorParameterDefinition Required(string name, string type, string label, double? min = null, double? max = null) =>
        new(name, type, Required: true, Default: null, Label: label, Help: null, EnumValues: null, Min: min, Max: max);

    public static GeneratorParameterDefinition Optional(string name, string type, string label, object? defaultValue,
        IReadOnlyList<string>? options = null, double? min = null, double? max = null) =>
        new(name, type, Required: false, Default: defaultValue is null ? null : JsonValue.Create(defaultValue), Label: label, Help: null, EnumValues: options, Min: min, Max: max);
}

// Registro de geradores disponíveis (C05 §4). Adicionar gerador = nova fábrica registrada (docs/15 §11).
public sealed class GeneratorRegistry : IGeneratorRegistry
{
    private readonly Dictionary<string, IGeneratorFactory> _factories = new(StringComparer.Ordinal);

    public GeneratorRegistry(IEnumerable<IGeneratorFactory> factories)
    {
        foreach (var factory in factories)
        {
            if (!_factories.TryAdd(factory.Descriptor.GeneratorId, factory))
            {
                throw new ArgumentException($"Gerador '{factory.Descriptor.GeneratorId}' registrado duas vezes.", nameof(factories));
            }
        }
    }

    // Biblioteca V1 implementada nesta versão do motor.
    public static GeneratorRegistry Standard() => new(BuiltInGenerators.All());

    public IReadOnlyList<GeneratorDescriptor> All => _factories.Values.Select(f => f.Descriptor).OrderBy(d => d.GeneratorId, StringComparer.Ordinal).ToList();

    public bool TryGet(string generatorId, out IGeneratorFactory factory) => _factories.TryGetValue(generatorId, out factory!);
}
