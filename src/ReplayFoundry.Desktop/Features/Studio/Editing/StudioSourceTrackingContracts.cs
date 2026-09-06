using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal readonly record struct StudioTrackingRegion(double X, double Y, double Width, double Height);

internal sealed record StudioTrackingFrameReview(
    string Label, StudioTrackingRegion? Feature, StudioTrackingRegion? Viewport, double? AmbiguityMargin)
{
    public override string ToString() => Label;
}

internal sealed record StudioSourceTrackingReview(
    StudioSourceCropTrack? Track,
    IReadOnlyList<StudioTrackingFrameReview> Samples,
    string Summary,
    int Width,
    int Height,
    Func<int, object> CreateFrameImage);

internal interface IStudioSourceTrackingService
{
    Task<StudioSourceTrackingReview> AnalyzeAsync(
        GenerationOutputAsset asset, StudioCropTrackingTarget target, StudioTrackingRegion seed,
        TimeSpan sourceStart, TimeSpan duration, double viewportZoom, CancellationToken cancellationToken);
}

internal static class StudioSourceTrackingFactory
{
    internal static IStudioSourceTrackingService Create() => new WpfStudioSourceTrackingReviewService();

    internal static StudioTrackingRegion ManualRegion(GenerationOutputAsset? asset, StudioCropTrackingTarget target)
    {
        var region = target == StudioCropTrackingTarget.Hud
            ? asset?.RenderSettings.Decoration.HudSourceRegion
            : asset?.RenderSettings.GameplayRegion;
        return region is null ? new(0, 0, 1, 1) : new(region.X, region.Y, region.Width, region.Height);
    }
}
