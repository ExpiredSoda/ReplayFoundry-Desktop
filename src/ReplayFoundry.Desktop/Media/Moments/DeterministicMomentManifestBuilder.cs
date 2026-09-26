using System.Diagnostics;
using System.IO;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class DeterministicMomentManifestBuilder
{
    internal static MediaMomentFindingManifest Build(
        MediaMomentFinderIdentity identity,
        MediaMomentFindingRequest request,
        IReadOnlyList<MomentAnchor> anchors,
        IReadOnlyList<MomentEventNeighborhood> neighborhoods,
        IReadOnlyList<MomentCandidate> candidates,
        IReadOnlyList<MomentEventEpisode> episodes,
        TimeSpan elapsed)
    {
        IEnumerable<KeyValuePair<MomentAnchorKind, int>>
            anchorCounts =
            Enum.GetValues<MomentAnchorKind>()
                .Select(
                    kind =>
                        new KeyValuePair<MomentAnchorKind, int>(
                            kind,
                            anchors.Count(
                                anchor =>
                                    anchor.Kind == kind)));

        return new MediaMomentFindingManifest(
            identity,
            DateTimeOffset.UtcNow,
            request.Media.FullPath,
            request.Media.Container.Duration,
            request.Options,
            request.Evidence.Manifest.AnalyzerName,
            request.Evidence.Manifest.AnalyzerVersion,
            request.Evidence.Manifest.SignalSchemaVersion,
            request.Evidence.Manifest.Options.VisualSignalSampleInterval,
            request.Evidence.Manifest.Options.AudioSignalWindowDuration,
            request.Evidence.Manifest.RequestedIncludedRegionRoles,
            request.Composition.Manifest.SchemaVersion,
            request.Composition.Manifest.CoordinateSpaceVersion,
            request.Composition.Manifest.Origin,
            anchorCounts,
            candidates.Count,
            candidates.Count(
                static candidate =>
                    candidate.Disposition is
                        MomentCandidateDisposition.RejectedBlack or
                        MomentCandidateDisposition.RejectedFreeze),
            candidates.Count(
                static candidate =>
                    candidate.Disposition ==
                    MomentCandidateDisposition.BelowThreshold),
            candidates.Count(
                static candidate =>
                    candidate.Disposition ==
                    MomentCandidateDisposition.SuppressedOverlap),
            candidates.Count(
                static candidate =>
                    candidate.Disposition ==
                        MomentCandidateDisposition.Selected),
            elapsed,
            "Full-timeline deterministic evidence; confirmed Gameplay/Presenter regions and global per-stream audio only. Activation, episode, and score outputs are heuristic—not probability or semantic understanding.",
            neighborhoods.Count,
            candidates.Count(
                static candidate =>
                    candidate.Disposition ==
                        MomentCandidateDisposition.SuppressedNeighborhood),
            policyHash: null,
            episodeCount: episodes.Count,
            episodeSuppressedCount:
                candidates.Count(
                    static candidate =>
                        candidate.Disposition is
                            MomentCandidateDisposition.SuppressedEpisode or
                            MomentCandidateDisposition.SuppressedSubepisode or
                            MomentCandidateDisposition.SuppressedCooldown));
    }
}
