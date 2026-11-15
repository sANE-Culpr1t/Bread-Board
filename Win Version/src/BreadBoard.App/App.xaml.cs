using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using BreadBoard.App.Diagnostics;
using BreadBoard.App.Lifecycle;
using BreadBoard.App.Views;
using BreadBoard.Core;
using BreadBoard.Core.Logging;
using BreadBoard.Platform;

namespace BreadBoard.App;

/// <summary>
/// Startup order: options → data paths → logging → crash handlers → single-instance check →
/// services (AppHost) → main window. Shutdown runs from the window's close path via
/// <see cref="ExitAsync"/> so audio and settings are released before the process ends.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;
    private MainWindow? _window;

    public static StartupOptions Options { get; private set; } = new(null, false, null);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var exportIcon = Array.IndexOf(e.Args, "--export-icon");
        if (exportIcon >= 0 && exportIcon + 1 < e.Args.Length)
        {
            var png = Array.IndexOf(e.Args, "--export-png");
            IconExport.Run(e.Args[exportIcon + 1], png >= 0 && png + 1 < e.Args.Length ? e.Args[png + 1] : null);
            Shutdown(0);
            return;
        }
        Options = StartupOptions.Parse(e.Args);
        if (Options.DataDir is not null) AppPaths.UseRoot(Options.DataDir);
        Log.Init(AppPaths.Logs);
        Log.Info($"Starting Bread Board {typeof(App).Assembly.GetName().Version} (.NET {Environment.Version}, {Environment.OSVersion.VersionString})");

        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogFatal("Unhandled exception", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Unobserved task exception", a.Exception); a.SetObserved(); };

        _instance = SingleInstance.TryAcquire(AppPaths.Roaming);
        if (_instance is null)
        {
            Log.Info("Another instance is running; asked it to come to the front");
            Log.Flush();
            Shutdown(0);
            return;
        }

        _host = new AppHost(Options);
        _window = new MainWindow(_host, ExitAsync);
        _instance.ListenForActivation(() => Dispatcher.BeginInvoke(_window.BringToFront));
        _window.Show();

        if (Options.SmokeTest) _ = SmokeTest.RunAsync(_host, _window);
        if (Options.PerfSeconds is { } secs) _ = PerfProbe.RunAsync(_window, secs);
        if (Options.ScreenshotDir is { } shots) _ = ScreenshotTour.RunAsync(_host, _window, shots);
    }

    /// <summary>Orderly exit: services first, then the dispatcher.</summary>
    public async Task ExitAsync(int exitCode = 0)
    {
        if (_host is not null) await _host.ShutdownAsync().ConfigureAwait(true);
        _instance?.Dispose();
        _instance = null;
        Shutdown(exitCode);
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal("UI exception", e.Exception);
        e.Handled = true;
        if (!Options.IsAutomated)
            MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to the log in {AppPaths.Logs}.",
                "Bread Board", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            _ = ExitAsync(2);
    }

    private void LogFatal(string what, Exception? ex)
    {
        Log.Error(what, ex);
        try { _host?.Config.Flush(); } catch (Exception flushEx) { Debug.WriteLine(flushEx); }
        Log.Flush();
    }
}
