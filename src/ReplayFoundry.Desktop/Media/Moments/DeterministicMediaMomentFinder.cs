using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class DeterministicMediaMomentFinder :
    IMediaMomentFinder
{
    public static readonly MediaMomentFinderIdentity
        CurrentIdentity =
        new(
            "ReplayFoundry.DeterministicMomentFinder",
            "1.4.0");

    public MediaMomentFinderIdentity Identity =>
        CurrentIdentity;

    public MediaMomentFindingResult Find(
        MediaMomentFindingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch =
            Stopwatch.StartNew();
        var warnings =
            DeterministicMomentWarnings.BuildInputWarnings(
                request);

        NormalizedMomentSignals signals =
            MomentSignalNormalizer.Normalize(
                request,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MomentAnchor> anchors =
            MomentAnchorBuilder.Build(
                request,
                signals,
                cancellationToken);

        if (anchors.Count == 0)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.NoCandidateAnchors,
                    "No deterministic Gameplay, scene, audio, or supported Presenter/audio anchors were found."));
        }

        MomentActivationSeries activation =
            MomentActivationCurveBuilder.Build(
                request,
                signals,
                anchors,
                cancellationToken);
        IReadOnlyList<MomentEventEpisode> episodes =
            MomentEventEpisodeDetector.Detect(
                request,
                activation,
                anchors,
                signals,
                cancellationToken);
        IReadOnlyList<ProposedMomentWindow> windows =
            episodes
                .Select(
                    episode =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return request.Options.OutputKind ==
                               MomentOutputKind.StandaloneClip
                            ? StandaloneMomentWindowShaper.Shape(
                                request,
                                episode,
                                anchors)
                            : MontageMomentSegmentShaper.Shape(
                                request,
                                episode,
                                activation,
                                anchors,
                                cancellationToken);
                    })
                .OrderBy(static proposal => proposal.Window.Start)
                .ThenBy(static proposal => proposal.Window.End)
                .ThenBy(static proposal => proposal.Episode!.Id, StringComparer.Ordinal)
                .ToArray();
        IReadOnlyList<MomentEventNeighborhood> neighborhoods =
            windows
                .Select(static item => item.Neighborhood)
                .ToArray();

        var candidates =
            new List<MomentCandidate>(
                windows.Count);

        foreach (ProposedMomentWindow window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScoredMomentProposal scored =
                MomentScoreCalculator.Score(
                    request,
                    signals,
                    activation,
                    window,
                    cancellationToken);

            MomentCandidateDisposition disposition =
                DeterministicMomentWarnings.GetInitialDisposition(
                    request.Options,
                    scored);

            string id =
                MomentStableId.Create(
                    "m",
                    Path.GetFullPath(request.Media.FullPath)
                        .ToUpperInvariant(),
                    request.Options.OutputKind,
                    window.Window.Start,
                    window.Window.End,
                    string.Join(
                        "|",
                        window.Anchors
                            .Select(static anchor => anchor.Id)
                            .OrderBy(static value => value, StringComparer.Ordinal)),
                    window.Episode!.Id);

            candidates.Add(
                new MomentCandidate(
                    id,
                    window.Window,
                    window.Reason,
                    window.Neighborhood,
                    window.Anchors,
                    scored.Score,
                    disposition,
                    scored.FullFrameBlackRatio,
                    scored.FullFrameFreezeRatio,
                    scored.GameplayBlackRatio,
                    scored.GameplayFreezeRatio,
                    scored.IntegrityReferences,
                    window.Episode,
                    window.ContextAllocation,
                    window.MontageObjective,
                    window.MontageSelectionReason,
                    scored.EpisodeFeatures,
                    scored.StandaloneFeatures,
                    scored.MontageFeatures));

            if (disposition ==
                MomentCandidateDisposition.RejectedBlack)
            {
                warnings.Add(
                    new MomentFindingWarning(
                        MomentFindingWarningCode.CandidateRejectedForBlackOverlap,
                        $"Candidate {id} was rejected because full-frame black overlap reached the configured capture-integrity limit.",
                        id));
            }
            else if (disposition ==
                     MomentCandidateDisposition.RejectedFreeze)
            {
                warnings.Add(
                    new MomentFindingWarning(
                        MomentFindingWarningCode.CandidateRejectedForFreezeOverlap,
                        $"Candidate {id} was rejected because full-frame freeze overlap reached the configured capture-integrity limit.",
                        id));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        MomentCandidate[] selected =
            DeterministicMomentSelection.Select(
                candidates,
                request.Options,
                cancellationToken);

        if (candidates.Count > 0 &&
            candidates.All(
                static candidate =>
                    candidate.Disposition ==
                    MomentCandidateDisposition.BelowThreshold))
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.AllCandidatesBelowThreshold,
                    "Every proposal scored below the configured heuristic threshold; the threshold was not lowered."));
        }
        if (selected.Length <
            request.Options.DesiredCandidateCount)
        {
            warnings.Add(
                new MomentFindingWarning(
                    MomentFindingWarningCode.DesiredResultCountNotMet,
                    $"Selected {selected.Length} of the requested {request.Options.DesiredCandidateCount} candidates without lowering the threshold."));
        }

        stopwatch.Stop();

        MediaMomentFindingManifest manifest =
            DeterministicMomentManifestBuilder.Build(
                Identity,
                request,
                anchors,
                neighborhoods,
                candidates,
                episodes,
                stopwatch.Elapsed);

        return new MediaMomentFindingResult(
            request,
            candidates,
            selected,
            warnings,
            manifest,
            activation,
            episodes);
    }
}
