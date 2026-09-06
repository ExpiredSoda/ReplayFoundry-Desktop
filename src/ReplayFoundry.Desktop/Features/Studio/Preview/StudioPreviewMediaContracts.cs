using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public enum StudioPreviewRangeMode
{
    EditableEnvelope,
    ExactSelection,
}

public enum StudioPreviewWorkIntent { Foreground, BackgroundPrewarm }

public sealed class StudioPreviewMediaRequest
{
    public StudioPreviewMediaRequest(
        GenerationOutputAsset asset,
        StudioPreviewRangeMode rangeMode =
            StudioPreviewRangeMode.EditableEnvelope,
        StudioPreviewWorkIntent workIntent = StudioPreviewWorkIntent.Foreground)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        if (!Enum.IsDefined(rangeMode) || !Enum.IsDefined(workIntent))
        {
            throw new ArgumentOutOfRangeException(nameof(rangeMode));
        }
        WorkIntent = workIntent;
        // Loudness processing depends on the input window. Match the final cut exactly
        // when it is enabled instead of normalizing the wider trim envelope differently.
        RangeMode = asset.RenderSettings.AudioMastering.NormalizeLoudness ? StudioPreviewRangeMode.ExactSelection : rangeMode;
        SourceStart = RangeMode == StudioPreviewRangeMode.ExactSelection
            ? asset.SourceStart
            : StudioClipBoundaryPolicy.GetEarliestStart(asset);
        SourceEnd = RangeMode == StudioPreviewRangeMode.ExactSelection
            ? asset.SourceEnd
            : StudioClipBoundaryPolicy.GetLatestEnd(asset);
        if (SourceEnd <= SourceStart)
        {
            throw new ArgumentException(
                "Studio preview context must contain a positive source interval.",
                nameof(asset));
        }
    }

    public GenerationOutputAsset Asset { get; }
    public StudioPreviewRangeMode RangeMode { get; }
    public StudioPreviewWorkIntent WorkIntent { get; }
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public TimeSpan Duration => SourceEnd - SourceStart;

}

public sealed class StudioPreviewMediaLease : IDisposable
{
    private Action? _cleanup;

    public StudioPreviewMediaLease(
        string mediaPath,
        TimeSpan sourceOffset,
        TimeSpan duration,
        Action cleanup)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) ||
            !Path.IsPathFullyQualified(mediaPath) ||
            !File.Exists(mediaPath) ||
            sourceOffset < TimeSpan.Zero ||
            duration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Studio preview lease requires an existing bounded media file.",
                nameof(mediaPath));
        }

        MediaPath = Path.GetFullPath(mediaPath);
        SourceOffset = sourceOffset;
        Duration = duration;
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
    }

    public string MediaPath { get; }
    public TimeSpan SourceOffset { get; }
    public TimeSpan Duration { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _cleanup, null)?.Invoke();
    }
}

public interface IStudioPreviewMediaService
{
    Task<StudioPreviewMediaLease> MaterializeAsync(
        StudioPreviewMediaRequest request,
        CancellationToken cancellationToken);
}
