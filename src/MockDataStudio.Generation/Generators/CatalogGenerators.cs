using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Generators;

// catalog.template (escalar) e catalog.template.field (campo de composto) (docs/15 §6, §7).
// Dados imutáveis carregados antes da execução; geradores sem estado mutável (seguros em paralelo).

internal enum TemplateSelection { Weighted, Uniform, Sequential }

internal sealed class CatalogScalarGenerator : IIndexableGenerator
{
    private readonly TemplateDataSet _data;
    private readonly TemplateSelection _selection;
    private readonly WeightedIndex? _weights;
    private readonly int? _padLeft;

    public CatalogScalarGenerator(TemplateDataSet data, TemplateSelection selection, int? padLeft)
    {
        _data = data;
        _selection = selection;
        _padLeft = padLeft;
        _weights = selection == TemplateSelection.Weighted && data.HasNonUniformWeights
            ? new WeightedIndex(Enumerable.Range(0, data.Count).Select(data.GetWeight).ToList())
            : null;
        var distinct = Enumerable.Range(0, data.Count).Select(data.GetScalar).Distinct(StringComparer.Ordinal).Count() == data.Count;
        Domain = new ValueDomain(data.Count, distinct && _weights is null);
    }

    public ValueDomain Domain { get; }

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        var index = _selection switch
        {
            TemplateSelection.Sequential => (int)(cell.RowIndex % _data.Count),
            TemplateSelection.Uniform => (int)cell.Rng.NextInt64(0, _data.Count),
            _ => _weights is null ? (int)cell.Rng.NextInt64(0, _data.Count) : cell.Rng.NextWeightedIndex(_weights),
        };
        return Pad(_data.GetScalar(index));
    }

    public object? ValueAt(long index) => Pad(_data.GetScalar((int)index));

    private string Pad(string value) =>
        _padLeft is { } width && value.Length > 0 && value.All(char.IsDigit)
            ? value.PadLeft(width, '0')
            : value;
}

internal sealed class CatalogFieldGenerator : IValueGenerator
{
    private readonly TemplateDataSet _data;
    private readonly string _field;
    private readonly string _slot;
    private readonly ulong _seed;
    private readonly string _tableScope;
    private readonly WeightedIndex? _weights;

    public CatalogFieldGenerator(TemplateDataSet data, string field, string slot, ulong seed, string tableScope, bool weighted)
    {
        _data = data;
        _field = field;
        _slot = slot;
        _seed = seed;
        _tableScope = tableScope;
        _weights = weighted && data.HasNonUniformWeights
            ? new WeightedIndex(Enumerable.Range(0, data.Count).Select(data.GetWeight).ToList())
            : null;
    }

    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    // O registro do slot é escolhido uma vez por linha (StreamKey(tabela, "slot:<slot>")) e reutilizado por todas as colunas do slot.
    public object? Generate(ref GenerationCell cell)
    {
        var key = StreamKey.Derive(_seed, _tableScope, "slot:" + _slot);
        var slotRng = DeterministicRandom.For(key, cell.RowIndex);
        var index = cell.Row.GetOrSelectSlotRecord(_slot, _data, _weights, ref slotRng);
        return _data.GetField(index, _field);
    }
}
