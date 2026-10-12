using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Core.Profiles;

// Configurações de perfil e geração conforme C06 e docs/15/16/18.
// Pertencem a Core.Profiles porque Projects os persiste (AR-02).

public enum KeyStrategy
{
    Explicit,
    Captured,
    NaturalKeyLookup,
    DatabaseDefault
}

public enum ReferenceSourceKind
{
    Generated,
    Existing,
    Fixed,
    Dataset,
    Query
}

public enum CardinalityMode
{
    ChildDriven,
    ParentDriven
}

public enum DistributionKind
{
    Uniform,
    Zipf,
    Weighted,
    Range,
    Poisson
}

public enum CycleBreakStrategy
{
    NullThenUpdate,
    DeferredConstraint,
    DisableChecks
}

public enum OutputKind
{
    SqlScript,
    DirectInsert,
    Csv,
    JsonLines
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifiers should not contain type names", Justification = "Contrato normativo C06 define DirectTransactionMode.Single (SAN-A.1).")]
public enum DirectTransactionMode
{
    PerBatch,
    Single
}

public enum ReferenceInstantMode
{
    Fixed,
    Now
}

public sealed record DistributionSpec(
    DistributionKind Kind,
    double? ZipfExponent = null,
    int? Min = null,
    int? Max = null,
    double? Lambda = null,
    IReadOnlyList<double>? Weights = null)
{
    public static Result<DistributionSpec> Create(
        DistributionKind kind,
        double? zipfExponent = null,
        int? min = null,
        int? max = null,
        double? lambda = null,
        IReadOnlyList<double>? weights = null)
    {
        switch (kind)
        {
            case DistributionKind.Zipf:
                if (zipfExponent is null or <= 0)
                {
                    return Result<DistributionSpec>.Fail(ErrorCodes.GenParamRange, "Distribuição Zipf exige expoente estritamente positivo.");
                }
                break;
            case DistributionKind.Range:
                if (min is null || max is null || min > max)
                {
                    return Result<DistributionSpec>.Fail(ErrorCodes.GenParamRange, "Distribuição Range exige Min e Max válidos onde Min <= Max.");
                }
                break;
            case DistributionKind.Poisson:
                if (lambda is null or <= 0)
                {
                    return Result<DistributionSpec>.Fail(ErrorCodes.GenParamRange, "Distribuição Poisson exige Lambda estritamente positivo.");
                }
                break;
            case DistributionKind.Weighted:
                if (weights is null || weights.Count == 0 || weights.Any(w => w < 0) || weights.Sum() <= 0)
                {
                    return Result<DistributionSpec>.Fail(ErrorCodes.GenParamRange, "Distribuição Weighted exige lista de pesos não-vazia com soma positiva.");
                }
                break;
        }

        return Result<DistributionSpec>.Ok(new DistributionSpec(kind, zipfExponent, min, max, lambda, weights));
    }
}

public sealed record CardinalitySpec(
    CardinalityMode Mode,
    DistributionSpec Distribution,
    bool OneToOne = false)
{
    public static Result<CardinalitySpec> Create(
        CardinalityMode mode,
        DistributionSpec distribution,
        bool oneToOne = false)
    {
        if (distribution is null)
        {
            return Result<CardinalitySpec>.Fail(ErrorCodes.GenParamRequired, "Distribuição de cardinalidade não informada.");
        }

        return Result<CardinalitySpec>.Ok(new CardinalitySpec(mode, distribution, oneToOne));
    }
}

public sealed record RelationSettings(
    string ForeignKeyName,
    ReferenceSourceKind Source,
    CardinalitySpec Cardinality,
    IReadOnlyList<JsonArray>? FixedValues = null,   // tuplas para Fixed
    string? DatasetPath = null,
    string? Query = null,
    long ExistingLimit = 100_000,
    double NullRate = 0,
    CycleBreakStrategy? CycleBreak = null,          // null = automático
    double RootRatio = 0.1,
    int MaxDepth = 6)                               // somente autorreferência (16 §4)
{
    public static Result<RelationSettings> Create(
        string foreignKeyName,
        ReferenceSourceKind source,
        CardinalitySpec cardinality,
        IReadOnlyList<JsonArray>? fixedValues = null,
        string? datasetPath = null,
        string? query = null,
        long existingLimit = 100_000,
        double nullRate = 0,
        CycleBreakStrategy? cycleBreak = null,
        double rootRatio = 0.1,
        int maxDepth = 6)
    {
        if (string.IsNullOrWhiteSpace(foreignKeyName))
        {
            return Result<RelationSettings>.Fail(ErrorCodes.GenParamRequired, "Nome da chave estrangeira não pode ser vazio.");
        }

        if (cardinality is null)
        {
            return Result<RelationSettings>.Fail(ErrorCodes.GenParamRequired, "Especificação de cardinalidade não pode ser nula.");
        }

        if (nullRate is < 0 or > 1)
        {
            return Result<RelationSettings>.Fail(ErrorCodes.GenParamRange, "Taxa de nulos deve estar entre 0.0 e 1.0.");
        }

        if (existingLimit < 0)
        {
            return Result<RelationSettings>.Fail(ErrorCodes.GenParamRange, "Limite de registros existentes não pode ser negativo.");
        }

        if (rootRatio is < 0 or > 1)
        {
            return Result<RelationSettings>.Fail(ErrorCodes.GenParamRange, "Proporção de raízes (RootRatio) deve estar entre 0.0 e 1.0.");
        }

        if (maxDepth < 1)
        {
            return Result<RelationSettings>.Fail(ErrorCodes.GenParamRange, "Profundidade máxima de autorreferência (MaxDepth) deve ser pelo menos 1.");
        }

        return Result<RelationSettings>.Ok(new RelationSettings(
            foreignKeyName, source, cardinality, fixedValues, datasetPath, query,
            existingLimit, nullRate, cycleBreak, rootRatio, maxDepth));
    }
}

public sealed record TableSettings(
    bool Include,
    long RowCount,
    KeyStrategy? KeyStrategy = null,
    long? KeyStartOverride = null,
    IReadOnlyDictionary<string, RelationSettings>? Relations = null)
{
    public static Result<TableSettings> Create(
        bool include,
        long rowCount,
        KeyStrategy? keyStrategy = null,
        long? keyStartOverride = null,
        IReadOnlyDictionary<string, RelationSettings>? relations = null)
    {
        if (rowCount < 0)
        {
            return Result<TableSettings>.Fail(ErrorCodes.GenParamRange, "Quantidade de linhas não pode ser negativa.");
        }

        if (keyStartOverride.HasValue && keyStartOverride.Value < 0)
        {
            return Result<TableSettings>.Fail(ErrorCodes.GenParamRange, "Sobrescrita de início de chave não pode ser negativa.");
        }

        return Result<TableSettings>.Ok(new TableSettings(include, rowCount, keyStrategy, keyStartOverride, relations));
    }
}

public sealed record ValidationSettings(
    bool TrialInsert = false,
    int TrialRows = 50,
    bool VerifyReferencesInTarget = true)
{
    public static Result<ValidationSettings> Create(
        bool trialInsert = false,
        int trialRows = 50,
        bool verifyReferencesInTarget = true)
    {
        if (trialRows < 0)
        {
            return Result<ValidationSettings>.Fail(ErrorCodes.GenParamRange, "Quantidade de linhas de teste não pode ser negativa.");
        }

        return Result<ValidationSettings>.Ok(new ValidationSettings(trialInsert, trialRows, verifyReferencesInTarget));
    }
}

public sealed record OutputSettings(
    OutputKind Kind,
    string? Directory = null,
    string? FileNamePattern = "{project}_{profile}_{yyyyMMdd_HHmmss}",
    long SegmentMaxBytes = 50L * 1024 * 1024,
    int RowsPerStatement = 0 /*0 = dialeto*/,
    int BatchSize = 1000,
    DirectTransactionMode TransactionMode = DirectTransactionMode.PerBatch,
    bool IncludeCleanupScript = true,
    bool ResyncSequences = true,
    string Encoding = "utf-8",
    bool EmitGuards = true,                 // comandos/comentários de verificação no script (16 §5.2, §9)
    bool AllowDisableFkChecks = false,      // opt-in para CycleBreakStrategy.DisableChecks (16 §3)
    bool QualifyNamespace = false,          // MySQL: prefixar database (18 §2)
    bool QuoteAllIdentifiers = true,
    bool IncludeTransaction = true,
    bool WriteBom = false,
    string LineEnding = "\n",
    string CsvSeparator = ",")
{
    public static Result<OutputSettings> Create(
        OutputKind kind,
        string? directory = null,
        string? fileNamePattern = "{project}_{profile}_{yyyyMMdd_HHmmss}",
        long segmentMaxBytes = 50L * 1024 * 1024,
        int rowsPerStatement = 0,
        int batchSize = 1000,
        DirectTransactionMode transactionMode = DirectTransactionMode.PerBatch,
        bool includeCleanupScript = true,
        bool resyncSequences = true,
        string encoding = "utf-8",
        bool emitGuards = true,
        bool allowDisableFkChecks = false,
        bool qualifyNamespace = false,
        bool quoteAllIdentifiers = true,
        bool includeTransaction = true,
        bool writeBom = false,
        string lineEnding = "\n",
        string csvSeparator = ",")
    {
        if (segmentMaxBytes <= 0)
        {
            return Result<OutputSettings>.Fail(ErrorCodes.GenParamRange, "Tamanho máximo do segmento deve ser positivo.");
        }

        if (rowsPerStatement < 0)
        {
            return Result<OutputSettings>.Fail(ErrorCodes.GenParamRange, "Linhas por instrução não pode ser negativo.");
        }

        if (batchSize <= 0)
        {
            return Result<OutputSettings>.Fail(ErrorCodes.GenParamRange, "Tamanho do lote deve ser estritamente positivo.");
        }

        if (string.IsNullOrWhiteSpace(encoding))
        {
            return Result<OutputSettings>.Fail(ErrorCodes.GenParamRequired, "Codificação não informada.");
        }

        return Result<OutputSettings>.Ok(new OutputSettings(
            kind, directory, fileNamePattern, segmentMaxBytes, rowsPerStatement, batchSize,
            transactionMode, includeCleanupScript, resyncSequences, encoding, emitGuards,
            allowDisableFkChecks, qualifyNamespace, quoteAllIdentifiers, includeTransaction,
            writeBom, lineEnding, csvSeparator));
    }
}

public sealed record GenerationProfile(
    string Name,
    ulong Seed,
    DateTimeOffset ReferenceInstant,
    ReferenceInstantMode ReferenceInstantMode,
    string TimeZoneId,
    IReadOnlyDictionary<string, TableSettings> Tables,   // por ObjectName.CanonicalId
    KeyStrategy DefaultKeyStrategy,
    OutputSettings Output,
    ValidationSettings Validation,
    long KeyOffset = 0)                                  // folga somada ao MAX(pk) do destino
{
    public static Result<GenerationProfile> Create(
        string name,
        ulong seed,
        DateTimeOffset referenceInstant,
        ReferenceInstantMode referenceInstantMode,
        string timeZoneId,
        IReadOnlyDictionary<string, TableSettings> tables,
        KeyStrategy defaultKeyStrategy,
        OutputSettings output,
        ValidationSettings validation,
        long keyOffset = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<GenerationProfile>.Fail(ErrorCodes.GenParamRequired, "Nome do perfil não pode ser vazio.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return Result<GenerationProfile>.Fail(ErrorCodes.GenParamRequired, "Fuso horário não pode ser vazio.");
        }

        if (tables is null)
        {
            return Result<GenerationProfile>.Fail(ErrorCodes.GenParamRequired, "Dicionário de tabelas não pode ser nulo.");
        }

        if (output is null)
        {
            return Result<GenerationProfile>.Fail(ErrorCodes.GenParamRequired, "Configurações de saída não informadas.");
        }

        if (validation is null)
        {
            return Result<GenerationProfile>.Fail(ErrorCodes.GenParamRequired, "Configurações de validação não informadas.");
        }

        if (keyOffset < 0)
        {
            return Result<GenerationProfile>.Fail(ErrorCodes.GenParamRange, "Folga de chave (KeyOffset) não pode ser negativa.");
        }

        return Result<GenerationProfile>.Ok(new GenerationProfile(
            name, seed, referenceInstant, referenceInstantMode, timeZoneId,
            tables, defaultKeyStrategy, output, validation, keyOffset));
    }
}
