using System.IO;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class StudioCaptionSidecarPaths
{
    // A same-stem sibling is auto-loaded by common players. Keep optional
    // upload/editing captions away from a video that already contains them.
    // Avoid conventional "subs"/"subtitles" directories, also auto-scanned.
    internal static string Resolve(string video, string extension, bool burnedCaptions) =>
        burnedCaptions
            ? Path.Combine(Path.GetDirectoryName(video)!, "caption-files",
                Path.GetFileNameWithoutExtension(video) + extension)
            : Path.ChangeExtension(video, extension);
}
