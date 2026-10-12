using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MockDataStudio.App.Composition;
using MockDataStudio.App.Resources;

namespace MockDataStudio.App;

public partial class App : Application
{
    private static readonly Action<ILogger, Exception?> LogUnhandledUiException =
        LoggerMessage.Define(LogLevel.Error, new EventId(1, nameof(OnDispatcherUnhandledException)), "Exceção não tratada na interface.");

    private static readonly Action<ILogger, Exception?> LogUnhandledDomainException =
        LoggerMessage.Define(LogLevel.Critical, new EventId(2, nameof(OnDomainUnhandledException)), "Exceção fatal fora da interface.");

    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _host = ServiceRegistration.BuildHost();
        _host.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandledUiException(Logger(), e.Exception);
        MessageBox.Show(Strings.ErrorUnexpected(e.Exception.Message), "Mock Data Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogUnhandledDomainException(Logger(), e.ExceptionObject as Exception ?? new InvalidOperationException("Exceção desconhecida."));
    }

    private ILogger Logger() =>
        _host?.Services.GetService<ILoggerFactory>()?.CreateLogger<App>() ?? (ILogger)NullLogger.Instance;
}
