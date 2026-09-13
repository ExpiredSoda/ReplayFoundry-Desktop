namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>An actionable caption review request, separate from encoder and filesystem failures.</summary>
public sealed class StudioCaptionTimingException(string assetId, string segmentId, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string AssetId { get; } = assetId;
    public string SegmentId { get; } = segmentId;
}
