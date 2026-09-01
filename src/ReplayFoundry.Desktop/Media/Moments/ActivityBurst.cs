using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class ActivityBurst
{
    private readonly ReadOnlyCollection<MomentEvidenceReference> _evidenceReferences;

    public ActivityBurst(
        string id,
        string targetKey,
        CompositionRegionRole role,
        TimeSpan start,
        TimeSpan peakTimestamp,
        TimeSpan end,
        double localBaseline,
        double localSpread,
        double rawPeakActivity,
        double peakProminence,
        double onsetStrength,
        double integratedExcess,
        double occupancy,
        double concentration,
        double returnToBaseline,
        IEnumerable<MomentEvidenceReference> evidenceReferences)
    {
        ValidateText(id, nameof(id));
        ValidateText(targetKey, nameof(targetKey));
        if (role is not (CompositionRegionRole.Gameplay or CompositionRegionRole.Presenter))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (start < TimeSpan.Zero || peakTimestamp < start || end <= start || peakTimestamp > end)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        ValidateFinite(localBaseline, nameof(localBaseline));
        ValidatePositive(localSpread, nameof(localSpread));
        ValidateFinite(rawPeakActivity, nameof(rawPeakActivity));
        ValidateRatio(peakProminence, nameof(peakProminence));
        ValidateRatio(onsetStrength, nameof(onsetStrength));
        ValidateNonNegative(integratedExcess, nameof(integratedExcess));
        ValidateRatio(occupancy, nameof(occupancy));
        ValidateRatio(concentration, nameof(concentration));
        ValidateRatio(returnToBaseline, nameof(returnToBaseline));
        ArgumentNullException.ThrowIfNull(evidenceReferences);

        MomentEvidenceReference[] snapshot =
            evidenceReferences
                .OrderBy(static reference => reference.Start)
                .ThenBy(static reference => reference.End)
                .ThenBy(static reference => reference.VisualTargetKey, StringComparer.Ordinal)
                .ToArray();
        if (snapshot.Length == 0 || snapshot.Any(static reference => reference is null))
        {
            throw new ArgumentException("An activity burst requires attributed evidence.", nameof(evidenceReferences));
        }

        Id = id.Trim();
        TargetKey = targetKey.Trim();
        Role = role;
        Start = start;
        PeakTimestamp = peakTimestamp;
        End = end;
        LocalBaseline = localBaseline;
        LocalSpread = localSpread;
        RawPeakActivity = rawPeakActivity;
        PeakProminence = peakProminence;
        OnsetStrength = onsetStrength;
        IntegratedExcess = integratedExcess;
        Occupancy = occupancy;
        Concentration = concentration;
        ReturnToBaseline = returnToBaseline;
        _evidenceReferences = Array.AsReadOnly(snapshot);
    }

    public string Id { get; }
    public string TargetKey { get; }
    public CompositionRegionRole Role { get; }
    public TimeSpan Start { get; }
    public TimeSpan PeakTimestamp { get; }
    public TimeSpan End { get; }
    public TimeSpan Duration => End - Start;
    public double LocalBaseline { get; }
    public double LocalSpread { get; }
    public double RawPeakActivity { get; }
    public double PeakProminence { get; }
    public double OnsetStrength { get; }
    public double IntegratedExcess { get; }
    public double Occupancy { get; }
    public double Concentration { get; }
    public double ReturnToBaseline { get; }
    public IReadOnlyList<MomentEvidenceReference> EvidenceReferences => _evidenceReferences;

    private static void ValidateText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A burst identity value cannot be blank.", name);
        }
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
