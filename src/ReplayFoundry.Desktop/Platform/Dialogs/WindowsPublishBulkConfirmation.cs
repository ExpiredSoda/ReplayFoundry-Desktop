using System.Windows;
using ReplayFoundry.Desktop.Features.Publish;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsPublishBulkConfirmation : IPublishBulkConfirmation
{
    public bool ConfirmPublishAllNow(int videoCount)
    {
        if (videoCount <= 0) throw new ArgumentOutOfRangeException(nameof(videoCount));
        var dialog = new PublishBulkConfirmationWindow(videoCount);
        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(static candidate => candidate.IsActive);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
