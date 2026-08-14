using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentEventNeighborhood
{
    private readonly ReadOnlyCollection<MomentAnchor> _anchors;
    private readonly ReadOnlyCollection<MomentSignalFamily> _signalFamilies;

    public MomentEventNeighborhood(
        string id,
        TimeSpan start,
        TimeSpan peakTimestamp,
        TimeSpan end,
        IEnumerable<MomentAnchor> anchors,
        IEnumerable<MomentSignalFamily> signalFamilies,
        string? parentNeighborhoodId = null,
        MomentNeighborhoodSplitReason splitReason = MomentNeighborhoodSplitReason.None)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("An event neighborhood requires a stable identifier.", nameof(id));
        }

        if (start < TimeSpan.Zero || peakTimestamp < start || end < start || peakTimestamp > end)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(signalFamilies);
        MomentAnchor[] anchorSnapshot =
            anchors
                .OrderBy(static anchor => anchor.Timestamp)
                .ThenBy(static anchor => anchor.Kind)
                .ThenBy(static anchor => anchor.Id, StringComparer.Ordinal)
                .ToArray();
        MomentSignalFamily[] familySnapshot =
            signalFamilies
                .Distinct()
                .OrderBy(static family => family)
                .ToArray();

        if (anchorSnapshot.Length == 0 ||
            anchorSnapshot.Any(static anchor => anchor is null) ||
            anchorSnapshot.Any(anchor => anchor.Timestamp < start || anchor.Timestamp > end))
        {
            throw new ArgumentException("A neighborhood requires bounded contributing anchors.", nameof(anchors));
        }

        if (familySnapshot.Length == 0 ||
            familySnapshot.Any(static family => !Enum.IsDefined(family)))
        {
            throw new ArgumentException("A neighborhood requires defined signal families.", nameof(signalFamilies));
        }

        if (!Enum.IsDefined(splitReason))
        {
            throw new ArgumentOutOfRangeException(nameof(splitReason));
        }

        if (splitReason == MomentNeighborhoodSplitReason.None && parentNeighborhoodId is not null ||
            splitReason != MomentNeighborhoodSplitReason.None && string.IsNullOrWhiteSpace(parentNeighborhoodId))
        {
            throw new ArgumentException(
                "Only a validated split neighborhood may identify a parent neighborhood.");
        }

        Id = id.Trim();
        Start = start;
        PeakTimestamp = peakTimestamp;
        End = end;
        ParentNeighborhoodId = parentNeighborhoodId?.Trim();
        SplitReason = splitReason;
        _anchors = Array.AsReadOnly(anchorSnapshot);
        _signalFamilies = Array.AsReadOnly(familySnapshot);
    }

    public string Id { get; }
    public TimeSpan Start { get; }
    public TimeSpan PeakTimestamp { get; }
    public TimeSpan End { get; }
    public TimeSpan Duration => End - Start;
    public string? ParentNeighborhoodId { get; }
    public MomentNeighborhoodSplitReason SplitReason { get; }
    public IReadOnlyList<MomentAnchor> Anchors => _anchors;
    public IReadOnlyList<MomentSignalFamily> SignalFamilies => _signalFamilies;
}

internal static class MomentSignalFamilyMap
{
    public static MomentSignalFamily FromAnchor(MomentAnchor anchor) =>
        anchor.Kind switch
        {
            MomentAnchorKind.GameplayActivityBurst =>
                MomentSignalFamily.GameplayBurst,
            MomentAnchorKind.GameplaySceneCluster or
            MomentAnchorKind.GameplaySceneBoundary =>
                MomentSignalFamily.GameplayScene,
            MomentAnchorKind.AudioNovelty or
            MomentAnchorKind.AudioReentry =>
                MomentSignalFamily.AudioNovelty,
            MomentAnchorKind.PresenterGatedSupport or
            MomentAnchorKind.PresenterAudioAgreement =>
                MomentSignalFamily.PresenterProminence,
            MomentAnchorKind.EpisodeActivationPeak =>
                MomentSignalFamily.EpisodeActivation,
            MomentAnchorKind.UserConfirmedPriority =>
                MomentSignalFamily.UserGuidance,
            _ => throw new ArgumentOutOfRangeException(nameof(anchor)),
        };
}
