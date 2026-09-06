using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed record StudioTrackingFrameChoice(int Index, string Label)
{
    public override string ToString() => Label;
}

public sealed class StudioSourceTrackingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IGenerationOutputEditor? _editor;
    private readonly IStudioSourceTrackingService _service;
    private readonly AsyncDelegateCommand _analyze;
    private readonly DelegateCommand _apply, _clear, _cancel;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private StudioSourceTrackingReview? _draft;
    private CancellationTokenSource? _cancellation;
    private Task _pending = Task.CompletedTask;
    private string _binding = "";
    private bool _busy, _disposed, _reviewCurrent;
    private StudioCropTrackingTarget _target;
    private double _x = 45, _y = 45, _width = 10, _height = 10, _offset, _duration = 10, _zoom = 1;
    private StudioTrackingFrameChoice? _selectedFrame;
    public StudioSourceTrackingViewModel(IGenerationOutputEditor? editor)
        : this(editor, StudioSourceTrackingFactory.Create()) { }

    internal StudioSourceTrackingViewModel(IGenerationOutputEditor? editor, IStudioSourceTrackingService service)
    {
        _editor = editor;
        _service = service;
        _analyze = new(() => _pending = AnalyzeAsync(), () => CanAdjust && _asset!.Duration.TotalSeconds >= .25);
        _apply = new(Apply, () => CanAdjust && _reviewCurrent && _draft?.Track is not null);
        _clear = new(Clear, () => CanAdjust && _asset!.RenderSettings.SourceCropTracks.Any(track => track.Target == Target));
        _cancel = new(Stop, () => _cancellation is not null);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand AnalyzeCommand => _analyze;
    public ICommand ApplyCommand => _apply;
    public ICommand ClearCommand => _clear;
    public ICommand CancelCommand => _cancel;
    public IReadOnlyList<StudioCropTrackingTarget> Targets { get; } = Enum.GetValues<StudioCropTrackingTarget>();
    public bool CanAdjust => !_disposed && !_busy && _cancellation is null && _editor is not null && _project?.IsFinalized == false && _asset is not null;
    public bool HasReview => _draft is not null;
    public string Status { get; private set; } =
        "Choose a small, distinctive textured source region. Tracking follows its translation only; " +
        "it does not recognize a person, game object or HUD meaning.";
    public string SavedStatus => _asset?.RenderSettings.SourceCropTracks.FirstOrDefault(track => track.Target == Target) is { } track
        ? $"Saved {Target} track: {track.AcceptedFrames}/{track.SampledFrames} frames accepted; {track.FinalState}. Manual view outside accepted intervals."
        : $"No saved {Target} track. The manual source region is active.";
    public StudioCropTrackingTarget Target { get => _target; set { if (_target == value || !Enum.IsDefined(value)) return; _target = value; Invalidate(); ResetSeed(); Notify(); } }
    public double SeedX { get => _x; set => Change(ref _x, value, 0, 99); }
    public double SeedY { get => _y; set => Change(ref _y, value, 0, 99); }
    public double SeedWidth { get => _width; set => Change(ref _width, value, 1, 80); }
    public double SeedHeight { get => _height; set => Change(ref _height, value, 1, 80); }
    public double StartOffsetSeconds { get => _offset; set => Change(ref _offset, value, 0, Math.Max(0, (_asset?.Duration.TotalSeconds ?? 0) - .25)); }
    public double DurationSeconds { get => _duration; set => Change(ref _duration, value, .25, 60); }
    public double ViewportZoom { get => _zoom; set => Change(ref _zoom, value, 1, 4); }
    public IReadOnlyList<StudioTrackingFrameChoice> Frames { get; private set; } = [];
    public StudioTrackingFrameChoice? SelectedFrame
    {
        get => _selectedFrame;
        set { if (value is null || _draft is null || value.Index < 0 || value.Index >= _draft.Samples.Count) return; _selectedFrame = value; ShowFrame(); Notify(); }
    }
    public object? SourceFrame { get; private set; }
    public double FrameWidth => _draft?.Width ?? 320;
    public double FrameHeight => _draft?.Height ?? 180;
    private StudioTrackingRegion? Feature => _draft is not null && _selectedFrame is not null ? _draft.Samples[_selectedFrame.Index].Feature : null;
    private StudioTrackingRegion? Viewport => _draft is not null && _selectedFrame is not null ? _draft.Samples[_selectedFrame.Index].Viewport : null;
    public double FeatureX => (Feature?.X ?? 0) * FrameWidth;
    public double FeatureY => (Feature?.Y ?? 0) * FrameHeight;
    public double FeatureWidth => (Feature?.Width ?? 0) * FrameWidth;
    public double FeatureHeight => (Feature?.Height ?? 0) * FrameHeight;
    public double ViewportX => (Viewport?.X ?? 0) * FrameWidth;
    public double ViewportY => (Viewport?.Y ?? 0) * FrameHeight;
    public double ViewportWidth => (Viewport?.Width ?? 0) * FrameWidth;
    public double ViewportHeight => (Viewport?.Height ?? 0) * FrameHeight;
    public double DraftSeedX => _x / 100 * FrameWidth;
    public double DraftSeedY => _y / 100 * FrameHeight;
    public double DraftSeedWidth => Math.Min(_width, 100 - _x) / 100 * FrameWidth;
    public double DraftSeedHeight => Math.Min(_height, 100 - _y) / 100 * FrameHeight;
    public string FrameStatus => _draft is not null && _selectedFrame is not null
        ? (_reviewCurrent ? "" : "Previous analysis; inputs changed. ") + _draft.Samples[_selectedFrame.Index].ToString() +
            (_draft.Samples[_selectedFrame.Index].AmbiguityMargin is double margin ? $" · separation {margin:F2}" : "")
        : "Analyze to inspect bounded source frames, the matched feature and the proposed crop.";

    public void Bind(GenerationOutputProject? project, GenerationOutputAsset? asset)
    {
        string binding = asset is null ? "" : asset.Id + "|" + asset.SourceFullPath + "|" +
            asset.SourceStart.Ticks + "|" + asset.SourceEnd.Ticks + "|" + asset.RenderSettings.CanonicalIdentity();
        bool changed = binding != _binding;
        if (changed) { Stop(); _draft = null; Frames = []; SourceFrame = null; _selectedFrame = null; _reviewCurrent = false; }
        bool newAsset = _asset?.Id != asset?.Id;
        _binding = binding; _project = project; _asset = asset;
        if (newAsset) { _offset = 0; _duration = Math.Min(10, asset?.Duration.TotalSeconds ?? 10); ResetSeed(); }
        if (changed) Status = "Select the first frame's textured seed, analyze, then inspect the accepted and failed frames before Apply.";
        Notify();
    }
    public void SetHostBusy(bool busy) { _busy = busy; if (busy) Stop(); Notify(); }
    private void Change(ref double field, double value, double min, double max)
    {
        if (!CanAdjust || !double.IsFinite(value)) return;
        value = Math.Clamp(value, min, max); if (field == value) return;
        field = value; Invalidate(); Notify();
    }
    private void Invalidate()
    {
        Stop(); _reviewCurrent = false;
        Status = "Inputs changed. Analyze again before applying motion. Dashed yellow shows the current seed on the displayed source frame.";
    }
    private void ResetSeed()
    {
        StudioTrackingRegion region = StudioSourceTrackingFactory.ManualRegion(_asset, Target);
        _width = Math.Min(10, region.Width * 50); _height = Math.Min(10, region.Height * 50);
        _x = (region.X + region.Width / 2) * 100 - _width / 2;
        _y = (region.Y + region.Height / 2) * 100 - _height / 2;
    }
    private async Task AnalyzeAsync()
    {
        if (!CanAdjust) return;
        GenerationOutputAsset asset = _asset!; string binding = _binding;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        _draft = null; SourceFrame = null; Frames = []; _selectedFrame = null; _reviewCurrent = false;
        Status = "Decoding and comparing up to 60 seconds at 8 frames/second on the CPU…"; Notify();
        try
        {
            var seed = new StudioTrackingRegion(_x / 100, _y / 100, _width / 100, _height / 100);
            TimeSpan start = asset.SourceStart + TimeSpan.FromSeconds(_offset);
            TimeSpan duration = TimeSpan.FromSeconds(Math.Min(_duration, (asset.SourceEnd - start).TotalSeconds));
            StudioSourceTrackingReview result = await _service.AnalyzeAsync(asset, Target, seed, start, duration, _zoom, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed || binding != _binding) return;
            _draft = result; _reviewCurrent = true;
            Frames = result.Samples.Select((sample, index) => new StudioTrackingFrameChoice(index, sample.ToString())).ToArray();
            SelectedFrame = Frames[0]; Status = result.Summary;
        }
        catch (OperationCanceledException) { if (!_disposed && binding == _binding) Status = "Tracking stopped; no draft motion was applied."; }
        catch (Exception exception) { if (!_disposed && binding == _binding) Status = "Tracking could not complete: " + exception.Message; }
        finally { if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null; Notify(); }
    }
    private void ShowFrame()
    {
        if (_draft is null || _selectedFrame is null) return;
        SourceFrame = _draft.CreateFrameImage(_selectedFrame.Index);
    }
    private void Apply()
    {
        if (!CanAdjust || !_reviewCurrent || _draft?.Track is not { } track) return;
        try
        {
            track.RequireSource(_asset!.SourceMedia, checkFile: true);
            if (!track.MatchesSettings(_asset.RenderSettings)) throw new InvalidOperationException("The source region or pane layout changed. Analyze again.");
            var tracks = _asset.RenderSettings.SourceCropTracks.Where(item => item.Target != track.Target).Append(track).ToArray();
            _editor!.ReplaceAsset(_project!.Id, _asset.WithRenderSettings(_asset.RenderSettings.WithSourceCropTracks(tracks)));
            _reviewCurrent = false; Status = "Reviewed motion saved. Preview and export share these source crops; manual framing remains outside the accepted interval.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { Status = "Motion was not applied: " + exception.Message; }
        Notify();
    }
    private void Clear()
    {
        if (!CanAdjust) return;
        try
        {
            _editor!.ReplaceAsset(_project!.Id, _asset!.WithRenderSettings(_asset.RenderSettings.WithSourceCropTracks(
                _asset.RenderSettings.SourceCropTracks.Where(track => track.Target != Target).ToArray())));
            _reviewCurrent = false; Status = $"Saved {Target} tracking cleared. The manual source region is active.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { Status = exception.Message; }
        Notify();
    }
    public void Stop() { if (_cancellation is null) return; _cancellation.Cancel(); Status = "Stopping tracking…"; Notify(); }
    public async Task StopAsync(CancellationToken cancellationToken) { Stop(); await _pending.WaitAsync(cancellationToken); }
    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); _draft = null; SourceFrame = null; }
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new(string.Empty));
        _analyze.RaiseCanExecuteChanged(); _apply.RaiseCanExecuteChanged(); _clear.RaiseCanExecuteChanged(); _cancel.RaiseCanExecuteChanged();
    }
}
