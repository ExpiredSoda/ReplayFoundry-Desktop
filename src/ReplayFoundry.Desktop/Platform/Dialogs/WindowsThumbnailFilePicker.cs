using Microsoft.Win32;
using ReplayFoundry.Desktop.Features.Publish;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

internal sealed class WindowsThumbnailFilePicker : IThumbnailFilePicker
{
    public string? PickThumbnail()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a YouTube Thumbnail",
            Filter = "YouTube thumbnails (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
        };
        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}
