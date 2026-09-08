using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

public static class GenerationMomentContentClassifier
{
    public static MomentContentProfile Classify(MomentCandidate candidate,
        IReadOnlyList<ClipPreferenceFeature> features, GenerationVisualSemanticCandidateObservation? review,
        GenerationCandidateRefinement? refinement = null)
    {
        bool Has(GenerationCandidateRefinementComponentCode code, double minimum) => refinement?.Components.Any(item =>
            item.Code == code && item.RawValue >= minimum) == true;
        bool indexed = Has(GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, .8);
        bool gameplay = indexed && Has(GenerationCandidateRefinementComponentCode.NeuralGameplay, .3);
        bool funny = indexed && Has(GenerationCandidateRefinementComponentCode.NeuralHumor, .3);
        // Track assignment describes routing, not who spoke or what the speech means.
        bool commentary = indexed && Has(GenerationCandidateRefinementComponentCode.NeuralCommentary, .3);
        bool lore = indexed && Has(GenerationCandidateRefinementComponentCode.NeuralLore, .3);
        if (review is null || !ReferenceEquals(review.Candidate, candidate) ||
            review.ReviewedSourceStart > candidate.Window.Start || review.ReviewedSourceEnd < candidate.Window.End ||
            review.Observation.UncertaintyReasons.Count > 0 || review.Observation.EvidenceIntervals.Count == 0 ||
            review.Observation.EditorialDisposition != VisualSemanticEditorialDisposition.Keep)
            return new(Gameplay: gameplay, Commentary: commentary, Funny: funny, RecordingIndexCompleted: indexed, Lore: lore);

        VisualSemanticObservableContentType type = review.Observation.ObservableContentType;
        // The recording classifier asks about gameplay specifically. A scene's
        // broader Action label can also describe scripted character movements.
        return new(Gameplay: indexed ? gameplay : type == VisualSemanticObservableContentType.Action, Commentary: commentary,
            Funny: funny || type == VisualSemanticObservableContentType.Humor,
            VisualReviewCompleted: type != VisualSemanticObservableContentType.Unknown, RecordingIndexCompleted: indexed, Lore: lore);
    }
}
