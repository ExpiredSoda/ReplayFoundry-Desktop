namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// Describes how much of the source was examined while producing a composition plan.
/// </summary>
public sealed class CompositionCoverage
{
    private CompositionCoverage(
        CompositionCoverageKind kind,
        TimeSpan sourceDuration,
        TimeSpan? requestedSampleInterval,
        TimeSpan? actualSampleInterval,
        int? requestedSampleCount,
        int? decodedSampleCount,
        int? failedSampleCount,
        int? decodedWidth,
        int? decodedHeight,
        string? pixelFormat,
        TimeSpan? maximumSampleGap,
        IEnumerable<CompositionCoverageWindow>? denseCoverageWindows)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                "Source duration must be greater than zero.");
        }

        Kind = kind;
        SourceDuration = sourceDuration;

        var windows = (denseCoverageWindows ?? [])
            .ToArray();

        if (kind != CompositionCoverageKind.FullTimelineSampled)
        {
            if (requestedSampleInterval is not null ||
                actualSampleInterval is not null ||
                requestedSampleCount is not null ||
                decodedSampleCount is not null ||
                failedSampleCount is not null ||
                decodedWidth is not null ||
                decodedHeight is not null ||
                pixelFormat is not null ||
                maximumSampleGap is not null ||
                windows.Length != 0)
            {
                throw new ArgumentException(
                    "Manual and recording-profile coverage cannot contain sampling metrics.");
            }

            DenseCoverageWindows = Array.AsReadOnly(windows);
            return;
        }

        if (requestedSampleInterval is null || requestedSampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedSampleInterval),
                "Requested sample interval must be greater than zero.");
        }

        if (actualSampleInterval is null || actualSampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualSampleInterval),
                "Actual sample interval must be greater than zero.");
        }

        if (requestedSampleCount is null || requestedSampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedSampleCount),
                "Requested sample count must be greater than zero.");
        }

        if (decodedSampleCount is null || decodedSampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodedSampleCount),
                "Decoded sample count cannot be negative.");
        }

        if (failedSampleCount is null || failedSampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failedSampleCount),
                "Failed sample count cannot be negative.");
        }

        if (decodedSampleCount > requestedSampleCount ||
            failedSampleCount > requestedSampleCount ||
            decodedSampleCount + failedSampleCount != requestedSampleCount)
        {
            throw new ArgumentException(
                "Decoded and failed sample counts must account for every requested sample.");
        }

        if (decodedWidth is null || decodedWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodedWidth),
                "Decoded width must be greater than zero.");
        }

        if (decodedHeight is null || decodedHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodedHeight),
                "Decoded height must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(pixelFormat))
        {
            throw new ArgumentException("Pixel format is required.", nameof(pixelFormat));
        }

        if (maximumSampleGap is null ||
            maximumSampleGap < TimeSpan.Zero ||
            maximumSampleGap > sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSampleGap),
                "Maximum sample gap must be between zero and the source duration.");
        }

        TimeSpan? previousEnd = null;
        foreach (var window in windows)
        {
            ArgumentNullException.ThrowIfNull(window);

            if (window.End > sourceDuration)
            {
                throw new ArgumentException(
                    "Dense coverage windows must remain within the source duration.",
                    nameof(denseCoverageWindows));
            }

            if (previousEnd is not null && window.Start < previousEnd)
            {
                throw new ArgumentException(
                    "Dense coverage windows must be ordered and cannot overlap.",
                    nameof(denseCoverageWindows));
            }

            previousEnd = window.End;
        }

        RequestedSampleInterval = requestedSampleInterval;
        ActualSampleInterval = actualSampleInterval;
        RequestedSampleCount = requestedSampleCount;
        DecodedSampleCount = decodedSampleCount;
        FailedSampleCount = failedSampleCount;
        DecodedWidth = decodedWidth;
        DecodedHeight = decodedHeight;
        PixelFormat = pixelFormat.Trim();
        MaximumSampleGap = maximumSampleGap;
        DenseCoverageWindows = Array.AsReadOnly(windows);
    }

    public CompositionCoverageKind Kind { get; }

    public TimeSpan SourceDuration { get; }

    public TimeSpan Start => TimeSpan.Zero;

    public TimeSpan End => SourceDuration;

    public TimeSpan? RequestedSampleInterval { get; }

    public TimeSpan? ActualSampleInterval { get; }

    public int? RequestedSampleCount { get; }

    public int? DecodedSampleCount { get; }

    public int? FailedSampleCount { get; }

    public int? DecodedWidth { get; }

    public int? DecodedHeight { get; }

    public string? PixelFormat { get; }

    public TimeSpan? MaximumSampleGap { get; }

    public IReadOnlyList<CompositionCoverageWindow> DenseCoverageWindows { get; }

    public static CompositionCoverage CreateManual(TimeSpan sourceDuration) =>
        new(
            CompositionCoverageKind.Manual,
            sourceDuration,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    public static CompositionCoverage CreateRecordingProfile(TimeSpan sourceDuration) =>
        new(
            CompositionCoverageKind.RecordingProfile,
            sourceDuration,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    public static CompositionCoverage CreateFullTimelineSampled(
        TimeSpan sourceDuration,
        TimeSpan requestedSampleInterval,
        TimeSpan actualSampleInterval,
        int requestedSampleCount,
        int decodedSampleCount,
        int failedSampleCount,
        int decodedWidth,
        int decodedHeight,
        string pixelFormat,
        TimeSpan maximumSampleGap,
        IEnumerable<CompositionCoverageWindow>? denseCoverageWindows = null) =>
        new(
            CompositionCoverageKind.FullTimelineSampled,
            sourceDuration,
            requestedSampleInterval,
            actualSampleInterval,
            requestedSampleCount,
            decodedSampleCount,
            failedSampleCount,
            decodedWidth,
            decodedHeight,
            pixelFormat,
            maximumSampleGap,
            denseCoverageWindows);
}
