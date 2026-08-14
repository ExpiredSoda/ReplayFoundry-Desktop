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

public sealed class StudioClipPreferenceViewModel : INotifyPropertyChanged
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
        _outputEditor = outputEditor;
        _decisionStore = decisionStore;
        _researchFeedback = researchFeedback;
        _setCommand = new DelegateCommand<StudioClipPreferenceRating>(
            SetPreference,
            _ => CanSetPreference());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StudioClipPreferenceRating? SelectedPreference =>
        _asset is not null &&
        _sessionRatings.TryGetValue(
            _asset.Id,
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
            "Saved — this moment will not push future choices up or down.",
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
                return "Local preference learning is unavailable; this does not block editing or rendering.";
            }

            StudioClipPreferenceStatus status = _service.Current;
            return status.IsReady
                ? $"Personalization is active from {status.RatedCount} rated clips. " +
                  "It uses only aggregate, game-agnostic measurements."
                : $"Learning safely: {status.RatedCount}/{status.MinimumRatedCount} " +
                  $"rated clips, with {status.LikeCount} Likes and " +
                  $"{status.DislikeCount} Dislikes. Until coverage is sufficient, " +
                  "ranking is unchanged.";
        }
    }
    public ICommand SetPreferenceCommand => _setCommand;

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
        ? "Kept for final render"
        : "Removed from the render; your edits and feedback stay saved";

    public void Bind(
        GenerationOutputProject? project,
        GenerationOutputAsset? asset)
    {
        _project = project;
        _asset = asset;
        if (asset is not null &&
            _decisionStore?.Find(asset.Id)?.Rating is { } saved)
        {
            _sessionRatings[asset.Id] = saved;
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
                "The candidate selection changed for this Studio session, " +
                "but its feedback record could not be saved: " +
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
            _sessionRatings[asset.Id] = rating;
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
                "Preference feedback could not be saved: " +
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
            ResolveRating(asset.Id),
            DateTimeOffset.UtcNow));
    }

    private StudioClipPreferenceRating? ResolveRating(string assetId) =>
        _sessionRatings.TryGetValue(
            assetId,
            out StudioClipPreferenceRating rating)
                ? rating
                : _decisionStore?.Find(assetId)?.Rating;

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
