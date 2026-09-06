using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal static class StudioInitialCaptionPlacement
{
    public static StudioClipAppearance Create(GenerationCaptionStylePreset style, CompositionPlan plan,
        TimeSpan start, TimeSpan end, VideoStreamInfo video, StudioRenderSettings renderSettings,
        StudioCaptionLook? savedLook = null)
    {
        // Only initial generation defaults use this policy. Saved looks and
        // all later Studio edits retain the creator's exact placement.
        if (savedLook is not null) return savedLook.CreateAppearance();
        StudioClipAppearance appearance = StudioClipAppearance.CreateDefault(style);
        var display = EffectiveDisplayGeometryCalculator.Calculate(video);
        var crop = renderSettings.GameplayRegion;
        if (display.Height <= display.Width || renderSettings.Layout != StudioCompositionLayout.Fit ||
            renderSettings.Canvas is not (StudioOutputCanvas.Portrait or StudioOutputCanvas.Source) ||
            crop.X != 0 || crop.Y != 0 || crop.Width != 1 || crop.Height != 1 ||
            renderSettings.FrameKeyframes.Count > 0)
            return appearance;
        var intervals = plan.Intervals.Where(interval => interval.Start < end && interval.End > start).ToArray();
        if (intervals.Length == 0) return appearance;
        var gameplay = intervals.Select(interval => interval.Regions.FirstOrDefault(region =>
            region.Role == CompositionRegionRole.Gameplay && Confirmed(region))).ToArray();
        if (gameplay.Any(static region => region is null)) return appearance;
        var presenters = intervals.SelectMany(static interval => interval.Regions).Where(region =>
            region.Role == CompositionRegionRole.Presenter && Confirmed(region)).ToArray();
        if (presenters.Length == 0) return appearance;
        double sourceAspect = display.Width / (double)display.Height;
        double canvasAspect = renderSettings.Canvas == StudioOutputCanvas.Source ? sourceAspect : 9d / 16;
        double scaleX = Math.Min(1, sourceAspect / canvasAspect);
        double scaleY = Math.Min(1, canvasAspect / sourceAspect);
        double offsetX = (1 - scaleX) / 2, offsetY = (1 - scaleY) / 2;
        const double halfCaptionHeight = .045;
        double halfCaptionWidth = appearance.CaptionMaximumWidthPercent / 200;
        bool HitsPresenter(double center) => presenters.Any(region =>
            offsetX + (region.Geometry.X + region.Geometry.Width) * scaleX > .5 - halfCaptionWidth &&
            offsetX + region.Geometry.X * scaleX < .5 + halfCaptionWidth &&
            offsetY + (region.Geometry.Y + region.Geometry.Height) * scaleY > center - halfCaptionHeight &&
            offsetY + region.Geometry.Y * scaleY < center + halfCaptionHeight);
        double defaultCenter = appearance.CaptionTypography.ConstrainVerticalPosition(appearance.CaptionVerticalPositionPercent) / 100;
        if (!HitsPresenter(defaultCenter)) return appearance;
        double top = gameplay.Max(region => offsetY + region!.Geometry.Y * scaleY);
        double bottom = gameplay.Min(region => offsetY + (region!.Geometry.Y + region.Geometry.Height) * scaleY);
        if (bottom - top < .12) return appearance;
        double position = appearance.CaptionTypography.ConstrainVerticalPosition(
            Math.Clamp(Math.Round((bottom - .06) * 100, 2),
                StudioClipAppearance.MinimumCaptionVerticalPositionPercent, StudioClipAppearance.MaximumCaptionVerticalPositionPercent));
        if (position / 100 - halfCaptionHeight < top || position / 100 + halfCaptionHeight > bottom || HitsPresenter(position / 100))
            return appearance;
        return new StudioCaptionLook(style, position, appearance.CaptionWordLimit,
            appearance.CaptionMaximumWidthPercent, appearance.CaptionFontScalePercent, appearance.CaptionTypography).ApplyTo(appearance);
    }

    private static bool Confirmed(CompositionRegion region) =>
        region.RoleSource == CompositionValueSource.UserConfirmed && region.GeometrySource == CompositionValueSource.UserConfirmed;
}
