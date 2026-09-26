using System;
using System.Collections.Generic;
using Microsoft.Win32;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.Desktop.Platform.Dialogs;

internal sealed class WindowsVideoFilePicker : IVideoFilePicker
{
    private const string VideoFileFilter =
        "Supported video files (*.mp4;*.mkv;*.mov;*.avi)|" +
        "*.mp4;*.mkv;*.mov;*.avi";

    public IReadOnlyList<string> PickSingleVideo()
    {
        return PickVideos(
            allowMultipleFiles: false);
    }

    public IReadOnlyList<string> PickMultipleVideos()
    {
        return PickVideos(
            allowMultipleFiles: true);
    }

    private static IReadOnlyList<string> PickVideos(
        bool allowMultipleFiles)
    {
        var dialog = new OpenFileDialog
        {
            Title = allowMultipleFiles
                ? "Select Video Files"
                : "Select a Video File",

            Filter = VideoFileFilter,
            Multiselect = allowMultipleFiles,
            CheckFileExists = true,
            CheckPathExists = true,
        };

        bool? result = dialog.ShowDialog();

        return result == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }
}
