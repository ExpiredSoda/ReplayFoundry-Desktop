using System.Windows;
using ReplayFoundry.Desktop.Features.Publish;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsPublishPreparationDialogService :
    IPublishPreparationDialogService
{
    public void Show(PublishViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new PublishPreparationWindow(viewModel);
        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(static candidate => candidate.IsActive);
        if (owner is not null) window.Owner = owner;
        window.ShowDialog();
    }
}
