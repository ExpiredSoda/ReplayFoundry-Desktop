using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public sealed class StudioPreviewMediaRequest
{
    public StudioPreviewMediaRequest(GenerationOutputAsset asset)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        SourceStart = StudioClipBoundaryPolicy.GetEarliestStart(asset);
        SourceEnd = StudioClipBoundaryPolicy.GetLatestEnd(asset);
        if (SourceEnd <= SourceStart)
        {
            throw new ArgumentException(
                "Studio preview context must contain a positive source interval.",
                nameof(asset));
        }
    }

    public GenerationOutputAsset Asset { get; }
    public TimeSpan SourceStart { get; }
    public TimeSpan SourceEnd { get; }
    public TimeSpan Duration => SourceEnd - SourceStart;

}

public sealed class StudioPreviewMediaLease : IDisposable
{
    private readonly Action _cleanup;
    private bool _isDisposed;

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
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cleanup();
    }
}

public interface IStudioPreviewMediaService
{
    Task<StudioPreviewMediaLease> MaterializeAsync(
        StudioPreviewMediaRequest request,
        CancellationToken cancellationToken);
}
