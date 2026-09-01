using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsMediaRightsConfirmation : IMediaRightsConfirmation
{
    public bool Confirm(IReadOnlyList<SelectedVideoSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0 || sources.Any(static source => source is null))
        {
            throw new ArgumentException(
                "At least one selected media source is required.",
                nameof(sources));
        }

        var dialog = new MediaRightsConfirmationWindow(sources);
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
