using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public sealed record VisualSemanticInputManifest
{
    public VisualSemanticInputManifest(
        string reviewVideoPath,
        string reviewVideoSha256,
        long reviewVideoByteLength,
        TimeSpan reviewVideoDuration,
        DateTimeOffset reviewVideoLastWriteTimeUtc)
    {
        if (string.IsNullOrWhiteSpace(reviewVideoPath) ||
            !Path.IsPathFullyQualified(reviewVideoPath))
        {
            throw new ArgumentException(
                "A visual-semantic input video path must be fully qualified.",
                nameof(reviewVideoPath));
        }

        string fullPath = Path.GetFullPath(reviewVideoPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The bounded visual-semantic review video does not exist.",
                fullPath);
        }

        if (reviewVideoByteLength <= 0 ||
            reviewVideoDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reviewVideoByteLength),
                "Review video length and duration must be positive.");
        }

        ModelArtifactManifest.RequireUtc(
            reviewVideoLastWriteTimeUtc,
            nameof(reviewVideoLastWriteTimeUtc));

        ReviewVideoPath = fullPath;
        ReviewVideoSha256 =
            ModelArtifactManifest.Sha256Value(
                reviewVideoSha256,
                nameof(reviewVideoSha256));
        ReviewVideoByteLength = reviewVideoByteLength;
        ReviewVideoDuration = reviewVideoDuration;
        ReviewVideoLastWriteTimeUtc =
            reviewVideoLastWriteTimeUtc;
    }

    public string ReviewVideoPath { get; }

    public string ReviewVideoSha256 { get; }

    public long ReviewVideoByteLength { get; }

    public TimeSpan ReviewVideoDuration { get; }

    public DateTimeOffset ReviewVideoLastWriteTimeUtc { get; }

    public async Task VerifyIntegrityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = new FileInfo(ReviewVideoPath);
        before.Refresh();
        RequireSnapshot(before);

        await using var stream =
            new FileStream(
                ReviewVideoPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
        string actualSha256 =
            Convert.ToHexString(
                await SHA256.HashDataAsync(
                    stream,
                    cancellationToken));
        var after = new FileInfo(ReviewVideoPath);
        after.Refresh();
        RequireSnapshot(after);

        if (!string.Equals(
                actualSha256,
                ReviewVideoSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Review-video integrity failed for '{ReviewVideoPath}': SHA-256 changed.");
        }
    }

    private void RequireSnapshot(FileInfo info)
    {
        DateTimeOffset lastWriteTimeUtc =
            new(
                DateTime.SpecifyKind(
                    info.LastWriteTimeUtc,
                    DateTimeKind.Utc));

        if (!info.Exists ||
            info.Length != ReviewVideoByteLength ||
            lastWriteTimeUtc != ReviewVideoLastWriteTimeUtc)
        {
            throw new InvalidDataException(
                $"Review-video integrity failed for '{ReviewVideoPath}': file length or UTC last-write time changed.");
        }
    }
}
