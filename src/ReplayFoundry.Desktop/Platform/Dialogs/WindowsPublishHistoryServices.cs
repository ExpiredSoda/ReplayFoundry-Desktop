using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using ReplayFoundry.Desktop.Features.Publish;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

internal sealed class WindowsPublishHistoryDialogService :
    IPublishHistoryDialogService
{
    public void Show(PublishHistoryViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new PublishHistoryWindow(viewModel);
        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(static candidate => candidate.IsActive);
        if (owner is not null) window.Owner = owner;
        window.ShowDialog();
    }
}

internal sealed class WindowsPublishHistoryLinkLauncher :
    IPublishHistoryLinkLauncher
{
    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!PublishHistoryLinkPolicy.TryCreateTrustedYouTubeUri(
                uri.AbsoluteUri,
                out Uri? trustedUri))
        {
            throw new ArgumentException(
                "A YouTube history link must be a trusted HTTPS YouTube page.",
                nameof(uri));
        }

        _ = Process.Start(new ProcessStartInfo(trustedUri.AbsoluteUri)
        {
            UseShellExecute = true,
        }) ?? throw new Win32Exception(
            "Windows did not open the YouTube history link.");
    }
}
