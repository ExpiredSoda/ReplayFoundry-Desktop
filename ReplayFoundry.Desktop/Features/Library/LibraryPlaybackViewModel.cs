using System.IO;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Library;

/// <summary>
/// Owns transport state for previewing an already-rendered Library asset.
/// The view supplies native MediaElement events; no media is copied,
/// extracted, or transcoded for Library playback.
/// </summary>
public sealed class LibraryPlaybackViewModel : ObservableObject
{
    private readonly DelegateCommand _playPauseCommand;
    private readonly DelegateCommand _rewindCommand;
    private readonly DelegateCommand _forwardCommand;
    private string? _mediaFullPath;
    private double _durationSeconds;
    private double _positionSeconds;
    private int _seekVersion;
    private bool _isPlaying;
    private bool _isScrubbing;
    private bool _resumeAfterScrub;
    private string _statusText = "Select a ready video to preview it.";

    public LibraryPlaybackViewModel()
    {
        _playPauseCommand = new DelegateCommand(TogglePlayback, CanUsePlayback);
        _rewindCommand = new DelegateCommand(() => SeekBy(-5), CanUsePlayback);
        _forwardCommand = new DelegateCommand(() => SeekBy(5), CanUsePlayback);
    }

    public string? MediaFullPath => _mediaFullPath;
    public bool IsAvailable => MediaFullPath is not null;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayPauseText));
            OnPropertyChanged(nameof(PlayPauseIconKey));
        }
    }
    public double PositionSeconds
    {
        get => _positionSeconds;
        set => SetPosition(value, requestSeek: true);
    }
    public double DurationSeconds => Math.Max(0, _durationSeconds);
    public int SeekVersion => _seekVersion;
    public string PositionText =>
        MediaTimeFormatter.Format(TimeSpan.FromSeconds(PositionSeconds));
    public string DurationText =>
        MediaTimeFormatter.Format(TimeSpan.FromSeconds(DurationSeconds));
    public string PlayPauseText => IsPlaying ? "Pause" : "Play";
    public string PlayPauseIconKey => IsPlaying ? "Icon.Pause" : "Icon.Play";
    public string StatusText => _statusText;
    public ICommand PlayPauseCommand => _playPauseCommand;
    public ICommand RewindCommand => _rewindCommand;
    public ICommand ForwardCommand => _forwardCommand;

    public void Load(LibraryItem? item)
    {
        string? path = item?.Asset is { IsAvailable: true } asset
            ? asset.OutputFullPath
            : null;
        bool sourceChanged = !string.Equals(
            path,
            _mediaFullPath,
            StringComparison.OrdinalIgnoreCase);

        IsPlaying = false;
        _isScrubbing = false;
        _resumeAfterScrub = false;
        _mediaFullPath = path;
        _durationSeconds = Math.Max(
            0,
            item?.Asset?.Duration.TotalSeconds ?? 0);
        _positionSeconds = 0;
        _statusText = path is null
            ? item?.Asset is null
                ? "Select a ready video to preview it."
                : "This Library video is missing locally. Relink it to restore playback."
            : "Preview ready. Space plays or pauses; Left and Right move five seconds.";

        if (sourceChanged)
        {
            OnPropertyChanged(nameof(MediaFullPath));
        }
        else if (path is not null)
        {
            RequestSeek();
        }
        NotifyTransportChanged();
    }

    public void BeginScrub()
    {
        if (!CanUsePlayback() || _isScrubbing) return;
        _isScrubbing = true;
        _resumeAfterScrub = IsPlaying;
        IsPlaying = false;
    }

    public void EndScrub()
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        RequestSeek();
        if (_resumeAfterScrub)
        {
            IsPlaying = true;
        }
        _resumeAfterScrub = false;
    }

    public void ReportOpened(TimeSpan naturalDuration)
    {
        if (!IsAvailable) return;
        if (naturalDuration > TimeSpan.Zero &&
            double.IsFinite(naturalDuration.TotalSeconds))
        {
            _durationSeconds = naturalDuration.TotalSeconds;
            _positionSeconds = Math.Clamp(_positionSeconds, 0, _durationSeconds);
        }
        _statusText =
            "Preview ready. Drag the timeline to inspect any point in the rendered video.";
        NotifyTransportChanged();
    }

    public void ReportPlaybackPosition(TimeSpan position)
    {
        if (!IsAvailable || _isScrubbing || position < TimeSpan.Zero) return;
        SetPosition(position.TotalSeconds, requestSeek: false);
    }

    public void ReportEnded()
    {
        IsPlaying = false;
        SetPosition(DurationSeconds, requestSeek: false);
    }

    public void ReportFailure(string? diagnostic)
    {
        IsPlaying = false;
        _statusText = string.IsNullOrWhiteSpace(diagnostic)
            ? "Windows could not play this rendered video."
            : "This rendered video could not be played: " + diagnostic.Trim();
        OnPropertyChanged(nameof(StatusText));
    }

    private bool CanUsePlayback() =>
        IsAvailable && MediaFullPath is { } path && File.Exists(path);

    private void TogglePlayback()
    {
        if (!CanUsePlayback()) return;
        if (PositionSeconds >= DurationSeconds - 0.05)
        {
            SetPosition(0, requestSeek: true);
        }
        IsPlaying = !IsPlaying;
    }

    private void SeekBy(double seconds)
    {
        if (!CanUsePlayback() || !double.IsFinite(seconds)) return;
        SetPosition(PositionSeconds + seconds, requestSeek: true);
    }

    private void SetPosition(double value, bool requestSeek)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        double normalized = Math.Clamp(value, 0, DurationSeconds);
        if (Math.Abs(_positionSeconds - normalized) < 0.0005) return;
        _positionSeconds = normalized;
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(PositionText));
        if (requestSeek) RequestSeek();
        if (normalized >= DurationSeconds - 0.05 && DurationSeconds > 0)
        {
            IsPlaying = false;
        }
    }

    private void RequestSeek()
    {
        _seekVersion++;
        OnPropertyChanged(nameof(SeekVersion));
    }

    private void NotifyTransportChanged()
    {
        foreach (string propertyName in new[]
        {
            nameof(IsAvailable), nameof(PositionSeconds),
            nameof(DurationSeconds), nameof(PositionText),
            nameof(DurationText), nameof(StatusText),
        })
        {
            OnPropertyChanged(propertyName);
        }
        _playPauseCommand.RaiseCanExecuteChanged();
        _rewindCommand.RaiseCanExecuteChanged();
        _forwardCommand.RaiseCanExecuteChanged();
    }
}
