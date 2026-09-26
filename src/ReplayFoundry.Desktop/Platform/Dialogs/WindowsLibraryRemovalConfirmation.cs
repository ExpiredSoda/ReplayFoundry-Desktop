using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsLibraryRemovalConfirmation : ILibraryRemovalConfirmation
{
    public bool ConfirmRemoveFromLibrary(IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Count == 0 || assets.Any(static asset => asset is null))
        {
            throw new ArgumentException(
                "At least one Library asset is required.",
                nameof(assets));
        }

        var dialog = new LibraryRemovalConfirmationWindow(assets);
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
