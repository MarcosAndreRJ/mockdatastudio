namespace MockDataStudio.Infrastructure;

/// <summary>
/// Pasta de dados do usuário: %LOCALAPPDATA%\MockDataStudio, ou o caminho da variável de ambiente
/// <c>MDS_DATA_DIR</c> quando definida (isolamento de testes e instalações portáteis).
/// </summary>
public static class AppPaths
{
    public const string DataDirVariable = "MDS_DATA_DIR";

    public static string Root
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataDirVariable);
            var root = string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MockDataStudio")
                : overridden;
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);
}
