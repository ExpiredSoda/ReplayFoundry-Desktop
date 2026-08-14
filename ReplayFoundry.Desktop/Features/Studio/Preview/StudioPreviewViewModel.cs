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
    private readonly DelegateCommand _playCommand;
    private readonly DelegateCommand _previousCommand;
    private readonly DelegateCommand _nextCommand;
    private readonly DelegateCommand _rewindCommand;
    private readonly DelegateCommand _forwardCommand;
    private readonly DelegateCommand _reloadCommand;
    private readonly DelegateCommand _toggleCaptionVisibilityCommand;
    private CancellationTokenSource? _loadCancellation;
    private StudioPreviewMediaLease? _lease;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private string? _previewMediaIdentity;
    private StudioClipAppearance? _draftAppearance;
    private GenerationCandidateCaptionTrack? _projectedCaptionTrack;
    private StudioCaptionWordLimitPreset? _projectedCaptionWordLimit;
    private IReadOnlyList<StudioCaptionCue> _projectedCaptionCues = [];
    private TimeSpan _rangeStart;
    private TimeSpan _rangeEnd;
    private int _loadGeneration;
    private double _positionSeconds;
    private double? _pendingPlaybackSyncSeconds;
    private long _pendingPlaybackSyncTimestamp;
    private int _pendingPlaybackSyncRetryCount;
    private int _seekVersion;
    private bool _hasProject;
    private bool _isPlaying;
    private bool _isUserScrubbing;
    private bool _resumeAfterScrub;
    private bool _isUpdatingRange;
    private bool _isLoading;
    private bool _isCaptionContentVisible = true;
    private bool _isDisposed;
    private string _status = "Select a clip to preview it.";
    private string? _error;

    public StudioPreviewViewModel(
        IStudioPreviewMediaService? mediaService,
        bool showCaptionControls = true,
        TimeProvider? timeProvider = null)
    {
        _mediaService = mediaService;
        _showCaptionControls = showCaptionControls;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            () => _ = ReloadAsync(),
            () => _asset is not null && !IsPreviewLoading);
        _toggleCaptionVisibilityCommand = new DelegateCommand(
            ToggleCaptionVisibility,
            () => _hasProject && _asset?.Captions is not null);
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
        : GenerationClipOutputProfile.FromReference(
            _asset.SourceMedia.PrimaryVideoStream).DisplayText;
    public double PreviewCanvasWidth => PreviewProfile.Width;
    public double PreviewCanvasHeight => PreviewProfile.Height;
    public string PreviewScaleText => PreviewCanvasHeight > PreviewCanvasWidth
        ? "PHONE FIT · 9:16"
        : "FIT · 16:9";
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
        _showCaptionControls && _asset?.Captions is not null;
    public bool HasLiveCaption =>
        IsCaptionContentVisible &&
        !string.IsNullOrWhiteSpace(LiveCaptionText);
    public string? LiveCaptionText
    {
        get
        {
            StudioCaptionCue? cue = FindLiveCaptionCue();
            if (cue is null)
            {
                return null;
            }
            if (LiveCaptionStyle != GenerationCaptionStylePreset.Pop ||
                cue.WordSpans.Count == 0)
            {
                return cue.Text;
            }
            return FindLiveCaptionActiveWordText(cue);
        }
    }
    public string? LiveCaptionActiveWord
    {
        get
        {
            if (LiveCaptionStyle is not
                (GenerationCaptionStylePreset.WordFocus or
                 GenerationCaptionStylePreset.KaraokeSweep))
            {
                return null;
            }
            StudioCaptionCue? cue = FindLiveCaptionCue();
            return cue is null
                ? null
                : FindLiveCaptionActiveWordText(cue);
        }
    }
    public int LiveCaptionAccentStartIndex =>
        FindLiveCaptionAccentState().Start;
    public int LiveCaptionAccentLength =>
        FindLiveCaptionAccentState().Length;
    public int LiveCaptionSweepLength =>
        FindLiveCaptionAccentState().SweepLength;
    public double LiveCaptionAccentProgress =>
        FindLiveCaptionAccentState().Progress;
    public double LiveCaptionScale => FindLiveCaptionScale();
    public double LiveCaptionVerticalPercent =>
        (_draftAppearance ?? _asset?.Appearance)?
            .CaptionVerticalPositionPercent ??
        StudioClipAppearance.DefaultCaptionVerticalPositionPercent;
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
    public string? LiveCaptionPresentationWarning
        => StudioCaptionPresentationPolicy.GetPresentationWarning(
            _asset?.Captions,
            ActiveCaptionAppearance);
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
        string? previewMediaIdentity = CreatePreviewMediaIdentity(asset);
        bool shouldReload = !string.Equals(
            _previewMediaIdentity,
            previewMediaIdentity,
            StringComparison.Ordinal);
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
            _projectedCaptionTrack = null;
            _projectedCaptionWordLimit = null;
            _projectedCaptionCues = [];
        }
        UpdateRange(
            asset?.SourceStart ?? TimeSpan.Zero,
            asset?.SourceEnd ?? TimeSpan.Zero);
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
            ClampPositionToRange();
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

    public void ReportPlaybackPosition(TimeSpan proxyPosition)
    {
        if (!IsPreviewAvailable || proxyPosition < TimeSpan.Zero)
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
            _status =
                "Preview ready. Space plays or pauses; Left and Right move five seconds; comma and period move one frame.";
            OnPropertyChanged(nameof(IsPreviewSynchronized));
            OnPropertyChanged(nameof(PreviewStatus));
            NotifyCommandState();
        }

        SetPosition(absolutePosition, fromPlayback: true);
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
        _pendingPlaybackSyncSeconds = _positionSeconds;
        _pendingPlaybackSyncTimestamp = _timeProvider.GetTimestamp();
        _pendingPlaybackSyncRetryCount = 0;
        _seekVersion++;
        OnPropertyChanged(nameof(PreviewSeekVersion));
        if (_resumeAfterScrub)
        {
            IsPreviewPlaying = true;
        }
        _resumeAfterScrub = false;
    }

    public void ReportOpened()
    {
        if (!IsPreviewAvailable)
        {
            return;
        }

        _error = null;
        _status = _pendingPlaybackSyncSeconds.HasValue
            ? "Opening this clip at its saved start…"
            : "Preview ready. Space plays or pauses; Left and Right move five seconds; comma and period move one frame.";
        NotifyPlaybackStatusProperties();
    }

    public void ReportFailure(string message)
    {
        IsPreviewPlaying = false;
        _pendingPlaybackSyncSeconds = null;
        _pendingPlaybackSyncRetryCount = 0;
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
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _loadGeneration++;
        _lease?.Dispose();
        _lease = null;
    }

    private bool CanUsePreview() =>
        IsPreviewSynchronized && !IsPreviewLoading;

    private void TogglePlayback()
    {
        if (!CanUsePreview())
        {
            return;
        }
        if (PreviewPositionSeconds >= PreviewPositionMaximumSeconds - 0.001)
        {
            PreviewPositionSeconds = PreviewPositionMinimumSeconds;
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
        if (Math.Abs(_positionSeconds - normalized) < 0.0005)
        {
            return;
        }

        _positionSeconds = normalized;
        OnPropertyChanged(nameof(PreviewPositionSeconds));
        OnPropertyChanged(nameof(PreviewTimecode));
        NotifyLiveCaptionContentProperties();
        if (!fromPlayback)
        {
            _pendingPlaybackSyncSeconds = normalized;
            _pendingPlaybackSyncTimestamp = _timeProvider.GetTimestamp();
            _pendingPlaybackSyncRetryCount = 0;
            _seekVersion++;
            OnPropertyChanged(nameof(PreviewSeekVersion));
        }
        if (fromPlayback && normalized >= PreviewPositionMaximumSeconds - 0.05)
        {
            IsPreviewPlaying = false;
        }
    }

    private void ClampPositionToRange()
    {
        if (_asset is null || _rangeEnd <= _rangeStart)
        {
            return;
        }
        _positionSeconds = Math.Clamp(
            _positionSeconds,
            _rangeStart.TotalSeconds,
            _rangeEnd.TotalSeconds);
    }

    private async Task ReloadAsync()
    {
        int loadGeneration = ++_loadGeneration;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellation.Token;
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
            _positionSeconds = requestedAsset?.SourceStart.TotalSeconds ?? 0;
            _error = null;
            _status = requestedAsset is null
                ? "Select a clip to preview it."
                : "Preview is unavailable right now. You can keep editing.";
            NotifyPreviewProperties();
            return;
        }

        _isLoading = true;
        _error = null;
        _status = "Getting this clip ready…";
        NotifyPreviewProperties();
        try
        {
            StudioPreviewMediaLease lease = await _mediaService.MaterializeAsync(
                new StudioPreviewMediaRequest(requestedAsset),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                loadGeneration != _loadGeneration)
            {
                lease.Dispose();
                return;
            }

            _lease = lease;
            previous?.Dispose();
            ClampPositionToRange();
            _pendingPlaybackSyncSeconds = _positionSeconds;
            _pendingPlaybackSyncTimestamp = _timeProvider.GetTimestamp();
            _pendingPlaybackSyncRetryCount = 0;
            _seekVersion++;
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
            if (loadGeneration == _loadGeneration)
            {
                _isLoading = false;
                NotifyPreviewProperties();
            }
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
        foreach (string propertyName in new[]
        {
            nameof(SequenceSummary),
            nameof(ProjectPromptTitle),
            nameof(PreviewFormatText),
            nameof(PreviewCanvasWidth),
            nameof(PreviewCanvasHeight),
            nameof(PreviewScaleText),
            nameof(CanShowCaptionControls),
            nameof(IsCaptionContentVisible),
            nameof(CaptionVisibilityText),
            nameof(CaptionVisibilityShortText),
        })
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void NotifyPreviewProperties()
    {
        foreach (string propertyName in new[]
        {
            nameof(PreviewMediaPath),
            nameof(PreviewSourceOffsetSeconds),
            nameof(PreviewSeekVersion),
            nameof(IsPreviewPlaying),
            nameof(IsPreviewLoading),
            nameof(IsPreviewAvailable),
            nameof(IsPreviewSynchronized),
            nameof(ProjectPromptTitle),
            nameof(PreviewStatus),
            nameof(PreviewError),
            nameof(HasPreviewError),
            nameof(PreviewPlayPauseText),
            nameof(PreviewPlayPauseIconKey),
            nameof(HasLiveCaption),
            nameof(LiveCaptionText),
            nameof(LiveCaptionActiveWord),
            nameof(LiveCaptionAccentStartIndex),
            nameof(LiveCaptionAccentLength),
            nameof(LiveCaptionSweepLength),
            nameof(LiveCaptionAccentProgress),
            nameof(LiveCaptionScale),
            nameof(LiveCaptionVerticalPercent),
            nameof(LiveCaptionStyle),
            nameof(LiveCaptionMaximumWidthPixels),
            nameof(LiveCaptionFontSizePixels),
            nameof(LiveCaptionPresentationWarning),
            nameof(HasLiveCaptionPresentationWarning),
        })
        {
            OnPropertyChanged(propertyName);
        }
        NotifyCommandState();
    }

    private void NotifyLiveCaptionProperties()
    {
        NotifyLiveCaptionContentProperties();
        OnPropertyChanged(nameof(LiveCaptionVerticalPercent));
        OnPropertyChanged(nameof(LiveCaptionStyle));
        OnPropertyChanged(nameof(LiveCaptionMaximumWidthPixels));
        OnPropertyChanged(nameof(LiveCaptionFontSizePixels));
        OnPropertyChanged(nameof(LiveCaptionPresentationWarning));
        OnPropertyChanged(nameof(HasLiveCaptionPresentationWarning));
    }

    private void NotifyLiveCaptionContentProperties()
    {
        OnPropertyChanged(nameof(HasLiveCaption));
        OnPropertyChanged(nameof(LiveCaptionText));
        OnPropertyChanged(nameof(LiveCaptionActiveWord));
        OnPropertyChanged(nameof(LiveCaptionAccentStartIndex));
        OnPropertyChanged(nameof(LiveCaptionAccentLength));
        OnPropertyChanged(nameof(LiveCaptionSweepLength));
        OnPropertyChanged(nameof(LiveCaptionAccentProgress));
        OnPropertyChanged(nameof(LiveCaptionScale));
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

    private StudioCaptionCue? FindLiveCaptionCue()
    {
        if (_asset?.Captions is not { } captions)
        {
            return null;
        }
        StudioCaptionWordLimitPreset wordLimit =
            ActiveCaptionAppearance.CaptionWordLimit;
        if (!ReferenceEquals(_projectedCaptionTrack, captions) ||
            _projectedCaptionWordLimit != wordLimit)
        {
            _projectedCaptionTrack = captions;
            _projectedCaptionWordLimit = wordLimit;
            _projectedCaptionCues =
                StudioCaptionPresentationPolicy.ProjectCues(
                    captions,
                    wordLimit);
        }
        double position = PreviewPositionSeconds;
        return _projectedCaptionCues.FirstOrDefault(cue =>
            QuantizeSourceBoundarySeconds(
                cue.AbsoluteSourceStart.TotalSeconds) <= position &&
            QuantizeSourceBoundarySeconds(
                cue.AbsoluteSourceEnd.TotalSeconds) > position);
    }

    private string? FindLiveCaptionActiveWordText(
        StudioCaptionCue cue)
    {
        double position = PreviewPositionSeconds;
        return cue.WordSpans.FirstOrDefault(span =>
            QuantizeSourceBoundarySeconds(
                span.Word.AbsoluteSourceStart.TotalSeconds) <= position &&
            QuantizeSourceBoundarySeconds(
                span.Word.AbsoluteSourceEnd.TotalSeconds) > position)?.Word.Text;
    }

    private (int Start, int Length, int SweepLength, double Progress)
        FindLiveCaptionAccentState()
    {
        GenerationCaptionStylePreset style = LiveCaptionStyle;
        if (style is not
            (GenerationCaptionStylePreset.WordFocus or
             GenerationCaptionStylePreset.KaraokeSweep))
        {
            return (-1, 0, 0, 0);
        }

        StudioCaptionCue? cue = FindLiveCaptionCue();
        if (cue is null)
        {
            return (-1, 0, 0, 0);
        }

        if (cue.WordSpans.Count == 0)
        {
            double start = QuantizeSourceBoundarySeconds(
                cue.AbsoluteSourceStart.TotalSeconds);
            double end = QuantizeSourceBoundarySeconds(
                cue.AbsoluteSourceEnd.TotalSeconds);
            if (end <= start)
            {
                return (-1, 0, 0, 0);
            }
            if (style == GenerationCaptionStylePreset.WordFocus)
            {
                double focusProgress = Math.Clamp(
                    (PreviewPositionSeconds - start) / (end - start),
                    0,
                    1);
                return (0, cue.Text.Length, cue.Text.Length, focusProgress);
            }
            double progress = Math.Clamp(
                (PreviewPositionSeconds - start) / (end - start),
                0,
                1);
            return (0, cue.Text.Length, cue.Text.Length, progress);
        }

        double position = PreviewPositionSeconds;
        foreach (StudioCaptionWordSpan span in cue.WordSpans)
        {
            double absoluteStart = Math.Max(
                QuantizeSourceBoundarySeconds(
                    span.Word.AbsoluteSourceStart.TotalSeconds),
                _rangeStart.TotalSeconds);
            double absoluteEnd = Math.Min(
                QuantizeSourceBoundarySeconds(
                    span.Word.AbsoluteSourceEnd.TotalSeconds),
                _rangeEnd.TotalSeconds);
            if (absoluteEnd <= absoluteStart)
            {
                continue;
            }
            if (style == GenerationCaptionStylePreset.WordFocus)
            {
                if (absoluteStart <= position && absoluteEnd > position)
                {
                    double progress = Math.Clamp(
                        (position - absoluteStart) /
                        Math.Max(0.001, absoluteEnd - absoluteStart),
                        0,
                        1);
                    return (
                        span.StartIndex,
                        span.Length,
                        span.Length,
                        progress);
                }
                continue;
            }

            if (position < absoluteStart)
            {
                return (
                    span.StartIndex,
                    0,
                    span.Length,
                    0);
            }
            if (absoluteEnd > position)
            {
                double duration = Math.Max(
                    0.001,
                    absoluteEnd - absoluteStart);
                double progress = Math.Clamp(
                    (position - absoluteStart) / duration,
                    0,
                    1);
                return (
                    span.StartIndex,
                    span.Length,
                    span.Length,
                    progress);
            }
        }
        return style == GenerationCaptionStylePreset.KaraokeSweep
            ? (cue.Text.Length, 0, 0, 1)
            : (-1, 0, 0, 0);
    }

    private double FindLiveCaptionScale()
    {
        if (LiveCaptionStyle != GenerationCaptionStylePreset.Pop)
        {
            return 1;
        }
        StudioCaptionCue? cue = FindLiveCaptionCue();
        if (cue is null)
        {
            return 1;
        }

        double position = PreviewPositionSeconds;
        StudioCaptionWordSpan? active = cue.WordSpans.FirstOrDefault(span =>
            QuantizeSourceBoundarySeconds(
                span.Word.AbsoluteSourceStart.TotalSeconds) <= position &&
            QuantizeSourceBoundarySeconds(
                span.Word.AbsoluteSourceEnd.TotalSeconds) > position);
        if (active is null && cue.WordSpans.Count > 0)
        {
            return 1;
        }
        TimeSpan activeStart = active is null
            ? cue.AbsoluteSourceStart
            : active.Word.AbsoluteSourceStart;
        double visibleStart = Math.Max(
            QuantizeSourceBoundarySeconds(
                activeStart.TotalSeconds),
            _rangeStart.TotalSeconds);
        double elapsedMilliseconds = Math.Max(
            0,
            (position - visibleStart) * 1000d);
        if (elapsedMilliseconds <= 120)
        {
            return 0.82 + (1.12 - 0.82) *
                elapsedMilliseconds / 120d;
        }
        if (elapsedMilliseconds <= 260)
        {
            return 1.12 + (1 - 1.12) *
                (elapsedMilliseconds - 120d) / 140d;
        }
        return 1;
    }

    private double QuantizeSourceBoundarySeconds(
        double absoluteSourceSeconds)
    {
        double relativeSeconds = Math.Max(
            0,
            absoluteSourceSeconds - _rangeStart.TotalSeconds);
        TimeSpan quantized =
            StudioCaptionPresentationPolicy.QuantizeRenderBoundary(
                TimeSpan.FromSeconds(relativeSeconds));
        return _rangeStart.TotalSeconds + quantized.TotalSeconds;
    }

    private StudioClipAppearance ActiveCaptionAppearance =>
        _draftAppearance ??
        _asset?.Appearance ??
        StudioClipAppearance.CreateDefault(
            GenerationCaptionStylePreset.Clean);

    private static string? CreatePreviewMediaIdentity(
        GenerationOutputAsset? asset) => asset is null
            ? null
            : StudioPreviewCacheKey.CreateMediaIdentity(
                new StudioPreviewMediaRequest(asset));

    private StudioCaptionFrameLayout LiveCaptionLayout =>
        StudioCaptionPresentationPolicy.CalculateFrameLayout(
            PreviewProfile.Width,
            PreviewProfile.Height,
            LiveCaptionStyle,
            ActiveCaptionAppearance.CaptionMaximumWidthPercent,
            ActiveCaptionAppearance.CaptionFontScalePercent);

    private GenerationClipOutputProfile PreviewProfile => _asset is null
        ? new GenerationClipOutputProfile(1080, 1920, 30)
        : GenerationClipOutputProfile.FromReference(
            _asset.SourceMedia.PrimaryVideoStream);

}

public sealed class StudioGraphicFileDroppedEventArgs : EventArgs
{
    public StudioGraphicFileDroppedEventArgs(string imageFullPath) =>
        ImageFullPath = imageFullPath ?? throw new ArgumentNullException(nameof(imageFullPath));

    public string ImageFullPath { get; }
}
