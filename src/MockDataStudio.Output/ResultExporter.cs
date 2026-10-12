using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MockDataStudio.Output;

// Exportação de resultados em streaming (prompt, seção 9). Não materializa o conjunto de linhas.
// NULL: JSON "null"; CSV campo vazio sem aspas (distinto de texto vazio, que sai entre aspas).
// Datas em ISO-8601, decimais e números em cultura invariante, binários em base64.
public static class ResultExporter
{
    public static async Task<long> WriteCsvAsync(Stream destination, IReadOnlyList<string> columns,
        IAsyncEnumerable<object?[]> rows, CancellationToken ct, string separator = ",")
    {
        ArgumentNullException.ThrowIfNull(destination);
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        await writer.WriteLineAsync(string.Join(separator, columns.Select(c => Quote(c, separator, forceQuote: true))));

        long count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            var fields = row.Select(v => v is null ? string.Empty : Quote(Text(v), separator, forceQuote: false));
            await writer.WriteLineAsync(string.Join(separator, fields));
            count++;
        }

        await writer.FlushAsync(ct);
        return count;
    }

    public static async Task<long> WriteJsonAsync(Stream destination, IReadOnlyList<string> columns,
        IAsyncEnumerable<object?[]> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();

        long count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            writer.WriteStartObject();
            for (var i = 0; i < columns.Count; i++)
            {
                writer.WritePropertyName(columns[i]);
                WriteValue(writer, i < row.Length ? row[i] : null);
            }

            writer.WriteEndObject();
            count++;
        }

        writer.WriteEndArray();
        await writer.FlushAsync(ct);
        return count;
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case decimal m:
                writer.WriteRawValue(m.ToString(CultureInfo.InvariantCulture), skipInputValidation: true);
                break;
            case double d when double.IsFinite(d):
                writer.WriteNumberValue(d);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateOnly date:
                writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                writer.WriteStringValue(Convert.ToBase64String(bytes));
                break;
            default:
                writer.WriteStringValue(Text(value));
                break;
        }
    }

    private static string Text(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    // RFC 4180: aspas quando há separador, aspas, quebra de linha ou quando forçado (cabeçalho e texto vazio).
    private static string Quote(string text, string separator, bool forceQuote)
    {
        var needs = forceQuote || text.Length == 0 || text.Contains(separator, StringComparison.Ordinal)
            || text.Contains('"') || text.Contains('\n') || text.Contains('\r');
        return needs ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}
