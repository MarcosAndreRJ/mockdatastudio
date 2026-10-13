using System.Text.Json;

namespace MockDataStudio.Application.Explorer;

// Estado de uma aba do editor para recuperação (C6r.3). Não guarda credenciais: só o perfil (id) e o database.
public sealed record TabSnapshot(int Number, Guid? ProfileId, string? Database, string Text, string? ScriptPath);

// Arquivo JSON com as abas abertas. A gravação é atômica (temporário + troca) e a leitura é tolerante:
// arquivo ausente ou corrompido devolve lista vazia em vez de impedir a abertura do editor.
public sealed class EditorTabStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    public async Task SaveAsync(IReadOnlyList<TabSnapshot> tabs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);

        var temporary = Path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, tabs, Options, ct);
        }

        File.Move(temporary, Path, overwrite: true);
    }

    public async Task<IReadOnlyList<TabSnapshot>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(Path);
            var tabs = await JsonSerializer.DeserializeAsync<List<TabSnapshot>>(stream, cancellationToken: ct);
            return tabs ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
