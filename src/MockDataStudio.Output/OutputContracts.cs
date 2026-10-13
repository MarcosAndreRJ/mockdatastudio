using System.Data.Common;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Planning;
using MockDataStudio.Providers;

namespace MockDataStudio.Output;

// Contratos de saída (C08, docs/18). O writer implementa IRowSink (Generation) e acrescenta o ciclo de vida.

public sealed record ScriptHeader(
    string ToolVersion, string StructureHash, string PlanHash, ulong Seed,
    DateTimeOffset ReferenceInstant, DateTimeOffset GeneratedAt, string TargetEngine, string? TargetServerVersion,
    IReadOnlyDictionary<string, int> GeneratorVersions,
    string Reproducibility = "Exact");

public sealed record OutputContext(
    Guid RunId, string ProjectName, string ProfileName, GenerationPlan Plan, SchemaModel Schema,
    OutputSettings Settings, ISqlDialect? Dialect, ScriptHeader Header);

public sealed record OutputArtifact(string Path, long Bytes, string Sha256, int SegmentIndex, string Kind /* script|cleanup|manifest|csv|jsonl */);

public sealed record OutputResult(IReadOnlyList<OutputArtifact> Artifacts, long RowsWritten, string? ManifestPath);

/// <summary>Writer de saída: recebe as linhas do motor e produz arquivos (ou inserções diretas).</summary>
public interface IOutputWriter : IRowSink, IAsyncDisposable
{
    OutputKind Kind { get; }

    Task BeginAsync(OutputContext ctx, CancellationToken ct);

    /// <summary>Fecha a saída. Com <see cref="RunStatus.Completed"/> os arquivos são finalizados; senão ficam como .partial.</summary>
    Task<OutputResult> CompleteAsync(RunStatus status, CancellationToken ct);
}

/// <summary>Sessão de destino para inserção direta (aberta pela Application com o perfil de destino confirmado).</summary>
public sealed class DirectInsertSession(DbConnection connection, IDatabaseProvider provider, string serverVersion, string fingerprintHash)
    : IAsyncDisposable
{
    public DbConnection Connection { get; } = connection;

    public IDatabaseProvider Provider { get; } = provider;

    public string ServerVersion { get; } = serverVersion;

    public string FingerprintHash { get; } = fingerprintHash;

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}

public sealed record OutputWriterOptions(
    OutputSettings Settings, ISqlDialect? Dialect, Func<CancellationToken, Task<DirectInsertSession>>? OpenTarget,
    long? CommitEveryRows = null);

public interface IOutputWriterFactory
{
    OutputKind Kind { get; }

    IOutputWriter Create(OutputWriterOptions options);
}

/// <summary>Arquivo de texto em segmentos: nunca corta um comando e finaliza cada segmento por renomeação atômica.</summary>
public interface ISegmentedTextSink : IAsyncDisposable
{
    /// <summary>Escreve um comando completo; abre novo segmento ANTES do comando se ele exceder o limite do segmento.</summary>
    Task WriteStatementAsync(string statement, CancellationToken ct);

    Task WriteCommentAsync(string comment, CancellationToken ct);

    IReadOnlyList<OutputArtifact> Completed { get; }
}

public interface ICleanupScriptBuilder
{
    /// <summary>DELETE em ordem reversa de dependência, por faixas de chave ou lista de chaves capturadas.</summary>
    IReadOnlyList<string> Build(GenerationPlan plan, IReadOnlyList<TableRunResult> results, ISqlDialect dialect);
}

public sealed record RunManifest(
    string Format /* "mds.run-manifest" */, string Version /* "1.0" */, Guid RunId, string Status, ScriptHeader Header,
    IReadOnlyList<OutputArtifact> Segments, IReadOnlyList<TableRunResult> Tables, string ExecutionOrderNote);

/// <summary>Erro de saída com código OUT-* do catálogo de erros.</summary>
public sealed class OutputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
