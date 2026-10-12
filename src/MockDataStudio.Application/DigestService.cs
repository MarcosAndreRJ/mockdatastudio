using System.Text;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Diff;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Schema;
using MockDataStudio.Projects;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Application;

/// <summary>Captura, exporta, importa e compara snapshots de estrutura (C09 §2, docs/12).</summary>
public interface IDigestService
{
    /// <summary>
    /// Conecta com o perfil (propósito Introspect), extrai o schema e gera o digest. O Id do snapshot devolvido é provisório
    /// ("snap-0000"): use <see cref="DigestService.NextSnapshotId"/> ao anexá-lo a um projeto.
    /// </summary>
    Task<Result<SnapshotEntry>> CaptureAsync(Guid profileId, CaptureOptions options, IProgress<ExtractionProgress>? progress, CancellationToken ct);

    Task<Result<bool>> ExportAsync(SnapshotEntry snapshot, string path, bool indented, CancellationToken ct);

    Task<Result<SnapshotEntry>> ImportAsync(string path, CancellationToken ct);

    Result<SchemaDiff> Compare(SnapshotEntry before, SnapshotEntry after);
}

public sealed class DigestService(
    IConnectionService connections,
    IDigestSerializer serializer,
    IStructureHasher hasher,
    ISchemaDiffer differ,
    IClock clock) : IDigestService
{
    public const string ProvisionalId = "snap-0000";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<Result<SnapshotEntry>> CaptureAsync(Guid profileId, CaptureOptions options, IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var opened = await connections.OpenAsync(profileId, ConnectionPurpose.Introspect, ct);
        if (!opened.IsSuccess)
        {
            return Result<SnapshotEntry>.Fail(opened.Error);
        }

        await using var session = opened.Value;
        Result<SchemaModel> extracted;
        try
        {
            extracted = await session.Provider.CreateSchemaExtractor().ExtractAsync(session.Connection, options, progress, ct);
        }
        catch (OperationCanceledException)
        {
            return Result<SnapshotEntry>.Fail(ErrorCodes.PrvCancelled, "Captura cancelada pelo usuário.");
        }
        catch (Exception ex) when (ex is not NotImplementedException)
        {
            var error = session.Provider.ErrorTranslator.Translate(ex);
            return Result<SnapshotEntry>.Fail(error.Code, error.Message);
        }
        catch (NotImplementedException)
        {
            return Result<SnapshotEntry>.Fail(ErrorCodes.PrvUnsupportedVersion,
                $"A captura de estrutura para {session.Provider.DisplayName} ainda não está disponível.");
        }

        if (!extracted.IsSuccess)
        {
            return Result<SnapshotEntry>.Fail(extracted.Error);
        }

        var model = extracted.Value;
        var hash = hasher.Compute(model);
        var digestJson = serializer.Serialize(model, new DigestWriteOptions(Indented: false));
        var capturedAt = clock.UtcNow;
        var label = $"Captura de {model.DatabaseName} em {capturedAt:yyyy-MM-dd HH:mm}";

        return Result<SnapshotEntry>.Ok(new SnapshotEntry(
            ProvisionalId, label, capturedAt, hash, session.Server.FingerprintHash, digestJson));
    }

    public async Task<Result<bool>> ExportAsync(SnapshotEntry snapshot, string path, bool indented, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var model = serializer.Deserialize(snapshot.DigestJson);
        if (!model.IsSuccess)
        {
            return Result<bool>.Fail(model.Error);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var json = serializer.Serialize(model.Value, new DigestWriteOptions(Indented: indented));
            await File.WriteAllTextAsync(path, json, Utf8NoBom, ct);
            return Result<bool>.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<bool>.Fail(ErrorCodes.DigJson, $"Não foi possível gravar o digest: {ex.Message}");
        }
    }

    public async Task<Result<SnapshotEntry>> ImportAsync(string path, CancellationToken ct)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<SnapshotEntry>.Fail(ErrorCodes.DigJson, $"Não foi possível ler o digest: {ex.Message}");
        }

        var model = serializer.Deserialize(json);
        if (!model.IsSuccess)
        {
            return Result<SnapshotEntry>.Fail(model.Error);
        }

        var hash = hasher.Compute(model.Value);
        var label = $"Importado: {model.Value.DatabaseName} ({Path.GetFileName(path)})";
        // O digest é regravado em forma canônica; sem fingerprint de servidor, pois a origem é um arquivo.
        return Result<SnapshotEntry>.Ok(new SnapshotEntry(
            ProvisionalId, label, model.Value.CapturedAt, hash, null, serializer.Serialize(model.Value, new DigestWriteOptions())));
    }

    public Result<SchemaDiff> Compare(SnapshotEntry before, SnapshotEntry after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var from = serializer.Deserialize(before.DigestJson);
        if (!from.IsSuccess)
        {
            return Result<SchemaDiff>.Fail(from.Error);
        }

        var target = serializer.Deserialize(after.DigestJson);
        return target.IsSuccess
            ? Result<SchemaDiff>.Ok(differ.Compare(from.Value, target.Value))
            : Result<SchemaDiff>.Fail(target.Error);
    }

    /// <summary>Próximo identificador "snap-NNNN" livre no projeto.</summary>
    public static string NextSnapshotId(MdsProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var highest = project.Snapshots
            .Select(s => s.Id.StartsWith("snap-", StringComparison.Ordinal) && int.TryParse(s.Id.AsSpan(5), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"snap-{highest + 1:0000}";
    }
}
