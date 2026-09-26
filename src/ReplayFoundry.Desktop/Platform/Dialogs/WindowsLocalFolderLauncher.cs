using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

public sealed class WindowsLocalFolderLauncher : ILocalFolderLauncher
{
    public void OpenFolder(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "A local folder path must be fully qualified.",
                nameof(fullPath));
        }

        string normalized = Path.GetFullPath(fullPath);
        Directory.CreateDirectory(normalized);
        Process? process = Process.Start(new ProcessStartInfo(normalized)
        {
            UseShellExecute = true,
        });
        if (process is null)
        {
            throw new Win32Exception(
                "Windows did not open the selected folder.");
        }
    }
}
