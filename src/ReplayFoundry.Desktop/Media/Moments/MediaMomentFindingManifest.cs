using System.Collections.ObjectModel;
using System.IO;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MediaMomentFindingManifest
{
    private readonly ReadOnlyDictionary<MomentAnchorKind, int>
        _anchorCounts;
    private readonly ReadOnlyCollection<CompositionRegionRole>
        _includedRoles;

    public MediaMomentFindingManifest(
        MediaMomentFinderIdentity finderIdentity,
        DateTimeOffset foundAtUtc,
        string sourcePath,
        TimeSpan sourceDuration,
        MediaMomentFindingOptions options,
        string evidenceAnalyzerName,
        string evidenceAnalyzerVersion,
        string evidenceSignalSchemaVersion,
        TimeSpan visualSampleCadence,
        TimeSpan audioWindowCadence,
        IEnumerable<CompositionRegionRole> includedRoles,
        string compositionSchemaVersion,
        string compositionCoordinateSpaceVersion,
        CompositionPlanOrigin compositionPlanOrigin,
        IEnumerable<KeyValuePair<MomentAnchorKind, int>> anchorCounts,
        int proposalCount,
        int hardRejectedCount,
        int belowThresholdCount,
        int overlapSuppressedCount,
        int selectedCount,
        TimeSpan totalElapsed,
        string deterministicCoverageStatement,
        int neighborhoodCount = 0,
        int neighborhoodSuppressedCount = 0,
        string? policyHash = null,
        int episodeCount = 0,
        int episodeSuppressedCount = 0)
    {
        ArgumentNullException.ThrowIfNull(finderIdentity);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(includedRoles);
        ArgumentNullException.ThrowIfNull(anchorCounts);

        if (foundAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The moment-finding timestamp must use UTC.",
                nameof(foundAtUtc));
        }

        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "The moment manifest requires a fully qualified source path.",
                nameof(sourcePath));
        }

        if (sourceDuration <= TimeSpan.Zero ||
            visualSampleCadence <= TimeSpan.Zero ||
            audioWindowCadence <= TimeSpan.Zero ||
            totalElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration));
        }

        ValidateText(evidenceAnalyzerName, nameof(evidenceAnalyzerName));
        ValidateText(evidenceAnalyzerVersion, nameof(evidenceAnalyzerVersion));
        ValidateText(evidenceSignalSchemaVersion, nameof(evidenceSignalSchemaVersion));
        ValidateText(compositionSchemaVersion, nameof(compositionSchemaVersion));
        ValidateText(compositionCoordinateSpaceVersion, nameof(compositionCoordinateSpaceVersion));
        ValidateText(deterministicCoverageStatement, nameof(deterministicCoverageStatement));

        if (!Enum.IsDefined(compositionPlanOrigin))
        {
            throw new ArgumentOutOfRangeException(nameof(compositionPlanOrigin));
        }

        CompositionRegionRole[] roleSnapshot =
            includedRoles.ToArray();

        if (roleSnapshot.Length == 0 ||
            roleSnapshot.Any(static role => !Enum.IsDefined(role)) ||
            roleSnapshot.Distinct().Count() != roleSnapshot.Length)
        {
            throw new ArgumentException(
                "Included roles must be a non-empty unique set of defined values.",
                nameof(includedRoles));
        }

        KeyValuePair<MomentAnchorKind, int>[] countSnapshot =
            anchorCounts.ToArray();

        if (countSnapshot.Any(
                static item =>
                    !Enum.IsDefined(item.Key) ||
                    item.Value < 0) ||
            countSnapshot.GroupBy(static item => item.Key)
                .Any(static group => group.Count() > 1) ||
            countSnapshot.Length !=
                Enum.GetValues<MomentAnchorKind>().Length)
        {
            throw new ArgumentException(
                "Anchor counts must contain every defined kind exactly once with non-negative counts.",
                nameof(anchorCounts));
        }

        ValidateCount(proposalCount, nameof(proposalCount));
        ValidateCount(hardRejectedCount, nameof(hardRejectedCount));
        ValidateCount(belowThresholdCount, nameof(belowThresholdCount));
        ValidateCount(overlapSuppressedCount, nameof(overlapSuppressedCount));
        ValidateCount(selectedCount, nameof(selectedCount));
        ValidateCount(neighborhoodCount, nameof(neighborhoodCount));
        ValidateCount(neighborhoodSuppressedCount, nameof(neighborhoodSuppressedCount));
        ValidateCount(episodeCount, nameof(episodeCount));
        ValidateCount(episodeSuppressedCount, nameof(episodeSuppressedCount));

        if (hardRejectedCount + belowThresholdCount +
            overlapSuppressedCount + neighborhoodSuppressedCount + episodeSuppressedCount +
            selectedCount > proposalCount)
        {
            throw new ArgumentException(
                "Manifest disposition counts cannot exceed the proposal count.");
        }

        FinderIdentity = finderIdentity;
        FoundAtUtc = foundAtUtc;
        SourcePath = sourcePath;
        SourceDuration = sourceDuration;
        Options = options;
        EvidenceAnalyzerName = evidenceAnalyzerName.Trim();
        EvidenceAnalyzerVersion = evidenceAnalyzerVersion.Trim();
        EvidenceSignalSchemaVersion = evidenceSignalSchemaVersion.Trim();
        VisualSampleCadence = visualSampleCadence;
        AudioWindowCadence = audioWindowCadence;
        CompositionSchemaVersion = compositionSchemaVersion.Trim();
        CompositionCoordinateSpaceVersion =
            compositionCoordinateSpaceVersion.Trim();
        CompositionPlanOrigin = compositionPlanOrigin;
        ProposalCount = proposalCount;
        HardRejectedCount = hardRejectedCount;
        BelowThresholdCount = belowThresholdCount;
        OverlapSuppressedCount = overlapSuppressedCount;
        NeighborhoodCount = neighborhoodCount;
        NeighborhoodSuppressedCount = neighborhoodSuppressedCount;
        EpisodeCount = episodeCount;
        EpisodeSuppressedCount = episodeSuppressedCount;
        SelectedCount = selectedCount;
        TotalElapsed = totalElapsed;
        DeterministicCoverageStatement =
            deterministicCoverageStatement.Trim();
        PolicyHash =
            string.IsNullOrWhiteSpace(policyHash)
                ? MomentPolicyFingerprint.Create(options)
                : policyHash.Trim();
        _includedRoles = Array.AsReadOnly(roleSnapshot);
        _anchorCounts =
            new ReadOnlyDictionary<MomentAnchorKind, int>(
                countSnapshot.ToDictionary(
                    static item => item.Key,
                    static item => item.Value));
    }

    public MediaMomentFinderIdentity FinderIdentity { get; }
    public DateTimeOffset FoundAtUtc { get; }
    public string SourcePath { get; }
    public TimeSpan SourceDuration { get; }
    public MediaMomentFindingOptions Options { get; }
    public string EvidenceAnalyzerName { get; }
    public string EvidenceAnalyzerVersion { get; }
    public string EvidenceSignalSchemaVersion { get; }
    public TimeSpan VisualSampleCadence { get; }
    public TimeSpan AudioWindowCadence { get; }
    public IReadOnlyList<CompositionRegionRole> IncludedRoles => _includedRoles;
    public string CompositionSchemaVersion { get; }
    public string CompositionCoordinateSpaceVersion { get; }
    public CompositionPlanOrigin CompositionPlanOrigin { get; }
    public IReadOnlyDictionary<MomentAnchorKind, int> AnchorCounts => _anchorCounts;
    public int ProposalCount { get; }
    public int HardRejectedCount { get; }
    public int BelowThresholdCount { get; }
    public int OverlapSuppressedCount { get; }
    public int NeighborhoodCount { get; }
    public int NeighborhoodSuppressedCount { get; }
    public int EpisodeCount { get; }
    public int EpisodeSuppressedCount { get; }
    public int SelectedCount { get; }
    public TimeSpan TotalElapsed { get; }
    public string DeterministicCoverageStatement { get; }
    public string PolicyHash { get; }

    private static void ValidateText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Manifest text values cannot be blank.",
                name);
        }
    }

    private static void ValidateCount(int value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
