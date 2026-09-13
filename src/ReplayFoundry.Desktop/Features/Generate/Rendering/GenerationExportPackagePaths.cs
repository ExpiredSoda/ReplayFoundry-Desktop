using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.Rendering;

/// <summary>Keeps finished videos separate from their portable publishing and editing material.</summary>
internal static class GenerationExportPackagePaths
{
    // Avoid conventional subs/subtitles folders, which players can auto-scan.
    internal static string SupportingDirectory(string outputDirectory) =>
        Path.Combine(outputDirectory, "Supporting files");

    internal static string ForVideo(string video, string extension) =>
        Path.Combine(SupportingDirectory(Path.GetDirectoryName(video)!),
            Path.GetFileNameWithoutExtension(video) + extension);

    internal static string FindThumbnail(string video)
    {
        string current = ForVideo(video, ".thumbnail.jpg");
        return File.Exists(current) ? current : Path.ChangeExtension(video, ".thumbnail.jpg");
    }
}
