# C05 — Contratos de geração (`MockDataStudio.Core.Bindings`, `Core.Templates`, `MockDataStudio.Generation`)

## 1. Bindings (`MockDataStudio.Core.Bindings`)

```csharp
namespace MockDataStudio.Core.Bindings;

/// Especificação serializável de um gerador. Parameters é JSON (System.Text.Json.Nodes.JsonObject).
public sealed record GeneratorSpec(string GeneratorId, int Version, JsonObject Parameters, IReadOnlyList<GeneratorSpec>? Children = null);

public enum BindingOrigin { Structural, Auto, Rule, User }
public enum BindingState { Active, NeedsReview, Orphaned }

public sealed record ColumnBinding(
    ColumnRef Column, GeneratorSpec Generator, double NullRate, BindingOrigin Origin,
    string ColumnFingerprint, BindingState State = BindingState.Active,
    string? ConfidenceLabel = null,     // "High", "Medium"... (texto para persistência)
    bool Unique = false,                // força unicidade mesmo sem UNIQUE no schema
    string? Note = null);
```

## 2. Dados de templates (`MockDataStudio.Core.Templates`)

```csharp
namespace MockDataStudio.Core.Templates;

public enum TemplateKind { Scalar, Composite }

public interface ITemplateDataSource
{
    Task<TemplateDataSet> LoadAsync(string templateCode, CancellationToken ct);
}

/// Imutável. Itens ordenados ordinalmente por Value (escalar) ou RecordKey (composto): a ordem faz parte do determinismo.
public sealed class TemplateDataSet
{
    public string TemplateCode { get; }
    public TemplateKind Kind { get; }
    public string ContentHash { get; }                  // "sha256:..." sobre itens ordenados + pesos
    public int Count { get; }
    public IReadOnlyList<string> FieldCodes { get; }    // vazio para escalar
    public string GetScalar(int index);
    public string? GetField(int index, string fieldCode);
    public double GetWeight(int index);
    public bool HasNonUniformWeights { get; }
}
```

## 3. RNG determinístico (`MockDataStudio.Generation.Random`)

```csharp
namespace MockDataStudio.Generation.Random;

/// Chave de fluxo: XxHash64 sobre UTF-8 de "v1|" + seed + "|" + scope + "|" + purpose. Especificação em 15 §4.
public readonly record struct StreamKey(ulong Value)
{
    public static StreamKey Derive(ulong seed, string scope, string purpose);
    public StreamKey Child(string purpose);
}

/// xoshiro256** inicializado por SplitMix64(key ^ (rowIndex * 0x9E3779B97F4A7C15)). Struct mutável; não compartilhar entre threads.
public struct DeterministicRandom
{
    public static DeterministicRandom For(StreamKey key, long rowIndex);
    public ulong NextUInt64();
    public long NextInt64(long minInclusive, long maxExclusive);   // rejeição sem viés (Lemire)
    public double NextDouble();                                   // [0,1), 53 bits
    public decimal NextDecimal(decimal min, decimal max, int scale);
    public bool NextBool(double probabilityTrue);
    public int NextWeightedIndex(WeightedIndex table);
}

/// Tabela de alias de Vose para amostragem ponderada O(1).
public sealed class WeightedIndex { public WeightedIndex(IReadOnlyList<double> weights); public int Count { get; } }

public static class RngAlgorithm { public const int Version = 1; }
```

## 4. Geradores (`MockDataStudio.Generation.Generators`)

```csharp
namespace MockDataStudio.Generation.Generators;

public enum GeneratorCategory { Basic, Numeric, DateTime, Text, Catalog, Relational, Composite, Brazil, Expression }
public enum Reproducibility { Stable, VersionBound, SourceBound }   // SourceBound = depende de dados externos (ex.: DB existente)

public sealed record GeneratorParameterDefinition(
    string Name, string Type /* int|long|decimal|double|string|bool|date|datetime|duration|enum|stringList|weightedList|spec|specList */,
    bool Required, JsonNode? Default, string Label, string? Help,
    IReadOnlyList<string>? EnumValues = null, double? Min = null, double? Max = null);

public sealed record GeneratorDescriptor(
    string GeneratorId, string DisplayName, GeneratorCategory Category, int CurrentVersion,
    IReadOnlyList<int> SupportedVersions, IReadOnlyList<LogicalType> OutputTypes,
    IReadOnlyList<GeneratorParameterDefinition> Parameters, Reproducibility Reproducibility, string Description);

public interface IGeneratorFactory
{
    GeneratorDescriptor Descriptor { get; }
    /// Valida parâmetros e cria instância. Nunca lança por parâmetro inválido: retorna Fail(GEN-PARAM-*).
    Result<IValueGenerator> Create(GeneratorSpec spec, GeneratorCreateContext ctx);
}

public interface IGeneratorRegistry
{
    IReadOnlyList<GeneratorDescriptor> All { get; }
    IGeneratorFactory Get(string generatorId);                     // lança se inexistente (erro de programação)
    bool TryGet(string generatorId, out IGeneratorFactory factory);
}

public sealed record GeneratorCreateContext(
    ObjectName Table, TableModel TableModel, ColumnModel Column,
    IReadOnlyDictionary<string, TemplateDataSet> Templates,        // pré-carregados pelo engine
    DateTimeOffset ReferenceInstant, IExprFunctionRegistry Functions,
    IKeyOracle Keys, IGeneratorRegistry Registry);

/// Domínio de valores para verificação de capacidade e unicidade construtiva.
public sealed record ValueDomain(long? Cardinality, bool SupportsIndexing);

public interface IValueGenerator
{
    ValueDomain Domain { get; }
    /// Colunas da MESMA linha das quais depende (expressões, slots). Usadas para ordenar avaliação.
    IReadOnlyList<string> DependsOnColumns { get; }
    /// Gera o valor da célula. Deve ser função pura de (cell.Rng, cell.RowIndex, cell.Row).
    object? Generate(ref GenerationCell cell);
}

/// Geradores com domínio enumerável implementam acesso por índice (unicidade bijetiva — 15 §8).
public interface IIndexableGenerator : IValueGenerator
{
    object? ValueAt(long index);   // 0 <= index < Domain.Cardinality
}

public ref struct GenerationCell
{
    public long RowIndex;
    public DeterministicRandom Rng;          // já posicionado para (coluna, linha)
    public RowContext Row;
}

/// Valores já gerados da linha atual e seleção de registros compostos por slot.
public sealed class RowContext
{
    public long RowIndex { get; }
    public object? Get(string column);                          // lança se coluna ainda não avaliada
    public bool TryGet(string column, out object? value);
    public int GetOrSelectSlotRecord(string slot, TemplateDataSet data, ref DeterministicRandom slotRng);
    public long? ParentIndex(string foreignKeyName);           // índice do pai escolhido (relacional)
}
```

### 4.1 Tipos de valor canônicos (contrato entre geradores e writers)

| LogicalType | Tipo CLR emitido |
|---|---|
| Boolean | `bool` |
| Integer | `long` (unsigned 64 → `ulong`) |
| Decimal | `decimal` |
| Float | `double` |
| String / Text / Enum | `string` |
| Binary | `byte[]` |
| Date | `DateOnly` |
| Time | `TimeOnly` |
| DateTime | `DateTime` (`Kind=Unspecified`) |
| DateTimeTz | `DateTimeOffset` |
| Uuid | `Guid` |
| Json | `string` (JSON válido) |
| Interval | `TimeSpan` |
| `null` | `null` |

O engine converte o valor do gerador para o tipo canônico da coluna via `ValueCoercer` (ex.: template `CEP` string → coluna Integer: remove não dígitos e converte; falha → erro `GEN-COERCE` na validação de preview).

## 5. MDS-Expr (`MockDataStudio.Generation.Expressions`)

```csharp
namespace MockDataStudio.Generation.Expressions;

public interface IExprCompiler
{
    Result<CompiledExpr> Compile(string source, ExprMode mode);   // Expression | Template ("...{expr}...")
}
public enum ExprMode { Expression, Template }
public sealed class CompiledExpr
{
    public IReadOnlyList<string> ReferencedColumns { get; }
    public object? Evaluate(ref GenerationCell cell);
}
public interface IExprFunction
{
    string Name { get; }               // minúsculo, ex. "lower"
    int MinArgs { get; } int MaxArgs { get; }
    bool UsesRandom { get; }
    object? Invoke(ReadOnlySpan<object?> args, ref GenerationCell cell);
}
public interface IExprFunctionRegistry { bool TryGet(string name, out IExprFunction fn); IReadOnlyList<IExprFunction> All { get; } }
```

## 6. Fonte de dados de referência (implementada pela Application)

```csharp
namespace MockDataStudio.Generation.Engine;

public interface IReferenceDataSource
{
    /// Chaves existentes no banco de referência, em ordem determinística (ORDER BY colunas), até `limit`.
    Task<KeyPool> LoadKeysAsync(ObjectName table, IReadOnlyList<string> columns, long limit, CancellationToken ct);
    /// Valores para lookup de coluna (rel.lookup) ou query configurada (somente SELECT).
    Task<KeyPool> LoadQueryAsync(string selectSql, CancellationToken ct);
}
public sealed class KeyPool
{
    public int Arity { get; }
    public long Count { get; }
    public string Fingerprint { get; }       // sha256 de (count + valores) — usado no relatório de reprodutibilidade
    public object?[] Get(long index);
}
```
