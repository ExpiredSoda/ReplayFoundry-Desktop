using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class WpfStudioSourceTrackingReviewService : IStudioSourceTrackingService
{
    private readonly FfmpegStudioSourceTrackingService _service = new();

    public async Task<StudioSourceTrackingReview> AnalyzeAsync(
        GenerationOutputAsset asset, StudioCropTrackingTarget target, StudioTrackingRegion seed,
        TimeSpan sourceStart, TimeSpan duration, double viewportZoom, CancellationToken cancellationToken)
    {
        StudioSourceTrackingDraft draft = await _service.AnalyzeAsync(
            asset, target, new NormalizedRectangle(seed.X, seed.Y, seed.Width, seed.Height),
            sourceStart, duration, viewportZoom, cancellationToken);
        return Project(draft);
    }

    internal static StudioSourceTrackingReview Project(StudioSourceTrackingDraft draft) => new(
        draft.Track,
        draft.Analysis.Samples.Select(sample => new StudioTrackingFrameReview(
            sample.ToString(), Region(sample.Feature), Region(sample.Viewport), sample.AmbiguityMargin)).ToArray(),
        draft.Analysis.Summary,
        draft.Width,
        draft.Height,
        index => CreateFrame(draft, index));

    private static StudioTrackingRegion? Region(NormalizedRectangle? rectangle) => rectangle is null
        ? null
        : new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private static object CreateFrame(StudioSourceTrackingDraft draft, int index)
    {
        var bitmap = BitmapSource.Create(draft.Width, draft.Height, 96, 96, PixelFormats.Gray8, null,
            draft.Frames[index], draft.Width);
        bitmap.Freeze();
        return bitmap;
    }
}
