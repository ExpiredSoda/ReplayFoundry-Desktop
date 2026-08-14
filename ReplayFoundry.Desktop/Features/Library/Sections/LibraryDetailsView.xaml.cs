using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Features.Library.Sections;

public partial class LibraryDetailsView : UserControl
{
    private readonly DispatcherTimer _positionTimer;
    private LibraryPlaybackViewModel? _playback;
    private bool _isScrubbing;
    private bool _isActive;

    public LibraryDetailsView()
    {
        InitializeComponent();
        _positionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            OnPositionTick,
            Dispatcher);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        UpdateActiveState();

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        Deactivate();

    private void OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateActiveState();
        }
    }

    private void UpdateActiveState()
    {
        if (IsVisible)
        {
            if (_isActive) return;
            _isActive = true;
            Bind((DataContext as LibraryViewModel)?.Playback);
            ApplyAll();
            _positionTimer.Start();
            return;
        }
        Deactivate();
    }

    private void Deactivate()
    {
        if (!_isActive && _playback is null) return;
        _isActive = false;
        _positionTimer.Stop();
        PreviewPlayer.Stop();
        PreviewPlayer.Source = null;
        Bind(null);
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_isActive)
        {
            Bind((e.NewValue as LibraryViewModel)?.Playback);
            ApplyAll();
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        Focus();

    private void Bind(LibraryPlaybackViewModel? playback)
    {
        if (ReferenceEquals(_playback, playback)) return;
        if (_playback is not null)
        {
            _playback.PropertyChanged -= OnPlaybackPropertyChanged;
        }
        _playback = playback;
        if (_playback is not null)
        {
            _playback.PropertyChanged += OnPlaybackPropertyChanged;
        }
    }

    private void OnPlaybackPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LibraryPlaybackViewModel.MediaFullPath):
                ApplySource();
                break;
            case nameof(LibraryPlaybackViewModel.SeekVersion):
                ApplyPosition();
                break;
            case nameof(LibraryPlaybackViewModel.IsPlaying):
                ApplyPlayback();
                break;
        }
    }

    private void ApplyAll()
    {
        ApplySource();
        ApplyPosition();
        ApplyPlayback();
    }

    private void ApplySource()
    {
        PreviewPlayer.Stop();
        PreviewPlayer.Source = _playback?.MediaFullPath is { } path
            ? new Uri(path, UriKind.Absolute)
            : null;
    }

    private void ApplyPosition()
    {
        if (_playback?.IsAvailable != true) return;
        PreviewPlayer.Position = TimeSpan.FromSeconds(
            Math.Max(0, _playback.PositionSeconds));
    }

    private void ApplyPlayback()
    {
        if (_playback?.IsPlaying == true)
        {
            PreviewPlayer.Play();
        }
        else
        {
            PreviewPlayer.Pause();
        }
    }

    private void OnPositionTick(object? sender, EventArgs e)
    {
        if (_playback?.IsPlaying == true)
        {
            _playback.ReportPlaybackPosition(PreviewPlayer.Position);
        }
    }

    private void PreviewPosition_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _isScrubbing = true;
        _playback?.BeginScrub();
    }

    private void PreviewPosition_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) => EndScrub();

    private void PreviewPosition_OnLostMouseCapture(
        object sender,
        MouseEventArgs e) => EndScrub();

    private void EndScrub()
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        _playback?.EndScrub();
    }

    private void PreviewPlayer_OnMediaOpened(
        object sender,
        RoutedEventArgs e)
    {
        TimeSpan duration = PreviewPlayer.NaturalDuration.HasTimeSpan
            ? PreviewPlayer.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
        _playback?.ReportOpened(duration);
        ApplyPosition();
        ApplyPlayback();
    }

    private void PreviewPlayer_OnMediaEnded(
        object sender,
        RoutedEventArgs e) => _playback?.ReportEnded();

    private void PreviewPlayer_OnMediaFailed(
        object sender,
        ExceptionRoutedEventArgs e) =>
        _playback?.ReportFailure(e.ErrorException?.Message);
}
