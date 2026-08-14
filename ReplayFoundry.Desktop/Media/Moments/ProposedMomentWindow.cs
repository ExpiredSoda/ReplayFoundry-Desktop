namespace ReplayFoundry.Desktop.Media.Moments;

internal sealed record ProposedMomentWindow(
    MomentCandidateWindow Window,
    MomentCandidateConstructionReason Reason,
    MomentEventNeighborhood Neighborhood,
    IReadOnlyList<MomentAnchor> Anchors,
    MomentEventEpisode? Episode = null,
    MomentWindowContextAllocation? ContextAllocation = null,
    MontageSegmentObjective? MontageObjective = null,
    MontageSegmentSelectionReason? MontageSelectionReason = null);
