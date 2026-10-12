# C08 — Contratos de saída (`MockDataStudio.Output`)

```csharp
namespace MockDataStudio.Output;

public sealed record OutputContext(
    Guid RunId, string ProjectName, string ProfileName, GenerationPlan Plan, SchemaModel Schema,
    OutputSettings Settings, ISqlDialect? Dialect, ScriptHeader Header);

public sealed record ScriptHeader(string ToolVersion, string StructureHash, string PlanHash, ulong Seed,
    DateTimeOffset ReferenceInstant, DateTimeOffset GeneratedAt, string TargetEngine, string? TargetServerVersion,
    IReadOnlyDictionary<string, int> GeneratorVersions);

/// IRowSink (C06) define os métodos de escrita chamados pelo engine; IOutputWriter acrescenta o ciclo de vida.
public interface IOutputWriter : IRowSink, IAsyncDisposable
{
    OutputKind Kind { get; }
    Task BeginAsync(OutputContext ctx, CancellationToken ct);
    Task<OutputResult> CompleteAsync(RunStatus status, CancellationToken ct);
}

public sealed record OutputArtifact(string Path, long Bytes, string Sha256, int SegmentIndex, string Kind /*script|cleanup|manifest|csv|jsonl*/);
public sealed record OutputResult(IReadOnlyList<OutputArtifact> Artifacts, long RowsWritten, string? ManifestPath);

public interface IOutputWriterFactory
{
    OutputKind Kind { get; }
    IOutputWriter Create(OutputWriterOptions options);
}
public sealed record OutputWriterOptions(OutputSettings Settings, ISqlDialect? Dialect, Func<CancellationToken, Task<DirectInsertSession>>? OpenTarget);

/// Sessão de destino para inserção direta (aberta pela Application com o perfil de destino confirmado).
public sealed class DirectInsertSession : IAsyncDisposable
{
    public DbConnection Connection { get; }
    public IDatabaseProvider Provider { get; }
    public string ServerVersion { get; }
    public string FingerprintHash { get; }
}

public interface ISegmentedTextSink : IAsyncDisposable
{
    /// Escreve um comando completo; abre novo segmento ANTES do comando se exceder SegmentMaxBytes.
    Task WriteStatementAsync(string statement, CancellationToken ct);
    Task WriteCommentAsync(string comment, CancellationToken ct);
    IReadOnlyList<OutputArtifact> Completed { get; }
}

public interface ICleanupScriptBuilder
{
    /// DELETE em ordem reversa de dependência, por faixas de chave ou lista de chaves capturadas.
    IReadOnlyList<string> Build(GenerationPlan plan, IReadOnlyList<TableRunResult> results, ISqlDialect dialect);
}

public sealed record RunManifest(string Format /* "mds.run-manifest" */, string Version /* "1.0" */, Guid RunId,
    string Status, ScriptHeader Header, IReadOnlyList<OutputArtifact> Segments, IReadOnlyList<TableRunResult> Tables, string ExecutionOrderNote);
```
