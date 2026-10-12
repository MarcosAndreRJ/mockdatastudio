using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Catalog;

// Contratos do catálogo (C03). Records imutáveis; tipos e nomes conforme o contrato.

public sealed record TemplateInfo(long Id, string Code, string Name, TemplateKind Kind, LogicalType? LogicalType,
    string Locale, IReadOnlyList<string> Tags, string? Description, string PackCode, bool IsBuiltIn,
    long ValueCount, string ContentHash);

public sealed record TemplateFieldInfo(string FieldCode, LogicalType LogicalType, int Ordinal, int? MaxLength, bool Required);

public sealed record TemplateAliasInfo(long Id, string TemplateCode, string? FieldCode, string Alias, string NormalizedAlias,
    double Weight, string? ContextTablePattern);

public sealed record TemplateValueRow(string Value, double Weight);

public sealed record TemplateRecordRow(string RecordKey, IReadOnlyDictionary<string, string?> Data, double Weight);

public sealed record TemplateQuery(string? Text = null, TemplateKind? Kind = null, string? PackCode = null);

public sealed record DraftField(string FieldCode, LogicalType LogicalType, int Ordinal, int? MaxLength = null, bool Required = true);

public sealed record DraftAlias(string Alias, string? FieldCode = null, double Weight = 1.0, string? ContextTablePattern = null);

public sealed record TemplateDraft(
    string Code, string Name, TemplateKind Kind, LogicalType? LogicalType, string? Description,
    IReadOnlyList<string> Tags, IReadOnlyList<DraftField> Fields, IReadOnlyList<DraftAlias> Aliases,
    IReadOnlyList<TemplateValueRow> Values, IReadOnlyList<TemplateRecordRow> Records);

public interface ITemplateRepository
{
    Task<IReadOnlyList<TemplateInfo>> ListAsync(TemplateQuery query, CancellationToken ct);
    Task<TemplateInfo?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<TemplateFieldInfo>> GetFieldsAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<TemplateAliasInfo>> GetAllAliasesAsync(CancellationToken ct);
    Task<Result<long>> SaveAsync(TemplateDraft draft, CancellationToken ct);
    Task<Result<bool>> DeleteAsync(string code, CancellationToken ct);
    Task<Result<string>> CloneAsync(string code, string newCode, CancellationToken ct);
}

public enum RuleTargetKind
{
    Template,
    TemplateField,
    Preset,
    Generator,
}

public enum RuleScope
{
    Global,
    Engine,
    Project,
}

public enum RuleSource
{
    BuiltIn,
    User,
    Learned,
}

public sealed record MappingRuleInfo(long Id, string ColumnPattern, string? TablePattern, DatabaseEngine? Engine,
    RuleTargetKind TargetKind, string TargetRef, string? ParametersJson, RuleScope Scope, int Priority, RuleSource Source, bool Enabled);

public interface IMappingRuleRepository
{
    Task<IReadOnlyList<MappingRuleInfo>> ListAsync(CancellationToken ct);
    Task<Result<long>> UpsertAsync(MappingRuleInfo rule, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
}

public sealed record GeneratorPresetInfo(long Id, string Code, string Name, string GeneratorId, int GeneratorVersion,
    string ParametersJson, string? Description, bool IsBuiltIn);

public interface IGeneratorPresetRepository
{
    Task<IReadOnlyList<GeneratorPresetInfo>> ListAsync(CancellationToken ct);
    Task<Result<long>> UpsertAsync(GeneratorPresetInfo preset, CancellationToken ct);
    Task<Result<bool>> DeleteAsync(string code, CancellationToken ct);
}

// Perfil de conexão sem segredo: a senha nunca é gravada no catálogo (23 §3, TASK-04.02).
public sealed record ConnectionProfileInfo(string Id, string Name, DatabaseEngine Engine, string SettingsJson,
    string Environment, bool IsReadOnly, string? Notes);

public interface IConnectionProfileRepository
{
    Task<IReadOnlyList<ConnectionProfileInfo>> ListAsync(CancellationToken ct);
    Task<Result<bool>> SaveAsync(ConnectionProfileInfo profile, CancellationToken ct);
}

public sealed record HistoryEntry(string? ProfileId, DateTimeOffset ExecutedAt, string SqlText, string StatementKind,
    long DurationMs, long? RowsAffected, bool Success, string? ErrorCode);

public interface IQueryHistoryRepository
{
    Task AddAsync(HistoryEntry entry, CancellationToken ct);
    Task<IReadOnlyList<HistoryEntry>> ListAsync(int limit, CancellationToken ct);
}

// Evidência de mapeamento aceito/rejeitado pelo usuário (14 §5). Sem reforço automático nesta sprint.
public sealed record MappingFeedbackInfo(string ColumnCompact, RuleTargetKind TargetKind, string TargetRef, int AcceptedCount, int RejectedCount, DateTimeOffset LastAt);

public interface IMappingFeedbackRepository
{
    Task RecordAsync(string columnCompact, RuleTargetKind targetKind, string targetRef, bool accepted, CancellationToken ct);
    Task<IReadOnlyList<MappingFeedbackInfo>> ListAsync(string columnCompact, CancellationToken ct);
}

// Registro de execuções de geração (generation_run, docs/09 §3). O relatório fica como JSON opaco.
public sealed record GenerationRunInfo(string RunId, string ProjectId, string? ProjectPath, string ProfileName, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, string Status, string Seed, string PlanHash, string StructureHash, string OutputKind,
    string? TargetProfileId, string? TargetFingerprint, long RowsTotal, string? ReportJson, string? CleanupPath);

public interface IGenerationRunRepository
{
    Task StartAsync(GenerationRunInfo run, CancellationToken ct);
    Task FinishAsync(string runId, string status, DateTimeOffset finishedAt, long rowsTotal, string? reportJson, CancellationToken ct);
    Task<IReadOnlyList<GenerationRunInfo>> ListAsync(string projectId, int limit, CancellationToken ct);
}

// ---------- Importação via staging (ADR-016) ----------

public enum MergeMode
{
    InsertOnly,
    Upsert,
    ReplaceTemplateData,
}

public sealed record StagingImportRequest(string SqlScript, MergeMode Mode, string? TargetPackCode);

public sealed record StagingAnalysis(
    int TemplatesNew, int TemplatesChanged, int AliasesNew, int ValuesNew, int ValuesDuplicated,
    int RecordsNew, IReadOnlyList<ImportIssue> Issues, bool CanMerge, string StagingSessionId);

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ImportIssue(IssueSeverity Severity, string Code, string Message, string? Table, int? StatementIndex, string? Detail);

public sealed record MergeReport(int Inserted, int Updated, int Skipped, string BackupPath);

public interface IStagingImporter
{
    // Executa o script num SQLite temporário com o schema de staging; NÃO toca o catálogo.
    Task<Result<StagingAnalysis>> AnalyzeAsync(StagingImportRequest request, IProgress<int>? progress, CancellationToken ct);

    // Aplica a análise ao catálogo numa única transação (backup premerge antes).
    Task<Result<MergeReport>> MergeAsync(string stagingSessionId, CancellationToken ct);

    void Discard(string stagingSessionId);
}

public sealed record CatalogHealth(bool IntegrityOk, int SchemaVersion, IReadOnlyList<string> Messages);

public interface ICatalogMaintenance
{
    Task<string> BackupAsync(string? destinationPath, CancellationToken ct);

    // Exige reinício do catálogo para ser efetiva.
    Task<Result<bool>> RestoreAsync(string backupPath, CancellationToken ct);

    Task<CatalogHealth> CheckIntegrityAsync(CancellationToken ct);
}
