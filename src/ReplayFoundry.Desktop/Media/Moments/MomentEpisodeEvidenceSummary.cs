using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentEpisodeEvidenceSummary
{
    private readonly ReadOnlyCollection<MomentSignalFamily> _families;
    private readonly ReadOnlyCollection<string> _burstIds;
    private readonly ReadOnlyCollection<string> _anchorIds;
    private readonly ReadOnlyCollection<MomentEvidenceReference> _references;

    public MomentEpisodeEvidenceSummary(
        IEnumerable<MomentSignalFamily> dominantSignalFamilies,
        IEnumerable<string> contributingBurstIds,
        IEnumerable<string> contributingAnchorIds,
        IEnumerable<MomentEvidenceReference> evidenceReferences)
    {
        ArgumentNullException.ThrowIfNull(dominantSignalFamilies);
        ArgumentNullException.ThrowIfNull(contributingBurstIds);
        ArgumentNullException.ThrowIfNull(contributingAnchorIds);
        ArgumentNullException.ThrowIfNull(evidenceReferences);

        MomentSignalFamily[] families = dominantSignalFamilies.Distinct().OrderBy(static item => item).ToArray();
        string[] bursts = SnapshotText(contributingBurstIds);
        string[] anchors = SnapshotText(contributingAnchorIds);
        MomentEvidenceReference[] references = evidenceReferences
            .Where(static item => item is not null)
            .OrderBy(static item => item.Start)
            .ThenBy(static item => item.End)
            .ThenBy(static item => item.Kind)
            .ToArray();
        if (families.Any(static item => !Enum.IsDefined(item)))
        {
            throw new ArgumentException("Episode signal families must be defined.", nameof(dominantSignalFamilies));
        }

        _families = Array.AsReadOnly(families);
        _burstIds = Array.AsReadOnly(bursts);
        _anchorIds = Array.AsReadOnly(anchors);
        _references = Array.AsReadOnly(references);
    }

    public IReadOnlyList<MomentSignalFamily> DominantSignalFamilies => _families;
    public IReadOnlyList<string> ContributingBurstIds => _burstIds;
    public IReadOnlyList<string> ContributingAnchorIds => _anchorIds;
    public IReadOnlyList<MomentEvidenceReference> EvidenceReferences => _references;

    private static string[] SnapshotText(IEnumerable<string> values)
    {
        string[] snapshot = values
            .Select(static item => item?.Trim() ?? string.Empty)
            .Where(static item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        return snapshot;
    }
}
