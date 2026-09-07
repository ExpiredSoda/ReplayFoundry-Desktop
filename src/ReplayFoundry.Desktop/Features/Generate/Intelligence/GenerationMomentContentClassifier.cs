using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public static class GenerationMomentContentClassifier
{
    public static MomentContentProfile Classify(MomentCandidate candidate,
        IReadOnlyList<ClipPreferenceFeature> features, GenerationVisualSemanticCandidateObservation? review)
    {
        bool commentary = features.Any(feature => feature.Code == ClipPreferenceFeatureCode.CreatorSpeech && feature.NormalizedValue >= 0.15);
        if (review is null || !ReferenceEquals(review.Candidate, candidate) ||
            review.ReviewedSourceStart > candidate.Window.Start || review.ReviewedSourceEnd < candidate.Window.End ||
            review.Observation.UncertaintyReasons.Count > 0 || review.Observation.EvidenceIntervals.Count == 0 ||
            review.Observation.EditorialDisposition != VisualSemanticEditorialDisposition.Keep)
            return new(Commentary: commentary);

        VisualSemanticObservableContentType type = review.Observation.ObservableContentType;
        return new(Gameplay: type == VisualSemanticObservableContentType.Action, Commentary: commentary,
            Funny: type == VisualSemanticObservableContentType.Humor,
            VisualReviewCompleted: type != VisualSemanticObservableContentType.Unknown);
    }
}
