using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioManualSource(string FullPath, TimeSpan Duration)
{
    public string Name => System.IO.Path.GetFileName(FullPath);

    internal static StudioManualSource FromMedia(MediaProbeResult media) => new(media.FullPath, media.Duration);
}
