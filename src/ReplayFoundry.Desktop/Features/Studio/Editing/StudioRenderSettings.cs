using System.Collections.ObjectModel;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public enum StudioOutputCanvas { Source, Portrait, Square, Landscape }
public enum StudioCompositionLayout { Fit, Fill, FacecamTop, FacecamTopFit }
public enum StudioExportQuality { Compact, Standard, High }
public enum StudioColorOutput { AutomaticSdr, PreserveSdr }
public enum StudioOutputResolution { FullHd1080, Hd720, Qhd1440, Uhd2160 }

public sealed class StudioAudioTrackMix
{
    public StudioAudioTrackMix(int streamIndex, double gainDecibels = 0, bool muted = false)
    {
        if (streamIndex < 0 || !double.IsFinite(gainDecibels) || gainDecibels is < -60 or > 12)
            throw new ArgumentOutOfRangeException(nameof(streamIndex));
        StreamIndex = streamIndex;
        GainDecibels = gainDecibels;
        Muted = muted;
    }
    public int StreamIndex { get; }
    public double GainDecibels { get; }
    public bool Muted { get; }
}

/// <summary>Persisted output decisions shared by the preview and final renderer.</summary>
public sealed class StudioRenderSettings
{
    public StudioRenderSettings(
        StudioOutputCanvas canvas = StudioOutputCanvas.Source,
        StudioCompositionLayout layout = StudioCompositionLayout.Fit,
        NormalizedRectangle? gameplayRegion = null,
        NormalizedRectangle? facecamRegion = null,
        double facecamHeightPercent = 25,
        IReadOnlyList<StudioAudioTrackMix>? audioTracks = null,
        int? voiceAudioStreamIndex = null,
        bool duckGameplay = false,
        StudioExportQuality quality = StudioExportQuality.Standard,
        StudioColorOutput colorOutput = StudioColorOutput.AutomaticSdr,
        StudioPlatformExportPreset platformPreset = StudioPlatformExportPreset.Custom,
        IReadOnlyList<StudioFrameKeyframe>? frameKeyframes = null,
        IReadOnlyList<StudioTimedTextOverlay>? timedTextOverlays = null,
        StudioAudioMastering? audioMastering = null,
        StudioCanvasDecoration? decoration = null,
        bool burnCaptions = true,
        StudioOutputResolution resolution = StudioOutputResolution.FullHd1080,
        IReadOnlyList<StudioSourceCropTrack>? sourceCropTracks = null)
    {
        if (!Enum.IsDefined(canvas) || !Enum.IsDefined(layout) || !Enum.IsDefined(quality) ||
            !Enum.IsDefined(colorOutput) || !Enum.IsDefined(platformPreset) || !Enum.IsDefined(resolution) || !double.IsFinite(facecamHeightPercent) ||
            facecamHeightPercent is < 10 or > 50 || voiceAudioStreamIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(canvas));
        StudioAudioTrackMix[] tracks = audioTracks?.ToArray() ?? [];
        if (tracks.Any(static track => track is null) ||
            tracks.Select(static track => track.StreamIndex).Distinct().Count() != tracks.Length)
            throw new ArgumentException("Audio tracks must have unique stream indices.", nameof(audioTracks));
        Canvas = canvas;
        Layout = layout;
        GameplayRegion = gameplayRegion ?? NormalizedRectangle.FullFrame;
        FacecamRegion = facecamRegion;
        FacecamHeightPercent = facecamHeightPercent;
        AudioTracks = new ReadOnlyCollection<StudioAudioTrackMix>(tracks);
        VoiceAudioStreamIndex = voiceAudioStreamIndex;
        DuckGameplay = duckGameplay;
        Quality = quality;
        ColorOutput = colorOutput;
        PlatformPreset = platformPreset;
        StudioFrameKeyframe[] frames = frameKeyframes?.ToArray() ?? [];
        StudioTimedTextOverlay[] texts = timedTextOverlays?.ToArray() ?? [];
        if (frames.Length > 40 || frames.Any(static frame => frame is null) ||
            frames.Select(static frame => frame.SourcePosition).Distinct().Count() != frames.Length)
            throw new ArgumentException("Use at most 40 framing waypoints, each at a different source time.", nameof(frameKeyframes));
        if (texts.Length > 20 || texts.Any(static text => text is null))
            throw new ArgumentException("Use at most 20 text overlays per clip.", nameof(timedTextOverlays));
        FrameKeyframes = Array.AsReadOnly(frames.OrderBy(static frame => frame.SourcePosition).ToArray());
        TimedTextOverlays = Array.AsReadOnly(texts);
        AudioMastering = audioMastering ?? new();
        Decoration = decoration ?? new();
        BurnCaptions = burnCaptions;
        Resolution = resolution;
        StudioSourceCropTrack[] cropTracks = sourceCropTracks?.ToArray() ?? [];
        if (cropTracks.Length > 2 || cropTracks.Any(static track => track is null) ||
            cropTracks.Select(static track => track.Target).Distinct().Count() != cropTracks.Length ||
            cropTracks.Any(track => !track.MatchesSettings(this)))
            throw new ArgumentException("Use one reviewed track per target, matching the saved manual source region.", nameof(sourceCropTracks));
        SourceCropTracks = Array.AsReadOnly(cropTracks);
    }
    public StudioOutputCanvas Canvas { get; }
    public StudioCompositionLayout Layout { get; }
    public NormalizedRectangle GameplayRegion { get; }
    public NormalizedRectangle? FacecamRegion { get; }
    public double FacecamHeightPercent { get; }
    public IReadOnlyList<StudioAudioTrackMix> AudioTracks { get; }
    public int? VoiceAudioStreamIndex { get; }
    public bool DuckGameplay { get; }
    public StudioExportQuality Quality { get; }
    public StudioColorOutput ColorOutput { get; }
    public StudioPlatformExportPreset PlatformPreset { get; }
    public IReadOnlyList<StudioFrameKeyframe> FrameKeyframes { get; }
    public IReadOnlyList<StudioTimedTextOverlay> TimedTextOverlays { get; }
    public StudioAudioMastering AudioMastering { get; }
    public StudioCanvasDecoration Decoration { get; }
    public bool BurnCaptions { get; }
    public StudioOutputResolution Resolution { get; }
    public IReadOnlyList<StudioSourceCropTrack> SourceCropTracks { get; }
    public StudioRenderSettings WithFrameEdits(IReadOnlyList<StudioFrameKeyframe> frames, IReadOnlyList<StudioTimedTextOverlay> texts) =>
        new(Canvas, Layout, GameplayRegion, FacecamRegion, FacecamHeightPercent, AudioTracks,
            VoiceAudioStreamIndex, DuckGameplay, Quality, ColorOutput, PlatformPreset, frames, texts,
            AudioMastering, Decoration, BurnCaptions, Resolution, SourceCropTracks);
    public StudioRenderSettings WithSourceCropTracks(IReadOnlyList<StudioSourceCropTrack> tracks) =>
        new(Canvas, Layout, GameplayRegion, FacecamRegion, FacecamHeightPercent, AudioTracks,
            VoiceAudioStreamIndex, DuckGameplay, Quality, ColorOutput, PlatformPreset, FrameKeyframes, TimedTextOverlays,
            AudioMastering, Decoration, BurnCaptions, Resolution, tracks);
    public StudioRenderSettings WithCompatibleSourceCropTracksFrom(StudioRenderSettings previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return WithSourceCropTracks(previous.SourceCropTracks.Where(track => track.MatchesSettings(this)).ToArray());
    }
    public string CanonicalIdentity() => JsonSerializer.Serialize(this);

    public static StudioRenderSettings FromComposition(CompositionPlan plan, TimeSpan position,
        ReplayFoundry.Desktop.Media.Inspection.VideoStreamInfo? video = null)
    {
        if (video is not null)
        {
            var display = ReplayFoundry.Desktop.Media.Geometry.EffectiveDisplayGeometryCalculator.Calculate(video);
            if (display.Height > display.Width)
                return new(StudioOutputCanvas.Portrait, StudioCompositionLayout.Fit);
        }
        CompositionLayoutInterval layout = plan.GetLayoutAt(position);
        NormalizedRectangle? gameplay = layout.Regions.FirstOrDefault(
            static region => region.Role == CompositionRegionRole.Gameplay)?.Geometry;
        NormalizedRectangle? facecam = layout.Regions.FirstOrDefault(
            static region => region.Role == CompositionRegionRole.Presenter)?.Geometry;
        return new(StudioOutputCanvas.Portrait,
            facecam is null ? StudioCompositionLayout.Fit : StudioCompositionLayout.FacecamTopFit,
            gameplay, facecam);
    }
}
