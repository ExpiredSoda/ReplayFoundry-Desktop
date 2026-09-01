using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsLocalDataCleanupConfirmation :
    ILocalDataCleanupConfirmation
{
    public bool Confirm(ReplayFoundryLocalDataResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dialog = new LocalDataCleanupConfirmationWindow(request);
        System.Windows.Window? owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(static window => window.IsActive);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
