using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Features.Generate.Enrichment;

public enum MomentEnrichmentProposalSource
{
    SelectedCandidates,
    EligibleTopN,
    CompleteProposalPool,
}

public sealed class MomentEnrichmentCandidateSnapshot
{
    public MomentEnrichmentCandidateSnapshot(
        string candidateId,
        TimeSpan start,
        TimeSpan end,
        TimeSpan sourceDuration,
        double heuristicScore,
        MomentCandidateDisposition disposition,
        bool isSelected,
        int sourceOrder)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new ArgumentException(
                "An enrichment candidate requires a stable identifier.",
                nameof(candidateId));
        }

        if (sourceDuration <= TimeSpan.Zero ||
            start < TimeSpan.Zero ||
            end <= start ||
            end > sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                "Candidate windows must remain inside the source.");
        }

        if (!double.IsFinite(heuristicScore) ||
            heuristicScore is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heuristicScore));
        }

        if (!Enum.IsDefined(disposition) ||
            sourceOrder < 0 ||
            isSelected &&
            disposition != MomentCandidateDisposition.Selected)
        {
            throw new ArgumentException(
                "Candidate disposition, selection, and source order must be consistent.");
        }

        CandidateId = candidateId.Trim();
        Start = start;
        End = end;
        SourceDuration = sourceDuration;
        HeuristicScore = heuristicScore;
        Disposition = disposition;
        IsSelected = isSelected;
        SourceOrder = sourceOrder;
    }

    public string CandidateId { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public TimeSpan SourceDuration { get; }

    public double HeuristicScore { get; }

    public MomentCandidateDisposition Disposition { get; }

    public bool IsSelected { get; }

    public int SourceOrder { get; }
}

public sealed class MomentEnrichmentOptions
{
    public const string CurrentPolicyVersion = "0.1";

    public MomentEnrichmentOptions(
        MomentEnrichmentProposalSource proposalSource,
        int maximumCandidateCount,
        TimeSpan contextBefore,
        TimeSpan contextAfter,
        TimeSpan maximumMergeGap,
        TimeSpan maximumNeighborhoodDuration,
        bool includeBelowThresholdProposals = false,
        bool allowCompleteProposalPool = false,
        string policyVersion = CurrentPolicyVersion)
    {
        if (!Enum.IsDefined(proposalSource))
        {
            throw new ArgumentOutOfRangeException(
                nameof(proposalSource));
        }

        if (maximumCandidateCount <= 0 ||
            contextBefore < TimeSpan.Zero ||
            contextAfter < TimeSpan.Zero ||
            maximumMergeGap < TimeSpan.Zero ||
            maximumNeighborhoodDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCandidateCount));
        }

        if (proposalSource ==
                MomentEnrichmentProposalSource.CompleteProposalPool &&
            !allowCompleteProposalPool)
        {
            throw new ArgumentException(
                "Complete proposal-pool enrichment is developer-only.",
                nameof(allowCompleteProposalPool));
        }

        if (includeBelowThresholdProposals &&
            proposalSource !=
            MomentEnrichmentProposalSource.CompleteProposalPool)
        {
            throw new ArgumentException(
                "Below-threshold proposals may be included only in the developer-only complete pool.",
                nameof(includeBelowThresholdProposals));
        }

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "Enrichment options require a policy version.",
                nameof(policyVersion));
        }

        ProposalSource = proposalSource;
        MaximumCandidateCount = maximumCandidateCount;
        ContextBefore = contextBefore;
        ContextAfter = contextAfter;
        MaximumMergeGap = maximumMergeGap;
        MaximumNeighborhoodDuration =
            maximumNeighborhoodDuration;
        IncludeBelowThresholdProposals =
            includeBelowThresholdProposals;
        AllowCompleteProposalPool = allowCompleteProposalPool;
        PolicyVersion = policyVersion.Trim();
    }

    public MomentEnrichmentProposalSource ProposalSource { get; }

    public int MaximumCandidateCount { get; }

    public TimeSpan ContextBefore { get; }

    public TimeSpan ContextAfter { get; }

    public TimeSpan MaximumMergeGap { get; }

    public TimeSpan MaximumNeighborhoodDuration { get; }

    public bool IncludeBelowThresholdProposals { get; }

    public bool AllowCompleteProposalPool { get; }

    public string PolicyVersion { get; }

    public static MomentEnrichmentOptions CreateResearchDefaults() =>
        new(
            MomentEnrichmentProposalSource.SelectedCandidates,
            maximumCandidateCount: 10,
            contextBefore: TimeSpan.FromSeconds(8),
            contextAfter: TimeSpan.FromSeconds(8),
            maximumMergeGap: TimeSpan.FromSeconds(2),
            maximumNeighborhoodDuration: TimeSpan.FromMinutes(2));
}

public sealed class MomentEnrichmentRequest
{
    private readonly ReadOnlyCollection<MomentEnrichmentCandidateSnapshot>
        _candidates;

    public MomentEnrichmentRequest(
        MediaMomentFindingResult moments,
        int absoluteAudioStreamIndex,
        MomentEnrichmentOptions options)
        : this(
            moments?.Request.Media.FullPath ??
                throw new ArgumentNullException(nameof(moments)),
            moments.Request.Media.Duration,
            moments.Manifest.FinderIdentity.Name,
            moments.Manifest.FinderIdentity.Version,
            moments.Manifest.PolicyHash,
            moments.Proposals.Select(
                (candidate, index) =>
                    new MomentEnrichmentCandidateSnapshot(
                        candidate.Id,
                        candidate.Window.Start,
                        candidate.Window.End,
                        candidate.Window.SourceDuration,
                        candidate.HeuristicScore,
                        candidate.Disposition,
                        moments.SelectedCandidates.Contains(candidate),
                        index)),
            absoluteAudioStreamIndex,
            options)
    {
    }

    public MomentEnrichmentRequest(
        string sourcePath,
        TimeSpan sourceDuration,
        string finderName,
        string finderVersion,
        string policyHash,
        IEnumerable<MomentEnrichmentCandidateSnapshot> candidates,
        int absoluteAudioStreamIndex,
        MomentEnrichmentOptions options)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "Enrichment requires a fully qualified source path.",
                nameof(sourcePath));
        }

        if (sourceDuration <= TimeSpan.Zero ||
            absoluteAudioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration));
        }

        if (string.IsNullOrWhiteSpace(finderName) ||
            string.IsNullOrWhiteSpace(finderVersion))
        {
            throw new ArgumentException(
                "Enrichment requires the imported deterministic finder identity.");
        }

        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        MomentEnrichmentCandidateSnapshot[] snapshot =
            candidates.ToArray();

        if (snapshot.Length == 0 ||
            snapshot.Any(static candidate => candidate is null) ||
            snapshot.Any(
                candidate =>
                    candidate.SourceDuration != sourceDuration) ||
            snapshot.GroupBy(
                    static candidate =>
                        candidate.CandidateId,
                    StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            snapshot
                .Select(static candidate => candidate.SourceOrder)
                .Distinct()
                .Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Enrichment candidates must be nonempty, unique, and bound to the source.",
                nameof(candidates));
        }

        SourcePath = Path.GetFullPath(sourcePath);
        SourceDuration = sourceDuration;
        FinderName = finderName.Trim();
        FinderVersion = finderVersion.Trim();
        PolicyHash =
            string.IsNullOrWhiteSpace(policyHash)
                ? throw new ArgumentException(
                    "Enrichment requires the deterministic policy hash.",
                    nameof(policyHash))
                : policyHash.Trim();
        AbsoluteAudioStreamIndex = absoluteAudioStreamIndex;
        Options = options;
        _candidates =
            Array.AsReadOnly(
                snapshot
                    .OrderBy(static candidate => candidate.SourceOrder)
                    .ToArray());
    }

    public string SourcePath { get; }

    public TimeSpan SourceDuration { get; }

    public string FinderName { get; }

    public string FinderVersion { get; }

    public string PolicyHash { get; }

    public IReadOnlyList<MomentEnrichmentCandidateSnapshot>
        Candidates =>
        _candidates;

    public int AbsoluteAudioStreamIndex { get; }

    public MomentEnrichmentOptions Options { get; }
}

public sealed record CandidateNeighborhoodMembership
{
    public CandidateNeighborhoodMembership(
        string candidateId,
        TimeSpan candidateStart,
        TimeSpan candidateEnd,
        int candidateSourceOrder)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            candidateStart < TimeSpan.Zero ||
            candidateEnd <= candidateStart ||
            candidateSourceOrder < 0)
        {
            throw new ArgumentException(
                "Neighborhood membership requires a valid candidate identity, window, and source order.");
        }

        CandidateId = candidateId.Trim();
        CandidateStart = candidateStart;
        CandidateEnd = candidateEnd;
        CandidateSourceOrder = candidateSourceOrder;
    }

    public string CandidateId { get; }

    public TimeSpan CandidateStart { get; }

    public TimeSpan CandidateEnd { get; }

    public int CandidateSourceOrder { get; }
}

public sealed class CandidateNeighborhood
{
    private readonly ReadOnlyCollection<CandidateNeighborhoodMembership>
        _memberships;

    public CandidateNeighborhood(
        string id,
        TimeSpan start,
        TimeSpan end,
        TimeSpan sourceDuration,
        IEnumerable<CandidateNeighborhoodMembership> memberships)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            sourceDuration <= TimeSpan.Zero ||
            start < TimeSpan.Zero ||
            end <= start ||
            end > sourceDuration)
        {
            throw new ArgumentException(
                "A neighborhood requires a stable identity and bounded source interval.");
        }

        ArgumentNullException.ThrowIfNull(memberships);

        CandidateNeighborhoodMembership[] snapshot =
            memberships
                .OrderBy(static item => item.CandidateSourceOrder)
                .ToArray();

        if (snapshot.Length == 0 ||
            snapshot.Any(static item => item is null) ||
            snapshot.GroupBy(
                    static item =>
                        item.CandidateId,
                    StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            snapshot.Any(
                item =>
                    item.CandidateStart < start ||
                    item.CandidateEnd > end))
        {
            throw new ArgumentException(
                "Neighborhood memberships must be unique and contained by the neighborhood.",
                nameof(memberships));
        }

        Id = id.Trim();
        Start = start;
        End = end;
        SourceDuration = sourceDuration;
        _memberships = Array.AsReadOnly(snapshot);
    }

    public string Id { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public TimeSpan SourceDuration { get; }

    public IReadOnlyList<CandidateNeighborhoodMembership>
        Memberships =>
        _memberships;
}

public sealed class CandidateNeighborhoodPlan
{
    private readonly ReadOnlyCollection<CandidateNeighborhood>
        _neighborhoods;

    public CandidateNeighborhoodPlan(
        MomentEnrichmentRequest request,
        IEnumerable<CandidateNeighborhood> neighborhoods)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(neighborhoods);

        CandidateNeighborhood[] snapshot =
            neighborhoods
                .OrderBy(static item => item.Start)
                .ThenBy(static item => item.Id, StringComparer.Ordinal)
                .ToArray();

        if (snapshot.Any(static item => item is null) ||
            snapshot.GroupBy(
                    static item =>
                        item.Id,
                    StringComparer.Ordinal)
                .Any(static group => group.Count() > 1) ||
            snapshot.Zip(
                    snapshot.Skip(1),
                    static (left, right) =>
                        right.Start < left.End)
                .Any(static overlaps => overlaps))
        {
            throw new ArgumentException(
                "Neighborhood plans must be unique, ordered, and non-overlapping.",
                nameof(neighborhoods));
        }

        string[] membershipIds =
            snapshot
                .SelectMany(static item => item.Memberships)
                .Select(static item => item.CandidateId)
                .ToArray();

        if (membershipIds.Distinct(StringComparer.Ordinal).Count() !=
            membershipIds.Length)
        {
            throw new ArgumentException(
                "Each candidate may belong to exactly one neighborhood.",
                nameof(neighborhoods));
        }

        Request = request;
        _neighborhoods = Array.AsReadOnly(snapshot);
    }

    public MomentEnrichmentRequest Request { get; }

    public IReadOnlyList<CandidateNeighborhood> Neighborhoods =>
        _neighborhoods;

    public int CandidateCount =>
        _neighborhoods.Sum(
            static item =>
                item.Memberships.Count);
}
