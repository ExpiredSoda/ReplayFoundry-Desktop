using System.Windows;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Shell;

namespace ReplayFoundry.Desktop;

public partial class App : Application
{
    private ApplicationComposition? _composition;
    private readonly bool _suppressCompositionForResourceTests;
    private int _crashCaptureStarted;

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
        Resources["Motion.IsReduced"] = reducedMotion;
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
        Resources["Accessibility.IsHighContrast"] =
            SystemParameters.HighContrast;
        Resources["Accessibility.TextScale"] = 1d;

        if (_suppressCompositionForResourceTests)
        {
            return;
        }

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException +=
            CurrentDomain_UnhandledException;
        try
        {
            _composition = ApplicationCompositionRoot.Create();
        }
        catch (Exception exception)
        {
            CaptureFatalException(exception);
            throw;
        }
        var window = new MainWindow(
            _composition.MainWindowViewModel);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -=
            CurrentDomain_UnhandledException;
        _composition?.Dispose();
        _composition = null;
        base.OnExit(e);
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
