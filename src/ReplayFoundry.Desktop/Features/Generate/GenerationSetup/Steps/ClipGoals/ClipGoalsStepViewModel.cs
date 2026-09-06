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
                "Reach the amount",
                "Try to make the number you requested. If needed, include the best safe alternatives."),

            new SelectionOption<ClipFulfillmentPreference>(
                ClipFulfillmentPreference.QualityFirst,
                "Only strong matches",
                "Return fewer clips rather than include weaker or very similar moments."),
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

    public IReadOnlyList<SelectionOption<GenerationMomentIntent>> IntentOptions { get; } =
    [
        new(GenerationMomentIntent.Any, "Any strong moment", "Discover a balanced range of moments."),
        new(GenerationMomentIntent.Action, "Action", "Prefer complete action events."),
        new(GenerationMomentIntent.Humor, "Humor and reactions", "Prefer moments observed as humor."),
        new(GenerationMomentIntent.Story, "Stories", "Prefer self-contained stories."),
        new(GenerationMomentIntent.Discovery, "Discoveries", "Prefer reveals and discoveries."),
        new(GenerationMomentIntent.Failure, "Failures", "Prefer observable failures."),
        new(GenerationMomentIntent.Dialogue, "Dialogue", "Prefer dialogue with sufficient context."),
        new(GenerationMomentIntent.Clutch, "Comebacks and clutches", "Search spoken context for recovery against the odds; review must establish what happened."),
        new(GenerationMomentIntent.Tutorial, "Tutorials", "Search spoken context for explanations and instructions."),
        new(GenerationMomentIntent.Reaction, "Creator reactions", "Search spoken context for surprise or excitement."),
    ];

    public SelectionOption<GenerationMomentIntent> SelectedIntentOption
    {
        get => IntentOptions.Single(value => value.Value == _draft.DiscoveryIntent.MomentType);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _draft.UpdateDiscoveryIntent(new(value.Value, SpokenSearchTerms, NaturalLanguageQuery));
            OnPropertyChanged();
        }
    }

    public string SpokenSearchTerms
    {
        get => _draft.DiscoveryIntent.SpokenTerms;
        set
        {
            _draft.UpdateDiscoveryIntent(new(SelectedIntentOption.Value, value, NaturalLanguageQuery));
            OnPropertyChanged();
        }
    }

    public string NaturalLanguageQuery
    {
        get => _draft.DiscoveryIntent.NaturalLanguageQuery;
        set
        {
            _draft.UpdateDiscoveryIntent(new(SelectedIntentOption.Value, SpokenSearchTerms, value));
            OnPropertyChanged();
        }
    }

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
            ? "Montage clips"
            : "Number of clips";

    public string ResultCountDescription =>
        _draft.Request.Mode ==
        GenerationMode.Montage
            ? "How many clips should make up the first montage?"
            : "How many separate clips should Replay Foundry find?";

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
        $"Clips will not be longer than {MaximumClipDurationText}. A complete moment can still end sooner.";

    public string SelectedFulfillmentDescription =>
        SelectedFulfillmentOption.Description;

    public string QualityThresholdDescription =>
        IsAutomaticResultCount
            ? "Automatic amount keeps every distinct clip at or above this quality level, up to 30."
            : SelectedFulfillmentOption.Value ==
            ClipFulfillmentPreference.QualityFirst
            ? "Replay Foundry may return fewer clips when there are not enough distinct moments at this quality."
            : "Replay Foundry starts with clips at this quality, then may use the best safe alternatives to reach your amount.";

    public string QualityControlLabel =>
        !IsAutomaticResultCount &&
        SelectedFulfillmentOption.Value == ClipFulfillmentPreference.FillRequestedCount
            ? "Preferred quality"
            : "Minimum quality";

    public string CountQualityRelationshipTitle =>
        IsAutomaticResultCount
            ? "Quality sets the amount"
            : SelectedFulfillmentOption.Value == ClipFulfillmentPreference.FillRequestedCount
                ? "Replay Foundry will reach your amount"
                : "You may get fewer clips";

    public string CountQualityRelationshipDescription =>
        IsAutomaticResultCount
            ? "Replay Foundry returns every distinct clip at or above this quality level, up to 30."
            : SelectedFulfillmentOption.Value == ClipFulfillmentPreference.FillRequestedCount
                ? "Replay Foundry starts with the strongest choices, then uses the best safe alternatives if more are needed."
                : "Replay Foundry leaves out moments below this quality, even when that means returning fewer clips.";

    public string SelectedEmphasisDescription =>
        SelectedEmphasisOption.Description;

    public bool IsValid =>
        DesiredResultCount is >= 1 and <= 30 &&
        QualityThreshold is >= 0 and <= 100;

    public string? ValidationMessage =>
        IsValid
            ? null
            : "Choose 1 to 30 clips and a quality level from 0 to 100.";

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
