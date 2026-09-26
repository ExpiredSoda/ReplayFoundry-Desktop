using System.IO;
using Microsoft.Win32;
using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsLibraryMediaFilePicker : ILibraryMediaFilePicker
{
    public string? PickReplacementMedia(LibraryMediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string? parent = Path.GetDirectoryName(asset.OutputFullPath);
        var dialog = new OpenFileDialog
        {
            Title = "Find the moved rendered clip",
            CheckFileExists = true,
            Multiselect = false,
            Filter =
                "Rendered video (*.mp4;*.mkv;*.mov;*.avi)|*.mp4;*.mkv;*.mov;*.avi|All files (*.*)|*.*",
            InitialDirectory = parent is not null && Directory.Exists(parent)
                ? parent
                : null,
        };
        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}
