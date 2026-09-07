using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;

namespace ReplayFoundry.Desktop.Features.Personalization;

public sealed class NeuralStudioClipPreferenceService(ITasteLearningService learning) : IStudioClipPreferenceService
{
    public StudioClipPreferenceStatus Current => new(learning.Status.Ratings, learning.Status.Likes, learning.Status.Dislikes, 8, learning.Status.IsActive);
    public event EventHandler? Changed { add => learning.Changed += value; remove => learning.Changed -= value; }
    public string LearningStatus => learning.Status.Message;
    public bool CanRate(GenerationOutputAsset asset) => asset is not null && asset.Duration > TimeSpan.Zero;
    public void Update(GenerationOutputAsset asset, StudioClipPreferenceRating? previous, StudioClipPreferenceRating current)
    {
        var signal = current switch
        {
            StudioClipPreferenceRating.Like => TasteSignal.Like,
            StudioClipPreferenceRating.Neutral => TasteSignal.Neutral,
            StudioClipPreferenceRating.Dislike => TasteSignal.Dislike,
            _ => throw new ArgumentOutOfRangeException(nameof(current)),
        };
        learning.Observe(TasteClipFactory.FromAsset(asset), signal);
    }
}
