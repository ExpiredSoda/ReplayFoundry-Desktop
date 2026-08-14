using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.ClipGoals;

public sealed class ClipGoalsStepViewModel : INotifyPropertyChanged
{
    private readonly GenerationSetupDraft _draft;

    private readonly SelectionOption<ContentEmphasis>[]
        _emphasisOptions;

    private readonly SelectionOption<ClipFulfillmentPreference>[]
        _fulfillmentOptions;

    private double _desiredResultCount;
    private double _qualityThreshold;
    private double _maximumClipDurationSeconds;
    private bool _isAutomaticResultCount;

    private SelectionOption<ContentEmphasis>
        _selectedEmphasisOption;

    private SelectionOption<ClipFulfillmentPreference>
        _selectedFulfillmentOption;

    public ClipGoalsStepViewModel(
        GenerationSetupDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = draft;

        _emphasisOptions =
        [
            new SelectionOption<ContentEmphasis>(
                ContentEmphasis.GameplayFocused,
                "Gameplay focused",
                "Prefer high-action gameplay moments with less commentary."),

            new SelectionOption<ContentEmphasis>(
                ContentEmphasis.Balanced,
                "Balanced",
                "Balance strong gameplay moments with useful commentary."),

            new SelectionOption<ContentEmphasis>(
                ContentEmphasis.CommentaryFocused,
                "Commentary focused",
                "Prefer moments with more creator speech, reactions, and storytelling."),
        ];

        _fulfillmentOptions =
        [
            new SelectionOption<ClipFulfillmentPreference>(
                ClipFulfillmentPreference.FillRequestedCount,
                "Fill requested count",
                "Make the requested number whenever enough safe windows exist. Lower-scoring or similar clips may be included as a last resort."),

            new SelectionOption<ClipFulfillmentPreference>(
                ClipFulfillmentPreference.QualityFirst,
                "Quality first",
                "Return only distinct clips that meet the quality target, even when that means returning fewer clips."),
        ];

        _desiredResultCount =
            draft.DesiredResultCount;

        _qualityThreshold =
            draft.QualityThreshold;
        _maximumClipDurationSeconds =
            draft.MaximumClipDuration.TotalSeconds;
        _isAutomaticResultCount =
            draft.ResultCountMode ==
            GenerationResultCountMode.Auto;

        _selectedEmphasisOption =
            _emphasisOptions.Single(
                option =>
                    option.Value ==
                    draft.ContentEmphasis);

        _selectedFulfillmentOption =
            _fulfillmentOptions.Single(
                option =>
                    option.Value ==
                    draft.ClipFulfillmentPreference);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SelectionOption<ContentEmphasis>>
        EmphasisOptions =>
        _emphasisOptions;

    public IReadOnlyList<SelectionOption<ClipFulfillmentPreference>>
        FulfillmentOptions =>
        _fulfillmentOptions;

    public double DesiredResultCount
    {
        get => _desiredResultCount;

        set
        {
            double roundedValue =
                Math.Round(value);

            if (roundedValue is < 1 or > 30)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The desired result count must be between 1 and 30.");
            }

            if (Math.Abs(
                    _desiredResultCount -
                    roundedValue) <
                0.001)
            {
                return;
            }

            _desiredResultCount =
                roundedValue;

            UpdateDraft();

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(DesiredResultCountText));

            OnPropertyChanged(
                nameof(IsValid));

            OnPropertyChanged(
                nameof(ValidationMessage));
        }
    }

    public bool IsAutomaticResultCount
    {
        get => _isAutomaticResultCount;
        set
        {
            if (_isAutomaticResultCount == value)
            {
                return;
            }
            _isAutomaticResultCount = value;
            if (value)
            {
                _desiredResultCount = 30;
                _selectedFulfillmentOption =
                    _fulfillmentOptions.Single(
                        option =>
                            option.Value ==
                            ClipFulfillmentPreference.QualityFirst);
            }
            UpdateDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExactResultCount));
            OnPropertyChanged(nameof(DesiredResultCount));
            OnPropertyChanged(nameof(DesiredResultCountText));
            OnPropertyChanged(nameof(SelectedFulfillmentOption));
            OnPropertyChanged(nameof(SelectedFulfillmentDescription));
            OnPropertyChanged(nameof(QualityThresholdDescription));
            OnPropertyChanged(nameof(QualityControlLabel));
            OnPropertyChanged(nameof(CountQualityRelationshipTitle));
            OnPropertyChanged(nameof(CountQualityRelationshipDescription));
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(ValidationMessage));
        }
    }

    public bool IsExactResultCount => !IsAutomaticResultCount;

    public double QualityThreshold
    {
        get => _qualityThreshold;

        set
        {
            if (value is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The quality threshold must be between 0 and 100.");
            }

            if (Math.Abs(
                    _qualityThreshold -
                    value) <
                0.001)
            {
                return;
            }

            _qualityThreshold = value;

            UpdateDraft();

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(QualityThresholdText));

            OnPropertyChanged(
                nameof(IsValid));

            OnPropertyChanged(
                nameof(ValidationMessage));
        }
    }

    public SelectionOption<ContentEmphasis>
        SelectedEmphasisOption
    {
        get => _selectedEmphasisOption;

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!_emphasisOptions.Contains(value))
            {
                throw new ArgumentException(
                    "The selected content-emphasis option is not available.",
                    nameof(value));
            }

            if (ReferenceEquals(
                    _selectedEmphasisOption,
                    value))
            {
                return;
            }

            _selectedEmphasisOption = value;

            UpdateDraft();

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(SelectedEmphasisDescription));
        }
    }

    public SelectionOption<ClipFulfillmentPreference>
        SelectedFulfillmentOption
    {
        get => _selectedFulfillmentOption;

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (IsAutomaticResultCount &&
                value.Value != ClipFulfillmentPreference.QualityFirst)
            {
                throw new InvalidOperationException(
                    "Auto amount always returns only clips meeting the selected quality target.");
            }

            if (!_fulfillmentOptions.Contains(value))
            {
                throw new ArgumentException(
                    "The selected clip-fulfillment option is not available.",
                    nameof(value));
            }

            if (ReferenceEquals(
                    _selectedFulfillmentOption,
                    value))
            {
                return;
            }

            _selectedFulfillmentOption = value;
            UpdateDraft();
            OnPropertyChanged();
            OnPropertyChanged(
                nameof(SelectedFulfillmentDescription));
            OnPropertyChanged(
                nameof(QualityThresholdDescription));
            OnPropertyChanged(nameof(QualityControlLabel));
            OnPropertyChanged(nameof(CountQualityRelationshipTitle));
            OnPropertyChanged(nameof(CountQualityRelationshipDescription));
        }
    }

    public string ResultCountLabel =>
        _draft.Request.Mode ==
        GenerationMode.Montage
            ? "Desired montage segments"
            : "Desired individual clips";

    public string ResultCountDescription =>
        _draft.Request.Mode ==
        GenerationMode.Montage
            ? "How many candidate moments should make up the initial montage storyboard?"
            : "How many separate candidate clips should Replay Foundry return?";

    public string DesiredResultCountText =>
        IsAutomaticResultCount
            ? "Auto · up to 30"
            : _draft.Request.Mode ==
        GenerationMode.Montage
            ? $"{(int)DesiredResultCount} " +
              (DesiredResultCount == 1
                  ? "segment"
                  : "segments")
            : $"{(int)DesiredResultCount} " +
              (DesiredResultCount == 1
                  ? "clip"
                  : "clips");

    public string QualityThresholdText =>
        $"{QualityThreshold:0}%";

    public double MaximumClipDurationSeconds
    {
        get => _maximumClipDurationSeconds;
        set
        {
            double rounded = Math.Round(value);
            if (rounded is < 10 or > 180)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "The maximum candidate length must be between 10 and 180 seconds.");
            }
            if (Math.Abs(_maximumClipDurationSeconds - rounded) < 0.001)
            {
                return;
            }
            _maximumClipDurationSeconds = rounded;
            _draft.UpdateMaximumClipDuration(
                TimeSpan.FromSeconds(rounded));
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaximumClipDurationText));
            OnPropertyChanged(nameof(MaximumClipDurationDescription));
        }
    }

    public string MaximumClipDurationText =>
        MediaTimeFormatter.Format(
            TimeSpan.FromSeconds(MaximumClipDurationSeconds));

    public string MaximumClipDurationDescription =>
        $"No candidate will exceed {MaximumClipDurationText}. Replay Foundry still ends a clip earlier when the strongest complete moment is shorter.";

    public string SelectedFulfillmentDescription =>
        SelectedFulfillmentOption.Description;

    public string QualityThresholdDescription =>
        IsAutomaticResultCount
            ? "Auto returns every distinct safe clip meeting this exact quality floor, up to the 30-clip cap."
            : SelectedFulfillmentOption.Value ==
            ClipFulfillmentPreference.QualityFirst
            ? "This is a strict quality floor. Replay Foundry may return fewer clips when too few distinct moments meet it."
            : "This is the quality target for first choices. Replay Foundry may use the best safe lower-scoring clips to reach the requested count.";

    public string QualityControlLabel =>
        !IsAutomaticResultCount &&
        SelectedFulfillmentOption.Value == ClipFulfillmentPreference.FillRequestedCount
            ? "Quality target"
            : "Quality threshold";

    public string CountQualityRelationshipTitle =>
        IsAutomaticResultCount
            ? "Quality decides the amount"
            : SelectedFulfillmentOption.Value == ClipFulfillmentPreference.FillRequestedCount
                ? "Count wins after quality-first choices"
                : "Quality can reduce the count";

    public string CountQualityRelationshipDescription =>
        IsAutomaticResultCount
            ? "Replay Foundry returns every distinct clip at or above this threshold, up to 30."
            : SelectedFulfillmentOption.Value == ClipFulfillmentPreference.FillRequestedCount
                ? "The quality value ranks the first choices, but it is not a hard cutoff. If needed, the best safe clips below the target can fill your requested count."
                : "The quality value is a hard cutoff. If fewer moments qualify, Replay Foundry returns fewer than requested.";

    public string SelectedEmphasisDescription =>
        SelectedEmphasisOption.Description;

    public bool IsValid =>
        DesiredResultCount is >= 1 and <= 30 &&
        QualityThreshold is >= 0 and <= 100;

    public string? ValidationMessage =>
        IsValid
            ? null
            : "Clip goals must use a result count from 1 to 30 and a quality threshold from 0 to 100.";

    private void UpdateDraft()
    {
        _draft.UpdateClipGoals(
            (int)Math.Round(
                DesiredResultCount),
            QualityThreshold,
            SelectedEmphasisOption.Value,
            SelectedFulfillmentOption.Value,
            IsAutomaticResultCount
                ? GenerationResultCountMode.Auto
                : GenerationResultCountMode.Exact);
    }

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}
