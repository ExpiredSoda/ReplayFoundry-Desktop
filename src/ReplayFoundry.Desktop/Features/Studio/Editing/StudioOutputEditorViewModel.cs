using ReplayFoundry.Desktop.Presentation.Commands;
using System.Windows.Input;
using System.Text.Json;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
// Shared immutable geometry; no media execution dependency.
using NormalizedRectangle = ReplayFoundry.Desktop.Media.Composition.NormalizedRectangle;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioAudioTrackEditor : INotifyPropertyChanged
{
    private readonly Action _changed;
    private bool _muted;
    private double _gain;
    public StudioAudioTrackEditor(int streamIndex, string label, StudioAudioTrackMix? mix, Action changed)
    {
        StreamIndex = streamIndex; Label = label; _changed = changed;
        _muted = mix?.Muted ?? false; _gain = mix?.GainDecibels ?? 0;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public int StreamIndex { get; }
    public string Label { get; }
    public bool Muted { get => _muted; set { if (_muted == value) return; _muted = value; Notify(); _changed(); } }
    public double GainDecibels
    {
        get => _gain;
        set { if (!double.IsFinite(value)) return; value = Math.Round(Math.Clamp(value, -60, 12), 1); if (_gain == value) return; _gain = value; Notify(); _changed(); }
    }
    public StudioAudioTrackMix Snapshot() => new(StreamIndex, _gain, _muted);
    public override string ToString() => Label;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class StudioOutputEditorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IGenerationOutputEditor? _editor;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private bool _busy;
    private StudioRenderSettings _settings = new();
    public StudioOutputEditorViewModel(IGenerationOutputEditor? editor) { _editor = editor; Tracking = new(editor); InitializePresets(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<StudioOutputCanvas> Canvases { get; } = Enum.GetValues<StudioOutputCanvas>();
    public IReadOnlyList<StudioCompositionLayout> Layouts { get; } = Enum.GetValues<StudioCompositionLayout>();
    public IReadOnlyList<StudioExportQuality> Qualities { get; } = Enum.GetValues<StudioExportQuality>();
    public IReadOnlyList<StudioResolutionChoice> Resolutions { get; } =
    [
        new(StudioOutputResolution.Hd720, "720p · smaller files"),
        new(StudioOutputResolution.FullHd1080, "1080p · Full HD"),
        new(StudioOutputResolution.Qhd1440, "1440p · QHD"),
        new(StudioOutputResolution.Uhd2160, "2160p · 4K UHD"),
    ];
    public IReadOnlyList<StudioColorOutput> ColorOutputs { get; } = Enum.GetValues<StudioColorOutput>();
    public IReadOnlyList<StudioPlatformPresetChoice> PlatformPresets { get; } = Enum.GetValues<StudioPlatformExportPreset>()
        .Select(static preset => new StudioPlatformPresetChoice(preset, StudioPlatformExportPresets.DisplayName(preset))).ToArray();
    public IReadOnlyList<StudioAudioTrackEditor> AudioTracks { get; private set; } = [];
    public StudioMixAudioAuditionViewModel MixAudition { get; } = new();
    public StudioSourceTrackingViewModel Tracking { get; }
    public bool CanEdit => !_busy && _editor is not null && _project?.IsFinalized == false && _asset is not null;
    public bool HasAudio => AudioTracks.Count > 0;
    public string Status { get; private set; } = "Changes are saved and used by both preview and export.";
    public StudioOutputCanvas Canvas { get => _settings.Canvas; set => Save(canvas: value); }
    public StudioPlatformExportPreset PlatformPreset { get => _settings.PlatformPreset; set => ApplyPlatformPreset(value); }
    public StudioCompositionLayout Layout { get => _settings.Layout; set => Save(layout: value); }
    public StudioExportQuality Quality { get => _settings.Quality; set => Save(quality: value); }
    public StudioOutputResolution Resolution { get => _settings.Resolution; set => Save(resolution: value); }
    public StudioColorOutput ColorOutput { get => _settings.ColorOutput; set => Save(color: value); }
    public bool DuckGameplay { get => _settings.DuckGameplay; set => Save(duck: value); }
    public bool BurnCaptions { get => _settings.BurnCaptions; set => Save(burnCaptions: value); }
    public bool NormalizeLoudness { get => _settings.AudioMastering.NormalizeLoudness; set => Master(normalize: value); }
    public double IntegratedLufs { get => _settings.AudioMastering.IntegratedLufs; set => Master(lufs: value); }
    public bool LimitTruePeak { get => _settings.AudioMastering.LimitTruePeak; set => Master(limit: value); }
    public double TruePeakDecibels { get => _settings.AudioMastering.TruePeakDecibels; set => Master(peak: value); }
    public int? VoiceAudioStreamIndex { get => _settings.VoiceAudioStreamIndex; set => Save(voice: value, changeVoice: true); }
    public double FacecamHeightPercent { get => _settings.FacecamHeightPercent; set => Save(faceHeight: value); }
    public double CropX { get => _settings.GameplayRegion.X * 100; set => Crop(0, value, false); }
    public double CropY { get => _settings.GameplayRegion.Y * 100; set => Crop(1, value, false); }
    public double CropWidth { get => _settings.GameplayRegion.Width * 100; set => Crop(2, value, false); }
    public double CropHeight { get => _settings.GameplayRegion.Height * 100; set => Crop(3, value, false); }
    private NormalizedRectangle Face => _settings.FacecamRegion ?? new(0, 0, .25, .25);
    public double FaceX { get => Face.X * 100; set => Crop(0, value, true); }
    public double FaceY { get => Face.Y * 100; set => Crop(1, value, true); }
    public double FaceWidth { get => Face.Width * 100; set => Crop(2, value, true); }
    public double FaceHeight { get => Face.Height * 100; set => Crop(3, value, true); }

    public void Bind(GenerationOutputProject? project, GenerationOutputAsset? asset)
    {
        _project = project; _asset = asset; _settings = asset?.RenderSettings ?? new();
        AudioTracks = asset?.SourceMedia.AudioStreams.Select(stream => new StudioAudioTrackEditor(
            stream.Index, string.IsNullOrWhiteSpace(stream.Title) ? $"Track {stream.Index}" : $"Track {stream.Index} · {stream.Title}",
            _settings.AudioTracks.FirstOrDefault(track => track.StreamIndex == stream.Index), () => Save())).ToArray() ?? [];
        MixAudition.Bind(asset);
        Tracking.Bind(project, asset);
        Notify();
    }
    public void SetHostBusy(bool busy) { _busy = busy; MixAudition.SetHostBusy(busy); Tracking.SetHostBusy(busy); Notify(); }
    public void Dispose() { MixAudition.Dispose(); Tracking.Dispose(); }
    private void Master(bool? normalize = null, double? lufs = null, bool? limit = null, double? peak = null)
    {
        if (lufs.HasValue && !double.IsFinite(lufs.Value) || peak.HasValue && !double.IsFinite(peak.Value)) return;
        Save(audioMastering: new StudioAudioMastering(normalize ?? NormalizeLoudness,
            Math.Clamp(lufs ?? IntegratedLufs, -30, -5), limit ?? LimitTruePeak, Math.Clamp(peak ?? TruePeakDecibels, -9, 0)));
    }
    private void ApplyPlatformPreset(StudioPlatformExportPreset preset)
    {
        if (!CanEdit || preset == _settings.PlatformPreset) return;
        try
        {
            bool clearedTracking = false;
            GenerationOutputAsset Apply(GenerationOutputAsset asset)
            {
                StudioRenderSettings settings = StudioPlatformExportPresets.Apply(preset, asset.RenderSettings);
                clearedTracking |= settings.SourceCropTracks.Count < asset.RenderSettings.SourceCropTracks.Count;
                return asset.WithStudioEdits(asset.SourceStart, asset.SourceEnd, StudioPlatformExportPresets.Apply(preset, asset.Appearance))
                    .WithRenderSettings(settings);
            }
            if (_project!.Mode == GenerationMode.Montage)
                _editor!.ReplaceAssets(_project.Id, _project.Assets.Select(Apply).ToArray());
            else _editor!.ReplaceAsset(_project.Id, Apply(_asset!));
            Status = preset == StudioPlatformExportPreset.Custom ? "Custom settings saved." :
                $"{StudioPlatformExportPresets.DisplayName(preset)}: portrait canvas and caption safe area saved. Final export includes a publishing package.";
            if (clearedTracking) Status += " Incompatible gameplay tracking was cleared for the changed canvas; analyze it again.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { Status = error.Message; }
        Notify();
    }
    private void Crop(int component, double value, bool face)
    {
        if (!double.IsFinite(value)) return;
        NormalizedRectangle current = face ? Face : _settings.GameplayRegion;
        double x = current.X, y = current.Y, w = current.Width, h = current.Height;
        double normalized = Math.Clamp(value / 100, 0, 1);
        switch (component)
        {
            case 0: x = Math.Min(normalized, .99); w = Math.Min(w, 1 - x); break;
            case 1: y = Math.Min(normalized, .99); h = Math.Min(h, 1 - y); break;
            case 2: w = Math.Clamp(normalized, .01, 1 - x); break;
            case 3: h = Math.Clamp(normalized, .01, 1 - y); break;
        }
        var region = new NormalizedRectangle(x, y, w, h);
        Save(gameplay: face ? null : region, face: face ? region : null);
    }
    private void Save(StudioOutputCanvas? canvas = null, StudioCompositionLayout? layout = null,
        StudioExportQuality? quality = null, bool? duck = null, int? voice = null, bool changeVoice = false,
        StudioColorOutput? color = null,
        double? faceHeight = null, NormalizedRectangle? gameplay = null, NormalizedRectangle? face = null,
        StudioAudioMastering? audioMastering = null, StudioCanvasDecoration? decoration = null, bool? burnCaptions = null,
        StudioOutputResolution? resolution = null)
    {
        if (!CanEdit) return;
        try
        {
            StudioCompositionLayout chosen = layout ?? _settings.Layout;
            var replacement = new StudioRenderSettings(canvas ?? _settings.Canvas, chosen,
                gameplay ?? _settings.GameplayRegion,
                face ?? _settings.FacecamRegion ?? (chosen is StudioCompositionLayout.FacecamTop or StudioCompositionLayout.FacecamTopFit ? Face : null),
                Math.Clamp(faceHeight ?? _settings.FacecamHeightPercent, 10, 50),
                AudioTracks.Select(static track => track.Snapshot()).ToArray(),
                changeVoice ? voice : _settings.VoiceAudioStreamIndex,
                duck ?? _settings.DuckGameplay, quality ?? _settings.Quality, color ?? _settings.ColorOutput,
                canvas.HasValue && canvas != StudioOutputCanvas.Portrait ? StudioPlatformExportPreset.Custom : _settings.PlatformPreset,
                _settings.FrameKeyframes, _settings.TimedTextOverlays,
                audioMastering ?? _settings.AudioMastering, decoration ?? _settings.Decoration, burnCaptions ?? _settings.BurnCaptions,
                resolution ?? _settings.Resolution);
            replacement = replacement.WithCompatibleSourceCropTracksFrom(_settings);
            bool clearedTracking = replacement.SourceCropTracks.Count < _settings.SourceCropTracks.Count;
            if (replacement.CanonicalIdentity() == _settings.CanonicalIdentity()) return;
            if ((canvas.HasValue || resolution.HasValue) && _project!.Mode == GenerationMode.Montage)
            {
                _editor!.ReplaceAssets(_project.Id, _project.Assets.Select(asset =>
                    asset.WithRenderSettings(asset.Id == _asset!.Id ? replacement :
                        new StudioRenderSettings(replacement.Canvas, asset.RenderSettings.Layout,
                            asset.RenderSettings.GameplayRegion, asset.RenderSettings.FacecamRegion,
                            asset.RenderSettings.FacecamHeightPercent, asset.RenderSettings.AudioTracks,
                            asset.RenderSettings.VoiceAudioStreamIndex, asset.RenderSettings.DuckGameplay,
                            asset.RenderSettings.Quality, asset.RenderSettings.ColorOutput,
                            replacement.Canvas != StudioOutputCanvas.Portrait ? StudioPlatformExportPreset.Custom : asset.RenderSettings.PlatformPreset,
                            asset.RenderSettings.FrameKeyframes, asset.RenderSettings.TimedTextOverlays,
                            asset.RenderSettings.AudioMastering, asset.RenderSettings.Decoration, asset.RenderSettings.BurnCaptions, replacement.Resolution)
                            .WithCompatibleSourceCropTracksFrom(asset.RenderSettings))).ToArray());
            }
            else _editor!.ReplaceAsset(_project!.Id, _asset!.WithRenderSettings(replacement));
            Status = clearedTracking
                ? "Saved. Incompatible source tracking was cleared because its source region or pane layout changed. Analyze it again for this layout."
                : "Saved. Preview and export use these settings.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { Status = error.Message; }
        Notify();
    }
    private void Notify() { PropertyChanged?.Invoke(this, new(string.Empty)); _saveLayoutPreset?.RaiseCanExecuteChanged(); }

    private readonly IStudioLayoutPresetStore _layoutStore = StudioEditorPreferences.CreateLayouts();
    private DelegateCommand _saveLayoutPreset = null!;
    private StudioLayoutPreset? _selectedLayoutPreset;
    public ObservableCollection<StudioLayoutPreset> LayoutPresets { get; } = [];
    public string LayoutPresetName { get; set; } = "My layout";
    public string LayoutPresetGame { get; set; } = "";
    public ICommand SaveLayoutPresetCommand => _saveLayoutPreset;
    public StudioLayoutPreset? SelectedLayoutPreset
    {
        get => _selectedLayoutPreset;
        set
        {
            if (value is null || !CanEdit) return;
            _selectedLayoutPreset = value;
            StudioRenderSettings chosen = value.Apply(_settings);
            Save(canvas: chosen.Canvas, layout: chosen.Layout, faceHeight: chosen.FacecamHeightPercent,
                gameplay: chosen.GameplayRegion, face: chosen.FacecamRegion, decoration: chosen.Decoration);
        }
    }
    public string BackgroundColor
    {
        get => _settings.Decoration.BackgroundColor;
        set
        {
            try { Save(decoration: new(value, _settings.Decoration.HudSourceRegion, _settings.Decoration.HudCanvasRegion)); }
            catch (ArgumentException exception) { Status = exception.Message; Notify(); }
        }
    }
    public bool HasHudLayer
    {
        get => _settings.Decoration.HudSourceRegion is not null;
        set => Save(decoration: new(BackgroundColor, value ? HudSource : null, value ? HudCanvas : null));
    }
    private NormalizedRectangle HudSource => _settings.Decoration.HudSourceRegion ?? new(0, .8, 1, .2);
    private NormalizedRectangle HudCanvas => _settings.Decoration.HudCanvasRegion ?? new(.05, .75, .9, .15);
    public double HudSourceX { get => HudSource.X * 100; set => EditHud(0, value, false); }
    public double HudSourceY { get => HudSource.Y * 100; set => EditHud(1, value, false); }
    public double HudSourceWidth { get => HudSource.Width * 100; set => EditHud(2, value, false); }
    public double HudSourceHeight { get => HudSource.Height * 100; set => EditHud(3, value, false); }
    public double HudCanvasX { get => HudCanvas.X * 100; set => EditHud(0, value, true); }
    public double HudCanvasY { get => HudCanvas.Y * 100; set => EditHud(1, value, true); }
    public double HudCanvasWidth { get => HudCanvas.Width * 100; set => EditHud(2, value, true); }
    public double HudCanvasHeight { get => HudCanvas.Height * 100; set => EditHud(3, value, true); }
    private void EditHud(int component, double value, bool canvas)
    {
        if (!double.IsFinite(value)) return;
        NormalizedRectangle current = canvas ? HudCanvas : HudSource;
        double x = current.X, y = current.Y, width = current.Width, height = current.Height;
        double normalized = Math.Clamp(value / 100, 0, 1);
        switch (component)
        {
            case 0: x = Math.Min(.99, normalized); width = Math.Min(width, 1 - x); break;
            case 1: y = Math.Min(.99, normalized); height = Math.Min(height, 1 - y); break;
            case 2: width = Math.Clamp(normalized, .01, 1 - x); break;
            case 3: height = Math.Clamp(normalized, .01, 1 - y); break;
        }
        var changed = new NormalizedRectangle(x, y, width, height);
        Save(decoration: new(BackgroundColor, canvas ? HudSource : changed, canvas ? changed : HudCanvas));
    }
    private void InitializePresets()
    {
        _saveLayoutPreset = new(SaveLayoutPreset, () => CanEdit);
        try { foreach (StudioLayoutPreset preset in _layoutStore.Load()) LayoutPresets.Add(preset); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Status = "Saved layouts could not be loaded: " + exception.Message; }
    }
    private void SaveLayoutPreset()
    {
        if (!CanEdit) return;
        try
        {
            _layoutStore.Save(StudioLayoutPreset.Capture(LayoutPresetName, LayoutPresetGame, _settings));
            LayoutPresets.Clear(); foreach (StudioLayoutPreset preset in _layoutStore.Load()) LayoutPresets.Add(preset);
            Status = "Layout saved for reuse in other projects. Source regions use percentages; check them against each recording.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { Status = "Layout was not saved: " + exception.Message; }
        Notify();
    }
}

public sealed record StudioPlatformPresetChoice(StudioPlatformExportPreset Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record StudioResolutionChoice(StudioOutputResolution Value, string Label)
{
    public override string ToString() => Label;
}
