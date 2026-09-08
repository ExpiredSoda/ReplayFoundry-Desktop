using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public partial class StudioPreviewView : UserControl
{
    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader), typeof(bool), typeof(StudioPreviewView), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowPositionSliderProperty = DependencyProperty.Register(
        nameof(ShowPositionSlider), typeof(bool), typeof(StudioPreviewView), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowDetailsProperty = DependencyProperty.Register(
        nameof(ShowDetails), typeof(bool), typeof(StudioPreviewView), new PropertyMetadata(true));
    public bool ShowHeader { get => (bool)GetValue(ShowHeaderProperty); set => SetValue(ShowHeaderProperty, value); }
    public bool ShowPositionSlider { get => (bool)GetValue(ShowPositionSliderProperty); set => SetValue(ShowPositionSliderProperty, value); }
    public bool ShowDetails { get => (bool)GetValue(ShowDetailsProperty); set => SetValue(ShowDetailsProperty, value); }

    private const int MaximumMediaOpenRetries = 1;
    private static readonly TimeSpan SeekPrimeStartupDelay =
        TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan SeekPrimeSettleDelay =
        TimeSpan.FromMilliseconds(125);
    private StudioPreviewViewModel? _viewModel;
    private readonly DispatcherTimer _positionTimer;
    private MediaElement _previewPlayer;
    private bool _isScrubbing;
    private bool _isPlaybackSurfaceActive;
    private bool _isMediaRetryPending;
    private bool _isSeekPrimePending;
    private bool _isPlaybackClockRunning;
    private int _mediaOpenRetryCount;
    private int _mediaSourceVersion;
    private int _seekPrimeVersion;
    private int _boundPreviewSessionVersion = -1;
    private bool _isCurrentSourceOpened;
    private long _playbackClockStartedTimestamp;
    private double _playbackClockStartedProxySeconds;
    private double _lastReportedProxySeconds;

    public StudioPreviewView()
    {
        InitializeComponent();
        _previewPlayer = CreateNativeMediaPlayer();
        PreviewPlayerHost.Children.Add(_previewPlayer);
        ConfigureNativeAudio();
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

    internal MediaElement PreviewPlayer => _previewPlayer;

    private void OnCaptionHelpClick(object sender, RoutedEventArgs e)
    {
        if (CaptionHelpButton.ToolTip is ToolTip tooltip) tooltip.IsOpen = false;
        CaptionHelpPopup.IsOpen = !CaptionHelpPopup.IsOpen;
    }

    private void OnCaptionHelpKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            OnCaptionHelpClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && CaptionHelpPopup.IsOpen)
        {
            CaptionHelpPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnCaptionHelpLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CaptionHelpPopup.IsOpen = false;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Bind(DataContext as StudioPreviewViewModel);
        UpdatePlaybackSurfaceActivity();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CaptionHelpPopup.IsOpen = false;
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
        _isMediaRetryPending = false;
        _mediaSourceVersion++;
        ReleaseNativeMediaGraph();
    }

    private void ApplySource()
    {
        Uri? requestedSource = _viewModel?.PreviewMediaPath is { } path
            ? new Uri(path, UriKind.Absolute)
            : null;
        int requestedSessionVersion =
            _viewModel?.PreviewSessionVersion ?? -1;
        if (Equals(PreviewPlayer.Source, requestedSource) &&
            _boundPreviewSessionVersion == requestedSessionVersion)
        {
            return;
        }

        _mediaOpenRetryCount = 0;
        _isMediaRetryPending = false;
        _boundPreviewSessionVersion = requestedSessionVersion;
        ReplaceNativeMediaSource(requestedSource);
        UpdatePositionSampling();
    }

    private void ApplyPosition()
    {
        if (_viewModel?.IsPreviewAvailable != true ||
            !IsBoundToCurrentPreviewSession())
        {
            return;
        }
        double proxyPosition = Math.Max(
            0,
            _viewModel.PreviewPositionSeconds -
            _viewModel.PreviewSourceOffsetSeconds);
        PreviewPlayer.Position = TimeSpan.FromSeconds(proxyPosition);
        if (_viewModel.IsPreviewPlaying)
        {
            CancelSeekPrime();
            StartPlaybackClock();
            return;
        }
        BeginPausedSeekPrime(proxyPosition);
    }

    private void ApplyPlayback()
    {
        if (_isSeekPrimePending)
        {
            UpdatePositionSampling();
            return;
        }
        if (_viewModel?.IsPreviewPlaying == true &&
            IsBoundToCurrentPreviewSession())
        {
            ConfigureNativeAudio();
            PreviewPlayer.Play();
            StartPlaybackClock();
        }
        else
        {
            ReportBestPlaybackPosition();
            PreviewPlayer.Pause();
            StopPlaybackClock();
        }
        UpdatePositionSampling();
    }

    private void UpdatePositionSampling()
    {
        if (_isPlaybackSurfaceActive &&
            PreviewPlayer.Source is not null &&
            IsBoundToCurrentPreviewSession() &&
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
            IsBoundToCurrentPreviewSession() &&
            _viewModel?.RequiresPlaybackPositionSampling == true)
        {
            ReportBestPlaybackPosition();
        }

        UpdatePositionSampling();
    }

    private void ReportBestPlaybackPosition()
    {
        if (_isSeekPrimePending ||
            _viewModel is null ||
            !IsBoundToCurrentPreviewSession())
        {
            return;
        }

        double maximumProxySeconds = Math.Max(
            0,
            _viewModel.PreviewPositionMaximumSeconds -
            _viewModel.PreviewSourceOffsetSeconds);
        double resolvedPosition;
        if (_isPlaybackClockRunning && PreviewPlayer.IsBuffering)
        {
            resolvedPosition = Math.Clamp(
                _lastReportedProxySeconds,
                0,
                maximumProxySeconds);
            RebaselinePlaybackClock(resolvedPosition);
        }
        else if (_isPlaybackClockRunning)
        {
            resolvedPosition = ResolvePlaybackPositionSeconds(
                _playbackClockStartedProxySeconds,
                Stopwatch.GetElapsedTime(
                        _playbackClockStartedTimestamp)
                    .TotalSeconds,
                PreviewPlayer.Position.TotalSeconds,
                maximumProxySeconds);
            resolvedPosition = Math.Max(
                _lastReportedProxySeconds,
                resolvedPosition);
        }
        else
        {
            resolvedPosition = ResolveNativePlaybackPositionSeconds(
                PreviewPlayer.Position.TotalSeconds,
                maximumProxySeconds);
        }
        _lastReportedProxySeconds = resolvedPosition;
        _viewModel.ReportPlaybackPosition(
            TimeSpan.FromSeconds(resolvedPosition),
            _boundPreviewSessionVersion);
    }

    internal static double ResolvePlaybackPositionSeconds(
        double playbackStartedProxySeconds,
        double elapsedSeconds,
        double nativePositionSeconds,
        double maximumProxySeconds)
    {
        double maximum = Math.Max(0, maximumProxySeconds);
        double start = double.IsFinite(playbackStartedProxySeconds)
            ? Math.Clamp(playbackStartedProxySeconds, 0, maximum)
            : 0;
        double elapsed = double.IsFinite(elapsedSeconds)
            ? Math.Max(0, elapsedSeconds)
            : 0;
        double clockPosition = Math.Clamp(
            start + elapsed,
            0,
            maximum);
        if (!double.IsFinite(nativePositionSeconds))
        {
            return clockPosition;
        }

        double nativePosition = Math.Clamp(
            nativePositionSeconds,
            0,
            maximum);
        bool nativeClockIsAdvancing =
            nativePosition > start + 0.05 &&
            Math.Abs(nativePosition - clockPosition) <= 1.0;
        return nativeClockIsAdvancing
            ? nativePosition
            : clockPosition;
    }

    internal static double ResolveNativePlaybackPositionSeconds(
        double nativePositionSeconds,
        double maximumProxySeconds)
    {
        if (!double.IsFinite(nativePositionSeconds))
        {
            return 0;
        }

        // MediaElement.Position is the only clock that represents presented
        // frames. Keeping its last value during buffering also keeps captions
        // and the playhead attached to what the user can actually see and hear.
        return Math.Clamp(
            nativePositionSeconds,
            0,
            Math.Max(0, maximumProxySeconds));
    }

    private void BeginPausedSeekPrime(double proxyPosition)
    {
        CancelSeekPrime();
        _isSeekPrimePending = true;
        int primeVersion = _seekPrimeVersion;
        int sourceVersion = _mediaSourceVersion;
        int sessionVersion = _boundPreviewSessionVersion;
        MediaElement player = PreviewPlayer;
        StopPlaybackClock();
        player.IsMuted = true;
        player.Play();
        _ = CompletePausedSeekPrimeAsync(
            player,
            proxyPosition,
            primeVersion,
            sourceVersion,
            sessionVersion);
    }

    private async Task CompletePausedSeekPrimeAsync(
        MediaElement player,
        double proxyPosition,
        int primeVersion,
        int sourceVersion,
        int sessionVersion)
    {
        await Task.Delay(SeekPrimeStartupDelay);
        if (!IsCurrentSeekPrime(
                player,
                primeVersion,
                sourceVersion,
                sessionVersion))
        {
            return;
        }

        // Position can report the requested value before Media Foundation has
        // presented it. Seek once the graph is actively decoding, then let one
        // bounded decode interval settle before pausing the exact cut frame.
        player.Position = TimeSpan.FromSeconds(proxyPosition);
        await Task.Delay(SeekPrimeSettleDelay);
        if (!IsCurrentSeekPrime(
                player,
                primeVersion,
                sourceVersion,
                sessionVersion))
        {
            return;
        }

        player.Pause();
        _isSeekPrimePending = false;
        ConfigureNativeAudio();
        _viewModel?.ReportPlaybackPosition(
            TimeSpan.FromSeconds(proxyPosition),
            sessionVersion);
        UpdatePositionSampling();
    }

    private bool IsCurrentSeekPrime(
        MediaElement player,
        int primeVersion,
        int sourceVersion,
        int sessionVersion) =>
        _isPlaybackSurfaceActive &&
        _isSeekPrimePending &&
        primeVersion == _seekPrimeVersion &&
        sourceVersion == _mediaSourceVersion &&
        sessionVersion == _boundPreviewSessionVersion &&
        ReferenceEquals(player, PreviewPlayer) &&
        IsBoundToCurrentPreviewSession();

    private void CancelSeekPrime()
    {
        _seekPrimeVersion++;
        _isSeekPrimePending = false;
        if (_previewPlayer is not null)
        {
            ConfigureNativeAudio();
        }
    }

    private void ReplaceNativeMediaSource(Uri? source)
    {
        // Media Foundation tears graphs down asynchronously. A fresh WPF
        // MediaElement gives each preview session an immutable event sender,
        // including when the same cached URI is selected again after A→B→A.
        _mediaSourceVersion++;
        ReleaseNativeMediaGraph();
        if (source is null)
        {
            return;
        }

        ConfigureNativeAudio();
        PreviewPlayer.Source = source;
    }

    private void ReleaseNativeMediaGraph()
    {
        _isCurrentSourceOpened = false;
        _positionTimer.Stop();
        StopPlaybackClock();
        CancelSeekPrime();
        MediaElement retired = _previewPlayer;
        retired.MediaOpened -= PreviewPlayer_OnMediaOpened;
        retired.MediaEnded -= PreviewPlayer_OnMediaEnded;
        retired.MediaFailed -= PreviewPlayer_OnMediaFailed;
        PreviewPlayerHost.Children.Remove(retired);
        retired.Stop();
        retired.Close();
        retired.Source = null;
        _previewPlayer = CreateNativeMediaPlayer();
        PreviewPlayerHost.Children.Add(_previewPlayer);
    }

    private MediaElement CreateNativeMediaPlayer()
    {
        var player = new MediaElement
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            ScrubbingEnabled = true,
            Stretch = System.Windows.Media.Stretch.Fill,
        };
        player.MediaOpened += PreviewPlayer_OnMediaOpened;
        player.MediaEnded += PreviewPlayer_OnMediaEnded;
        player.MediaFailed += PreviewPlayer_OnMediaFailed;
        return player;
    }

    private void ConfigureNativeAudio()
    {
        PreviewPlayer.IsMuted = false;
        PreviewPlayer.Volume = 1d;
        PreviewPlayer.Balance = 0d;
    }

    private void StartPlaybackClock()
    {
        double proxyPosition = Math.Max(
            0,
            (_viewModel?.PreviewPositionSeconds ?? 0) -
            (_viewModel?.PreviewSourceOffsetSeconds ?? 0));
        RebaselinePlaybackClock(proxyPosition);
    }

    private void RebaselinePlaybackClock(double proxyPosition)
    {
        _playbackClockStartedProxySeconds = Math.Max(0, proxyPosition);
        _lastReportedProxySeconds = _playbackClockStartedProxySeconds;
        _playbackClockStartedTimestamp = Stopwatch.GetTimestamp();
        _isPlaybackClockRunning = true;
    }

    private void StopPlaybackClock()
    {
        _isPlaybackClockRunning = false;
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
        if (sender is not MediaElement openedPlayer ||
            !ReferenceEquals(openedPlayer, PreviewPlayer) ||
            !_isPlaybackSurfaceActive)
        {
            return;
        }

        if (!IsBoundToCurrentPreviewSession(requireOpened: false))
        {
            return;
        }

        _isCurrentSourceOpened = true;
        int openedSourceVersion = _mediaSourceVersion;
        int openedSessionVersion = _boundPreviewSessionVersion;
        _viewModel?.ReportOpened(openedSessionVersion);
        ConfigureNativeAudio();
        SafeDiagnosticTrace.Write(
            "Studio preview media opened",
            $"retry={_mediaOpenRetryCount}; " +
            $"durationKnown={openedPlayer.NaturalDuration.HasTimeSpan}");
        UpdatePositionSampling();
        Uri? openedSource = openedPlayer.Source;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (!_isPlaybackSurfaceActive ||
                    !ReferenceEquals(openedPlayer, PreviewPlayer) ||
                    openedSource != openedPlayer.Source ||
                    openedSourceVersion != _mediaSourceVersion ||
                    openedSessionVersion != _boundPreviewSessionVersion ||
                    !IsBoundToCurrentPreviewSession())
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
        if (sender is not MediaElement endedPlayer ||
            !ReferenceEquals(endedPlayer, PreviewPlayer))
        {
            return;
        }
        double selectedEndInProxySeconds = Math.Max(
            0,
            (_viewModel?.PreviewPositionMaximumSeconds ?? 0) -
            (_viewModel?.PreviewSourceOffsetSeconds ?? 0));
        if (!_isPlaybackSurfaceActive ||
            !IsBoundToCurrentPreviewSession())
        {
            return;
        }

        // MediaElement may reset Position before raising MediaEnded. The event
        // sender is the immutable current graph, so completion is authoritative
        // even when its native position has already returned to zero.
        _viewModel?.ReportPlaybackPosition(
            TimeSpan.FromSeconds(selectedEndInProxySeconds),
            _boundPreviewSessionVersion);
    }

    private void PreviewPlayer_OnMediaFailed(
        object? sender,
        ExceptionRoutedEventArgs e)
    {
        if (sender is not MediaElement failedPlayer ||
            !ReferenceEquals(failedPlayer, PreviewPlayer) ||
            !_isPlaybackSurfaceActive ||
            !IsBoundToCurrentPreviewSession(requireOpened: false))
        {
            return;
        }

        Exception failure = e.ErrorException ?? new InvalidOperationException(
            "Windows could not play this Studio preview.");
        SafeDiagnosticTrace.Write("Studio preview media failed", failure);
        if (TryScheduleMediaRetry())
        {
            return;
        }

        _viewModel?.ReportFailure(
            failure.Message,
            _boundPreviewSessionVersion);
    }

    private bool TryScheduleMediaRetry()
    {
        Uri? failedSource = PreviewPlayer.Source;
        if (failedSource is null ||
            _isMediaRetryPending ||
            _mediaOpenRetryCount >= MaximumMediaOpenRetries)
        {
            return false;
        }

        _mediaOpenRetryCount++;
        _isMediaRetryPending = true;
        int expectedSourceVersion = _mediaSourceVersion;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => RetryMediaSource(
                failedSource,
                expectedSourceVersion)));
        return true;
    }

    private void RetryMediaSource(Uri failedSource, int expectedSourceVersion)
    {
        _isMediaRetryPending = false;
        Uri? requestedSource = _viewModel?.PreviewMediaPath is { } path
            ? new Uri(path, UriKind.Absolute)
            : null;
        if (!_isPlaybackSurfaceActive ||
            expectedSourceVersion != _mediaSourceVersion ||
            !Equals(failedSource, requestedSource))
        {
            return;
        }

        SafeDiagnosticTrace.Write(
            "Studio preview media retry",
            $"attempt={_mediaOpenRetryCount}; sourceVersion={_mediaSourceVersion}");
        ReplaceNativeMediaSource(failedSource);
        UpdatePositionSampling();
    }

    private bool IsBoundToCurrentPreviewSession(bool requireOpened = true)
    {
        if (_viewModel is null ||
            _boundPreviewSessionVersion !=
                _viewModel.PreviewSessionVersion ||
            PreviewPlayer.Source is not { } currentSource ||
            _viewModel.PreviewMediaPath is not { } currentPath ||
            !Equals(currentSource, new Uri(currentPath, UriKind.Absolute)))
        {
            return false;
        }

        return !requireOpened || _isCurrentSourceOpened;
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
