using ReplayFoundry.Desktop.Features.Generate.RecentProjects;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsRecentProjectsClearConfirmation :
    IRecentProjectsClearConfirmation
{
    public bool ConfirmClear(int projectCount)
    {
        var dialog = new RecentProjectsClearConfirmationWindow(projectCount);
        System.Windows.Window? owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(static window => window.IsActive);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        return dialog.ShowDialog() == true;
    }
}
