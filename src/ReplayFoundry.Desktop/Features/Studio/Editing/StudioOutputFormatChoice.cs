namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioOutputFormatChoice(string Label, StudioPlatformExportPreset Platform, StudioOutputCanvas Canvas)
{
    public static IReadOnlyList<StudioOutputFormatChoice> All { get; } =
    [
        new("YouTube Shorts · vertical", StudioPlatformExportPreset.YouTubeShorts, StudioOutputCanvas.Portrait),
        new("TikTok · vertical", StudioPlatformExportPreset.TikTok, StudioOutputCanvas.Portrait),
        new("Instagram Reels · vertical", StudioPlatformExportPreset.InstagramReels, StudioOutputCanvas.Portrait),
        new("Custom · original shape", StudioPlatformExportPreset.Custom, StudioOutputCanvas.Source),
        new("Custom · vertical 9:16", StudioPlatformExportPreset.Custom, StudioOutputCanvas.Portrait),
        new("Custom · square 1:1", StudioPlatformExportPreset.Custom, StudioOutputCanvas.Square),
        new("Custom · widescreen 16:9", StudioPlatformExportPreset.Custom, StudioOutputCanvas.Landscape),
    ];
}
