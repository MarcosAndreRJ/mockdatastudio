using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MockDataStudio.Infrastructure.Logging;

public static class LoggingSetup
{
    /// <summary>Pasta padrão de logs: %LOCALAPPDATA%\MockDataStudio\logs.</summary>
    public static string DefaultLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MockDataStudio", "logs");

    /// <summary>
    /// Arquivo rolling diário (mds-AAAAMMDD.log), retenção de 14 arquivos, 20 MB por arquivo, JSON compacto com redação de segredos
    /// (docs/25 §2, docs/23 §6). O chamador descarta o logger ao encerrar para liberar o arquivo.
    /// </summary>
    public static Logger CreateLogger(string? directory = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var path = Path.Combine(directory ?? DefaultLogDirectory(), "mds-.log");
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Destructure.With<ConnectionDestructuringPolicy>()
            .WriteTo.File(
                new RedactingJsonFormatter(),
                path,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 20L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: false)
            .CreateLogger();
    }
}
