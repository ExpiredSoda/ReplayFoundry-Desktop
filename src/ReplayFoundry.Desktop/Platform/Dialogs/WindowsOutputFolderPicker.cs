using System.IO;
using Microsoft.Win32;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsOutputFolderPicker : IOutputFolderPicker
{
    public string? PickOutputFolder(string currentRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRootDirectory);
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where Replay Foundry renders finished clips",
            Multiselect = false,
            InitialDirectory = Directory.Exists(currentRootDirectory)
                ? currentRootDirectory
                : null,
        };
        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }
}
