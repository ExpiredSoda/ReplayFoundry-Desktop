using System.IO;

namespace ReplayFoundry.Desktop.Features.Publish;

/// <summary>File snapshot at upload time. A replacement must never inherit an earlier upload badge.</summary>
public sealed record PublishedFileRevision(long Length, long LastWriteUtcTicks)
{
    public static PublishedFileRevision? Capture(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new(file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
