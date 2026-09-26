using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentAnchor
{
    private readonly ReadOnlyCollection<MomentEvidenceReference>
        _evidenceReferences;

    public MomentAnchor(
        string id,
        MomentAnchorKind kind,
        TimeSpan timestamp,
        double rawFeatureValue,
        double normalizedStrength,
        IEnumerable<MomentEvidenceReference> evidenceReferences)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A moment anchor requires a stable identifier.",
                nameof(id));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind));
        }

        if (timestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp));
        }

        if (!double.IsFinite(rawFeatureValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawFeatureValue));
        }

        if (!double.IsFinite(normalizedStrength) ||
            normalizedStrength is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalizedStrength));
        }

        ArgumentNullException.ThrowIfNull(evidenceReferences);

        MomentEvidenceReference[] snapshot =
            evidenceReferences.ToArray();

        if (snapshot.Length == 0 ||
            snapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "A moment anchor requires at least one evidence reference.",
                nameof(evidenceReferences));
        }

        Id = id.Trim();
        Kind = kind;
        Timestamp = timestamp;
        RawFeatureValue = rawFeatureValue;
        NormalizedStrength = normalizedStrength;
        _evidenceReferences =
            Array.AsReadOnly(snapshot);
    }

    public string Id { get; }

    public MomentAnchorKind Kind { get; }

    public TimeSpan Timestamp { get; }

    public double RawFeatureValue { get; }

    public double NormalizedStrength { get; }

    public IReadOnlyList<MomentEvidenceReference>
        EvidenceReferences =>
        _evidenceReferences;
}
