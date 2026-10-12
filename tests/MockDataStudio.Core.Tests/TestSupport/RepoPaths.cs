namespace MockDataStudio.Core.Tests.TestSupport;

// Localiza a raiz do repositório (pasta que contém MockDataStudio.slnx) a partir do diretório de execução.
public static class RepoPaths
{
    private static readonly Lazy<string> Root = new(FindRoot);

    public static string Examples(string engine) =>
        Path.Combine(Root.Value, "docs", "examples", $"digest-{engine}.json");

    public static string Fixtures(params string[] parts) =>
        Path.Combine([Root.Value, "tests", "fixtures", .. parts]);

    public static string ReadExample(string engine) => File.ReadAllText(Examples(engine));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MockDataStudio.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
