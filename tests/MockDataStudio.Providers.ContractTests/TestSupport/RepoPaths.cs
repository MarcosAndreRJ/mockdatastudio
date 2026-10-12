namespace MockDataStudio.Providers.ContractTests.TestSupport;

// Localiza a raiz da solution a partir do diretório de saída dos testes.
internal static class RepoPaths
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);

    public static string Root => RootLazy.Value;

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "MockDataStudio.slnx")) || File.Exists(Path.Combine(dir, "MockDataStudio.sln")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        throw new InvalidOperationException("Não foi possível localizar a raiz da solution MockDataStudio.");
    }
}
