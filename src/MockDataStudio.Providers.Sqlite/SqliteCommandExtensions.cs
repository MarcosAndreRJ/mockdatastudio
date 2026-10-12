using System.Data.Common;

namespace MockDataStudio.Providers.Sqlite;

// Parâmetros via CreateParameter: a coleção genérica DbParameterCollection não expõe AddWithValue.
internal static class SqliteCommandExtensions
{
    public static void AddParameter(this DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
