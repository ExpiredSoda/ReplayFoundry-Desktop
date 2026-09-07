using System.Globalization;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioManualClipViewModel : ObservableObject, IDisposable
{
    private readonly IGenerationManualClipEditor? _editor;
    private readonly Func<bool> _canMutate;
    private readonly DelegateCommand _add;
    private readonly DelegateCommand _toggle;
    private readonly DebouncedUiAction _previewDebounce;
    private readonly StudioHiddenMomentPreviewWarmup _suggestionWarmup;
    private bool _isOpen;
    private bool _updatingPreview;
    private bool _pendingPreviewNavigation;
    private GenerationOutputProject? _project;
    private StudioManualSource? _source;
    private string _start = "00:00:00";
    private string _end = "00:00:30";
    private double _position;
    private double _viewportStart;
    private double _viewportDuration = 120;
    private readonly StudioManualRangeHistory _rangeHistory = new();
    private StudioManualRange? _gestureStart;
    private StudioManualRange? _lastValidRange;
    private StudioManualRange CurrentRange => new(SelectionStartSeconds, SelectionEndSeconds);
    private readonly DelegateCommand _undoRange;
    private readonly DelegateCommand _redoRange;

    public StudioManualClipViewModel(IGenerationManualClipEditor? editor,
        IStudioPreviewMediaService? previewMedia, Func<bool> canMutate, StudioTimelineFilmstrip? timelineFilmstrip = null)
    {
        _editor = editor;
        _canMutate = canMutate;
        _suggestionWarmup = new(previewMedia);
        Suggestions = new(PreviewSuggestion, UseSuggestion);
        Suggestions.Filters.PropertyChanged += SuggestionFiltersChanged;
        Filmstrip = timelineFilmstrip ?? new(null);
        Preview = new(previewMedia, showCaptionControls: false, rangeMode: StudioPreviewRangeMode.ExactSelection, useSourceTimecodes: true);
        _add = new(Add, CanAdd);
        _toggle = new(() => IsOpen = !IsOpen, () => _project?.IsFinalized == false && Sources.Count > 0);
        MarkStartCommand = new DelegateCommand(MarkStart);
        MarkEndCommand = new DelegateCommand(MarkEnd);
        _undoRange = new(() => RestoreRange(_rangeHistory.Undo(CurrentRange)), () => _rangeHistory.CanUndo);
        _redoRange = new(() => RestoreRange(_rangeHistory.Redo(CurrentRange)), () => _rangeHistory.CanRedo);
        MoveRangeCommand = new DelegateCommand<double>(MoveRange);
        PreviousFrameCommand = new DelegateCommand(() => StepFrames(-1));
        NextFrameCommand = new DelegateCommand(() => StepFrames(1));
        PreviewSelectionCommand = new DelegateCommand(PreviewSelection);
        ZoomInCommand = new DelegateCommand(() => Zoom(0.5));
        ZoomOutCommand = new DelegateCommand(() => Zoom(2));
        ShowAllCommand = new DelegateCommand(() => SetViewport(0, SourceMaximumSeconds));
        ShowSelectionCommand = new DelegateCommand(() => SetViewport(
            SelectionStartSeconds - 5, SelectionEndSeconds - SelectionStartSeconds + 10));
        _previewDebounce = new(TimeSpan.FromMilliseconds(250), PrepareAroundPlayhead);
        Preview.PropertyChanged += PreviewChanged;
    }

    public event EventHandler? ClipAdded;
    public IReadOnlyList<StudioManualSource> Sources { get; private set; } = [];
    public StudioPreviewViewModel Preview { get; }
    public StudioTimelineFilmstrip Filmstrip { get; }
    public StudioMomentSuggestionsViewModel Suggestions { get; }
    public ICommand AddCommand => _add;
    public ICommand ToggleCommand => _toggle;
    public ICommand MarkStartCommand { get; }
    public ICommand MarkEndCommand { get; }
    public ICommand MoveRangeCommand { get; }
    public ICommand UndoRangeCommand => _undoRange;
    public ICommand RedoRangeCommand => _redoRange;
    public ICommand PreviousFrameCommand { get; }
    public ICommand NextFrameCommand { get; }
    public double FrameStepSeconds => 1 / (_source?.FramesPerSecond ?? 30);
    public ICommand PreviewSelectionCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ShowAllCommand { get; }
    public ICommand ShowSelectionCommand { get; }
    public bool HasMultipleSources => Sources.Count > 1;
    public string RangeSummary => ValidRange() ? $"{SelectionEndSeconds - SelectionStartSeconds:0.##} second clip" : "Choose a clip up to 3 minutes long";
    public string RecordingLength => _source is null ? string.Empty : $"{Format(_source.Duration.TotalSeconds)} recording";
    public string PlayheadText => Format(_position);
    public string Status => ValidRange()
        ? "Drag the middle of your clip to move the whole range. Drag its yellow edges to trim. Click the ruler to move the playhead."
        : "Choose an end after the start, within the recording. A clip can be up to 3 minutes long.";
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            OnPropertyChanged();
            _previewDebounce.Cancel();
            RefreshFilmstrip();
            if (value) PrepareAroundPlayhead(); else { _suggestionWarmup.Cancel(); Preview.Bind(false, null, null); }
        }
    }
    public StudioManualSource? SelectedSource
    {
        get => _source;
        set
        {
            if (value is null || _source?.FullPath.Equals(value.FullPath, StringComparison.OrdinalIgnoreCase) == true) return;
            _source = value;
            _rangeHistory.Clear(); _gestureStart = null;
            _start = "00:00:00";
            _end = Format(Math.Min(30, value.Duration.TotalSeconds));
            _lastValidRange = CurrentRange;
            _position = 0;
            _suggestionWarmup.Cancel();
            SetViewport(0, value.Duration.TotalSeconds);
            Suggestions.Bind(_project, value.FullPath);
            Notify();
            if (IsOpen) PrepareAroundPlayhead();
        }
    }
    public string StartText { get => _start; set { var before = CurrentRange; _start = value; RememberRange(before); NotifyRange(); } }
    public string EndText { get => _end; set { var before = CurrentRange; _end = value; RememberRange(before); NotifyRange(); } }
    public double SelectionStartSeconds
    {
        get => TryTime(_start, out var time) ? time.TotalSeconds : 0;
        set
        {
            if (!double.IsFinite(value)) return;
            StartText = Format(Math.Clamp(value, Math.Max(0, SelectionEndSeconds - 180), Math.Max(0, SelectionEndSeconds - 0.05)));
        }
    }
    public double SelectionEndSeconds
    {
        get => TryTime(_end, out var time) ? time.TotalSeconds : 0;
        set
        {
            if (!double.IsFinite(value)) return;
            double minimum = Math.Min(SourceMaximumSeconds, SelectionStartSeconds + 0.05);
            EndText = Format(Math.Clamp(value, minimum, Math.Max(minimum, Math.Min(SourceMaximumSeconds, SelectionStartSeconds + 180))));
        }
    }
    public double SourceMaximumSeconds => _source?.Duration.TotalSeconds ?? 0;
    public double SourcePositionSeconds
    {
        get => _position;
        set
        {
            if (!double.IsFinite(value) || _source is null) return;
            _position = Math.Clamp(value, 0, SourceMaximumSeconds);
            OnPropertyChanged(); OnPropertyChanged(nameof(PlayheadText));
            if (!IsOpen) return;
            if (Preview.IsPreviewPlaying && Preview.PlayCommand.CanExecute(null)) Preview.PlayCommand.Execute(null);
            if ((Preview.IsPreviewAvailable || Preview.IsPreviewLoading) && _position >= Preview.PreviewPositionMinimumSeconds &&
                _position < Preview.PreviewPositionMaximumSeconds)
            {
                _previewDebounce.Cancel();
                _pendingPreviewNavigation = false;
                _updatingPreview = true;
                Preview.PreviewPositionSeconds = _position;
                _updatingPreview = false;
            }
            else { _pendingPreviewNavigation = true; _previewDebounce.Restart(); }
        }
    }
    public double ViewportStartSeconds { get => _viewportStart; set => SetViewport(value, _viewportDuration); }
    public double ViewportDurationSeconds { get => _viewportDuration; set => SetViewport(_viewportStart, value); }

    public void Bind(GenerationOutputProject? project)
    {
        bool changed = _project?.Id != project?.Id;
        _project = project;
        Sources = project?.SourceMedia.Select(static media => StudioManualSource.FromMedia(media)).ToArray() ?? [];
        if (changed || _source is null)
        {
            IsOpen = false;
            _source = null;
            SelectedSource = Sources.FirstOrDefault();
            Preview.Bind(false, null, null);
        }
        else _source = Sources.FirstOrDefault(value => value.FullPath.Equals(_source.FullPath, StringComparison.OrdinalIgnoreCase));
        Suggestions.Bind(_project, _source?.FullPath);
        Notify();
    }
    public void RefreshAvailability() => _add.RaiseCanExecuteChanged();
    internal Task StopAsync(CancellationToken token)
    {
        _previewDebounce.Cancel();
        return Task.WhenAll(Preview.StopAsync(token), Filmstrip.StopAsync(token), _suggestionWarmup.StopAsync(token));
    }
    public void Dispose()
    {
        Suggestions.Filters.PropertyChanged -= SuggestionFiltersChanged;
        _previewDebounce.Dispose();
        _suggestionWarmup.Dispose();
        Filmstrip.Dispose();
        Preview.PropertyChanged -= PreviewChanged;
        Preview.Dispose();
    }
    private void SuggestionFiltersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => _suggestionWarmup.Cancel();
    private void PreviewChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_updatingPreview || _pendingPreviewNavigation || !IsOpen || e.PropertyName != nameof(Preview.PreviewPositionSeconds)) return;
        _position = Preview.PreviewPositionSeconds;
        OnPropertyChanged(nameof(SourcePositionSeconds)); OnPropertyChanged(nameof(PlayheadText));
    }
    private void MarkStart()
    {
        var before = CurrentRange;
        // Marking a distant moment starts a new selection there; scrubbing alone never changes the cut.
        double start = Math.Min(_position, Math.Max(0, SourceMaximumSeconds - 0.05));
        if (start >= SelectionEndSeconds || SelectionEndSeconds - start > 180)
            _end = Format(Math.Min(SourceMaximumSeconds, start + 30));
        _start = Format(start);
        RememberRange(before);
        NotifyRange();
    }
    private void MarkEnd()
    {
        var before = CurrentRange;
        double end = Math.Max(Math.Min(0.05, SourceMaximumSeconds), _position);
        if (end <= SelectionStartSeconds || end - SelectionStartSeconds > 180) _start = Format(Math.Max(0, end - 30));
        _end = Format(end);
        RememberRange(before);
        NotifyRange();
    }
    public void StepFrames(int frames)
    {
        SourcePositionSeconds += frames * FrameStepSeconds;
        RevealPlayhead();
    }
    public void MoveRange(double start)
    {
        if (!double.IsFinite(start) || !ValidRange()) return;
        var before = CurrentRange;
        RestoreRange(before.MoveTo(start, SourceMaximumSeconds));
        RememberRange(before);
        NotifyRange();
    }
    public void BeginRangeGesture() => _gestureStart = CurrentRange;
    public void EndRangeGesture(bool cancel)
    {
        if (_gestureStart is not { } before) return;
        _gestureStart = null;
        if (cancel) RestoreRange(before); else _rangeHistory.Record(before, CurrentRange);
        NotifyRange();
    }
    private void RememberRange(StudioManualRange before)
    {
        if (!ValidRange()) return;
        bool beforeWasValid = before.Start >= 0 && before.End > before.Start &&
            before.End <= SourceMaximumSeconds && before.End - before.Start <= 180;
        if (_gestureStart is null)
            _rangeHistory.Record(beforeWasValid ? before : _lastValidRange ?? CurrentRange, CurrentRange);
        _lastValidRange = CurrentRange;
    }
    private void RestoreRange(StudioManualRange range)
    {
        _start = Format(range.Start); _end = Format(range.End);
        _lastValidRange = CurrentRange;
        NotifyRange();
    }
    private void RevealPlayhead()
    {
        if (_position < _viewportStart) SetViewport(_position, _viewportDuration);
        else if (_position > _viewportStart + _viewportDuration)
            SetViewport(_position - _viewportDuration, _viewportDuration);
    }
    private void SetViewport(double start, double duration)
    {
        if (!double.IsFinite(start) || !double.IsFinite(duration)) return;
        _viewportDuration = Math.Clamp(duration, Math.Min(5, SourceMaximumSeconds), Math.Max(5, SourceMaximumSeconds));
        _viewportStart = Math.Clamp(start, 0, Math.Max(0, SourceMaximumSeconds - _viewportDuration));
        OnPropertyChanged(nameof(ViewportStartSeconds)); OnPropertyChanged(nameof(ViewportDurationSeconds));
        RefreshFilmstrip();
    }
    private void RefreshFilmstrip() => Filmstrip.Request(IsOpen
        ? _project?.SourceMedia.FirstOrDefault(media => media.FullPath.Equals(_source?.FullPath, StringComparison.OrdinalIgnoreCase))
        : null, _viewportStart, _viewportDuration);
    private void Zoom(double factor)
    {
        double center = _position >= _viewportStart && _position <= _viewportStart + _viewportDuration
            ? _position : _viewportStart + _viewportDuration / 2;
        double duration = Math.Clamp(_viewportDuration * factor, Math.Min(5, SourceMaximumSeconds), Math.Max(5, SourceMaximumSeconds));
        SetViewport(center - duration / 2, duration);
    }
    private bool ValidRange() => _project?.IsFinalized == false && _source is not null &&
        TryTime(_start, out var start) && TryTime(_end, out var end) &&
        start >= TimeSpan.Zero && end > start && end <= _source.Duration && end - start <= TimeSpan.FromMinutes(3);
    private bool CanAdd() => _editor is not null && _canMutate() && ValidRange();
    private void PrepareAroundPlayhead()
    {
        if (!IsOpen || _source is null || _project is null) return;
        // Stable, bounded windows make scrubbing within a prepared minute an immediate seek.
        double start = Math.Max(0, Math.Min(Math.Floor(_position / 30) * 30, SourceMaximumSeconds - 60));
        BindPreview(start, Math.Min(start + 60, SourceMaximumSeconds), _position);
    }
    private void PreviewSelection()
    {
        if (!ValidRange()) return;
        _position = SelectionStartSeconds;
        OnPropertyChanged(nameof(SourcePositionSeconds)); OnPropertyChanged(nameof(PlayheadText));
        BindPreview(SelectionStartSeconds, SelectionEndSeconds, _position);
    }
    private void PreviewSuggestion(StudioMomentSuggestion moment)
    {
        if (!IsOpen || _project is null || _source is null) return;
        _position = moment.Start;
        OnPropertyChanged(nameof(SourcePositionSeconds)); OnPropertyChanged(nameof(PlayheadText));
        if (moment.Start < _viewportStart || moment.Start >= _viewportStart + _viewportDuration)
            SetViewport(moment.Start - _viewportDuration * 0.15, _viewportDuration);
        BindPreview(moment.Start, Math.Min(moment.End, moment.Start + 180), moment.Start);
        _suggestionWarmup.Restart(Suggestions.Items.SkipWhile(item => item.Id != moment.Id).Skip(1).Take(2)
            .Select(item => _project.CreateManualSourceAsset(_source.FullPath, TimeSpan.FromSeconds(item.Start),
                TimeSpan.FromSeconds(Math.Min(item.End, item.Start + 180)))).ToArray());
    }
    private void UseSuggestion(StudioMomentSuggestion moment)
    {
        var before = CurrentRange;
        _start = Format(moment.Start);
        _end = Format(Math.Min(moment.End, moment.Start + 180));
        RememberRange(before);
        NotifyRange();
        PreviewSuggestion(moment);
    }
    private void BindPreview(double start, double end, double position)
    {
        _previewDebounce.Cancel();
        _pendingPreviewNavigation = false;
        _updatingPreview = true;
        try
        {
            Preview.Bind(true, _project, _project!.CreateManualSourceAsset(_source!.FullPath,
                TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)));
            Preview.PreviewPositionSeconds = position;
        }
        finally { _updatingPreview = false; }
    }
    private void Add()
    {
        if (!CanAdd()) return;
        TryTime(_start, out var start); TryTime(_end, out var end);
        _editor!.AddManualSourceClip(_project!.Id, _source!.FullPath, start, end);
        ClipAdded?.Invoke(this, EventArgs.Empty);
        IsOpen = false;
    }
    private void NotifyRange()
    {
        foreach (string name in new[] { nameof(StartText), nameof(EndText), nameof(SelectionStartSeconds),
                     nameof(SelectionEndSeconds), nameof(RangeSummary), nameof(Status) }) OnPropertyChanged(name);
        _add.RaiseCanExecuteChanged();
        _undoRange.RaiseCanExecuteChanged(); _redoRange.RaiseCanExecuteChanged();
    }
    private void Notify()
    {
        foreach (string name in new[] { nameof(Sources), nameof(SelectedSource), nameof(SourceMaximumSeconds),
                     nameof(SourcePositionSeconds), nameof(HasMultipleSources), nameof(RecordingLength), nameof(PlayheadText) }) OnPropertyChanged(name);
        NotifyRange();
        _toggle.RaiseCanExecuteChanged();
    }
    private static bool TryTime(string value, out TimeSpan time)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
        { time = TimeSpan.FromSeconds(seconds); return true; }
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
    }
    private static string Format(double seconds) => TimeSpan.FromSeconds(Math.Round(seconds, 3))
        .ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
}
