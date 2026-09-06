using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Generate.Rendering;

internal sealed record GenerationClipOutputProfile(
    int Width,
    int Height,
    double FramesPerSecond)
{
    public static GenerationClipOutputProfile FromAsset(GenerationOutputAsset asset)
    {
        GenerationClipOutputProfile source = FromReference(asset.SourceMedia.PrimaryVideoStream, asset.RenderSettings.Resolution);
        (int shortEdge, int longEdge) = ResolutionEdges(asset.RenderSettings.Resolution);
        return asset.RenderSettings.Canvas switch
        {
            StudioOutputCanvas.Portrait => new(shortEdge, longEdge, source.FramesPerSecond),
            StudioOutputCanvas.Square => new(shortEdge, shortEdge, source.FramesPerSecond),
            StudioOutputCanvas.Landscape => new(longEdge, shortEdge, source.FramesPerSecond),
            _ => source,
        };
    }
    public static GenerationClipOutputProfile FromReference(
        VideoStreamInfo video, StudioOutputResolution resolution = StudioOutputResolution.FullHd1080)
    {
        ArgumentNullException.ThrowIfNull(video);
        EffectiveDisplayGeometry geometry =
            EffectiveDisplayGeometryCalculator.Calculate(video);
        (int maximumShortEdge, int maximumLongEdge) = ResolutionEdges(resolution);
        double scale = Math.Min(
            1d,
            Math.Min(
                maximumLongEdge /
                    (double)Math.Max(geometry.Width, geometry.Height),
                maximumShortEdge /
                    (double)Math.Min(geometry.Width, geometry.Height)));
        int width = PositiveEvenNearest(geometry.Width * scale);
        int height = PositiveEvenNearest(geometry.Height * scale);
        double framesPerSecond = video.PreferredFrameRate is double sourceRate &&
            double.IsFinite(sourceRate) && sourceRate > 0 ? Math.Min(60, sourceRate) : 30;
        return new(width, height, framesPerSecond);
    }

    public string DisplayText =>
        $"{Width} × {Height} · {FramesPerSecond:0.###} FPS";
    public string FfmpegFrameRate => FramesPerSecond.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static (int ShortEdge, int LongEdge) ResolutionEdges(StudioOutputResolution resolution) => resolution switch
    {
        StudioOutputResolution.Hd720 => (720, 1280),
        StudioOutputResolution.FullHd1080 => (1080, 1920),
        StudioOutputResolution.Qhd1440 => (1440, 2560),
        StudioOutputResolution.Uhd2160 => (2160, 3840),
        _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
    };

    private static int PositiveEvenNearest(double value)
    {
        int rounded = Math.Max(2, checked((int)Math.Round(value)));
        return (rounded + 1) & ~1;
    }
}
