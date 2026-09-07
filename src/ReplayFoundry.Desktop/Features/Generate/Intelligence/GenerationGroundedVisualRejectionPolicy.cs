using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationGroundedVisualRejectionPolicy
{
    // Joint corroboration for unavailable gameplay, not independent image
    // quality thresholds. Dark action, static readable scenes, and a missing
    // visible payoff remain provisionally eligible when either signal is absent.
    // The final completed editorial verdict is applied by GenerationReviewedSelectionPolicy.
    internal const double PredominantlyBlackRatio = 0.5;
    internal const double NearStaticRatio = 0.8;

    internal static bool HasCorroboratedUnavailableGameplay(
        GenerationVisualSemanticCandidateObservation reviewed,
        GenerationSetupOptions setup)
    {
        VisualSemanticEditorialObservation observation = reviewed.Observation;
        var candidate = reviewed.Candidate;
        if (!GenerationCaptureContextPolicy.MayScreen(setup.DiscoveryIntent, setup.ContentEmphasis) ||
            !HasCompleteUnambiguousVisualRejection(reviewed) ||
            observation.RejectReason != VisualSemanticEditorialRejectReason.NoObservablePayoff ||
            observation.HasObservablePayoff != VisualSemanticTernary.No ||
            candidate.GameplayBlackOverlapRatio < PredominantlyBlackRatio ||
            candidate.GameplayFreezeOverlapRatio < NearStaticRatio)
            return false;

        TimeSpan midpoint = candidate.Window.Start + TimeSpan.FromTicks(candidate.Window.Duration.Ticks / 2);
        var layout = reviewed.Source.CompositionPlan.Plan.GetLayoutAt(midpoint);
        var gameplay = CompositionRegionSelector.FindPrimary(layout, CompositionRegionRole.Gameplay);
        if (gameplay?.RoleSource != CompositionValueSource.UserConfirmed ||
            gameplay.GeometrySource != CompositionValueSource.UserConfirmed)
            return false;

        string sourcePath = reviewed.Source.PreparedSource.Media.FullPath;
        return !setup.MomentGuidance.ForSource(sourcePath).Any(guidance =>
            guidance.Kind == UserMomentGuidanceKind.PriorityPoint
                ? candidate.Window.Contains(guidance.Timestamp)
                : candidate.Window.Start < guidance.End && candidate.Window.End > guidance.Start);
    }

    public static bool ShouldExcludeAutomatically(
        GenerationCandidateRefinement refinement,
        GenerationVisualSemanticCandidateObservation reviewed)
    {
        VisualSemanticEditorialObservation observation = reviewed.Observation;
        if (!HasCompleteUnambiguousVisualRejection(reviewed))
        {
            return false;
        }

        // A picture-only observation cannot disqualify a creator's spoken
        // story. Likewise, absence of a visible payoff is not proof that an
        // event is worthless. Only affirmative, grounded rejection facts gate
        // this refinement stage; uncertain observations remain ranking signals.
        bool hasSpeech = refinement.Components.Any(static component =>
            component.Code == GenerationCandidateRefinementComponentCode.SpeechCoverage &&
            component.RawValue > 0);
        if (observation.TranscriptContextSupport == VisualSemanticTranscriptContextSupport.Supports ||
            hasSpeech && observation.TranscriptContextSupport is
                VisualSemanticTranscriptContextSupport.NotSupplied or
                VisualSemanticTranscriptContextSupport.UnreliableOrAmbiguous)
        {
            return false;
        }

        return observation.RejectReason switch
        {
            VisualSemanticEditorialRejectReason.RoutineTraversal or
            VisualSemanticEditorialRejectReason.MenuOrInventoryOnly =>
                observation.RoutineTraversalOrMenuOnly == VisualSemanticTernary.Yes &&
                observation.HasDistinctEvent == VisualSemanticTernary.No,
            VisualSemanticEditorialRejectReason.AmbientChangeOnly =>
                observation.CandidateContainsOnlyAmbientChange == VisualSemanticTernary.Yes &&
                observation.HasDistinctEvent == VisualSemanticTernary.No,
            VisualSemanticEditorialRejectReason.MissingRequiredContext =>
                observation.CandidateRequiresMissingContext == VisualSemanticTernary.Yes,
            _ => false,
        };
    }

    private static bool HasCompleteUnambiguousVisualRejection(
        GenerationVisualSemanticCandidateObservation reviewed) =>
        reviewed.Observation.EditorialDisposition == VisualSemanticEditorialDisposition.Reject &&
        reviewed.ReviewedSourceStart <= reviewed.Candidate.Window.Start &&
        reviewed.ReviewedSourceEnd >= reviewed.Candidate.Window.End &&
        reviewed.Observation.UncertaintyReasons.Count == 0 &&
        reviewed.Observation.EvidenceIntervals.Any(static interval =>
            interval.EvidenceBasis is VisualSemanticEvidenceBasis.Visual or VisualSemanticEvidenceBasis.Both);
}
