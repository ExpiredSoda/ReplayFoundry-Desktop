using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation.Accessibility;
using ReplayFoundry.Desktop.Shell;

namespace ReplayFoundry.Desktop;

public partial class App : Application
{
    private static readonly TimeSpan ShutdownQuiescenceTimeout =
        TimeSpan.FromSeconds(30);
    private ApplicationComposition? _composition;
    private HighContrastThemeController? _highContrastTheme;
    private readonly bool _suppressCompositionForResourceTests;
    private int _crashCaptureStarted;
    private bool _shutdownPrepared;
    private bool _shutdownInProgress;

    public App()
    {
    }

    internal App(bool suppressCompositionForResourceTests)
    {
        _suppressCompositionForResourceTests =
            suppressCompositionForResourceTests;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool reducedMotion = !SystemParameters.ClientAreaAnimation;
        if (reducedMotion)
        {
            Resources["Motion.Hover"] = new Duration(TimeSpan.Zero);
            Resources["Motion.Press"] = new Duration(TimeSpan.Zero);
            Resources["Motion.Release"] = new Duration(TimeSpan.Zero);
            Resources["Motion.Panel"] = new Duration(TimeSpan.Zero);
            Resources["Motion.Popup"] = new Duration(TimeSpan.Zero);
            Resources["Motion.Signal"] = new Duration(TimeSpan.Zero);
            Resources["Motion.Ambient"] = new Duration(TimeSpan.Zero);
        }
        _highContrastTheme = new HighContrastThemeController(Resources);

        if (_suppressCompositionForResourceTests)
        {
            return;
        }

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException +=
            CurrentDomain_UnhandledException;
        AsyncDelegateCommand.UnhandledExecutionFailure +=
            AsyncDelegateCommand_UnhandledExecutionFailure;
        try
        {
#if DEBUG
            string? debugProjectPath = e.Args is ["--debug-project", var path] ? path : null;
            _composition = ApplicationCompositionRoot.Create(debugProjectPath);
#else
            _composition = ApplicationCompositionRoot.Create();
#endif
        }
        catch (Exception exception)
        {
            CaptureFatalException(exception);
            throw;
        }
        var window = new MainWindow(
            _composition.MainWindowViewModel);
        MainWindow = window;
        window.Closing += MainWindow_Closing;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -=
            CurrentDomain_UnhandledException;
        AsyncDelegateCommand.UnhandledExecutionFailure -=
            AsyncDelegateCommand_UnhandledExecutionFailure;
        if (MainWindow is not null)
        {
            MainWindow.Closing -= MainWindow_Closing;
        }
        _composition?.Dispose();
        _composition = null;
        _highContrastTheme?.Dispose();
        _highContrastTheme = null;
        base.OnExit(e);
    }

    private async void MainWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_shutdownPrepared || sender is not Window window)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        window.IsEnabled = false;
        try
        {
            if (_composition is not null)
            {
                using var timeout = new CancellationTokenSource(
                    ShutdownQuiescenceTimeout);
                await _composition.StopAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            SafeDiagnosticTrace.Write(
                "Application shutdown exceeded its quiescence timeout",
                new TimeoutException(
                    $"Shutdown work did not finish within {ShutdownQuiescenceTimeout.TotalSeconds:0} seconds."));
        }
        catch (Exception exception)
        {
            SafeDiagnosticTrace.Write(
                "Application shutdown could not fully quiesce",
                exception);
        }
        finally
        {
            _shutdownPrepared = true;
            _shutdownInProgress = false;
            window.Close();
        }
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CaptureFatalException(e.Exception);
        // Keep WPF's fatal behavior. A crash report is only a local record for
        // review on the next launch; capturing it must not hide a corrupt state.
    }

    private void CurrentDomain_UnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CaptureFatalException(exception);
        }
    }

    private void AsyncDelegateCommand_UnhandledExecutionFailure(
        object? sender,
        AsyncCommandFailureEventArgs e)
    {
        bool captured =
            _composition?.UserReports.TryCaptureCrash(e.Exception) == true ||
            LocalCrashReportFallback.TryCapture(e.Exception);
        if (!captured)
        {
            SafeDiagnosticTrace.Write(
                "Asynchronous UI command failed",
                e.Exception);
        }
    }

    private void CaptureFatalException(Exception exception)
    {
        if (Interlocked.Exchange(ref _crashCaptureStarted, 1) != 0)
        {
            return;
        }

        bool captured =
            _composition?.UserReports.TryCaptureCrash(exception) == true ||
            LocalCrashReportFallback.TryCapture(exception);
        if (!captured)
        {
            Interlocked.Exchange(ref _crashCaptureStarted, 0);
        }
    }
}
