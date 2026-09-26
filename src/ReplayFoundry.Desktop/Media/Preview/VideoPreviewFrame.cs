using System.IO;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Preview;

public sealed class VideoPreviewFrame
{
    private readonly byte[] _pngData;

    public VideoPreviewFrame(
        string sourcePath,
        TimeSpan sourceDuration,
        int videoStreamIndex,
        TimeSpan requestedTimestamp,
        TimeSpan? decodedTimestamp,
        int width,
        int height,
        CompositionCoordinateSpace coordinateSpace,
        ReadOnlySpan<byte> pngData,
        VideoPreviewFrameManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "A preview frame requires a fully qualified source path.",
                nameof(sourcePath));
        }

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Preview source duration must be positive.");
        }

        if (videoStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(videoStreamIndex),
                videoStreamIndex,
                "Video stream index cannot be negative.");
        }

        if (requestedTimestamp < TimeSpan.Zero ||
            requestedTimestamp >= sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedTimestamp),
                requestedTimestamp,
                "Requested timestamp must be within the source timeline.");
        }

        if (decodedTimestamp is TimeSpan actual &&
            (actual < TimeSpan.Zero ||
             actual >= sourceDuration))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodedTimestamp),
                decodedTimestamp,
                "Decoded timestamp must be within the source timeline.");
        }

        if (width <= 0 ||
            height <= 0 ||
            (width & 1) != 0 ||
            (height & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Preview dimensions must be positive even values.");
        }

        if (coordinateSpace !=
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinateSpace),
                coordinateSpace,
                "Preview frames must use the effective-display composition coordinate space.");
        }

        if (pngData.IsEmpty)
        {
            throw new ArgumentException(
                "A preview frame requires PNG data.",
                nameof(pngData));
        }

        ArgumentNullException.ThrowIfNull(manifest);

        SourcePath = sourcePath;
        SourceDuration = sourceDuration;
        VideoStreamIndex = videoStreamIndex;
        RequestedTimestamp = requestedTimestamp;
        DecodedTimestamp = decodedTimestamp;
        Width = width;
        Height = height;
        CoordinateSpace = coordinateSpace;
        _pngData = pngData.ToArray();
        Manifest = manifest;
    }

    public string SourcePath { get; }

    public TimeSpan SourceDuration { get; }

    public int VideoStreamIndex { get; }

    public TimeSpan RequestedTimestamp { get; }

    public TimeSpan? DecodedTimestamp { get; }

    public int Width { get; }

    public int Height { get; }

    public CompositionCoordinateSpace CoordinateSpace { get; }

    public ReadOnlyMemory<byte> PngData =>
        _pngData;

    public VideoPreviewFrameManifest Manifest { get; }
}
