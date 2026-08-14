using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public enum StudioClipPreferenceRating
{
    Dislike = -1,
    Neutral = 0,
    Like = 1,
}

public sealed record StudioClipPreferenceStatus(
    int RatedCount,
    int LikeCount,
    int DislikeCount,
    int MinimumRatedCount,
    bool IsReady);

public interface IStudioClipPreferenceService
{
    StudioClipPreferenceStatus Current { get; }

    bool CanRate(GenerationOutputAsset asset);

    void Update(
        GenerationOutputAsset asset,
        StudioClipPreferenceRating? previous,
        StudioClipPreferenceRating current);
}

public sealed class StudioClipPreferenceService :
    IStudioClipPreferenceService
{
    private readonly IClipPreferenceFeedbackStore _store;

    public StudioClipPreferenceService(
        IClipPreferenceFeedbackStore store)
    {
        _store = store ??
            throw new ArgumentNullException(nameof(store));
    }

    public StudioClipPreferenceStatus Current
    {
        get
        {
            ClipPreferenceProfile profile = _store.Current;
            return new StudioClipPreferenceStatus(
                profile.RatedCount,
                profile.LikeCount,
                profile.DislikeCount,
                ClipPreferenceProfile.MinimumRatedCount,
                profile.IsReady);
        }
    }

    public bool CanRate(GenerationOutputAsset asset) =>
        asset?.PreferenceFeatures is not null;

    public void Update(
        GenerationOutputAsset asset,
        StudioClipPreferenceRating? previous,
        StudioClipPreferenceRating current)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!Enum.IsDefined(current) ||
            previous is { } previousValue &&
            !Enum.IsDefined(previousValue) ||
            asset.PreferenceFeatures is not
                ClipPreferenceFeatureVector features)
        {
            throw new ArgumentException(
                "Studio preference feedback requires a defined rating and a bounded feature vector.");
        }

        _store.Update(
            features,
            previous is null
                ? null
                : Map(previous.Value),
            Map(current));
    }

    private static ClipPreferenceRating Map(
        StudioClipPreferenceRating rating) => rating switch
        {
            StudioClipPreferenceRating.Dislike =>
                ClipPreferenceRating.Dislike,
            StudioClipPreferenceRating.Neutral =>
                ClipPreferenceRating.Neutral,
            StudioClipPreferenceRating.Like =>
                ClipPreferenceRating.Like,
            _ => throw new ArgumentOutOfRangeException(nameof(rating)),
        };
}
