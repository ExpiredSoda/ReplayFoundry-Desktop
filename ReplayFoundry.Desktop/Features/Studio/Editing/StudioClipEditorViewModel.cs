using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal sealed record StudioClipEditorDraftSnapshot(
    double StartAdjustmentSeconds,
    double EndAdjustmentSeconds,
    GenerationCaptionStylePreset CaptionStyle,
    StudioCaptionWordLimitPreset CaptionWordLimit,
    double CaptionVerticalPositionPercent,
    double CaptionMaximumWidthPercent,
    double CaptionFontScalePercent,
    StudioVideoEffectPreset VideoEffect,
    double VideoEffectIntensityPercent);

public sealed class StudioClipEditorViewModel : INotifyPropertyChanged
{
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly IReadOnlyList<SelectionOption<GenerationCaptionStylePreset>>
        _captionStyleOptions = StudioSurfaceCatalog.CaptionStyles;
    private readonly IReadOnlyList<SelectionOption<StudioCaptionWordLimitPreset>>
        _captionWordLimitOptions = StudioSurfaceCatalog.CaptionWordLimits;
    private readonly IReadOnlyList<SelectionOption<StudioVideoEffectPreset>>
        _videoEffectOptions = StudioSurfaceCatalog.VideoEffects;
    private readonly DelegateCommand _applyCommand;
    private readonly DelegateCommand _resetCommand;
    private readonly DelegateCommand _applyCaptionLayoutToAllCommand;
    private readonly DelegateCommand _nudgeStartEarlierCommand;
    private readonly DelegateCommand _nudgeStartLaterCommand;
    private readonly DelegateCommand _nudgeEndEarlierCommand;
    private readonly DelegateCommand _nudgeEndLaterCommand;
    private GenerationOutputProject? _project;
    private GenerationOutputAsset? _asset;
    private double _startAdjustmentSeconds;
    private double _endAdjustmentSeconds;
    private SelectionOption<GenerationCaptionStylePreset> _selectedCaptionStyle;
    private SelectionOption<StudioCaptionWordLimitPreset>
        _selectedCaptionWordLimit;
    private SelectionOption<StudioVideoEffectPreset> _selectedVideoEffect;
    private double _captionVerticalPositionPercent =
        StudioClipAppearance.DefaultCaptionVerticalPositionPercent;
    private double _captionMaximumWidthPercent =
        StudioClipAppearance.DefaultCaptionMaximumWidthPercent;
    private double _captionFontScalePercent =
        StudioClipAppearance.DefaultCaptionFontScalePercent;
    private double _videoEffectIntensityPercent;
    private bool _isHostBusy;
    private string _status =
        "Select a generated clip to adjust its boundaries.";
    private string? _error;

    public StudioClipEditorViewModel(IGenerationOutputEditor? outputEditor)
    {
        _outputEditor = outputEditor;
        _selectedCaptionStyle = _captionStyleOptions[0];
        _selectedCaptionWordLimit = _captionWordLimitOptions.Single(
            static option =>
                option.Value == StudioCaptionWordLimitPreset.Streamlined);
        _selectedVideoEffect = _videoEffectOptions[0];
        _applyCommand = new DelegateCommand(
            ApplyBoundaryEdit,
            CanApplyBoundaryEdit);
        _resetCommand = new DelegateCommand(
            ResetDraft,
            CanResetDraft);
        _applyCaptionLayoutToAllCommand = new DelegateCommand(
            ApplyCaptionLayoutToAll,
            CanApplyCaptionLayoutToAll);
        _nudgeStartEarlierCommand = new DelegateCommand(
            () => NudgeStart(-BoundaryFrameStepSeconds),
            () => CanNudgeStart(-BoundaryFrameStepSeconds));
        _nudgeStartLaterCommand = new DelegateCommand(
            () => NudgeStart(BoundaryFrameStepSeconds),
            () => CanNudgeStart(BoundaryFrameStepSeconds));
        _nudgeEndEarlierCommand = new DelegateCommand(
            () => NudgeEnd(-BoundaryFrameStepSeconds),
            () => CanNudgeEnd(-BoundaryFrameStepSeconds));
        _nudgeEndLaterCommand = new DelegateCommand(
            () => NudgeEnd(BoundaryFrameStepSeconds),
            () => CanNudgeEnd(BoundaryFrameStepSeconds));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DraftRangeChanged;
    public event EventHandler? DraftAppearanceChanged;

    public double StartAdjustmentSeconds
    {
        get => _startAdjustmentSeconds;
        set
        {
            double normalized = Math.Round(
                Math.Clamp(
                    value,
                    StartAdjustmentMinimumSeconds,
                    StartAdjustmentMaximumSeconds),
                3,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(_startAdjustmentSeconds - normalized) < 0.0005)
            {
                return;
            }

            _startAdjustmentSeconds = normalized;
            ValidateDraft();
            NotifyDraftProperties();
        }
    }

    public double EndAdjustmentSeconds
    {
        get => _endAdjustmentSeconds;
        set
        {
            double normalized = Math.Round(
                Math.Clamp(
                    value,
                    EndAdjustmentMinimumSeconds,
                    EndAdjustmentMaximumSeconds),
                3,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(_endAdjustmentSeconds - normalized) < 0.0005)
            {
                return;
            }

            _endAdjustmentSeconds = normalized;
            ValidateDraft();
            NotifyDraftProperties();
        }
    }

    public double StartAdjustmentMinimumSeconds => _asset is null
        ? -StudioClipBoundaryPolicy.MaximumAdjustment.TotalSeconds
        : (StudioClipBoundaryPolicy.GetEarliestStart(_asset) -
           _asset.OriginalSourceStart).TotalSeconds;
    public double StartAdjustmentMaximumSeconds => _asset is null
        ? StudioClipBoundaryPolicy.MaximumAdjustment.TotalSeconds
        : (StudioClipBoundaryPolicy.GetLatestStart(_asset) -
           _asset.OriginalSourceStart).TotalSeconds;
    public double EndAdjustmentMinimumSeconds => _asset is null
        ? -StudioClipBoundaryPolicy.MaximumAdjustment.TotalSeconds
        : (StudioClipBoundaryPolicy.GetEarliestEnd(_asset) -
           _asset.OriginalSourceEnd).TotalSeconds;
    public double EndAdjustmentMaximumSeconds => _asset is null
        ? StudioClipBoundaryPolicy.MaximumAdjustment.TotalSeconds
        : (StudioClipBoundaryPolicy.GetLatestEnd(_asset) -
           _asset.OriginalSourceEnd).TotalSeconds;
    public TimeSpan DraftSourceStart => _asset is null
        ? TimeSpan.Zero
        : _asset.OriginalSourceStart +
          TimeSpan.FromSeconds(StartAdjustmentSeconds);
    public TimeSpan DraftSourceEnd => _asset is null
        ? TimeSpan.Zero
        : _asset.OriginalSourceEnd +
          TimeSpan.FromSeconds(EndAdjustmentSeconds);
    public TimeSpan DraftDuration => DraftSourceEnd - DraftSourceStart;
    public string DraftSourceStartText =>
        StudioTimeFormatter.FormatTime(DraftSourceStart);
    public string DraftSourceEndText =>
        StudioTimeFormatter.FormatTime(DraftSourceEnd);
    public string DraftDurationText => DraftDuration > TimeSpan.Zero
        ? StudioTimeFormatter.FormatTime(DraftDuration)
        : "Invalid range";
    public string StartAdjustmentText =>
        StudioTimeFormatter.FormatAdjustment(StartAdjustmentSeconds);
    public string EndAdjustmentText =>
        StudioTimeFormatter.FormatAdjustment(EndAdjustmentSeconds);
    public string StartAdjustmentSummary =>
        BoundaryAdjustmentSummary(StartAdjustmentSeconds);
    public string EndAdjustmentSummary =>
        BoundaryAdjustmentSummary(EndAdjustmentSeconds);
    public double BoundaryFrameStepSeconds => 1d / SourceFramesPerSecond;
    public string BoundaryPrecisionText =>
        $"{SourceFramesPerSecond:0.##} FPS · Arrow keys move one frame · " +
        "Page Up or Down moves one second";
    public bool IsBoundaryDraftValid =>
        _asset is not null &&
        StudioClipBoundaryPolicy.IsValid(
            _asset,
            DraftSourceStart,
            DraftSourceEnd);
    public bool HasPendingEdit =>
        _asset is not null &&
        (DraftSourceStart != _asset.SourceStart ||
         DraftSourceEnd != _asset.SourceEnd ||
         _asset.Captions is not null &&
         (SelectedCaptionStyle.Value != _asset.Appearance.CaptionStyle ||
          SelectedCaptionWordLimit.Value !=
              _asset.Appearance.CaptionWordLimit ||
          Math.Abs(
              CaptionVerticalPositionPercent -
              _asset.Appearance.CaptionVerticalPositionPercent) > 0.05 ||
          Math.Abs(
              CaptionMaximumWidthPercent -
              _asset.Appearance.CaptionMaximumWidthPercent) > 0.05 ||
          Math.Abs(
              CaptionFontScalePercent -
              _asset.Appearance.CaptionFontScalePercent) > 0.5) ||
         SelectedVideoEffect.Value != _asset.Appearance.VideoEffect ||
         Math.Abs(
             VideoEffectIntensityPercent -
             _asset.Appearance.VideoEffectIntensityPercent) > 0.5);
    public bool IsApplyingBoundaryEdit => _isHostBusy;
    public string BoundaryEditStatus => _status;
    public string? BoundaryEditError => _error;
    public bool HasBoundaryEditError => !string.IsNullOrWhiteSpace(_error);
    public IReadOnlyList<SelectionOption<GenerationCaptionStylePreset>>
        CaptionStyleOptions => _captionStyleOptions;

    public SelectionOption<GenerationCaptionStylePreset> SelectedCaptionStyle
    {
        get => _selectedCaptionStyle;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_captionStyleOptions.Contains(value))
            {
                throw new ArgumentException(
                    "The Studio caption style is not available.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedCaptionStyle, value))
            {
                return;
            }

            _selectedCaptionStyle = value;
            OnPropertyChanged();
            NotifyDraftProperties();
            DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<SelectionOption<StudioCaptionWordLimitPreset>>
        CaptionWordLimitOptions => _captionWordLimitOptions;

    public SelectionOption<StudioCaptionWordLimitPreset>
        SelectedCaptionWordLimit
    {
        get => _selectedCaptionWordLimit;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_captionWordLimitOptions.Contains(value))
            {
                throw new ArgumentException(
                    "The Studio caption word limit is not available.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedCaptionWordLimit, value))
            {
                return;
            }

            _selectedCaptionWordLimit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCaptionWordLimitDescription));
            NotifyCommandState();
            OnPropertyChanged(nameof(HasPendingEdit));
            DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedCaptionWordLimitDescription =>
        SelectedCaptionWordLimit.Description;

    public double CaptionMaximumWidthPercent
    {
        get => _captionMaximumWidthPercent;
        set
        {
            double normalized = Math.Round(
                Math.Clamp(
                    value,
                    StudioClipAppearance.MinimumCaptionMaximumWidthPercent,
                    StudioClipAppearance.MaximumCaptionMaximumWidthPercent),
                0,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(_captionMaximumWidthPercent - normalized) < 0.5)
            {
                return;
            }

            _captionMaximumWidthPercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CaptionMaximumWidthText));
            OnPropertyChanged(nameof(HasPendingEdit));
            NotifyCommandState();
            DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CaptionMaximumWidthText =>
        $"{CaptionMaximumWidthPercent:0}% of safe width";

    public double CaptionFontScalePercent
    {
        get => _captionFontScalePercent;
        set
        {
            double normalized = Math.Round(
                Math.Clamp(
                    value,
                    StudioClipAppearance.MinimumCaptionFontScalePercent,
                    StudioClipAppearance.MaximumCaptionFontScalePercent),
                0,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(_captionFontScalePercent - normalized) < 0.5)
            {
                return;
            }

            _captionFontScalePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CaptionFontScaleText));
            OnPropertyChanged(nameof(HasPendingEdit));
            NotifyCommandState();
            DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CaptionFontScaleText =>
        $"{CaptionFontScalePercent:0}%";

    public double CaptionVerticalPositionPercent
    {
        get => _captionVerticalPositionPercent;
        set
        {
            double normalized = Math.Round(
                Math.Clamp(
                    value,
                    StudioClipAppearance.MinimumCaptionVerticalPositionPercent,
                    StudioClipAppearance.MaximumCaptionVerticalPositionPercent),
                1,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(_captionVerticalPositionPercent - normalized) < 0.05)
            {
                return;
            }

            _captionVerticalPositionPercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CaptionVerticalPositionText));
            NotifyCommandState();
            DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CaptionVerticalPositionText =>
        $"{CaptionVerticalPositionPercent:0.#}% from top";
    public string? CaptionPresentationWarning =>
        StudioCaptionPresentationPolicy.GetPresentationWarning(
            _asset?.Captions,
            DraftAppearance);
    public bool HasCaptionPresentationWarning =>
        CaptionPresentationWarning is not null;
    public int CaptionedClipCount =>
        _project?.Assets.Count(static asset => asset.HasCaptions) ?? 0;
    public string ApplyCaptionLayoutToAllText => CaptionedClipCount switch
    {
        0 => "No captioned clips in this project",
        1 => "Only this clip has captions",
        int count => $"Apply this caption layout to all {count} captioned clips",
    };
    public ICommand ApplyCaptionLayoutToAllCommand =>
        _applyCaptionLayoutToAllCommand;
    public IReadOnlyList<SelectionOption<StudioVideoEffectPreset>>
        VideoEffectOptions => _videoEffectOptions;

    public SelectionOption<StudioVideoEffectPreset> SelectedVideoEffect
    {
        get => _selectedVideoEffect;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_videoEffectOptions.Contains(value))
            {
                throw new ArgumentException(
                    "The selected Studio video treatment is unavailable.",
                    nameof(value));
            }
            if (ReferenceEquals(_selectedVideoEffect, value))
            {
                return;
            }

            _selectedVideoEffect = value;
            if (value.Value == StudioVideoEffectPreset.None)
            {
                _videoEffectIntensityPercent = 0;
                OnPropertyChanged(nameof(VideoEffectIntensityPercent));
                OnPropertyChanged(nameof(VideoEffectIntensityText));
            }
            else if (_videoEffectIntensityPercent <= 0)
            {
                _videoEffectIntensityPercent = 50;
                OnPropertyChanged(nameof(VideoEffectIntensityPercent));
                OnPropertyChanged(nameof(VideoEffectIntensityText));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedVideoEffectDescription));
            NotifyCommandState();
            DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedVideoEffectDescription =>
        SelectedVideoEffect.Description;

    public double VideoEffectIntensityPercent
    {
        get => _videoEffectIntensityPercent;
        set
        {
            double normalized = Math.Round(
                Math.Clamp(value, 0, 100),
                0,
                MidpointRounding.AwayFromZero);
            if (SelectedVideoEffect.Value == StudioVideoEffectPreset.None)
            {
                normalized = 0;
            }
            if (Math.Abs(_videoEffectIntensityPercent - normalized) < 0.5)
            {
                return;
            }

            _videoEffectIntensityPercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoEffectIntensityText));
            NotifyCommandState();
            DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string VideoEffectIntensityText =>
        $"{VideoEffectIntensityPercent:0}%";
    public StudioClipAppearance DraftAppearance => new(
        _asset?.Captions is null
            ? GenerationCaptionStylePreset.Clean
            : SelectedCaptionStyle.Value,
        CaptionVerticalPositionPercent,
        SelectedVideoEffect.Value,
        VideoEffectIntensityPercent,
        _asset?.Appearance.GraphicOverlays,
        SelectedCaptionWordLimit.Value,
        CaptionMaximumWidthPercent,
        CaptionFontScalePercent);
    public ICommand ApplyBoundaryEditCommand => _applyCommand;
    public ICommand ResetBoundaryDraftCommand => _resetCommand;
    public ICommand NudgeStartEarlierCommand => _nudgeStartEarlierCommand;
    public ICommand NudgeStartLaterCommand => _nudgeStartLaterCommand;
    public ICommand NudgeEndEarlierCommand => _nudgeEndEarlierCommand;
    public ICommand NudgeEndLaterCommand => _nudgeEndLaterCommand;

    public void Bind(
        GenerationOutputProject? project,
        GenerationOutputAsset? asset)
    {
        _project = project;
        _asset = asset;
        LoadDraftFromAsset();
        NotifyDraftProperties();
        OnPropertyChanged(nameof(CaptionedClipCount));
        OnPropertyChanged(nameof(ApplyCaptionLayoutToAllText));
    }

    public void SetHostBusy(bool isBusy)
    {
        if (_isHostBusy == isBusy)
        {
            return;
        }

        _isHostBusy = isBusy;
        OnPropertyChanged(nameof(IsApplyingBoundaryEdit));
        NotifyCommandState();
    }

    internal StudioClipEditorDraftSnapshot? CapturePendingDraft() =>
        HasPendingEdit
            ? new StudioClipEditorDraftSnapshot(
                StartAdjustmentSeconds,
                EndAdjustmentSeconds,
                SelectedCaptionStyle.Value,
                SelectedCaptionWordLimit.Value,
                CaptionVerticalPositionPercent,
                CaptionMaximumWidthPercent,
                CaptionFontScalePercent,
                SelectedVideoEffect.Value,
                VideoEffectIntensityPercent)
            : null;

    internal void RestorePendingDraft(
        StudioClipEditorDraftSnapshot draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (_asset is null || _project?.IsFinalized != false)
        {
            return;
        }

        _startAdjustmentSeconds = draft.StartAdjustmentSeconds;
        _endAdjustmentSeconds = draft.EndAdjustmentSeconds;
        _selectedCaptionStyle = _captionStyleOptions.Single(
            option => option.Value == draft.CaptionStyle);
        _selectedCaptionWordLimit = _captionWordLimitOptions.Single(
            option => option.Value == draft.CaptionWordLimit);
        _captionVerticalPositionPercent =
            draft.CaptionVerticalPositionPercent;
        _captionMaximumWidthPercent =
            draft.CaptionMaximumWidthPercent;
        _captionFontScalePercent = draft.CaptionFontScalePercent;
        _selectedVideoEffect = _videoEffectOptions.Single(
            option => option.Value == draft.VideoEffect);
        _videoEffectIntensityPercent = draft.VideoEffectIntensityPercent;
        ValidateDraft();
        NotifyDraftProperties();
    }

    public bool ApplyPendingEdit()
    {
        if (!CanApplyBoundaryEdit())
        {
            return false;
        }

        ApplyBoundaryEdit();
        return true;
    }

    private void ApplyBoundaryEdit()
    {
        if (!CanApplyBoundaryEdit() ||
            _project is null ||
            _asset is null ||
            _outputEditor is null)
        {
            throw new InvalidOperationException(
                "The Studio clip-boundary edit is not ready to apply.");
        }

        var appearance = new StudioClipAppearance(
            _asset.Captions is null
                ? GenerationCaptionStylePreset.Clean
                : SelectedCaptionStyle.Value,
            CaptionVerticalPositionPercent,
            SelectedVideoEffect.Value,
            VideoEffectIntensityPercent,
            _asset.Appearance.GraphicOverlays,
            SelectedCaptionWordLimit.Value,
            CaptionMaximumWidthPercent,
            CaptionFontScalePercent);
        StudioClipBoundaryPolicy.Validate(
            _asset,
            DraftSourceStart,
            DraftSourceEnd);
        GenerationOutputAsset replacement = _asset.WithStudioEdits(
            DraftSourceStart,
            DraftSourceEnd,
            appearance);
        _outputEditor.ReplaceAsset(_project.Id, replacement);
        _error = null;
        _status =
            "Studio saved the edit without rendering. Finish the project to create the final files.";
        NotifyDraftProperties();
    }

    private bool CanApplyCaptionLayoutToAll() =>
        _outputEditor is not null &&
        _project is { IsFinalized: false } project &&
        !_isHostBusy &&
        _asset?.HasCaptions == true &&
        project.Assets.Count(static asset => asset.HasCaptions) > 1;

    private void ApplyCaptionLayoutToAll()
    {
        if (!CanApplyCaptionLayoutToAll() ||
            _project is null ||
            _outputEditor is null)
        {
            throw new InvalidOperationException(
                "Caption position cannot be applied across this Studio project.");
        }

        double position = CaptionVerticalPositionPercent;
        double maximumWidth = CaptionMaximumWidthPercent;
        double fontScale = CaptionFontScalePercent;
        GenerationOutputAsset[] replacements = _project.Assets
            .Where(static asset => asset.HasCaptions)
            .Select(asset => asset.WithStudioEdits(
                asset.SourceStart,
                asset.SourceEnd,
                new StudioClipAppearance(
                    asset.Appearance.CaptionStyle,
                    position,
                    asset.Appearance.VideoEffect,
                    asset.Appearance.VideoEffectIntensityPercent,
                    asset.Appearance.GraphicOverlays,
                    asset.Appearance.CaptionWordLimit,
                    maximumWidth,
                    fontScale)))
            .ToArray();
        _outputEditor.ReplaceAssets(_project.Id, replacements);
        _error = null;
        _status =
            $"Caption position, width, and text size applied to all {replacements.Length} captioned clips without rendering.";
        NotifyDraftProperties();
    }

    private bool CanApplyBoundaryEdit() =>
        _outputEditor is not null &&
        _project is { IsFinalized: false } &&
        !_isHostBusy &&
        IsBoundaryDraftValid &&
        HasPendingEdit;

    private bool CanNudgeStart(double delta)
    {
        if (_asset is null || _isHostBusy || _project?.IsFinalized != false)
        {
            return false;
        }

        double candidate = Math.Clamp(
            StartAdjustmentSeconds + delta,
            StartAdjustmentMinimumSeconds,
            StartAdjustmentMaximumSeconds);
        TimeSpan sourceStart = _asset.OriginalSourceStart +
            TimeSpan.FromSeconds(candidate);
        return Math.Abs(candidate - StartAdjustmentSeconds) >= 0.0005 &&
               StudioClipBoundaryPolicy.IsValid(
                   _asset,
                   sourceStart,
                   DraftSourceEnd);
    }

    private bool CanNudgeEnd(double delta)
    {
        if (_asset is null || _isHostBusy || _project?.IsFinalized != false)
        {
            return false;
        }

        double candidate = Math.Clamp(
            EndAdjustmentSeconds + delta,
            EndAdjustmentMinimumSeconds,
            EndAdjustmentMaximumSeconds);
        TimeSpan sourceEnd = _asset.OriginalSourceEnd +
            TimeSpan.FromSeconds(candidate);
        return Math.Abs(candidate - EndAdjustmentSeconds) >= 0.0005 &&
               StudioClipBoundaryPolicy.IsValid(
                   _asset,
                   DraftSourceStart,
                   sourceEnd);
    }

    private void NudgeStart(double delta)
    {
        if (!CanNudgeStart(delta)) return;
        StartAdjustmentSeconds += delta;
    }

    private void NudgeEnd(double delta)
    {
        if (!CanNudgeEnd(delta)) return;
        EndAdjustmentSeconds += delta;
    }

    private bool CanResetDraft() =>
        _asset is not null &&
        !_isHostBusy &&
        _project?.IsFinalized != true;

    private void ResetDraft()
    {
        if (_asset is null)
        {
            return;
        }

        _startAdjustmentSeconds = 0;
        _endAdjustmentSeconds = 0;
        LoadAppearanceFromAsset();
        _error = null;
        _status =
            "The saved Studio appearance and generated boundaries are restored in the draft.";
        NotifyDraftProperties();
        DraftAppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadDraftFromAsset()
    {
        _startAdjustmentSeconds = _asset is null
            ? 0
            : (_asset.SourceStart - _asset.OriginalSourceStart).TotalSeconds;
        _endAdjustmentSeconds = _asset is null
            ? 0
            : (_asset.SourceEnd - _asset.OriginalSourceEnd).TotalSeconds;
        _error = null;
        _status = _asset is null
            ? "Select a generated clip to adjust its boundaries."
            : _project?.IsFinalized == true
                ? "This clip is finalized and available in Library."
                : "Move either boundary by up to one minute, then apply the Studio edit.";
        LoadAppearanceFromAsset();
    }

    private void LoadAppearanceFromAsset()
    {
        if (_asset?.Captions is not null)
        {
            _selectedCaptionStyle = _captionStyleOptions.Single(
                option => option.Value == _asset.Captions.RequestedStyle);
        }
        if (_asset is null)
        {
            return;
        }

        _captionVerticalPositionPercent =
            _asset.Appearance.CaptionVerticalPositionPercent;
        _selectedCaptionWordLimit = _captionWordLimitOptions.Single(
            option => option.Value == _asset.Appearance.CaptionWordLimit);
        _captionMaximumWidthPercent =
            _asset.Appearance.CaptionMaximumWidthPercent;
        _captionFontScalePercent =
            _asset.Appearance.CaptionFontScalePercent;
        _selectedVideoEffect = _videoEffectOptions.Single(
            option => option.Value == _asset.Appearance.VideoEffect);
        _videoEffectIntensityPercent =
            _asset.Appearance.VideoEffectIntensityPercent;
    }

    private void ValidateDraft()
    {
        _error = IsBoundaryDraftValid
            ? null
            : "The clip end must remain after its start.";
        _status = IsBoundaryDraftValid
            ? "Boundary changes are a draft until you apply them."
            : "Adjust the boundaries so the clip has a positive duration.";
    }

    private void NotifyDraftProperties()
    {
        foreach (string propertyName in new[]
        {
            nameof(StartAdjustmentSeconds),
            nameof(EndAdjustmentSeconds),
            nameof(StartAdjustmentMinimumSeconds),
            nameof(StartAdjustmentMaximumSeconds),
            nameof(EndAdjustmentMinimumSeconds),
            nameof(EndAdjustmentMaximumSeconds),
            nameof(DraftSourceStart),
            nameof(DraftSourceEnd),
            nameof(DraftDuration),
            nameof(DraftSourceStartText),
            nameof(DraftSourceEndText),
            nameof(DraftDurationText),
            nameof(StartAdjustmentText),
            nameof(EndAdjustmentText),
            nameof(StartAdjustmentSummary),
            nameof(EndAdjustmentSummary),
            nameof(BoundaryFrameStepSeconds),
            nameof(BoundaryPrecisionText),
            nameof(IsBoundaryDraftValid),
            nameof(HasPendingEdit),
            nameof(IsApplyingBoundaryEdit),
            nameof(BoundaryEditStatus),
            nameof(BoundaryEditError),
            nameof(HasBoundaryEditError),
            nameof(CaptionStyleOptions),
            nameof(SelectedCaptionStyle),
            nameof(CaptionWordLimitOptions),
            nameof(SelectedCaptionWordLimit),
            nameof(SelectedCaptionWordLimitDescription),
            nameof(CaptionVerticalPositionPercent),
            nameof(CaptionVerticalPositionText),
            nameof(CaptionPresentationWarning),
            nameof(HasCaptionPresentationWarning),
            nameof(CaptionMaximumWidthPercent),
            nameof(CaptionMaximumWidthText),
            nameof(CaptionFontScalePercent),
            nameof(CaptionFontScaleText),
            nameof(CaptionedClipCount),
            nameof(ApplyCaptionLayoutToAllText),
            nameof(VideoEffectOptions),
            nameof(SelectedVideoEffect),
            nameof(SelectedVideoEffectDescription),
            nameof(VideoEffectIntensityPercent),
            nameof(VideoEffectIntensityText),
        })
        {
            OnPropertyChanged(propertyName);
        }

        NotifyCommandState();
        DraftRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyCommandState()
    {
        _applyCommand.RaiseCanExecuteChanged();
        _resetCommand.RaiseCanExecuteChanged();
        _applyCaptionLayoutToAllCommand.RaiseCanExecuteChanged();
        _nudgeStartEarlierCommand.RaiseCanExecuteChanged();
        _nudgeStartLaterCommand.RaiseCanExecuteChanged();
        _nudgeEndEarlierCommand.RaiseCanExecuteChanged();
        _nudgeEndLaterCommand.RaiseCanExecuteChanged();
    }

    private double SourceFramesPerSecond
    {
        get
        {
            double? rate = _asset?.SourceMedia.PrimaryVideoStream
                .PreferredFrameRate;
            return rate is > 0 && double.IsFinite(rate.Value)
                ? Math.Clamp(rate.Value, 1, 240)
                : 30;
        }
    }

    private string BoundaryAdjustmentSummary(double seconds)
    {
        if (Math.Abs(seconds) < 0.0005)
        {
            return "Generated cut";
        }

        int frames = Math.Max(
            1,
            (int)Math.Round(
                Math.Abs(seconds) * SourceFramesPerSecond,
                MidpointRounding.AwayFromZero));
        string unit = frames == 1 ? "frame" : "frames";
        string direction = seconds < 0 ? "earlier" : "later";
        string frameSummary = $"{frames} {unit} {direction}";
        return Math.Abs(seconds) < 0.5
            ? frameSummary
            : $"{StudioTimeFormatter.FormatAdjustment(seconds)} · {frameSummary}";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
