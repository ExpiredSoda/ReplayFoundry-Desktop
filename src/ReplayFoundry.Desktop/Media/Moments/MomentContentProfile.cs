namespace ReplayFoundry.Desktop.Media.Moments;

// Multiple labels may describe one moment. Missing review is not a negative classification.
public sealed record MomentContentProfile(bool Gameplay = false, bool Commentary = false,
    bool Funny = false, bool VisualReviewCompleted = false)
{
    public bool HasLabels => Gameplay || Commentary || Funny;
}
