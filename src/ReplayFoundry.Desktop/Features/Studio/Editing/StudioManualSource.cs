using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioManualSource(string FullPath, TimeSpan Duration)
{
    public double FramesPerSecond { get; init; } = 30;
    public string Name => System.IO.Path.GetFileName(FullPath);

    internal static StudioManualSource FromMedia(MediaProbeResult media) => new(media.FullPath, media.Duration)
    {
        FramesPerSecond = media.VideoStreams.FirstOrDefault()?.PreferredFrameRate is { } rate &&
            double.IsFinite(rate) && rate > 0 ? rate : 30,
    };
}
