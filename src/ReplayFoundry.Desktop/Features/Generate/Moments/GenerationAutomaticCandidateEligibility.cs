using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

internal static class GenerationAutomaticCandidateEligibility
{
    public static bool IsEligible(MomentCandidate candidate,
        GenerationCandidateRefinement? refinement) =>
        candidate.Disposition is not (MomentCandidateDisposition.RejectedBlack or
            MomentCandidateDisposition.RejectedFreeze) &&
        refinement?.HasIncompleteSpeechEnding != true &&
        refinement?.HasIncompleteSpeechBeginning != true &&
        refinement?.HasGroundedVisualRejection != true &&
        refinement?.HasNonGameplayCapture != true &&
        refinement?.HasApplicationStartupLeadIn != true &&
        (candidate.ConstructionReason != MomentCandidateConstructionReason.SemanticExploration ||
            refinement?.RequiresSemanticReview == false);
}
