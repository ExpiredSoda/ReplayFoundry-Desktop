using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioClipPreferenceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStudioClipPreferenceService? _service;
    private readonly IGenerationOutputEditor? _outputEditor;
    private readonly IStudioCandidateDecisionStore? _decisionStore;
    private readonly IResearchFeedbackRecorder? _researchFeedback;
    private readonly Dictionary<string, StudioClipPreferenceRating>
        _sessionRatings = new(StringComparer.Ordinal);
    private readonly DelegateCommand<StudioClipPreferenceRating> _setCommand;
    private GenerationOutputAsset? _asset;
    private GenerationOutputProject? _project;
    private string? _error;
    private bool _isHostBusy;

    public StudioClipPreferenceViewModel(
        IStudioClipPreferenceService? service,
        IGenerationOutputEditor? outputEditor = null,
        IStudioCandidateDecisionStore? decisionStore = null,
        IResearchFeedbackRecorder? researchFeedback = null)
    {
        _service = service;
        if (service is not null) service.Changed += LearningChanged;
        _outputEditor = outputEditor;
        _decisionStore = decisionStore;
        _researchFeedback = researchFeedback;
        _setCommand = new DelegateCommand<StudioClipPreferenceRating>(
            SetPreference,
            _ => CanSetPreference());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void LearningChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(PreferenceLearningStatus));
    public void Dispose() { if (_service is not null) _service.Changed -= LearningChanged; }

    public StudioClipPreferenceRating? SelectedPreference =>
        _asset is not null &&
        _sessionRatings.TryGetValue(
            RatingKey(_asset),
            out StudioClipPreferenceRating rating)
                ? rating
                : null;
    public bool IsLikeSelected =>
        SelectedPreference == StudioClipPreferenceRating.Like;
    public bool IsNeutralSelected =>
        SelectedPreference == StudioClipPreferenceRating.Neutral;
    public bool IsDislikeSelected =>
        SelectedPreference == StudioClipPreferenceRating.Dislike;
    public string PreferenceSelectionText => SelectedPreference switch
    {
        StudioClipPreferenceRating.Like => "Liked",
        StudioClipPreferenceRating.Neutral => "Neutral",
        StudioClipPreferenceRating.Dislike => "Disliked",
        _ => "Not rated",
    };
    public string PreferenceActionText => SelectedPreference switch
    {
        StudioClipPreferenceRating.Like =>
            "Saved — you want more moments with patterns like this.",
        StudioClipPreferenceRating.Neutral =>
            "Saved — this teaches the difference between a favorite, an okay moment, and one you dislike.",
        StudioClipPreferenceRating.Dislike =>
            "Saved — you want fewer moments with patterns like this.",
        _ => "Choose a preference to help future moment suggestions.",
    };
    public string PreferenceLearningStatus
    {
        get
        {
            if (_error is not null)
            {
                return _error;
            }
            if (_service is null)
            {
                return "Personalized suggestions are unavailable. You can still edit and create finished files.";
            }

            if (_service.LearningStatus is { } learnedStatus) return learnedStatus;
            StudioClipPreferenceStatus status = _service.Current;
            return status.IsReady
                ? $"Personalized suggestions are active after {status.RatedCount} clip ratings. " +
                  "Replay Foundry learns general patterns, never your words or game names."
                : $"Learning from {status.RatedCount}/{status.MinimumRatedCount} clip ratings, " +
                  $"including {status.LikeCount} Likes and {status.DislikeCount} Dislikes. " +
                  "Suggestions stay unchanged until there is enough balanced feedback.";
        }
    }
    public ICommand SetPreferenceCommand => _setCommand;
    public string? PreferenceError => _error;
    public bool HasPreferenceError => _error is not null;

    public bool IsIncludedInFinalRender
    {
        get => _asset?.IsIncludedInFinalRender == true;
        set
        {
            if (_asset is not { } asset ||
                _project is not { } project)
            {
                return;
            }

            SetRenderInclusion(project, asset, value);
        }
    }

    public bool CanChangeRenderInclusion =>
        _asset is { } asset &&
        _project is { } project &&
        CanSetRenderInclusion(project, asset, !asset.IsIncludedInFinalRender);

    public string RenderDispositionText => IsIncludedInFinalRender
        ? "Included in finished files"
        : "Not included; your edits and feedback stay saved";

    public void Bind(
        GenerationOutputProject? project,
        GenerationOutputAsset? asset)
    {
        _project = project;
        _asset = asset;
        if (asset is not null && _decisionStore?.Find(asset.Id) is { Rating: { } saved } decision &&
            decision.SourceStart == asset.SourceStart && decision.SourceEnd == asset.SourceEnd)
        {
            _sessionRatings[RatingKey(asset)] = saved;
        }
        NotifyProperties();
    }

    public void SetHostBusy(bool value)
    {
        if (_isHostBusy == value)
        {
            return;
        }

        _isHostBusy = value;
        NotifyProperties();
    }

    internal bool CanSetRenderInclusion(
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        bool isIncluded) =>
        !_isHostBusy &&
        _outputEditor is not null &&
        !project.IsFinalized &&
        ReferenceEquals(_project, project) &&
        project.Assets.Any(candidate => ReferenceEquals(candidate, asset)) &&
        asset.IsIncludedInFinalRender != isIncluded;

    internal bool SetRenderInclusion(
        GenerationOutputProject project,
        GenerationOutputAsset asset,
        bool isIncluded)
    {
        if (!CanSetRenderInclusion(project, asset, isIncluded))
        {
            return false;
        }

        bool changed = false;
        try
        {
            GenerationOutputAssetDisposition disposition = isIncluded
                ? GenerationOutputAssetDisposition.IncludeInFinalRender
                : GenerationOutputAssetDisposition.ExcludeFromFinalRender;
            GenerationOutputAsset replacement = asset.WithDisposition(disposition);
            _outputEditor!.ReplaceAsset(project.Id, replacement);
            changed = true;
            if (_asset?.Id.Equals(asset.Id, StringComparison.Ordinal) == true)
            {
                _asset = replacement;
            }
            SaveDecision(project, replacement);
            RecordResearch(
                replacement,
                ResearchFeedbackChannel.StudioSelection,
                isIncluded
                    ? ResearchFeedbackValue.Included
                    : ResearchFeedbackValue.Excluded);
            _error = null;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _error =
                "Your clip choice changed for this Studio session, " +
                "but Replay Foundry could not save it for next time: " +
                exception.Message;
        }
        NotifyProperties();
        return changed;
    }

    private bool CanSetPreference() =>
        !_isHostBusy &&
        _asset is { } asset && _service?.CanRate(asset) == true;

    private void SetPreference(StudioClipPreferenceRating rating)
    {
        if (!Enum.IsDefined(rating) ||
            _asset is not { } asset ||
            _service is null)
        {
            return;
        }

        StudioClipPreferenceRating? previous = SelectedPreference;
        try
        {
            _service.Update(asset, previous, rating);
            _sessionRatings[RatingKey(asset)] = rating;
            SaveDecision(_project, asset);
            RecordResearch(
                asset,
                ResearchFeedbackChannel.Satisfaction,
                rating switch
                {
                    StudioClipPreferenceRating.Like =>
                        ResearchFeedbackValue.Like,
                    StudioClipPreferenceRating.Neutral =>
                        ResearchFeedbackValue.Neutral,
                    StudioClipPreferenceRating.Dislike =>
                        ResearchFeedbackValue.Dislike,
                    _ => throw new ArgumentOutOfRangeException(nameof(rating)),
                });
            _error = null;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _error =
                "Your rating changed for this session, but it could not be saved for next time: " +
                exception.Message;
        }
        NotifyProperties();
    }

    private void NotifyProperties()
    {
        foreach (string propertyName in new[]
        {
            nameof(SelectedPreference),
            nameof(IsLikeSelected),
            nameof(IsNeutralSelected),
            nameof(IsDislikeSelected),
            nameof(PreferenceSelectionText),
            nameof(PreferenceActionText),
            nameof(PreferenceLearningStatus),
            nameof(PreferenceError),
            nameof(HasPreferenceError),
            nameof(IsIncludedInFinalRender),
            nameof(CanChangeRenderInclusion),
            nameof(RenderDispositionText),
        })
        {
            OnPropertyChanged(propertyName);
        }
        _setCommand.RaiseCanExecuteChanged();
    }

    private void SaveDecision(
        GenerationOutputProject? project,
        GenerationOutputAsset asset)
    {
        if (project is null || _decisionStore is null)
        {
            return;
        }
        string sourceIdentity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                asset.SourceFullPath.ToUpperInvariant() + "|" +
                asset.SourceDuration.Ticks)));
        _decisionStore.Upsert(new StudioCandidateDecision(
            asset.Id,
            project.Id,
            sourceIdentity,
            asset.SourceStart,
            asset.SourceEnd,
            asset.Disposition,
            ResolveRating(asset),
            DateTimeOffset.UtcNow));
    }

    private StudioClipPreferenceRating? ResolveRating(GenerationOutputAsset asset) =>
        _sessionRatings.TryGetValue(
            RatingKey(asset),
            out StudioClipPreferenceRating rating)
                ? rating
                : _decisionStore?.Find(asset.Id) is { } decision && decision.SourceStart == asset.SourceStart && decision.SourceEnd == asset.SourceEnd
                    ? decision.Rating : null;

    private static string RatingKey(GenerationOutputAsset asset) => $"{asset.Id}|{asset.SourceStart.Ticks}|{asset.SourceEnd.Ticks}";

    private void RecordResearch(
        GenerationOutputAsset asset,
        ResearchFeedbackChannel channel,
        ResearchFeedbackValue value)
    {
        if (_researchFeedback is null ||
            asset.PreferenceFeatures is not { } features)
        {
            return;
        }
        _researchFeedback.Record(
            asset.Id,
            asset.SourceFullPath,
            asset.SourceDuration,
            features,
            channel,
            value);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
