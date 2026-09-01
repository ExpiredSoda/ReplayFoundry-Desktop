using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Generate.Rendering;

internal sealed record GenerationClipOutputProfile(
    int Width,
    int Height,
    int FramesPerSecond)
{
    public static GenerationClipOutputProfile FromReference(
        VideoStreamInfo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        EffectiveDisplayGeometry geometry =
            EffectiveDisplayGeometryCalculator.Calculate(video);
        const int maximumLongEdge = 1920;
        const int maximumShortEdge = 1080;
        double scale = Math.Min(
            1d,
            Math.Min(
                maximumLongEdge /
                    (double)Math.Max(geometry.Width, geometry.Height),
                maximumShortEdge /
                    (double)Math.Min(geometry.Width, geometry.Height)));
        int width = PositiveEvenNearest(geometry.Width * scale);
        int height = PositiveEvenNearest(geometry.Height * scale);
        int framesPerSecond =
            video.PreferredFrameRate is >= 50
                ? 60
                : 30;
        return new(width, height, framesPerSecond);
    }

    public string DisplayText =>
        $"{Width} × {Height} · {FramesPerSecond} FPS";

    private static int PositiveEvenNearest(double value)
    {
        int rounded = Math.Max(2, checked((int)Math.Round(value)));
        return (rounded + 1) & ~1;
    }
}
