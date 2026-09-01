using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourceFileSnapshot
{
    public GenerationSourceFileSnapshot(
        string fullPath,
        long fileLength,
        DateTimeOffset lastWriteTimeUtc)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "A source snapshot requires a path.",
                nameof(fullPath));
        }

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "A source snapshot path must be fully qualified.",
                nameof(fullPath));
        }

        if (fileLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileLength),
                fileLength,
                "A source snapshot file length cannot be negative.");
        }

        if (lastWriteTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A source snapshot last-write timestamp must use UTC.",
                nameof(lastWriteTimeUtc));
        }

        FullPath = fullPath;
        FileLength = fileLength;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public string FullPath { get; }

    public long FileLength { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }
}
