using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public partial class StudioPreviewView : UserControl
{
    private StudioPreviewViewModel? _viewModel;
    private readonly DispatcherTimer _positionTimer;
    private bool _isScrubbing;
    private bool _isPlaybackSurfaceActive;
    private bool _isPlaybackClockRunning;
    private long _playbackClockStartedTimestamp;
    private double _playbackClockStartedProxySeconds;

    public StudioPreviewView()
    {
        InitializeComponent();
        _positionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1000d / 30d),
            DispatcherPriority.Normal,
            OnPositionTimerTick,
            Dispatcher);
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        PreviewMouseDown += (_, _) => Focus();
        PreviewPositionSlider.AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(
                PreviewPosition_OnPreviewMouseLeftButtonDown),
            handledEventsToo: true);
        PreviewPositionSlider.AddHandler(
            Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler(
                PreviewPosition_OnPreviewMouseLeftButtonUp),
            handledEventsToo: true);
        PreviewPositionSlider.AddHandler(
            Mouse.LostMouseCaptureEvent,
            new MouseEventHandler(PreviewPosition_OnLostMouseCapture),
            handledEventsToo: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Bind(DataContext as StudioPreviewViewModel);
        UpdatePlaybackSurfaceActivity();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DeactivatePlaybackSurface();
        Bind(null);
    }

    private void OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        UpdatePlaybackSurfaceActivity();

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        Bind(e.NewValue as StudioPreviewViewModel);
        if (_isPlaybackSurfaceActive)
        {
            ApplyAll();
        }
    }

    private void Bind(StudioPreviewViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!_isPlaybackSurfaceActive)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(StudioPreviewViewModel.PreviewMediaPath):
                ApplySource();
                break;
            case nameof(StudioPreviewViewModel.PreviewSeekVersion):
                if (!_isScrubbing)
                {
                    ApplyPosition();
                }
                break;
            case nameof(StudioPreviewViewModel.IsPreviewPlaying):
                ApplyPlayback();
                break;
        }

        UpdatePositionSampling();
    }

    private void ApplyAll()
    {
        ApplySource();
        ApplyPosition();
        ApplyPlayback();
    }

    private void UpdatePlaybackSurfaceActivity()
    {
        if (IsLoaded && IsVisible)
        {
            if (_isPlaybackSurfaceActive)
            {
                return;
            }

            _isPlaybackSurfaceActive = true;
            ApplyAll();
            UpdatePositionSampling();
            return;
        }

        DeactivatePlaybackSurface();
    }

    private void DeactivatePlaybackSurface()
    {
        _isPlaybackSurfaceActive = false;
        _isScrubbing = false;
        _isPlaybackClockRunning = false;
        _positionTimer.Stop();
        PreviewPlayer.Stop();
        PreviewPlayer.Source = null;
    }

    private void ApplySource()
    {
        Uri? requestedSource = _viewModel?.PreviewMediaPath is { } path
            ? new Uri(path, UriKind.Absolute)
            : null;
        if (Equals(PreviewPlayer.Source, requestedSource))
        {
            return;
        }

        PreviewPlayer.Stop();
        PreviewPlayer.Source = requestedSource;
        UpdatePositionSampling();
    }

    private void ApplyPosition()
    {
        if (_viewModel?.IsPreviewAvailable != true)
        {
            return;
        }
        PreviewPlayer.Position = TimeSpan.FromSeconds(
            Math.Max(
                0,
                _viewModel.PreviewPositionSeconds -
                _viewModel.PreviewSourceOffsetSeconds));
        if (_viewModel.IsPreviewPlaying)
        {
            StartPlaybackClock();
        }
    }

    private void ApplyPlayback()
    {
        if (_viewModel?.IsPreviewPlaying == true)
        {
            StartPlaybackClock();
            PreviewPlayer.Play();
        }
        else
        {
            ReportBestPlaybackPosition();
            _isPlaybackClockRunning = false;
            PreviewPlayer.Pause();
        }
        UpdatePositionSampling();
    }

    private void UpdatePositionSampling()
    {
        if (_isPlaybackSurfaceActive &&
            PreviewPlayer.Source is not null &&
            _viewModel?.RequiresPlaybackPositionSampling == true)
        {
            _positionTimer.Start();
            return;
        }

        _positionTimer.Stop();
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_isPlaybackSurfaceActive &&
            PreviewPlayer.Source is not null &&
            _viewModel?.RequiresPlaybackPositionSampling == true)
        {
            ReportBestPlaybackPosition();
        }

        UpdatePositionSampling();
    }

    private void StartPlaybackClock()
    {
        if (_viewModel is null)
        {
            _isPlaybackClockRunning = false;
            return;
        }

        _playbackClockStartedProxySeconds = Math.Max(
            0,
            _viewModel.PreviewPositionSeconds -
            _viewModel.PreviewSourceOffsetSeconds);
        _playbackClockStartedTimestamp = Stopwatch.GetTimestamp();
        _isPlaybackClockRunning = true;
    }

    private void ReportBestPlaybackPosition()
    {
        if (_viewModel is null)
        {
            return;
        }

        TimeSpan nativePosition = PreviewPlayer.Position;
        if (!_isPlaybackClockRunning)
        {
            _viewModel.ReportPlaybackPosition(nativePosition);
            return;
        }

        double maximumProxySeconds = Math.Max(
            0,
            _viewModel.PreviewPositionMaximumSeconds -
            _viewModel.PreviewSourceOffsetSeconds);
        _viewModel.ReportPlaybackPosition(
            TimeSpan.FromSeconds(
                ResolvePlaybackPositionSeconds(
                    _playbackClockStartedProxySeconds,
                    Stopwatch.GetElapsedTime(
                            _playbackClockStartedTimestamp)
                        .TotalSeconds,
                    nativePosition.TotalSeconds,
                    maximumProxySeconds)));
    }

    internal static double ResolvePlaybackPositionSeconds(
        double playbackStartedProxySeconds,
        double elapsedSeconds,
        double nativePositionSeconds,
        double maximumProxySeconds)
    {
        double clockPositionSeconds = Math.Clamp(
            playbackStartedProxySeconds + elapsedSeconds,
            0,
            Math.Max(0, maximumProxySeconds));

        // Some Windows Media Foundation graphs visibly play native frames while
        // MediaElement.Position remains fixed at zero. Prefer native time once it
        // demonstrably follows the monotonic playback clock; otherwise keep the
        // playhead and captions driven by that same real-time playback interval.
        bool nativeClockIsAdvancing =
            double.IsFinite(nativePositionSeconds) &&
            nativePositionSeconds > playbackStartedProxySeconds + 0.05 &&
            Math.Abs(nativePositionSeconds - clockPositionSeconds) <= 1.0;
        return nativeClockIsAdvancing
            ? Math.Clamp(nativePositionSeconds, 0, maximumProxySeconds)
            : clockPositionSeconds;
    }

    private void PreviewPosition_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }
        _isScrubbing = true;
        _viewModel?.BeginScrub();
    }

    private void PreviewPosition_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            EndScrub();
        }
    }

    private void PreviewPosition_OnLostMouseCapture(
        object sender,
        MouseEventArgs e) =>
        EndScrub();

    private void EndScrub()
    {
        if (!_isScrubbing)
        {
            return;
        }
        _isScrubbing = false;
        _viewModel?.EndScrub();
    }

    private void PreviewPlayer_OnMediaOpened(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isPlaybackSurfaceActive)
        {
            return;
        }

        _viewModel?.ReportOpened();
        UpdatePositionSampling();
        Uri? openedSource = PreviewPlayer.Source;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (!_isPlaybackSurfaceActive ||
                    openedSource != PreviewPlayer.Source)
                {
                    return;
                }

                // MediaElement can ignore a seek issued synchronously from
                // MediaOpened while the native graph is still completing.
                // Defer the bounded-preview seek before starting playback so
                // source-relative time never appears stuck at zero.
                ApplyPosition();
                ApplyPlayback();
            }));
    }

    private void PreviewPlayer_OnMediaEnded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isPlaybackSurfaceActive)
        {
            return;
        }

        _isPlaybackClockRunning = false;
        _viewModel?.ReportPlaybackPosition(
            TimeSpan.FromSeconds(
                Math.Max(
                    0,
                    (_viewModel.PreviewPositionMaximumSeconds -
                     _viewModel.PreviewSourceOffsetSeconds))));
    }

    private void PreviewPlayer_OnMediaFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        if (_isPlaybackSurfaceActive)
        {
            _viewModel?.ReportFailure(
                e.ErrorException?.Message ??
                "Windows could not decode the bounded Studio preview.");
        }
    }

    private void OnGraphicDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = TryGetSingleFile(e.Data, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnGraphicDrop(object sender, DragEventArgs e)
    {
        if (TryGetSingleFile(e.Data, out string? path))
        {
            _viewModel?.TryAddGraphicFile(path!);
        }
        e.Handled = true;
    }

    private static bool TryGetSingleFile(IDataObject data, out string? path)
    {
        path = (data.GetData(DataFormats.FileDrop) as string[])?
            .SingleOrDefault();
        if (path is null)
        {
            return false;
        }
        string extension = System.IO.Path.GetExtension(path);
        return new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase);
    }
}
