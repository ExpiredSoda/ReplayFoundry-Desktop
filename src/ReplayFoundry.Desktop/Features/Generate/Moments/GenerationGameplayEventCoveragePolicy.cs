using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Moments;

internal static class GenerationGameplayEventCoveragePolicy
{
    internal const double MaximumRankingTradeoff = 4;
    internal const double MinimumStrength = 0.55;
    internal const double MinimumBoundaryStrength = 0.40;

    internal static bool IsDeterministicGameplayEvent(
        MomentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        double onset = Component(
            candidate,
            MomentScoreComponentCode.GameplayOnset);
        double sceneChange = Component(
            candidate,
            MomentScoreComponentCode.GameplaySceneChange);
        return Strength(candidate) >= MinimumStrength &&
               Math.Max(onset, sceneChange) >= MinimumBoundaryStrength;
    }

    internal static double Strength(MomentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Math.Clamp(
            Component(candidate, MomentScoreComponentCode.GameplayProminence) * 0.30 +
            Component(candidate, MomentScoreComponentCode.GameplayBurstIntegration) * 0.25 +
            Component(candidate, MomentScoreComponentCode.GameplayOnset) * 0.20 +
            Component(candidate, MomentScoreComponentCode.GameplaySceneChange) * 0.10 +
            Component(candidate, MomentScoreComponentCode.GameplaySceneDensity) * 0.05 +
            Component(candidate, MomentScoreComponentCode.IndependentFamilyAgreement) * 0.10 -
            Component(candidate, MomentScoreComponentCode.ContinuousUniformityPenalty) * 0.20,
            0,
            1);
    }

    internal static bool HasVisualReview(
        GenerationCandidateRefinement? refinement) =>
        refinement?.Components.Any(static component =>
            component.Code ==
                GenerationCandidateRefinementComponentCode
                    .VisualSemanticActionEvidence) == true;

    internal static bool HasQualifiedVisualAction(
        GenerationCandidateRefinement? refinement) =>
        refinement?.Components.Any(static component =>
            component.Code ==
                GenerationCandidateRefinementComponentCode
                    .VisualSemanticActionEvidence &&
            component.RawValue > 0.5) == true;

    private static double Component(
        MomentCandidate candidate,
        MomentScoreComponentCode code) =>
        candidate.Score.Components
            .SingleOrDefault(component => component.Code == code)
            ?.NormalizedValue ?? 0;
}
