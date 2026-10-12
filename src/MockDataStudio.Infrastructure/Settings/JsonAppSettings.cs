using System.Text.Json;
using MockDataStudio.Application;

namespace MockDataStudio.Infrastructure.Settings;

/// <summary>Preferências em %LOCALAPPDATA%\MockDataStudio\settings.json. Arquivo ausente ou inválido volta aos padrões.</summary>
public sealed class JsonAppSettings : IAppSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private const int MaxRecent = 10;

    private readonly string _path;

    public JsonAppSettings(string? path = null)
    {
        _path = path ?? Path.Combine(
            AppPaths.Root, "settings.json");
        Load();
    }

    public string Theme { get; set; } = "light";

    public string Language { get; set; } = "pt-BR";

    public bool DefaultReadOnly { get; set; } = true;

    private readonly List<string> _recentProjects = [];

    public IReadOnlyList<string> RecentProjects => _recentProjects;

    public void AddRecentProject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        _recentProjects.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _recentProjects.Insert(0, full);
        if (_recentProjects.Count > MaxRecent)
        {
            _recentProjects.RemoveRange(MaxRecent, _recentProjects.Count - MaxRecent);
        }

        Save();
    }

    public void RemoveRecentProject(string path)
    {
        if (_recentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Data(Theme, Language, DefaultReadOnly, _recentProjects), Options));
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(_path));
            if (data is null)
            {
                return;
            }

            Theme = data.Theme is "light" or "dark" ? data.Theme : Theme;
            Language = string.IsNullOrWhiteSpace(data.Language) ? Language : data.Language;
            DefaultReadOnly = data.DefaultReadOnly;
            _recentProjects.Clear();
            _recentProjects.AddRange((data.RecentProjects ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxRecent));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Preferências corrompidas ou ilegíveis: mantém os padrões.
        }
    }

    private sealed record Data(string Theme, string Language, bool DefaultReadOnly, List<string>? RecentProjects = null);
}
