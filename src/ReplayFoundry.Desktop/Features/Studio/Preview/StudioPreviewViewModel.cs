using System.IO;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Studio.Preview;

public sealed class StudioPreviewViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PlaybackSyncRetryDelay =
        TimeSpan.FromMilliseconds(750);
    private const int MaximumPlaybackSyncRetries = 2;
    private readonly IStudioPreviewMediaService? _mediaService;
    private readonly TimeProvider _timeProvider;
    private readonly bool _showCaptionControls;
    private readonly StudioPreviewRangeMode _rangeMode;
    private readonly DelegateCommand _playCommand;
    private readonly DelegateCommand _previousCommand;
    private readonly DelegateCommand _nextCommand;
    private readonly DelegateCommand _rewindCommand;
    private readonly DelegateCommand _forwardCommand;
    private readonly DelegateCommand _reloadCommand;
    private readonly DelegateCommand _toggleCaptionVisibilityCommand;
    private readonly object _loadSync = new();
    private CancellationTokenSource? _loadCancellation;
    private TaskCompletionSource<bool>? _loadQuiescence;
    private int _activeLoadCount;
    private StudioPreviewMediaLease? _lease;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private string? _previewMediaIdentity;
    private StudioClipAppearance? _draftAppearance;
    private readonly StudioLiveCaptionFrameCalculator
        _captionFrameCalculator = new();
    private LiveCaptionFrameState _liveCaptionFrame =
        LiveCaptionFrameState.Empty;
    private TimeSpan _rangeStart;
    private TimeSpan _rangeEnd;
    private int _loadGeneration;
    private int _previewSessionVersion;
    private double _positionSeconds;
    private double? _pendingPlaybackSyncSeconds;
    private long _pendingPlaybackSyncTimestamp;
    private int _pendingPlaybackSyncRetryCount;
    private int _seekVersion;
    private bool _hasProject;
    private bool _isPlaying;
    private bool _isUserScrubbing;
    private bool _resumeAfterScrub;
    private bool _playWhenSynchronized;
    private bool _isUpdatingRange;
    private bool _isBinding;
    private bool _needsEnvelope;
    private bool _isLoading;
    private bool _isCaptionContentVisible = true;
    private bool _isStopping;
    private bool _isDisposed;
    private string _status = "Select a clip to preview it.";
    private string? _error;

    public StudioPreviewViewModel(
        IStudioPreviewMediaService? mediaService,
        bool showCaptionControls = true,
        TimeProvider? timeProvider = null,
        StudioPreviewRangeMode rangeMode =
            StudioPreviewRangeMode.EditableEnvelope)
    {
        if (!Enum.IsDefined(rangeMode))
        {
            throw new ArgumentOutOfRangeException(nameof(rangeMode));
        }
        _mediaService = mediaService;
        _showCaptionControls = showCaptionControls;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _rangeMode = rangeMode;
        _playCommand = new DelegateCommand(TogglePlayback, CanUsePreview);
        _previousCommand = new DelegateCommand(
            () => SeekBy(-1d / 30d),
            CanUsePreview);
        _nextCommand = new DelegateCommand(
            () => SeekBy(1d / 30d),
            CanUsePreview);
        _rewindCommand = new DelegateCommand(
            () => SeekBy(-5),
            CanUsePreview);
        _forwardCommand = new DelegateCommand(
            () => SeekBy(5),
            CanUsePreview);
        _reloadCommand = new DelegateCommand(
            () => _ = ReloadPreviewAsync(),
            () => _asset is not null && !IsPreviewLoading);
        _toggleCaptionVisibilityCommand = new DelegateCommand(
            ToggleCaptionVisibility,
            () => _hasProject && _asset?.Captions is not null && _asset.RenderSettings.BurnCaptions);
    }


    public string ModeBadge => "STUDIO / EDIT";
    public event EventHandler<StudioGraphicFileDroppedEventArgs>? GraphicFileDropped;
    public string SequenceSummary => _project is null
        ? "Sequence 01 · vertical social cut"
        : $"{_project.SelectedCount} generated " +
          (_project.SelectedCount == 1 ? "moment" : "moments");
    public string ProjectPromptTitle => IsPreviewLoading
        ? "Getting your preview ready"
        : IsPreviewAvailable
            ? "Preview ready"
            : _hasProject
                ? "Preview unavailable"
                : "Bring a generated clip into Studio";
    public string? PreviewMediaPath => _lease?.MediaPath;
    public double PreviewSourceOffsetSeconds =>
        _lease?.SourceOffset.TotalSeconds ?? 0;
    public double PreviewPositionMinimumSeconds => _rangeStart.TotalSeconds;
    public double PreviewPositionMaximumSeconds => _rangeEnd.TotalSeconds;
    public double PreviewPositionSeconds
    {
        get => _positionSeconds;
        set => SetPosition(value, fromPlayback: false);
    }
    public int PreviewSeekVersion => _seekVersion;
    public int PreviewSessionVersion => _previewSessionVersion;
    public bool IsPreviewPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewPlayPauseText));
            OnPropertyChanged(nameof(PreviewPlayPauseIconKey));
        }
    }
    public bool IsPreviewLoading => _isLoading;
    public bool IsPreviewAvailable =>
        PreviewMediaPath is not null;
    public bool IsPreviewSynchronized =>
        IsPreviewAvailable && !_pendingPlaybackSyncSeconds.HasValue;
    public bool UsesSecondaryGuidancePlacement =>
        _rangeMode == StudioPreviewRangeMode.ExactSelection;
    internal bool RequiresPlaybackPositionSampling =>
        IsPreviewPlaying || _pendingPlaybackSyncSeconds.HasValue;
    public string PreviewStatus => _status;
    public string? PreviewError => _error;
    public bool HasPreviewError => !string.IsNullOrWhiteSpace(PreviewError);
    public string PreviewPlayPauseText => IsPreviewPlaying ? "Pause" : "Play";
    public string PreviewPlayPauseIconKey => IsPreviewPlaying
        ? "Icon.Pause"
        : "Icon.Play";
    public string PreviewFormatText => _asset is null
        ? "1080 × 1920 · 30 FPS"
        : GenerationClipOutputProfile.FromAsset(_asset).DisplayText;
    public double PreviewCanvasWidth => PreviewProfile.Width;
    public double PreviewCanvasHeight => PreviewProfile.Height;
    public string PreviewScaleText => _asset?.RenderSettings.Canvas switch
    {
        StudioOutputCanvas.Portrait => "PORTRAIT · 9:16",
        StudioOutputCanvas.Square => "SQUARE · 1:1",
        StudioOutputCanvas.Landscape => "LANDSCAPE · 16:9",
        _ => "SOURCE ASPECT",
    };
    public string PreviewTimecode => _asset is null
        ? "0:00"
        : StudioTimeFormatter.FormatDuration(
            TimeSpan.FromSeconds(
                Math.Max(0, PreviewPositionSeconds - _rangeStart.TotalSeconds)));
    public string PreviewDurationText => _asset is null
        ? "0:00"
        : StudioTimeFormatter.FormatDuration(_rangeEnd - _rangeStart);
    public bool IsCaptionContentVisible => _isCaptionContentVisible;
    public bool CanShowCaptionControls =>
        _showCaptionControls && _asset?.Captions is not null && _asset.RenderSettings.BurnCaptions;
    public bool HasLiveCaption =>
        IsCaptionContentVisible && _asset?.RenderSettings.BurnCaptions == true &&
        !string.IsNullOrWhiteSpace(_liveCaptionFrame.Text);
    public string? LiveCaptionText => _liveCaptionFrame.Text;
    public IReadOnlyList<StudioCaptionWordSpan> LiveCaptionEmphasisSpans =>
        _liveCaptionFrame.EmphasisSpans ?? [];
    public string? LiveSecondaryCaptionText => _liveCaptionFrame.SecondaryText;
    public bool HasLiveSecondaryCaption => IsCaptionContentVisible && _asset?.RenderSettings.BurnCaptions == true &&
        !string.IsNullOrWhiteSpace(LiveSecondaryCaptionText);
    public string? LiveCaptionActiveWord =>
        _liveCaptionFrame.ActiveWord;
    public int LiveCaptionAccentStartIndex =>
        _liveCaptionFrame.AccentStart;
    public int LiveCaptionAccentLength =>
        _liveCaptionFrame.AccentLength;
    public int LiveCaptionSweepLength =>
        _liveCaptionFrame.SweepLength;
    public double LiveCaptionAccentProgress =>
        _liveCaptionFrame.AccentProgress;
    public double LiveCaptionScale => _liveCaptionFrame.Scale;
    public double LiveCaptionVerticalPercent =>
        Math.Min(_asset?.Captions?.Segments.Any(segment => !string.IsNullOrWhiteSpace(segment.SecondaryText)) == true ? 80 : 100,
            LiveCaptionTypography.ConstrainVerticalPosition(ActiveCaptionAppearance.CaptionVerticalPositionPercent));
    public double LiveSecondaryCaptionVerticalPercent => Math.Min(90, LiveCaptionVerticalPercent + 10);
    public double LiveSecondaryCaptionFontSizePixels =>
        Math.Round(LiveCaptionLayout.BaseFontSizePixels * .75) * .75;
    public StudioCaptionTypography LiveCaptionTypography => ActiveCaptionAppearance.CaptionTypography;
    public GenerationCaptionStylePreset LiveCaptionStyle =>
        _asset?.Captions is { } captions
            ? StudioCaptionPresentationPolicy.ResolveEffectiveStyle(
                captions,
                ActiveCaptionAppearance.CaptionStyle)
            : GenerationCaptionStylePreset.Clean;
    public double LiveCaptionMaximumWidthPixels =>
        LiveCaptionLayout.MaximumWidthPixels;
    public double LiveCaptionFontSizePixels =>
        StudioCaptionPresentationPolicy.GetWpfPreviewFontSize(
            LiveCaptionLayout);
    private (GenerationCandidateCaptionTrack Track, StudioClipAppearance Appearance, int Width, int Height, TimeSpan Start, TimeSpan Duration)? _captionBoundsKey;
    private string? _captionBoundsWarning;
    private string? _captionTimingWarning;
    public string? LiveCaptionPresentationWarning
    {
        get
        {
            if (_asset?.Captions is not { } track || !_asset.RenderSettings.BurnCaptions) return null;
            var appearance = ActiveCaptionAppearance;
            var key = (track, appearance, PreviewProfile.Width, PreviewProfile.Height, _asset.SourceStart, _asset.Duration);
            if (_captionBoundsKey != key)
            {
                var cut = StudioCaptionCutProjection.Project(track, _asset.SourceStart, _asset.SourceEnd).Track;
                _captionTimingWarning = StudioCaptionPresentationPolicy.GetPresentationWarning(cut, appearance);
                _captionBoundsWarning = StudioCaptionBoundsReview.GetWarning(cut, LiveCaptionTypography, LiveCaptionStyle,
                    StudioCaptionPresentationPolicy.ResolveEffectiveWordLimit(LiveCaptionStyle, appearance.CaptionWordLimit),
                    LiveCaptionLayout, key.Width, key.Height, LiveCaptionVerticalPercent);
                _captionBoundsKey = key;
            }
            string warning = string.Join(" ", new[]
            {
                _captionTimingWarning,
                StudioCaptionFontResolver.Resolve(LiveCaptionTypography.FontFamily).Warning, _captionBoundsWarning,
            }.Where(static value => value is not null));
            return warning.Length == 0 ? null : warning;
        }
    }
    public bool HasLiveCaptionPresentationWarning =>
        LiveCaptionPresentationWarning is not null;
    public string CaptionVisibilityText =>
        IsCaptionContentVisible ? "Hide captions" : "Show captions";
    public string CaptionVisibilityShortText =>
        IsCaptionContentVisible ? "CC ON" : "CC OFF";

    public ICommand PlayCommand => _playCommand;
    public ICommand PreviousCommand => _previousCommand;
    public ICommand NextCommand => _nextCommand;
    public ICommand RewindPreviewCommand => _rewindCommand;
    public ICommand ForwardPreviewCommand => _forwardCommand;
    public ICommand ReloadPreviewCommand => _reloadCommand;
    public ICommand ToggleCaptionVisibilityCommand =>
        _toggleCaptionVisibilityCommand;

    public void Bind(
        bool hasProject,
        GenerationOutputProject? project,
        GenerationOutputAsset? asset)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        bool selectedAssetChanged = !string.Equals(
            _asset?.Id,
            asset?.Id,
            StringComparison.Ordinal);
        string? previewMediaIdentity = CreatePreviewMediaIdentity(
            asset,
            _rangeMode);
        bool shouldReload = !string.Equals(
            _previewMediaIdentity,
            previewMediaIdentity,
            StringComparison.Ordinal);
        if (shouldReload)
        {
            _needsEnvelope = false;
            StartPreviewSession();
        }
        _hasProject = hasProject;
        _project = project;
        _asset = asset;
        _previewMediaIdentity = previewMediaIdentity;
        if (selectedAssetChanged)
        {
            _positionSeconds = asset?.SourceStart.TotalSeconds ?? 0;
            _isCaptionContentVisible = true;
        }
        if (shouldReload)
        {
            _draftAppearance = null;
            _captionFrameCalculator.Reset();
        }
        _isBinding = true;
        try
        {
            UpdateRange(
                asset?.SourceStart ?? TimeSpan.Zero,
                asset?.SourceEnd ?? TimeSpan.Zero);
        }
        finally { _isBinding = false; }
        NotifyContextProperties();
        NotifyLiveCaptionProperties();
        NotifyCommandState();

        if (shouldReload)
        {
            _ = ReloadAsync();
        }
    }

    public void UpdateRange(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero || end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                "The Studio preview range must be ordered and non-negative.");
        }

        _isUpdatingRange = true;
        try
        {
            _rangeStart = start;
            _rangeEnd = end;
            bool positionChanged = ClampPositionToRange();
            if (positionChanged && IsPreviewAvailable)
            {
                bool resumeAfterSynchronization = IsPreviewPlaying;
                IsPreviewPlaying = false;
                QueuePlaybackSynchronization(
                    _positionSeconds,
                    resumeAfterSynchronization);
            }
            else if (IsPreviewPlaying &&
                     _positionSeconds >= _rangeEnd.TotalSeconds - 0.05)
            {
                IsPreviewPlaying = false;
            }
            foreach (string propertyName in new[]
            {
                nameof(PreviewPositionMinimumSeconds),
                nameof(PreviewPositionMaximumSeconds),
                nameof(PreviewPositionSeconds),
                nameof(PreviewTimecode),
                nameof(PreviewDurationText),
            })
            {
                OnPropertyChanged(propertyName);
            }
        }
        finally
        {
            _isUpdatingRange = false;
        }
        RefreshLiveCaptionFrame();
        NotifyLiveCaptionContentProperties();
        if (!_isBinding && !_isLoading && _asset is not null && _lease is not null &&
            _rangeMode == StudioPreviewRangeMode.EditableEnvelope && !CoversCurrentRange(_lease))
        {
            // The first foreground request renders only the selected cut. Pay
            // for trim context only when an edit actually needs footage outside
            // that proxy; a completed background envelope can satisfy this hit.
            _needsEnvelope = true;
            StartPreviewSession();
            _ = ReloadAsync();
        }
    }

    public void UpdateAppearanceDraft(StudioClipAppearance appearance)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _draftAppearance = appearance ??
            throw new ArgumentNullException(nameof(appearance));
        NotifyLiveCaptionProperties();
        _status = "Studio changes are shown immediately where possible and the rendered preview refreshes after the draft is saved.";
        OnPropertyChanged(nameof(PreviewStatus));
    }

    public bool TryAddGraphicFile(string imageFullPath)
    {
        if (_asset is null || _project?.IsFinalized != false)
        {
            return false;
        }
        string extension = Path.GetExtension(imageFullPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(imageFullPath) ||
            !Path.IsPathFullyQualified(imageFullPath) ||
            !File.Exists(imageFullPath) ||
            !new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        GraphicFileDropped?.Invoke(
            this,
            new StudioGraphicFileDroppedEventArgs(Path.GetFullPath(imageFullPath)));
        return true;
    }

    public void ReportPlaybackPosition(TimeSpan proxyPosition) =>
        ReportPlaybackPosition(proxyPosition, PreviewSessionVersion);

    public void ReportPlaybackPosition(
        TimeSpan proxyPosition,
        int previewSessionVersion)
    {
        if (previewSessionVersion != PreviewSessionVersion ||
            !IsPreviewAvailable ||
            proxyPosition < TimeSpan.Zero)
        {
            return;
        }
        if (_isUserScrubbing)
        {
            if (!IsPreviewPlaying)
            {
                return;
            }

            // Starting playback while this flag is still set means the native
            // Slider consumed its release/capture event. A real drag always
            // pauses playback in BeginScrub, so recover the stale latch and let
            // the MediaElement clock drive both the playhead and live captions.
            _isUserScrubbing = false;
            _resumeAfterScrub = false;
        }

        double absolutePosition =
            PreviewSourceOffsetSeconds + proxyPosition.TotalSeconds;
        bool playAfterSynchronization = false;
        if (_pendingPlaybackSyncSeconds is double pendingPosition)
        {
            const double synchronizationToleranceSeconds = 0.25;
            if (Math.Abs(absolutePosition - pendingPosition) >
                synchronizationToleranceSeconds)
            {
                if (_timeProvider.GetElapsedTime(
                        _pendingPlaybackSyncTimestamp) <
                    PlaybackSyncRetryDelay)
                {
                    return;
                }

                if (_pendingPlaybackSyncRetryCount <
                    MaximumPlaybackSyncRetries)
                {
                    _pendingPlaybackSyncRetryCount++;
                    _pendingPlaybackSyncTimestamp =
                        _timeProvider.GetTimestamp();
                    _seekVersion++;
                    _status = "Almost ready…";
                    OnPropertyChanged(nameof(PreviewSeekVersion));
                    OnPropertyChanged(nameof(PreviewStatus));
                    return;
                }

                _pendingPlaybackSyncSeconds = null;
                ReportFailure(
                    "The Studio preview could not reach the selected position. Reload the preview and try again.");
                return;
            }

            _pendingPlaybackSyncSeconds = null;
            _pendingPlaybackSyncRetryCount = 0;
            playAfterSynchronization = _playWhenSynchronized;
            _playWhenSynchronized = false;
            _status =
                "Preview ready. Space plays or pauses; Left and Right move five seconds; comma and period move one frame.";
            OnPropertyChanged(nameof(IsPreviewSynchronized));
            OnPropertyChanged(nameof(PreviewStatus));
            NotifyCommandState();
        }

        SetPosition(absolutePosition, fromPlayback: true);
        if (playAfterSynchronization && CanUsePreview())
        {
            IsPreviewPlaying = true;
        }
    }

    public void BeginScrub()
    {
        if (!CanUsePreview() || _isUserScrubbing)
        {
            return;
        }
        _isUserScrubbing = true;
        _resumeAfterScrub = IsPreviewPlaying;
        IsPreviewPlaying = false;
    }

    public void EndScrub()
    {
        if (!_isUserScrubbing)
        {
            return;
        }
        _isUserScrubbing = false;
        QueuePlaybackSynchronization(
            _positionSeconds,
            _resumeAfterScrub);
        _resumeAfterScrub = false;
    }

    public void ReportOpened() => ReportOpened(PreviewSessionVersion);

    public void ReportOpened(int previewSessionVersion)
    {
        if (previewSessionVersion != PreviewSessionVersion ||
            !IsPreviewAvailable)
        {
            return;
        }

        _error = null;
        _status = _pendingPlaybackSyncSeconds.HasValue
            ? "Opening this clip at its saved start…"
            : "Preview ready. Space plays or pauses; Left and Right move five seconds; comma and period move one frame.";
        NotifyPlaybackStatusProperties();
    }

    public void ReportFailure(string message) =>
        ReportFailure(message, PreviewSessionVersion);

    public void ReportFailure(string message, int previewSessionVersion)
    {
        if (previewSessionVersion != PreviewSessionVersion)
        {
            return;
        }

        IsPreviewPlaying = false;
        _pendingPlaybackSyncSeconds = null;
        _pendingPlaybackSyncRetryCount = 0;
        _playWhenSynchronized = false;
        _error = string.IsNullOrWhiteSpace(message)
            ? "The Studio preview could not be played."
            : message.Trim();
        _status =
            "Preview playback needs attention. You can retry without changing the open Studio session.";
        OnPropertyChanged(nameof(IsPreviewSynchronized));
        NotifyPlaybackStatusProperties();
        NotifyCommandState();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _loadGeneration++;
        CancellationTokenSource? cancellation;
        lock (_loadSync)
        {
            cancellation = _loadCancellation;
        }
        TryCancel(cancellation);
        _lease?.Dispose();
        _lease = null;
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? activeCancellation;
        Task completion;
        lock (_loadSync)
        {
            _isStopping = true;
            activeCancellation = _loadCancellation;
            completion = _loadQuiescence?.Task ?? Task.CompletedTask;
        }
        TryCancel(activeCancellation);
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool CanUsePreview() =>
        IsPreviewSynchronized && !IsPreviewLoading;

    private Task ReloadPreviewAsync()
    {
        StartPreviewSession();
        return ReloadAsync();
    }

    private void StartPreviewSession()
    {
        _loadGeneration++;
        _previewSessionVersion++;
        CancelActiveLoad();
        IsPreviewPlaying = false;
        _pendingPlaybackSyncSeconds = null;
        _pendingPlaybackSyncRetryCount = 0;
        _playWhenSynchronized = false;

        StudioPreviewMediaLease? previous = _lease;
        _lease = null;
        foreach (string propertyName in new[]
        {
            nameof(PreviewSessionVersion),
            nameof(PreviewMediaPath),
            nameof(PreviewSourceOffsetSeconds),
            nameof(IsPreviewAvailable),
            nameof(IsPreviewSynchronized),
        })
        {
            OnPropertyChanged(propertyName);
        }

        // MediaElement observes the null path synchronously and closes its
        // graph before this lease can make the old cache entry evictable.
        previous?.Dispose();
        NotifyCommandState();
    }

    private void TogglePlayback()
    {
        if (!CanUsePreview())
        {
            return;
        }
        if (MediaPlaybackBoundary.HasReachedEnd(
                PreviewPositionSeconds,
                PreviewPositionMaximumSeconds))
        {
            IsPreviewPlaying = false;
            int seekVersion = _seekVersion;
            _playWhenSynchronized = true;
            PreviewPositionSeconds = PreviewPositionMinimumSeconds;
            if (_seekVersion == seekVersion)
            {
                QueuePlaybackSynchronization(
                    PreviewPositionMinimumSeconds,
                    playWhenSynchronized: true);
            }
            return;
        }
        IsPreviewPlaying = !IsPreviewPlaying;
    }

    private void SeekBy(double seconds)
    {
        if (!CanUsePreview() || !double.IsFinite(seconds))
        {
            return;
        }
        PreviewPositionSeconds += seconds;
    }

    private void SetPosition(double value, bool fromPlayback)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if (_isUpdatingRange && !fromPlayback)
        {
            return;
        }

        double normalized = Math.Clamp(
            value,
            PreviewPositionMinimumSeconds,
            PreviewPositionMaximumSeconds);
        bool reachedPlaybackEnd = fromPlayback &&
            normalized >= PreviewPositionMaximumSeconds - 0.05;
        if (Math.Abs(_positionSeconds - normalized) < 0.0005)
        {
            if (reachedPlaybackEnd)
            {
                IsPreviewPlaying = false;
            }
            return;
        }

        _positionSeconds = normalized;
        RefreshLiveCaptionFrame();
        OnPropertyChanged(nameof(PreviewPositionSeconds));
        OnPropertyChanged(nameof(PreviewTimecode));
        NotifyLiveCaptionContentProperties();
        if (!fromPlayback)
        {
            QueuePlaybackSynchronization(normalized);
        }
        if (reachedPlaybackEnd)
        {
            IsPreviewPlaying = false;
        }
    }

    private void QueuePlaybackSynchronization(
        double absolutePosition,
        bool playWhenSynchronized = false)
    {
        _pendingPlaybackSyncSeconds = absolutePosition;
        _pendingPlaybackSyncTimestamp = _timeProvider.GetTimestamp();
        _pendingPlaybackSyncRetryCount = 0;
        _playWhenSynchronized |= playWhenSynchronized;
        _seekVersion++;
        OnPropertyChanged(nameof(PreviewSeekVersion));
        OnPropertyChanged(nameof(IsPreviewSynchronized));
        NotifyCommandState();
    }

    private bool ClampPositionToRange()
    {
        if (_asset is null || _rangeEnd <= _rangeStart)
        {
            return false;
        }
        double normalized = Math.Clamp(
            _positionSeconds,
            _rangeStart.TotalSeconds,
            _rangeEnd.TotalSeconds);
        if (Math.Abs(_positionSeconds - normalized) < 0.0005)
        {
            return false;
        }
        _positionSeconds = normalized;
        return true;
    }

    private async Task ReloadAsync()
    {
        if (_isStopping)
        {
            return;
        }
        int loadGeneration = ++_loadGeneration;
        GenerationOutputAsset? sourceAsset = _asset;
        GenerationOutputAsset? requestedAsset = sourceAsset is null
            ? null
            : _draftAppearance is null
                ? sourceAsset
                : sourceAsset.WithStudioEdits(
                    sourceAsset.SourceStart,
                    sourceAsset.SourceEnd,
                    _draftAppearance);
        StudioPreviewMediaLease? previous = _lease;
        IsPreviewPlaying = false;

        if (requestedAsset is null || _mediaService is null)
        {
            CancelActiveLoad();
            _positionSeconds = requestedAsset?.SourceStart.TotalSeconds ?? 0;
            RefreshLiveCaptionFrame();
            _error = null;
            _status = requestedAsset is null
                ? "Select a clip to preview it."
                : "Preview is unavailable right now. You can keep editing.";
            NotifyPreviewProperties();
            return;
        }

        var loadCancellation = new CancellationTokenSource();
        if (!BeginLoad(loadCancellation))
        {
            loadCancellation.Dispose();
            return;
        }
        CancellationToken cancellationToken = loadCancellation.Token;
        _isLoading = true;
        _error = null;
        _status = "Getting this clip ready…";
        NotifyPreviewProperties();
        bool reloadForRange = false;
        try
        {
            StudioPreviewMediaRequest request = CreateMediaRequest(requestedAsset);
            StudioPreviewMediaLease lease = await _mediaService.MaterializeAsync(
                request,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                loadGeneration != _loadGeneration)
            {
                lease.Dispose();
                return;
            }
            if (_rangeMode == StudioPreviewRangeMode.EditableEnvelope && !CoversCurrentRange(lease))
            {
                // A trim may move while the exact-cut proxy is being prepared.
                // Never expose a proxy that cannot seek to the current range.
                lease.Dispose();
                if (request.SourceStart <= _rangeStart && request.SourceEnd >= _rangeEnd)
                    throw new InvalidDataException("The prepared preview does not cover the requested source range. Reload the preview to try again.");
                _needsEnvelope = true;
                reloadForRange = true;
                return;
            }

            _lease = lease;
            OnPropertyChanged(nameof(PreviewMediaPath));
            previous?.Dispose();
            ClampPositionToRange();
            RefreshLiveCaptionFrame();
            QueuePlaybackSynchronization(_positionSeconds);
            _status = "Opening this clip at your last edit point…";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (loadGeneration == _loadGeneration)
            {
                _error = exception.Message;
                _status =
                    previous is null
                        ? "Replay Foundry could not prepare this preview. The open Studio session remains editable."
                        : "Replay Foundry could not prepare the replacement preview. The prior preview remains available.";
            }
        }
        finally
        {
            CompleteLoad(loadCancellation);
            if (!_isDisposed && loadGeneration == _loadGeneration)
            {
                _isLoading = false;
                NotifyPreviewProperties();
                if (reloadForRange && !_isStopping)
                {
                    StartPreviewSession();
                    _ = ReloadAsync();
                }
            }
        }
    }

    private bool BeginLoad(CancellationTokenSource cancellation)
    {
        CancellationTokenSource? superseded;
        lock (_loadSync)
        {
            if (_isStopping || _isDisposed)
            {
                return false;
            }
            superseded = _loadCancellation;
            _loadCancellation = cancellation;
            if (_activeLoadCount++ == 0)
            {
                _loadQuiescence = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        TryCancel(superseded);
        return true;
    }

    private void CompleteLoad(CancellationTokenSource cancellation)
    {
        TaskCompletionSource<bool>? quiescence = null;
        lock (_loadSync)
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }
            _activeLoadCount--;
            if (_activeLoadCount == 0)
            {
                quiescence = _loadQuiescence;
                _loadQuiescence = null;
            }
        }
        cancellation.Dispose();
        quiescence?.TrySetResult(true);
    }

    private void CancelActiveLoad()
    {
        CancellationTokenSource? cancellation;
        lock (_loadSync)
        {
            cancellation = _loadCancellation;
        }
        TryCancel(cancellation);
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The load completed between capture and cancellation.
        }
    }

    private void ToggleCaptionVisibility()
    {
        _isCaptionContentVisible = !_isCaptionContentVisible;
        OnPropertyChanged(nameof(IsCaptionContentVisible));
        OnPropertyChanged(nameof(CaptionVisibilityText));
        OnPropertyChanged(nameof(CaptionVisibilityShortText));
        OnPropertyChanged(nameof(HasLiveCaption));
    }

    private void NotifyContextProperties()
    {
        StudioPreviewPropertyNotifications.Context(OnPropertyChanged);
    }

    private void NotifyPreviewProperties()
    {
        StudioPreviewPropertyNotifications.Preview(OnPropertyChanged);
        NotifyCommandState();
    }

    private void NotifyLiveCaptionProperties()
    {
        RefreshLiveCaptionFrame();
        StudioPreviewPropertyNotifications.LiveCaption(OnPropertyChanged);
    }

    private void NotifyLiveCaptionContentProperties()
    {
        StudioPreviewPropertyNotifications.LiveCaptionContent(OnPropertyChanged);
    }

    private void NotifyCommandState()
    {
        _playCommand.RaiseCanExecuteChanged();
        _previousCommand.RaiseCanExecuteChanged();
        _nextCommand.RaiseCanExecuteChanged();
        _rewindCommand.RaiseCanExecuteChanged();
        _forwardCommand.RaiseCanExecuteChanged();
        _reloadCommand.RaiseCanExecuteChanged();
        _toggleCaptionVisibilityCommand.RaiseCanExecuteChanged();
    }

    private void NotifyPlaybackStatusProperties()
    {
        OnPropertyChanged(nameof(PreviewStatus));
        OnPropertyChanged(nameof(PreviewError));
        OnPropertyChanged(nameof(HasPreviewError));
    }

    private void RefreshLiveCaptionFrame()
        => _liveCaptionFrame = _captionFrameCalculator.Calculate(
            _asset?.Captions,
            ActiveCaptionAppearance.CaptionWordLimit,
            LiveCaptionStyle,
            _positionSeconds,
            _rangeStart,
            _rangeEnd);

    private StudioClipAppearance ActiveCaptionAppearance =>
        _draftAppearance ??
        _asset?.Appearance ??
        StudioClipAppearance.CreateDefault(
            GenerationCaptionStylePreset.Clean);

    private static string? CreatePreviewMediaIdentity(GenerationOutputAsset? asset, StudioPreviewRangeMode rangeMode) =>
        StudioPreviewRequestPolicy.CreateIdentity(asset, rangeMode);

    private StudioPreviewMediaRequest CreateMediaRequest(GenerationOutputAsset asset) =>
        StudioPreviewRequestPolicy.CreateRequest(asset, _rangeMode, _needsEnvelope, _rangeStart, _rangeEnd);

    private bool CoversCurrentRange(StudioPreviewMediaLease lease) =>
        lease.SourceOffset <= _rangeStart && lease.SourceOffset + lease.Duration >= _rangeEnd;

    private StudioCaptionFrameLayout LiveCaptionLayout =>
        StudioCaptionPresentationPolicy.CalculateFrameLayout(
            PreviewProfile.Width,
            PreviewProfile.Height,
            LiveCaptionStyle,
            LiveCaptionTypography.ConstrainMaximumWidth(ActiveCaptionAppearance.CaptionMaximumWidthPercent),
            ActiveCaptionAppearance.CaptionFontScalePercent);

    private GenerationClipOutputProfile PreviewProfile => _asset is null
        ? new GenerationClipOutputProfile(1080, 1920, 30)
        : GenerationClipOutputProfile.FromAsset(_asset);

}

public sealed class StudioGraphicFileDroppedEventArgs : EventArgs
{
    public StudioGraphicFileDroppedEventArgs(string imageFullPath) =>
        ImageFullPath = imageFullPath ?? throw new ArgumentNullException(nameof(imageFullPath));

    public string ImageFullPath { get; }
}
