using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class DeterministicMomentWarnings
{
    internal static List<MomentFindingWarning>
        BuildInputWarnings(
            MediaMomentFindingRequest request)
    {
        var warnings =
            new List<MomentFindingWarning>();

        bool hasGameplayActivity =
            request.Evidence.RegionVisualResults
                .Where(
                    static result =>
                        result.Target.Role ==
                        CompositionRegionRole.Gameplay)
                .SelectMany(
                    static result =>
                        result.SignalSamples)
                .Any(
                    static sample =>
                        sample.NormalizedActivity is not null);

        if (!hasGameplayActivity)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.NoGameplayActivityEvidence,
                    "No finite Gameplay visual-activity samples are available."));
        }

        bool hasGameplayScenes =
            request.Evidence.RegionVisualResults
                .Where(
                    static result =>
                        result.Target.Role ==
                        CompositionRegionRole.Gameplay)
                .Any(
                    static result =>
                        result.SceneBoundaries.Count > 0);

        if (!hasGameplayScenes)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.NoGameplaySceneEvidence,
                    "No Gameplay-region scene boundaries are available."));
        }

        if (request.Evidence.AudioSignalCoverages.Count == 0)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.NoAudioStreams,
                    "No audio streams are available; audio activity contributes zero."));
        }

        bool hasPresenter =
            request.Evidence.RegionVisualResults.Any(
                static result =>
                    result.Target.Role ==
                    CompositionRegionRole.Presenter);

        if (!hasPresenter)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.NoPresenterEvidence,
                    "No confirmed Presenter target is available; the finder continues with Gameplay and audio evidence."));
        }

        if (request.Options.ContentEmphasis ==
            MomentContentEmphasis.CommentaryFocused)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.CommentarySemanticEvidenceUnavailable,
                    "Commentary-focused scoring is a policy combining confirmed Presenter-region activity with non-semantic audio energy; it does not detect speech, emotion, or reactions."));
        }

        if (request.Media.Container.Duration <
            request.Options.MinimumDuration)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.SourceShorterThanMinimumWindow,
                    "The source is shorter than the configured minimum window, so the complete valid source is used."));
        }

        if (request.Options.OutputKind ==
            MomentOutputKind.MontageSegment)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.MontageSequencingNotImplemented,
                    "Montage mode returns ranked short segments only; sequencing and transition compatibility are not implemented."));
        }

        bool sparseVisual =
            request.Evidence.Manifest.VisualSignalCoverages.Any(
                static coverage =>
                    !coverage.TargetIntervalTraversed);
        bool sparseAudio =
            request.Evidence.Manifest.AudioSignalCoverages.Any(
                static coverage =>
                    !coverage.SourceTimelineTraversed);

        if (sparseVisual || sparseAudio)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.SparseSignalCoverage,
                    "One or more deterministic signal streams did not traverse their complete intended timeline."));
        }

        return warnings;
    }

    internal static MomentCandidateDisposition
        GetInitialDisposition(
            MediaMomentFindingOptions options,
            ScoredMomentProposal proposal)
    {
        if (proposal.FullFrameBlackRatio >=
            options.FullFrameBlackHardRejectionRatio)
        {
            return MomentCandidateDisposition.RejectedBlack;
        }

        if (proposal.FullFrameFreezeRatio >=
            options.FullFrameFreezeHardRejectionRatio)
        {
            return MomentCandidateDisposition.RejectedFreeze;
        }

        return proposal.Score.HeuristicScore <
            options.MinimumHeuristicScore
                ? MomentCandidateDisposition.BelowThreshold
                : MomentCandidateDisposition.Eligible;
    }
}
