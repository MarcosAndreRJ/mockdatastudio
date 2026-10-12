namespace MockDataStudio.App.Controls;

public enum SeverityLevel
{
    Error,
    Warning,
    Info,
    Success,
}

// Item de diagnóstico exibido por DiagnosticsList.
public sealed record DiagnosticItem(SeverityLevel Severity, string Code, string Message);
