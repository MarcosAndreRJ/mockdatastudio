# C09 — Contratos de projeto e serviços de aplicação

## 1. Projeto (`MockDataStudio.Projects`)

```csharp
namespace MockDataStudio.Projects;

public sealed record ConnectionRef(Guid? ProfileId, string DisplayName, DatabaseEngine Engine, string? FingerprintHash);
public sealed record ProjectConnections(ConnectionRef? Source, ConnectionRef? Target, ConnectionRef? Reference);

public sealed record SnapshotEntry(string Id, string Label, DateTimeOffset CapturedAt, string StructureHash,
    string SourceFingerprintHash, string DigestJson /* digest 1.x completo */);

public sealed record CatalogDependency(string TemplateCode, string ContentHash, long ValueCount);

public sealed record RunSummary(Guid RunId, string ProfileName, DateTimeOffset StartedAt, string Status, long Rows,
    string PlanHash, ulong Seed, string OutputKind, string? TargetFingerprintHash);

public sealed record MdsProject(
    Guid Id, string Name, string FormatVersion, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt,
    ProjectConnections Connections, IReadOnlyList<SnapshotEntry> Snapshots, string? ActiveSnapshotId,
    IReadOnlyDictionary<string, ColumnBinding> Bindings, IReadOnlyList<GenerationProfile> Profiles, string ActiveProfile,
    IReadOnlyList<CatalogDependency> CatalogDependencies, IReadOnlyList<RunSummary> Runs, string? Notes);

public interface IProjectSerializer
{
    string CurrentFormatVersion { get; }                        // "1.0"
    string Serialize(MdsProject project);                       // indentado, ordenação estável de chaves
    Result<MdsProject> Deserialize(string json);                // valida + migra (22 §5)
}

public interface IProjectStore
{
    Task<Result<MdsProject>> OpenAsync(string path, CancellationToken ct);
    Task<Result<bool>> SaveAsync(MdsProject project, string path, CancellationToken ct);   // escrita atômica (tmp + replace)
    Task<Result<bool>> ExportSanitizedAsync(MdsProject project, string path, SanitizeOptions options, CancellationToken ct);
}
public sealed record SanitizeOptions(bool RemoveConnectionRefs = true, bool RemoveRunHistory = true, bool RemoveFingerprints = true, bool KeepOnlyActiveSnapshot = false);
```

## 2. Serviços de aplicação (`MockDataStudio.Application`)

```csharp
namespace MockDataStudio.Application;

/// Implementado por Infrastructure (DpapiSecretStore). Segredos nunca saem deste serviço exceto para montar a connection string.
public interface ISecretStore
{
    byte[] Protect(string secret);            // DPAPI CurrentUser + entropia da instalação (23 §3)
    string Unprotect(byte[] protectedSecret); // lança CryptographicException se outro usuário/máquina
}

public interface IConnectionService
{
    Task<IReadOnlyList<ConnectionProfile>> ListAsync(CancellationToken ct);
    Task<Result<Guid>> SaveAsync(ConnectionProfile profile, string? newPassword, CancellationToken ct);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct);
    Task<Result<ServerInfo>> TestAsync(ConnectionProfile profile, string? passwordOverride, CancellationToken ct);
    Task<Result<OpenedConnection>> OpenAsync(Guid profileId, ConnectionPurpose purpose, CancellationToken ct);
}
public enum ConnectionPurpose { Explore, Query, Introspect, ReadReferences, InspectTarget, WriteTarget }
public sealed class OpenedConnection : IAsyncDisposable
{ public ConnectionProfile Profile { get; } public DbConnection Connection { get; } public IDatabaseProvider Provider { get; } public ServerInfo Server { get; } }

public interface IDigestService
{
    Task<Result<SnapshotEntry>> CaptureAsync(Guid profileId, CaptureOptions options, IProgress<ExtractionProgress>? progress, CancellationToken ct);
    Task<Result<bool>> ExportAsync(SnapshotEntry snapshot, string path, bool indented, CancellationToken ct);
    Task<Result<SnapshotEntry>> ImportAsync(string path, CancellationToken ct);
    SchemaDiff Compare(SnapshotEntry from, SnapshotEntry to);
}

public interface IMappingService
{
    Task<MappingContext> BuildContextAsync(MdsProject project, CancellationToken ct);       // carrega MappingKnowledge do catálogo
    IReadOnlyList<MappingSuggestion> Suggest(MappingContext ctx, ObjectName table);
    MdsProject AutoBindAll(MdsProject project, MappingContext ctx);
    MdsProject SetBinding(MdsProject project, ColumnBinding binding, bool learnAsRule);
    Task<BindingMigrationResult> MigrateBindingsAsync(MdsProject project, SnapshotEntry newSnapshot, CancellationToken ct); // 12 §9
}
public sealed record BindingMigrationResult(MdsProject Project, int Kept, int NeedsReview, int Orphaned, IReadOnlyList<RenameProposal> RenameProposals);
public sealed record RenameProposal(ColumnRef From, ColumnRef To, double Similarity);

public interface IGenerationService
{
    Task<PlanResult> PlanAsync(MdsProject project, string profileName, Guid? targetProfileId, CancellationToken ct);
    Task<ValidationReport> ValidateAsync(MdsProject project, GenerationPlan plan, CancellationToken ct);
    Task<RowBatch> PreviewAsync(MdsProject project, GenerationPlan plan, ObjectName table, int rows, CancellationToken ct);
    /// Requer ExecutionAuthorization válida quando Output = DirectInsert (23 §5).
    Task<RunOutcome> ExecuteAsync(MdsProject project, GenerationPlan plan, ExecutionAuthorization? authorization,
        IProgress<RunProgress>? progress, CancellationToken ct);
}

/// Token emitido pelo ExecutionGuard após confirmação do usuário; vinculado ao fingerprint do destino e ao PlanHash.
public sealed record ExecutionAuthorization(Guid TargetProfileId, string TargetFingerprintHash, string PlanHash, DateTimeOffset IssuedAt, string ConfirmationText);

public interface IExecutionGuard
{
    GuardDecision EvaluateQuery(ConnectionProfile profile, SqlStatementInfo statement, TransactionMode mode);
    GuardDecision EvaluateGeneration(ConnectionProfile target, GenerationPlan plan);
    Result<ExecutionAuthorization> Authorize(ConnectionProfile target, ServerInfo liveServer, GenerationPlan plan, string typedConfirmation);
}
public enum GuardLevel { Allow, Confirm, TypedConfirm, Block }
public sealed record GuardDecision(GuardLevel Level, string Reason, string? RequiredConfirmationText);
public enum TransactionMode { AutoCommit, Manual }

public interface IQueryExecutionService   // editor SQL
{
    Task<Result<QuerySession>> OpenSessionAsync(Guid profileId, CancellationToken ct);
}
public sealed class QuerySession : IAsyncDisposable
{
    public TransactionMode Mode { get; set; }
    public bool HasPendingTransaction { get; }
    public IAsyncEnumerable<StatementExecutionResult> ExecuteScriptAsync(string script, ScriptExecutionOptions options, CancellationToken ct);
    public Task CommitAsync(CancellationToken ct);
    public Task RollbackAsync(CancellationToken ct);
}
public sealed record ScriptExecutionOptions(bool StopOnError = true, int PageSize = 500, Func<GuardDecision, SqlStatementInfo, Task<bool>>? ConfirmCallback = null);
public sealed record StatementExecutionResult(int Index, SqlStatementInfo Statement, QueryResultPage? FirstPage, int? RecordsAffected, MdsError? Error, TimeSpan Elapsed);
```

`SqlStatementInfo` é definido em `MockDataStudio.SqlTooling`:

```csharp
namespace MockDataStudio.SqlTooling;

public enum SqlDialectKind { Sqlite, MySql, PostgreSql, Firebird }
public enum StatementKind { Select, Insert, Update, Delete, Merge, Ddl, Transaction, Session, Call, ExecuteBlock, ClientDirective, Unknown }
public sealed record SqlStatementInfo(int Index, int StartOffset, int Length, string Text, StatementKind Kind,
    bool IsDestructive, bool HasWhereClause, IReadOnlyList<string> TargetObjects, string? DestructiveReason);

public interface ISqlScriptSplitter { IReadOnlyList<SqlStatementInfo> Split(string script, SqlDialectKind dialect); }
public interface ISqlLexer { IEnumerable<SqlToken> Tokenize(string text, SqlDialectKind dialect, string terminator = ";"); }
public interface ICompletionEngine { IReadOnlyList<CompletionItem> Complete(string text, int caretOffset, SqlDialectKind dialect, ICompletionSchemaSource schema); }
public interface ICompletionSchemaSource
{
    IEnumerable<string> Namespaces(); IEnumerable<ObjectName> Tables(string? ns); IEnumerable<string> Columns(ObjectName table);
}
public enum CompletionKind { Keyword, Namespace, Table, View, Column, Function, Alias, Snippet }
public sealed record CompletionItem(string Label, string InsertText, CompletionKind Kind, string? Detail, int Priority);
```
