using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Generators;

// Contratos de geradores (C05 §4). Um gerador é uma função pura de (cell.Rng, cell.RowIndex, cell.Row).

public enum GeneratorCategory { Basic, Numeric, DateTime, Text, Catalog, Relational, Composite, Brazil, Expression }

public enum Reproducibility { Stable, VersionBound, SourceBound }

public sealed record GeneratorParameterDefinition(
    string Name, string Type, // int|long|decimal|double|string|bool|enum|stringList|doubleList|duration|datetime
    bool Required, JsonNode? Default, string Label, string? Help,
    IReadOnlyList<string>? EnumValues = null, double? Min = null, double? Max = null);

public sealed record GeneratorDescriptor(
    string GeneratorId, string DisplayName, GeneratorCategory Category, int CurrentVersion,
    IReadOnlyList<int> SupportedVersions, IReadOnlyList<LogicalType> OutputTypes,
    IReadOnlyList<GeneratorParameterDefinition> Parameters, Reproducibility Reproducibility, string Description);

// Domínio de valores para verificação de capacidade e unicidade construtiva (15 §8).
public sealed record ValueDomain(long? Cardinality, bool SupportsIndexing);

public interface IValueGenerator
{
    ValueDomain Domain { get; }

    // Colunas da MESMA linha das quais depende (expressões, slots). Usadas para ordenar a avaliação.
    IReadOnlyList<string> DependsOnColumns { get; }

    // Valor da célula: função pura de (cell.Rng, cell.RowIndex, cell.Row).
    object? Generate(ref GenerationCell cell);
}

// Geradores com domínio enumerável implementam acesso por índice (unicidade bijetiva, 15 §8.2).
public interface IIndexableGenerator : IValueGenerator
{
    // 0 <= index < Domain.Cardinality
    object? ValueAt(long index);
}

// Célula avaliada. Rng é retornada por referência: DeterministicRandom é struct mutável, e uma cópia
// perderia o avanço do estado a cada chamada.
public ref struct GenerationCell(long rowIndex, DeterministicRandom rng, RowContext row)
{
    public long RowIndex { get; } = rowIndex;

    // Campo (não propriedade) para que cell.Rng.Método() avance o estado no próprio campo, acessado por ref.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields",
        Justification = "Struct mutável: uma propriedade copiaria o estado do RNG a cada chamada (15 §4.1).")]
    public DeterministicRandom Rng = rng;

    public RowContext Row { get; } = row;
}

// Valores já gerados da linha atual e seleção de registros compostos por slot (15 §7).
public sealed class RowContext(long rowIndex)
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _slotRecords = new(StringComparer.Ordinal);

    public long RowIndex { get; } = rowIndex;

    public void Set(string column, object? value) => _values[column] = value;

    public bool TryGet(string column, out object? value) => _values.TryGetValue(column, out value);

    public object? Get(string column) =>
        _values.TryGetValue(column, out var value)
            ? value
            : throw new InvalidOperationException($"Coluna '{column}' ainda não foi avaliada nesta linha.");

    // Seleciona (uma vez por linha e slot) o registro do template; todas as colunas do slot leem o mesmo registro.
    public int GetOrSelectSlotRecord(string slot, TemplateDataSet data, WeightedIndex? weights, ref DeterministicRandom slotRng)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_slotRecords.TryGetValue(slot, out var index))
        {
            return index;
        }

        index = weights is null
            ? (int)slotRng.NextInt64(0, data.Count)
            : slotRng.NextWeightedIndex(weights);
        _slotRecords[slot] = index;
        return index;
    }
}

public interface IGeneratorFactory
{
    GeneratorDescriptor Descriptor { get; }

    // Valida parâmetros e cria a instância. Parâmetro inválido retorna Fail(GEN-PARAM-*); não lança.
    Result<IValueGenerator> Create(GeneratorSpec spec, GeneratorCreateContext ctx);
}

public interface IGeneratorRegistry
{
    IReadOnlyList<GeneratorDescriptor> All { get; }

    bool TryGet(string generatorId, out IGeneratorFactory factory);
}

public sealed record GeneratorCreateContext(
    ulong Seed, ObjectName Table, ColumnModel Column,
    IReadOnlyDictionary<string, TemplateDataSet> Templates,
    DateTimeOffset ReferenceInstant,
    TimeZoneInfo TimeZone,
    IGeneratorRegistry? Registry = null);
