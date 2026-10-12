using System.Text;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Projects;

/// <summary>Leitura e gravação de arquivos .mdsproj. A gravação é atômica e mantém um backup (.bak) do conteúdo anterior.</summary>
public sealed class ProjectStore(IProjectSerializer serializer) : IProjectStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<Result<MdsProject>> OpenAsync(string path, CancellationToken ct)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<MdsProject>.Fail(ErrorCodes.PrjJson, $"Não foi possível ler o arquivo de projeto: {ex.Message}");
        }

        var result = serializer.Deserialize(json);
        if (!result.IsSuccess && File.Exists(BackupPath(path)))
        {
            return Result<MdsProject>.Fail(result.Error.Code,
                result.Error.Message + $" Existe um backup em \"{BackupPath(path)}\" que pode ser aberto.");
        }

        return result;
    }

    public async Task<Result<bool>> SaveAsync(MdsProject project, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        var json = serializer.Serialize(project with { FormatVersion = serializer.CurrentFormatVersion });
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(temp, json, Utf8NoBom, ct);

            if (File.Exists(path))
            {
                File.Replace(temp, path, BackupPath(path));
            }
            else
            {
                File.Move(temp, path);
            }

            return Result<bool>.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return Result<bool>.Fail(ErrorCodes.PrjJson, $"Não foi possível gravar o projeto: {ex.Message}");
        }
    }

    public async Task<Result<bool>> ExportSanitizedAsync(MdsProject project, string path, SanitizeOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var sanitized = Sanitize(project, options);
        var json = serializer.Serialize(sanitized);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, json, Utf8NoBom, ct);
            return Result<bool>.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<bool>.Fail(ErrorCodes.PrjJson, $"Não foi possível gravar a exportação: {ex.Message}");
        }
    }

    /// <summary>Remove do projeto o que não deve ser compartilhado (docs/22 §3).</summary>
    public static MdsProject Sanitize(MdsProject project, SanitizeOptions options)
    {
        ConnectionRef? Clean(ConnectionRef? reference) => reference is null
            ? null
            : reference with
            {
                ProfileId = options.RemoveConnectionRefs ? null : reference.ProfileId,
                FingerprintHash = options.RemoveFingerprints ? null : reference.FingerprintHash,
            };

        var snapshots = project.Snapshots
            .Where(s => !options.KeepOnlyActiveSnapshot || project.ActiveSnapshotId is null || s.Id == project.ActiveSnapshotId)
            .Select(s => options.RemoveFingerprints ? s with { SourceFingerprintHash = null } : s)
            .ToList();

        var runs = options.RemoveRunHistory
            ? []
            : project.Runs.Select(r => options.RemoveFingerprints ? r with { TargetFingerprintHash = null } : r).ToList();

        return project with
        {
            Connections = new ProjectConnections(
                Clean(project.Connections.Source), Clean(project.Connections.Target), Clean(project.Connections.Reference)),
            Snapshots = snapshots,
            Runs = runs,
        };
    }

    private static string BackupPath(string path) => path + ".bak";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Resíduo do .tmp não impede o uso do projeto.
        }
    }
}
