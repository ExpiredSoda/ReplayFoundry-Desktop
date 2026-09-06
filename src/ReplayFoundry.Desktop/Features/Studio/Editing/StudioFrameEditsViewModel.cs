using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioFrameEditsViewModel : INotifyPropertyChanged
{
    private readonly IGenerationOutputEditor? _editor;
    private readonly DelegateCommand _saveFrame, _removeFrame, _saveText, _removeText;
    private readonly Dictionary<string, InputDraft> _drafts = new(StringComparer.Ordinal);
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private bool _busy;
    private StudioFrameKeyframe? _selectedFrame;
    private StudioTimedTextOverlay? _selectedText;
    public StudioFrameEditsViewModel(IGenerationOutputEditor? editor)
    {
        _editor = editor;
        _saveFrame = new DelegateCommand(SaveFrame, () => CanEdit);
        _removeFrame = new DelegateCommand(RemoveFrame, () => CanEdit && SelectedFrame is not null);
        _saveText = new DelegateCommand(SaveText, () => CanEdit);
        _removeText = new DelegateCommand(RemoveText, () => CanEdit && SelectedText is not null);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanEdit => !_busy && _editor is not null && _project?.IsFinalized == false && _asset is not null;
    public IReadOnlyList<StudioFrameKeyframe> Frames => _asset?.RenderSettings.FrameKeyframes ?? [];
    public IReadOnlyList<StudioTimedTextOverlay> Texts => _asset?.RenderSettings.TimedTextOverlays ?? [];
    public double FrameOffsetSeconds { get; set; }
    public double Zoom { get; set; } = 1;
    public double PanXPercent { get; set; } = 50;
    public double PanYPercent { get; set; } = 50;
    public string Text { get; set; } = "";
    public double TextStartSeconds { get; set; }
    public double TextEndSeconds { get; set; } = 3;
    public double TextVerticalPercent { get; set; } = 20;
    public double TextFontSizePercent { get; set; } = 5;
    public string Status { get; private set; } = "Times are seconds from this clip's start. Saved edits stay anchored to the source when you trim.";
    public StudioFrameKeyframe? SelectedFrame
    {
        get => _selectedFrame;
        set
        {
            if (ReferenceEquals(_selectedFrame, value)) return;
            _selectedFrame = value;
            if (value is not null && _asset is not null)
            {
                FrameOffsetSeconds = (value.SourcePosition - _asset.SourceStart).TotalSeconds;
                Zoom = value.Zoom; PanXPercent = value.PanXPercent; PanYPercent = value.PanYPercent;
            }
            Notify();
        }
    }
    public StudioTimedTextOverlay? SelectedText
    {
        get => _selectedText;
        set
        {
            if (ReferenceEquals(_selectedText, value)) return;
            _selectedText = value;
            if (value is not null && _asset is not null)
            {
                Text = value.Text; TextStartSeconds = (value.SourceStart - _asset.SourceStart).TotalSeconds;
                TextEndSeconds = (value.SourceEnd - _asset.SourceStart).TotalSeconds;
                TextVerticalPercent = value.CenterYPercent; TextFontSizePercent = value.FontSizePercent;
            }
            Notify();
        }
    }
    public ICommand SaveFrameCommand => _saveFrame;
    public ICommand RemoveFrameCommand => _removeFrame;
    public ICommand SaveTextCommand => _saveText;
    public ICommand RemoveTextCommand => _removeText;
    public void Bind(GenerationOutputProject? project, GenerationOutputAsset? asset)
    {
        bool changed = _asset?.Id != asset?.Id || _project?.Id != project?.Id;
        if (changed && _asset is not null && _project is not null)
            _drafts[_project.Id + ":" + _asset.Id] = new(FrameOffsetSeconds, Zoom, PanXPercent, PanYPercent,
                Text, TextStartSeconds, TextEndSeconds, TextVerticalPercent, TextFontSizePercent, _asset.SourceStart,
                SelectedFrame, SelectedText);
        TimeSpan? previousStart = _asset?.SourceStart;
        _project = project; _asset = asset;
        if (changed)
        {
            _selectedFrame = null; _selectedText = null;
            InputDraft input = asset is not null && project is not null && _drafts.TryGetValue(project.Id + ":" + asset.Id, out var saved)
                ? saved : new(0, 1, 50, 50, "", 0, Math.Min(3, asset?.Duration.TotalSeconds ?? 3), 20, 5, asset?.SourceStart ?? TimeSpan.Zero);
            double shift = (input.SourceStart - (asset?.SourceStart ?? TimeSpan.Zero)).TotalSeconds;
            FrameOffsetSeconds = input.Time + shift; Zoom = input.Zoom; PanXPercent = input.X; PanYPercent = input.Y;
            Text = input.Text; TextStartSeconds = input.Start + shift; TextEndSeconds = input.End + shift;
            TextVerticalPercent = input.Vertical; TextFontSizePercent = input.Size;
            _selectedFrame = Frames.FirstOrDefault(frame => frame.SourcePosition == input.SelectedFrame?.SourcePosition);
            _selectedText = Texts.FirstOrDefault(text => input.SelectedText is { } savedText &&
                text.SourceStart == savedText.SourceStart && text.SourceEnd == savedText.SourceEnd && text.Text == savedText.Text &&
                text.CenterXPercent == savedText.CenterXPercent && text.CenterYPercent == savedText.CenterYPercent);
        }
        else if (asset is not null && previousStart is { } oldStart && oldStart != asset.SourceStart)
        {
            double shift = (oldStart - asset.SourceStart).TotalSeconds;
            FrameOffsetSeconds += shift; TextStartSeconds += shift; TextEndSeconds += shift;
        }
        Notify();
    }
    public void SetHostBusy(bool busy) { _busy = busy; Notify(); }
    private void SaveFrame() => Save(() =>
    {
        RequireTime(FrameOffsetSeconds, SelectedFrame is not null);
        var frame = new StudioFrameKeyframe(_asset!.SourceStart + TimeSpan.FromSeconds(FrameOffsetSeconds), Zoom, PanXPercent, PanYPercent);
        StudioFrameKeyframe[] frames = Frames.Where(item => !ReferenceEquals(item, SelectedFrame) && item.SourcePosition != frame.SourcePosition).Append(frame).ToArray();
        return _asset.RenderSettings.WithFrameEdits(frames, Texts);
    });
    private void RemoveFrame() => Save(() => _asset!.RenderSettings.WithFrameEdits(
        Frames.Where(item => !ReferenceEquals(item, SelectedFrame)).ToArray(), Texts));
    private void SaveText() => Save(() =>
    {
        RequireTime(TextStartSeconds, SelectedText is not null); RequireTime(TextEndSeconds, SelectedText is not null);
        var overlay = new StudioTimedTextOverlay(Text, _asset!.SourceStart + TimeSpan.FromSeconds(TextStartSeconds),
            _asset.SourceStart + TimeSpan.FromSeconds(TextEndSeconds), SelectedText?.CenterXPercent ?? 50, TextVerticalPercent, TextFontSizePercent);
        return _asset.RenderSettings.WithFrameEdits(Frames, Texts.Where(item => !ReferenceEquals(item, SelectedText)).Append(overlay).ToArray());
    });
    private void RemoveText() => Save(() => _asset!.RenderSettings.WithFrameEdits(Frames,
        Texts.Where(item => !ReferenceEquals(item, SelectedText)).ToArray()));
    private void RequireTime(double seconds, bool existingEdit)
    {
        double minimum = existingEdit ? -_asset!.SourceStart.TotalSeconds : 0;
        double maximum = existingEdit ? (_asset!.SourceDuration - _asset.SourceStart).TotalSeconds : _asset!.Duration.TotalSeconds;
        if (!double.IsFinite(seconds) || seconds < minimum || seconds > maximum)
            throw new ArgumentException(existingEdit ? "Keep this saved edit inside the original source recording." : "Choose a time inside the current clip.");
    }
    private void Save(Func<StudioRenderSettings> create)
    {
        if (!CanEdit) return;
        try
        {
            StudioRenderSettings settings = create();
            _editor!.ReplaceAsset(_project!.Id, _asset!.WithRenderSettings(settings));
            _selectedFrame = null; _selectedText = null;
            Status = "Saved. The preview and export use the same timing and placement.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Status = error.Message; }
        Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new(string.Empty));
        _saveFrame.RaiseCanExecuteChanged(); _removeFrame.RaiseCanExecuteChanged();
        _saveText.RaiseCanExecuteChanged(); _removeText.RaiseCanExecuteChanged();
    }
    private sealed record InputDraft(double Time, double Zoom, double X, double Y, string Text,
        double Start, double End, double Vertical, double Size, TimeSpan SourceStart,
        StudioFrameKeyframe? SelectedFrame = null, StudioTimedTextOverlay? SelectedText = null);
}
