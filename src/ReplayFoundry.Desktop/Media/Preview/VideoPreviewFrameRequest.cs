using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Preview;

public sealed class VideoPreviewFrameRequest
{
    public const int DefaultMaximumWidth = 1280;
    public const int DefaultMaximumHeight = 720;
    public const int MinimumDimension = 2;
    public const int MaximumDimension = 8192;

    public VideoPreviewFrameRequest(
        MediaProbeResult media,
        TimeSpan timestamp,
        int maximumWidth = DefaultMaximumWidth,
        int maximumHeight = DefaultMaximumHeight,
        NormalizedRectangle? contentRegion = null)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (timestamp < TimeSpan.Zero ||
            timestamp >= media.Duration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                timestamp,
                "Preview timestamp must be within the source timeline.");
        }

        ValidateMaximumDimension(
            maximumWidth,
            nameof(maximumWidth));

        ValidateMaximumDimension(
            maximumHeight,
            nameof(maximumHeight));

        Media = media;
        Timestamp = timestamp;
        MaximumWidth = maximumWidth;
        MaximumHeight = maximumHeight;
        ContentRegion = contentRegion;
    }

    public MediaProbeResult Media { get; }

    public TimeSpan Timestamp { get; }

    public int MaximumWidth { get; }

    public int MaximumHeight { get; }

    /// <summary>
    /// Optional crop in the same effective-display coordinate space used by
    /// composition review and deterministic regional evidence.
    /// </summary>
    public NormalizedRectangle? ContentRegion { get; }

    private static void ValidateMaximumDimension(
        int value,
        string parameterName)
    {
        if (value is < MinimumDimension or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Preview dimensions must be between {MinimumDimension} " +
                $"and {MaximumDimension} pixels.");
        }

        if ((value & 1) != 0)
        {
            throw new ArgumentException(
                "Preview dimensions must be even for consistent media scaling.",
                parameterName);
        }
    }
}
