using Microsoft.Data.Sqlite;

namespace MockDataStudio.Catalog.Database;

// Abertura do catalog.db com os pragmas do produto (09 §1). Cada operação abre e fecha sua conexão,
// para que backup, restauração e substituição de arquivo não encontrem o arquivo bloqueado (TASK-04.05).
public sealed class CatalogDatabase(string filePath)
{
    public string FilePath { get; } = Path.GetFullPath(filePath);

    public bool Exists => File.Exists(FilePath);

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString(FilePath));
        connection.Open();

        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragmas.ExecuteNonQuery();

        return connection;
    }

    public static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
}
