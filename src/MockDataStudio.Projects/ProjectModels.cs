using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Profiles;

namespace MockDataStudio.Projects;

// Modelo do projeto (.mdsproj) conforme C09 §1 e docs/22. O arquivo nunca guarda host, usuário, senha ou connection string.

public sealed record ConnectionRef(Guid? ProfileId, string DisplayName, DatabaseEngine Engine, string? FingerprintHash);

public sealed record ProjectConnections(ConnectionRef? Source, ConnectionRef? Target, ConnectionRef? Reference)
{
    public static ProjectConnections Empty { get; } = new(null, null, null);
}

/// <summary>Captura da estrutura do banco. DigestJson é o digest 1.x completo (embutido como objeto no arquivo).</summary>
public sealed record SnapshotEntry(string Id, string Label, DateTimeOffset CapturedAt, string StructureHash,
    string? SourceFingerprintHash, string DigestJson);

public sealed record CatalogDependency(string TemplateCode, string ContentHash, long ValueCount);

public sealed record RunSummary(Guid RunId, string ProfileName, DateTimeOffset StartedAt, string Status, long Rows,
    string PlanHash, ulong Seed, string OutputKind, string? TargetFingerprintHash);

/// <summary>Regra de mapeamento do escopo do projeto (docs/22 §1, "projectRules").</summary>
public sealed record ProjectRule(string ColumnPattern, string? TablePattern, string TargetKind, string TargetRef,
    JsonObject? Parameters, int? Priority);

public sealed record MdsProject(
    Guid Id, string Name, string FormatVersion, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt,
    ProjectConnections Connections, IReadOnlyList<SnapshotEntry> Snapshots, string? ActiveSnapshotId,
    IReadOnlyDictionary<string, ColumnBinding> Bindings, IReadOnlyList<GenerationProfile> Profiles, string ActiveProfile,
    IReadOnlyList<CatalogDependency> CatalogDependencies, IReadOnlyList<RunSummary> Runs, string? Notes,
    IReadOnlyList<ProjectRule>? ProjectRules = null, string? ToolVersion = null);

public sealed record SanitizeOptions(bool RemoveConnectionRefs = true, bool RemoveRunHistory = true,
    bool RemoveFingerprints = true, bool KeepOnlyActiveSnapshot = false);

public interface IProjectSerializer
{
    /// <summary>Versão de formato gravada ao salvar ("1.0").</summary>
    string CurrentFormatVersion { get; }

    /// <summary>JSON indentado (2 espaços, "\n"), chaves de mapas em ordem ordinal.</summary>
    string Serialize(MdsProject project);

    /// <summary>Valida (docs/22 §5) e migra em memória. Erros estruturais devolvem falha com o código PRJ-*.</summary>
    Result<MdsProject> Deserialize(string json);
}

public interface IProjectStore
{
    Task<Result<MdsProject>> OpenAsync(string path, CancellationToken ct);

    /// <summary>Escrita atômica: arquivo.tmp e troca, mantendo um .bak do conteúdo anterior.</summary>
    Task<Result<bool>> SaveAsync(MdsProject project, string path, CancellationToken ct);

    Task<Result<bool>> ExportSanitizedAsync(MdsProject project, string path, SanitizeOptions options, CancellationToken ct);
}
