using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Projects;

namespace MockDataStudio.Application;

/// <summary>
/// Projeto aberto na aplicação (S10): criar, abrir, salvar, recentes e histórico de alterações com desfazer/refazer.
/// É a única fonte do projeto corrente; as telas leem <see cref="Current"/> e alteram por <see cref="Update"/>.
/// </summary>
public sealed class ProjectWorkspace(IProjectStore store, IAppSettings settings, IClock clock)
{
    private const int MaxHistory = 100;

    private readonly Stack<(string Description, MdsProject Project)> _undo = new();
    private readonly Stack<(string Description, MdsProject Project)> _redo = new();

    public MdsProject? Current { get; private set; }

    /// <summary>Caminho do arquivo .mdsproj; nulo enquanto o projeto novo não foi salvo.</summary>
    public string? Path { get; private set; }

    public bool IsDirty { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;

    public event EventHandler? Changed;

    public IReadOnlyList<string> RecentProjects => settings.RecentProjects;

    /// <summary>Cria um projeto vazio com um perfil de geração padrão. Descarta o projeto anterior (o chamador confirma alterações).</summary>
    public MdsProject CreateNew(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = clock.UtcNow;
        var profile = new GenerationProfile(
            "default", (ulong)Random.Shared.NextInt64(1, long.MaxValue), now, ReferenceInstantMode.Fixed, "UTC",
            new Dictionary<string, TableSettings>(), KeyStrategy.Explicit, new OutputSettings(OutputKind.SqlScript), new ValidationSettings());

        var project = new MdsProject(
            Guid.NewGuid(), name.Trim(), "1.0", now, now, ProjectConnections.Empty, [], null,
            new Dictionary<string, ColumnBinding>(), [profile], "default", [], [], null);

        Replace(project, path: null, dirty: true);
        return project;
    }

    public async Task<Result<MdsProject>> OpenAsync(string path, CancellationToken ct)
    {
        var result = await store.OpenAsync(path, ct);
        if (!result.IsSuccess)
        {
            settings.RemoveRecentProject(path);
            return result;
        }

        Replace(result.Value, path, dirty: false);
        settings.AddRecentProject(path);
        return result;
    }

    public Task<Result<bool>> SaveAsync(CancellationToken ct) =>
        Path is null
            ? Task.FromResult(Result<bool>.Fail(ErrorCodes.PrjSchema, "O projeto ainda não tem arquivo: use Salvar como."))
            : SaveAsAsync(Path, ct);

    public async Task<Result<bool>> SaveAsAsync(string path, CancellationToken ct)
    {
        if (Current is null)
        {
            return Result<bool>.Fail(ErrorCodes.PrjSchema, "Não há projeto aberto.");
        }

        var toSave = Current with { ModifiedAt = clock.UtcNow };
        var result = await store.SaveAsync(toSave, path, ct);
        if (!result.IsSuccess)
        {
            return result;
        }

        Current = toSave;
        Path = path;
        IsDirty = false;
        settings.AddRecentProject(path);
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Aplica uma alteração ao projeto, registra-a no histórico e marca o projeto como modificado.</summary>
    public void Update(string description, Func<MdsProject, MdsProject> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var current = Current ?? throw new InvalidOperationException("Não há projeto aberto.");

        var next = change(current);
        if (ReferenceEquals(next, current))
        {
            return;
        }

        _undo.Push((description, current));
        TrimHistory();
        _redo.Clear();
        Current = next with { ModifiedAt = clock.UtcNow };
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (Current is null || _undo.Count == 0)
        {
            return false;
        }

        var (description, previous) = _undo.Pop();
        _redo.Push((description, Current));
        Current = previous;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (Current is null || _redo.Count == 0)
        {
            return false;
        }

        var (description, next) = _redo.Pop();
        _undo.Push((description, Current));
        Current = next;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Anexa um snapshot capturado ou importado, torna-o o ativo e registra a origem em <c>Connections.Source</c>.
    /// O Id definitivo ("snap-NNNN") é atribuído aqui.
    /// </summary>
    public SnapshotEntry AttachSnapshot(SnapshotEntry snapshot, ConnectionRef? source)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SnapshotEntry attached = snapshot;
        Update("Capturar estrutura", project =>
        {
            attached = snapshot with { Id = DigestService.NextSnapshotId(project) };
            return project with
            {
                Snapshots = [.. project.Snapshots, attached],
                ActiveSnapshotId = attached.Id,
                Connections = source is null ? project.Connections : project.Connections with { Source = source },
            };
        });
        return attached;
    }

    private void Replace(MdsProject project, string? path, bool dirty)
    {
        _undo.Clear();
        _redo.Clear();
        Current = project;
        Path = path;
        IsDirty = dirty;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TrimHistory()
    {
        if (_undo.Count <= MaxHistory)
        {
            return;
        }

        var kept = _undo.Take(MaxHistory).Reverse().ToList();
        _undo.Clear();
        foreach (var entry in kept)
        {
            _undo.Push(entry);
        }
    }
}
