using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioLayoutPreset
{
    public StudioLayoutPreset(string name, string? gameName, StudioOutputCanvas canvas, StudioCompositionLayout layout,
        NormalizedRectangle gameplayRegion, NormalizedRectangle? facecamRegion, double facecamHeightPercent,
        StudioCanvasDecoration? decoration = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || gameName?.Trim().Length > 120)
            throw new ArgumentException("Use a layout name of 1–80 characters and an optional game name of up to 120 characters.");
        var validated = new StudioRenderSettings(canvas, layout, gameplayRegion, facecamRegion, facecamHeightPercent, decoration: decoration);
        Name = name.Trim(); GameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim();
        Canvas = validated.Canvas; Layout = validated.Layout; GameplayRegion = validated.GameplayRegion;
        FacecamRegion = validated.FacecamRegion; FacecamHeightPercent = validated.FacecamHeightPercent; Decoration = validated.Decoration;
    }
    public string Name { get; }
    public string? GameName { get; }
    public StudioOutputCanvas Canvas { get; }
    public StudioCompositionLayout Layout { get; }
    public NormalizedRectangle GameplayRegion { get; }
    public NormalizedRectangle? FacecamRegion { get; }
    public double FacecamHeightPercent { get; }
    public StudioCanvasDecoration Decoration { get; }
    public override string ToString() => GameName is null ? Name : GameName + " · " + Name;
    public static StudioLayoutPreset Capture(string name, string? game, StudioRenderSettings settings) => new(
        name, game, settings.Canvas, settings.Layout, settings.GameplayRegion, settings.FacecamRegion, settings.FacecamHeightPercent, settings.Decoration);
    public StudioRenderSettings Apply(StudioRenderSettings current) => new StudioRenderSettings(Canvas, Layout, GameplayRegion, FacecamRegion,
        FacecamHeightPercent, current.AudioTracks, current.VoiceAudioStreamIndex, current.DuckGameplay, current.Quality,
        current.ColorOutput, Canvas == StudioOutputCanvas.Portrait ? current.PlatformPreset : StudioPlatformExportPreset.Custom,
        current.FrameKeyframes, current.TimedTextOverlays, current.AudioMastering, Decoration, current.BurnCaptions, current.Resolution)
        .WithCompatibleSourceCropTracksFrom(current);
}
