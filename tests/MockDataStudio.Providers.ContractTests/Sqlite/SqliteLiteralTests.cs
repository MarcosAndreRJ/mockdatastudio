using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;
using MockDataStudio.Providers.Sqlite;
using Shouldly;
using Xunit;

namespace MockDataStudio.Providers.ContractTests.Sqlite;

// Literais SQLite com valores adversariais (T-OUT-LIT-SQLITE): cada literal gerado é executado e relido.
public sealed class SqliteLiteralTests
{
    private static readonly string[] AdversarialTexts =
    [
        "O'Reilly",
        "C:\\pasta\\arquivo",
        "a\0b",
        "emoji 😀 e acentuação ção",
        "linha1\nlinha2\r\n",
        "-- não é comentário",
        "/* também não */",
        "x;DROP TABLE t;",
        "$$ $tag$ ?",
        string.Empty,
    ];

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-OUT-LIT-SQLITE")]
    public async Task LiteraisAdversariaisVoltamIguaisAoSerLidosPeloSqlite()
    {
        var literals = SqliteProvider.CreateDialect(SqliteValues.DateStorageText).Literals;
        var text = new DataTypeInfo("text", LogicalType.Text);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        foreach (var value in AdversarialTexts)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT " + literals.Format(value, text);
            var read = Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            read.ShouldBe(value, $"Literal divergente para o valor '{value.Replace("\0", "\\0", StringComparison.Ordinal)}'.");
        }

        literals.Format(null, text).ShouldBe("NULL");
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-SQLITE-DATE-001")]
    public async Task ArmazenamentoDeDatasSegueOPerfilTextUnixOuJulian()
    {
        var moment = new DateTime(2026, 10, 12, 10, 20, 30, DateTimeKind.Utc);
        var type = new DataTypeInfo("datetime", LogicalType.DateTime);
        var unixSeconds = (long)(moment - DateTime.UnixEpoch).TotalSeconds;

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var text = await ReadAsync(connection, SqliteProvider.CreateDialect(SqliteValues.DateStorageText).Literals.Format(moment, type));
        text.Type.ShouldBe("text");
        text.Value.ShouldBe("2026-10-12 10:20:30.000");

        var unix = await ReadAsync(connection, SqliteProvider.CreateDialect(SqliteValues.DateStorageUnix).Literals.Format(moment, type));
        unix.Type.ShouldBe("integer");
        unix.Value.ShouldBe(unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var julian = await ReadAsync(connection, SqliteProvider.CreateDialect(SqliteValues.DateStorageJulian).Literals.Format(moment, type));
        julian.Type.ShouldBe("real");
        double.Parse(julian.Value, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(unixSeconds / 86400.0 + 2440587.5, tolerance: 1e-6);
    }

    private static async Task<(string Type, string Value)> ReadAsync(SqliteConnection connection, string literal)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT typeof({literal}), {literal}";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        return (reader.GetString(0), Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }
}
