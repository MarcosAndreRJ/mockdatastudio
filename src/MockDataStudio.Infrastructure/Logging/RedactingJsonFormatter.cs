using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace MockDataStudio.Infrastructure.Logging;

/// <summary>
/// Formato JSON compacto (uma linha por evento) com mensagem, exceção e valores textuais passados por <see cref="SecretRedactor"/>.
/// </summary>
public sealed class RedactingJsonFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("@t", logEvent.Timestamp.UtcDateTime.ToString("O"));
            json.WriteString("@l", logEvent.Level.ToString());
            json.WriteString("@m", SecretRedactor.Redact(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)));
            if (logEvent.Exception is { } ex)
            {
                json.WriteString("@x", SecretRedactor.Redact(ex.ToString()));
            }

            foreach (var (name, value) in logEvent.Properties)
            {
                json.WriteString(name, SecretRedactor.Redact(value is ScalarValue { Value: string s } ? s : value.ToString(null, System.Globalization.CultureInfo.InvariantCulture)));
            }

            json.WriteEndObject();
        }

        output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
