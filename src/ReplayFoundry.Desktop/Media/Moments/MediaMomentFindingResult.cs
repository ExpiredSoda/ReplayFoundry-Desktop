using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MediaMomentFindingResult
{
    private readonly ReadOnlyCollection<MomentCandidate> _proposals;
    private readonly ReadOnlyCollection<MomentCandidate> _selectedCandidates;
    private readonly ReadOnlyCollection<MomentFindingWarning> _warnings;
    private readonly ReadOnlyCollection<MomentEventEpisode> _episodes;

    public MediaMomentFindingResult(
        MediaMomentFindingRequest request,
        IEnumerable<MomentCandidate> proposals,
        IEnumerable<MomentCandidate> selectedCandidates,
        IEnumerable<MomentFindingWarning> warnings,
        MediaMomentFindingManifest manifest,
        MomentActivationSeries? activationSeries = null,
        IEnumerable<MomentEventEpisode>? episodes = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(selectedCandidates);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(manifest);

        MomentCandidate[] proposalSnapshot =
            proposals.ToArray();
        MomentCandidate[] selectedSnapshot =
            selectedCandidates.ToArray();
        MomentFindingWarning[] warningSnapshot =
            warnings.ToArray();
        MomentEventEpisode[] episodeSnapshot =
            episodes?.OrderBy(static item => item.Start)
                .ThenBy(static item => item.Id, StringComparer.Ordinal)
                .ToArray() ?? [];

        if (proposalSnapshot.Any(static candidate => candidate is null) ||
            selectedSnapshot.Any(static candidate => candidate is null) ||
            warningSnapshot.Any(static warning => warning is null))
        {
            throw new ArgumentException(
                "Moment result collections cannot contain null entries.");
        }

        if (episodeSnapshot.Any(static item => item is null) ||
            episodeSnapshot.GroupBy(static item => item.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            proposalSnapshot.Any(candidate =>
                candidate.EpisodeId is not null &&
                !episodeSnapshot.Any(episode =>
                    ReferenceEquals(episode, candidate.Episode))))
        {
            throw new ArgumentException(
                "Episode results must be unique and own every episode-backed proposal.",
                nameof(episodes));
        }

        if (proposalSnapshot
            .GroupBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Moment proposal identifiers must be unique.",
                nameof(proposals));
        }

        string[] selectedIds =
            selectedSnapshot
                .Select(static candidate => candidate.Id)
                .ToArray();

        if (selectedIds.Distinct(StringComparer.Ordinal).Count() !=
                selectedIds.Length ||
            selectedSnapshot.Any(
                candidate =>
                    candidate.Disposition !=
                    MomentCandidateDisposition.Selected) ||
            selectedSnapshot.Any(
                selected =>
                    !proposalSnapshot.Any(
                        proposal =>
                            ReferenceEquals(
                                proposal,
                                selected))))
        {
            throw new ArgumentException(
                "Selected candidates must be unique selected entries in the complete proposal pool.",
                nameof(selectedCandidates));
        }

        if (manifest.ProposalCount != proposalSnapshot.Length ||
            manifest.SelectedCount != selectedSnapshot.Length ||
            manifest.HardRejectedCount !=
                proposalSnapshot.Count(
                    static candidate =>
                        candidate.Disposition is
                            MomentCandidateDisposition.RejectedBlack or
                            MomentCandidateDisposition.RejectedFreeze) ||
            manifest.BelowThresholdCount !=
                proposalSnapshot.Count(
                    static candidate =>
                        candidate.Disposition ==
                        MomentCandidateDisposition.BelowThreshold) ||
            manifest.OverlapSuppressedCount !=
                proposalSnapshot.Count(
                    static candidate =>
                        candidate.Disposition ==
                        MomentCandidateDisposition.SuppressedOverlap) ||
            manifest.NeighborhoodSuppressedCount !=
                proposalSnapshot.Count(
                    static candidate =>
                        candidate.Disposition ==
                        MomentCandidateDisposition.SuppressedNeighborhood) ||
            manifest.EpisodeSuppressedCount !=
                proposalSnapshot.Count(
                    static candidate =>
                        candidate.Disposition is
                            MomentCandidateDisposition.SuppressedEpisode or
                            MomentCandidateDisposition.SuppressedSubepisode or
                            MomentCandidateDisposition.SuppressedCooldown) ||
            manifest.SelectedCount !=
                proposalSnapshot.Count(
                    static candidate =>
                        candidate.Disposition ==
                        MomentCandidateDisposition.Selected) ||
            warningSnapshot.Any(
                warning =>
                    warning.CandidateId is not null &&
                    !proposalSnapshot.Any(
                        proposal =>
                            proposal.Id ==
                            warning.CandidateId)) ||
            !string.Equals(
                request.Media.FullPath,
                manifest.SourcePath,
                StringComparison.OrdinalIgnoreCase) ||
            request.Media.Container.Duration != manifest.SourceDuration)
        {
            throw new ArgumentException(
                "The moment manifest does not match the result payload.",
                nameof(manifest));
        }

        Request = request;
        Manifest = manifest;
        _proposals = Array.AsReadOnly(proposalSnapshot);
        _selectedCandidates = Array.AsReadOnly(selectedSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
        _episodes = Array.AsReadOnly(episodeSnapshot);
        ActivationSeries = activationSeries;
    }

    public MediaMomentFindingRequest Request { get; }
    public IReadOnlyList<MomentCandidate> Proposals => _proposals;
    public IReadOnlyList<MomentCandidate> SelectedCandidates => _selectedCandidates;
    public IReadOnlyList<MomentFindingWarning> Warnings => _warnings;
    public MomentActivationSeries? ActivationSeries { get; }
    public IReadOnlyList<MomentEventEpisode> Episodes => _episodes;
    public MediaMomentFindingManifest Manifest { get; }
}
