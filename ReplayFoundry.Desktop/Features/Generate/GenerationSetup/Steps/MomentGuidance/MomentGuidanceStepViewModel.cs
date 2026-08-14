using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;

public sealed class MomentGuidanceStepViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly GenerationSetupDraft _draft;
    private readonly ReadOnlyCollection<MomentGuidanceSourceViewModel> _sources;
#pragma warning disable CA2213 // Alias to an item owned and disposed through Sources.
    private MomentGuidanceSourceViewModel _selectedSource;
#pragma warning restore CA2213

    public MomentGuidanceStepViewModel(
        GenerationSetupDraft draft,
        IVideoPreviewFrameProvider? previewFrameProvider = null)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        MomentGuidanceSourceViewModel[] sources = draft.Request.PreparedSources
            .Select(
                source => new MomentGuidanceSourceViewModel(
                    source,
                    draft.MomentGuidance.ForSource(source.Media.FullPath),
                    GuidanceChanged,
                    previewFrameProvider))
            .ToArray();
        if (sources.Length == 0)
        {
            throw new ArgumentException(
                "Moment guidance requires at least one prepared source.",
                nameof(draft));
        }
        _sources = Array.AsReadOnly(sources);
        _selectedSource = sources.Single(static source => source.IsReference);
        _ = _selectedSource.RefreshPreviewAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MomentGuidanceSourceViewModel> Sources => _sources;

    public MomentGuidanceSourceViewModel SelectedSource
    {
        get => _selectedSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_sources.Contains(value))
            {
                throw new ArgumentException(
                    "The selected guidance source must belong to this setup.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedSource, value))
            {
                return;
            }
            _selectedSource = value;
            OnPropertyChanged();
            _ = _selectedSource.RefreshPreviewAsync();
        }
    }

    public bool IsValid => true;

    public void Dispose()
    {
        foreach (MomentGuidanceSourceViewModel source in _sources)
        {
            source.Dispose();
        }
    }

    public string Summary => _sources.Sum(static source => source.Items.Count) switch
    {
        0 => "Optional — no human moment guidance added.",
        1 => "1 human priority added.",
        int count => $"{count} human priorities added.",
    };

    private void GuidanceChanged()
    {
        _draft.UpdateMomentGuidance(
            new GenerationMomentGuidance(
                _sources.SelectMany(
                    static source => source.Items.Select(static item => item.Guidance))));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsValid));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MomentGuidanceSourceViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly PreparedGenerationSource _source;
    private readonly Action _changed;
    private readonly ObservableCollection<UserMomentGuidanceItemViewModel> _items;
    private readonly DelegateCommand _addPointCommand;
    private readonly DelegateCommand _captureRangeStartCommand;
    private readonly DelegateCommand _captureRangeEndCommand;
    private readonly DelegateCommand _addRangeCommand;
    private readonly DelegateCommand<UserMomentGuidanceItemViewModel> _removeCommand;
    private readonly AsyncDelegateCommand _refreshPreviewCommand;
    private double _currentPositionSeconds;
    private double _rangeStartSeconds;
    private double _rangeEndSeconds;
    private bool _isPlaybackOpen;
    private bool _isPlaybackPlaying;
    private string _playbackStatus =
        "Opening the prepared source for playback…";

    public MomentGuidanceSourceViewModel(
        PreparedGenerationSource source,
        IEnumerable<UserMomentGuidance> existing,
        Action changed,
        IVideoPreviewFrameProvider? previewFrameProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentNullException.ThrowIfNull(existing);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _items = new ObservableCollection<UserMomentGuidanceItemViewModel>(
            existing.Select(item => new UserMomentGuidanceItemViewModel(item, Remove)));
        _currentPositionSeconds = Math.Min(30, MaximumSeconds / 2);
        _rangeStartSeconds = _currentPositionSeconds;
        _rangeEndSeconds = Math.Min(MaximumSeconds, _currentPositionSeconds + 30);
        _addPointCommand = new DelegateCommand(AddPoint);
        _captureRangeStartCommand = new DelegateCommand(CaptureRangeStart);
        _captureRangeEndCommand = new DelegateCommand(CaptureRangeEnd);
        _addRangeCommand = new DelegateCommand(AddRange, () => CanAddRange);
        _removeCommand = new DelegateCommand<UserMomentGuidanceItemViewModel>(
            Remove,
            item => item is not null && _items.Contains(item));
        Preview = previewFrameProvider is null
            ? null
            : new CompositionPreviewViewModel(source, previewFrameProvider);
        if (Preview is not null)
        {
            Preview.RequestedTimestampSeconds = Math.Min(
                _currentPositionSeconds,
                Preview.MaximumTimestampSeconds);
        }
        _refreshPreviewCommand = new AsyncDelegateCommand(
            RefreshPreviewAsync,
            () => Preview is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName =>
        _source.Source.FileName + (_source.Source.IsReference ? " · reference" : string.Empty);

    public override string ToString() => DisplayName;

    public bool IsReference => _source.Source.IsReference;
    public string SourceFullPath => _source.Media.FullPath;
    public double MaximumSeconds => _source.Media.Duration.TotalSeconds;
    public double PreviewMaximumSeconds => MaximumSeconds;
    public string DurationText => MediaTimeFormatter.Format(_source.Media.Duration);
    public IReadOnlyList<UserMomentGuidanceItemViewModel> Items => _items;
    public bool HasItems => _items.Count > 0;
    public CompositionPreviewViewModel? Preview { get; }
    public bool HasVisualPreview => Preview is not null;
    public bool IsPlaybackOpen => _isPlaybackOpen;
    public bool IsPlaybackPlaying => _isPlaybackPlaying;
    public string PlaybackStatus => _playbackStatus;
    public string PlayPauseIconKey => IsPlaybackPlaying
        ? "Icon.Pause"
        : "Icon.Play";

    public double CurrentPositionSeconds
    {
        get => _currentPositionSeconds;
        set
        {
            double bounded = Math.Clamp(value, 0, MaximumSeconds);
            if (Math.Abs(_currentPositionSeconds - bounded) < 0.001)
            {
                return;
            }
            _currentPositionSeconds = bounded;
            if (Preview is not null)
            {
                Preview.RequestedTimestampSeconds = Math.Min(
                    bounded,
                    Preview.MaximumTimestampSeconds);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText));
        }
    }

    public string CurrentPositionText =>
        MediaTimeFormatter.Format(ToTimeSpan(CurrentPositionSeconds));
    public string RangeStartText =>
        MediaTimeFormatter.Format(ToTimeSpan(_rangeStartSeconds));
    public string RangeEndText =>
        MediaTimeFormatter.Format(ToTimeSpan(_rangeEndSeconds));
    public string RangeDurationText =>
        MediaTimeFormatter.Format(
            ToTimeSpan(Math.Max(0, _rangeEndSeconds - _rangeStartSeconds)));
    public bool CanAddRange => _rangeEndSeconds - _rangeStartSeconds >= 0.25;
    public string RangeBehaviorText => !CanAddRange
        ? "Choose an end after the start."
        : _rangeEndSeconds - _rangeStartSeconds <=
          GenerationMomentGuidance.ReservedRangeMaximumDuration.TotalSeconds
            ? "This range reserves one safe candidate search; Replay Foundry still chooses the best trim inside it."
            : "This long range receives priority, but it does not force a candidate or a long output clip.";

    public ICommand AddPointCommand => _addPointCommand;
    public ICommand CaptureRangeStartCommand => _captureRangeStartCommand;
    public ICommand CaptureRangeEndCommand => _captureRangeEndCommand;
    public ICommand AddRangeCommand => _addRangeCommand;
    public ICommand RemoveCommand => _removeCommand;
    public ICommand RefreshPreviewCommand => _refreshPreviewCommand;

    public async Task RefreshPreviewAsync()
    {
        if (Preview is not null)
        {
            await Preview.LoadAsync();
            if (!Preview.IsCurrent)
            {
                await Preview.LoadAsync();
            }
        }
    }

    public void Dispose() => Preview?.Dispose();

    public void ReportPlaybackOpened()
    {
        _isPlaybackOpen = true;
        _playbackStatus =
            "Play, pause, seek, or scrub the full prepared source before adding a mark.";
        OnPropertyChanged(nameof(IsPlaybackOpen));
        OnPropertyChanged(nameof(PlaybackStatus));
    }

    public void ReportPlaybackState(bool isPlaying)
    {
        _isPlaybackPlaying = isPlaying && IsPlaybackOpen;
        OnPropertyChanged(nameof(IsPlaybackPlaying));
        OnPropertyChanged(nameof(PlayPauseIconKey));
    }

    public void ReportPlaybackFailure(string detail)
    {
        _isPlaybackOpen = false;
        _isPlaybackPlaying = false;
        _playbackStatus = string.IsNullOrWhiteSpace(detail)
            ? "Windows could not play this source. The representative frame remains available."
            : "Windows could not play this source: " + detail;
        OnPropertyChanged(nameof(IsPlaybackOpen));
        OnPropertyChanged(nameof(IsPlaybackPlaying));
        OnPropertyChanged(nameof(PlayPauseIconKey));
        OnPropertyChanged(nameof(PlaybackStatus));
    }

    private void AddPoint()
    {
        UserMomentGuidance guidance = UserMomentGuidance.CreatePoint(
            _source.Media.FullPath,
            _source.Media.Duration,
            ToTimeSpan(CurrentPositionSeconds));
        AddUnique(guidance);
    }

    private void CaptureRangeStart()
    {
        _rangeStartSeconds = CurrentPositionSeconds;
        if (_rangeEndSeconds <= _rangeStartSeconds)
        {
            _rangeEndSeconds = Math.Min(MaximumSeconds, _rangeStartSeconds + 30);
        }
        RefreshRange();
    }

    private void CaptureRangeEnd()
    {
        _rangeEndSeconds = CurrentPositionSeconds;
        RefreshRange();
    }

    private void AddRange()
    {
        if (!CanAddRange)
        {
            throw new InvalidOperationException("A priority range requires an end after its start.");
        }
        UserMomentGuidance guidance = UserMomentGuidance.CreateRange(
            _source.Media.FullPath,
            _source.Media.Duration,
            ToTimeSpan(_rangeStartSeconds),
            ToTimeSpan(_rangeEndSeconds));
        AddUnique(guidance);
    }

    private void AddUnique(UserMomentGuidance guidance)
    {
        if (_items.Any(item => item.Guidance.Id == guidance.Id))
        {
            return;
        }
        _items.Add(new UserMomentGuidanceItemViewModel(guidance, Remove));
        OnPropertyChanged(nameof(HasItems));
        _changed();
    }

    private void Remove(UserMomentGuidanceItemViewModel? item)
    {
        if (item is null || !_items.Remove(item))
        {
            return;
        }
        OnPropertyChanged(nameof(HasItems));
        _removeCommand.RaiseCanExecuteChanged();
        _changed();
    }

    private void RefreshRange()
    {
        OnPropertyChanged(nameof(RangeStartText));
        OnPropertyChanged(nameof(RangeEndText));
        OnPropertyChanged(nameof(RangeDurationText));
        OnPropertyChanged(nameof(CanAddRange));
        OnPropertyChanged(nameof(RangeBehaviorText));
        _addRangeCommand.RaiseCanExecuteChanged();
    }

    private static TimeSpan ToTimeSpan(double seconds) =>
        TimeSpan.FromMilliseconds(Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class UserMomentGuidanceItemViewModel
{
    public UserMomentGuidanceItemViewModel(
        UserMomentGuidance guidance,
        Action<UserMomentGuidanceItemViewModel> remove)
    {
        Guidance = guidance ?? throw new ArgumentNullException(nameof(guidance));
        ArgumentNullException.ThrowIfNull(remove);
        RemoveCommand = new DelegateCommand(() => remove(this));
    }

    public UserMomentGuidance Guidance { get; }
    public ICommand RemoveCommand { get; }
    public string Kind => Guidance.Kind == UserMomentGuidanceKind.PriorityPoint
        ? "Priority tick"
        : Guidance.ReservesCandidateSearch
            ? "Reserved candidate range"
            : "Priority range";
    public string Timing => Guidance.Kind == UserMomentGuidanceKind.PriorityPoint
        ? Format(Guidance.Timestamp)
        : $"{Format(Guidance.Start)} – {Format(Guidance.End)} ({Format(Guidance.Duration)})";
    public double StartSeconds => Guidance.Kind == UserMomentGuidanceKind.PriorityPoint
        ? Guidance.Timestamp.TotalSeconds
        : Guidance.Start.TotalSeconds;
    public double DurationSeconds => Guidance.Kind == UserMomentGuidanceKind.PriorityPoint
        ? 0
        : Guidance.Duration.TotalSeconds;
    public bool IsPoint => Guidance.Kind == UserMomentGuidanceKind.PriorityPoint;

    private static string Format(TimeSpan value) =>
        MediaTimeFormatter.Format(value);
}
