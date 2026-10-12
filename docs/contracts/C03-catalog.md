# C03 — Contratos do catálogo (`MockDataStudio.Catalog`)

```csharp
namespace MockDataStudio.Catalog;

using MockDataStudio.Core.Templates;   // TemplateKind, ITemplateDataSource, TemplateDataSet (C05 §2)

public sealed record TemplateInfo(long Id, string Code, string Name, TemplateKind Kind, LogicalType? LogicalType,
    string Locale, IReadOnlyList<string> Tags, string? Description, string PackCode, bool IsBuiltIn,
    long ValueCount, string ContentHash);
public sealed record TemplateFieldInfo(string FieldCode, LogicalType LogicalType, int Ordinal, int? MaxLength, bool Required);
public sealed record TemplateAliasInfo(long Id, string TemplateCode, string? FieldCode, string Alias, string NormalizedAlias, double Weight, string? ContextTablePattern);
public sealed record TemplateValueRow(string Value, double Weight);
public sealed record TemplateRecordRow(string RecordKey, IReadOnlyDictionary<string, string?> Data, double Weight);

public interface ITemplateRepository
{
    Task<IReadOnlyList<TemplateInfo>> ListAsync(TemplateQuery query, CancellationToken ct);
    Task<TemplateInfo?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<TemplateFieldInfo>> GetFieldsAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<TemplateAliasInfo>> GetAllAliasesAsync(CancellationToken ct);       // usado pelo mapeador (cacheado)
    Task<Result<long>> SaveAsync(TemplateDraft draft, CancellationToken ct);                 // valida invariantes
    Task<Result<bool>> DeleteAsync(string code, CancellationToken ct);                       // bloqueado se IsBuiltIn
    Task<Result<string>> CloneAsync(string code, string newCode, CancellationToken ct);
}

// ITemplateDataSource e TemplateDataSet são DEFINIDOS em MockDataStudio.Core.Templates (C05 §2)
// e IMPLEMENTADOS aqui por CatalogTemplateDataSource — Generation não referencia Catalog (AR-02).
public sealed class CatalogTemplateDataSource : ITemplateDataSource { /* ... */ }

public interface IMappingRuleRepository
{
    Task<IReadOnlyList<MappingRuleInfo>> ListAsync(CancellationToken ct);
    Task<Result<long>> UpsertAsync(MappingRuleInfo rule, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
}
public enum RuleTargetKind { Template, TemplateField, Preset, Generator }
public enum RuleScope { Global, Engine, Project }
public enum RuleSource { BuiltIn, User, Learned }
public sealed record MappingRuleInfo(long Id, string ColumnPattern, string? TablePattern, DatabaseEngine? Engine,
    RuleTargetKind TargetKind, string TargetRef, string? ParametersJson, RuleScope Scope, int Priority, RuleSource Source, bool Enabled);

public interface IGeneratorPresetRepository { /* CRUD análogo; ParametersJson validado pelo IGeneratorFactory */ }

// ---------- Importação via staging (ADR-016) ----------
public enum MergeMode { InsertOnly, Upsert, ReplaceTemplateData }
public sealed record StagingImportRequest(string SqlScript, MergeMode Mode, string? TargetPackCode);
public sealed record StagingAnalysis(
    int TemplatesNew, int TemplatesChanged, int AliasesNew, int ValuesNew, int ValuesDuplicated,
    int RecordsNew, IReadOnlyList<ImportIssue> Issues, bool CanMerge, string StagingSessionId);
public enum IssueSeverity { Info, Warning, Error }
public sealed record ImportIssue(IssueSeverity Severity, string Code, string Message, string? Table, int? StatementIndex, string? Detail);

public interface IStagingImporter
{
    /// Executa o script num SQLite temporário com o schema de staging; NÃO toca o catálogo.
    Task<Result<StagingAnalysis>> AnalyzeAsync(StagingImportRequest request, IProgress<int>? progress, CancellationToken ct);
    /// Aplica a análise ao catálogo numa única transação (backup antes).
    Task<Result<MergeReport>> MergeAsync(string stagingSessionId, CancellationToken ct);
    void Discard(string stagingSessionId);
}
public sealed record MergeReport(int Inserted, int Updated, int Skipped, string BackupPath);

// ---------- Packs ----------
public interface ITemplatePackService
{
    Task<Result<string>> ExportAsync(IReadOnlyList<string> templateCodes, string filePath, CancellationToken ct); // JSON template-pack-1.0
    Task<Result<StagingAnalysis>> AnalyzeImportAsync(string filePath, MergeMode mode, CancellationToken ct);       // reutiliza staging
}

public interface ICatalogMaintenance
{
    Task<string> BackupAsync(string? destinationPath, CancellationToken ct); // VACUUM INTO
    Task<Result<bool>> RestoreAsync(string backupPath, CancellationToken ct); // exige reinício do catálogo
    Task<CatalogHealth> CheckIntegrityAsync(CancellationToken ct);
}
public sealed record CatalogHealth(bool IntegrityOk, int SchemaVersion, IReadOnlyList<string> Messages);

public interface ICatalogMigrator
{
    int CurrentVersion { get; }
    int TargetVersion { get; }
    Task<Result<int>> MigrateAsync(CancellationToken ct);  // backup automático antes
}
```
