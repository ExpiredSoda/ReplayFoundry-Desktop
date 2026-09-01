using System.Windows;
using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Platform.RuntimePacks;

internal interface IRuntimeMaintenanceApplicationLifecycle
{
    bool RequestMainWindowClose();
    void ForceShutdown();
}

internal static class RuntimeMaintenanceShutdownCoordinator
{
    public static void Request(
        IRuntimeMaintenanceApplicationLifecycle application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!application.RequestMainWindowClose())
            application.ForceShutdown();
    }
}

internal sealed class WpfRuntimeMaintenanceApplicationLifecycle :
    IRuntimeMaintenanceApplicationLifecycle
{
    public bool RequestMainWindowClose()
    {
        Window? window = Application.Current?.MainWindow;
        if (window is null)
            return false;

        if (window.Dispatcher.CheckAccess())
        {
            window.Close();
        }
        else
        {
            _ = window.Dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(window.Close));
        }

        return true;
    }

    public void ForceShutdown() => Application.Current?.Shutdown();
}
