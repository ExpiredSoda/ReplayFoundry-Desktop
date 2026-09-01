using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentEventEpisode
{
    private readonly ReadOnlyCollection<MomentEventEpisodePhase> _phases;
    private readonly ReadOnlyCollection<string> _warnings;

    public MomentEventEpisode(
        string id,
        TimeSpan start,
        TimeSpan onsetTimestamp,
        TimeSpan primaryPeakTimestamp,
        TimeSpan end,
        double peakActivation,
        double integratedActivation,
        double activationOccupancy,
        double? localBaselineBefore,
        double? localRecoveryAfter,
        MomentEpisodeEvidenceSummary evidenceSummary,
        IEnumerable<MomentEventEpisodePhase> phases,
        string rationale,
        string? parentEpisodeId = null,
        MomentEpisodeSplitRationale splitRationale = MomentEpisodeSplitRationale.None,
        IEnumerable<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "An event episode requires a stable identifier.",
                nameof(id));
        }

        if (start < TimeSpan.Zero ||
            onsetTimestamp < start ||
            primaryPeakTimestamp < onsetTimestamp ||
            end <= start ||
            end < primaryPeakTimestamp)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        ValidateRatio(peakActivation, nameof(peakActivation));
        ValidateNonNegative(integratedActivation, nameof(integratedActivation));
        ValidateRatio(activationOccupancy, nameof(activationOccupancy));
        ValidateOptionalRatio(localBaselineBefore, nameof(localBaselineBefore));
        ValidateOptionalRatio(localRecoveryAfter, nameof(localRecoveryAfter));
        ArgumentNullException.ThrowIfNull(evidenceSummary);
        ArgumentNullException.ThrowIfNull(phases);
        if (string.IsNullOrWhiteSpace(rationale))
        {
            throw new ArgumentException(
                "Episode rationale cannot be blank.",
                nameof(rationale));
        }
        if (!Enum.IsDefined(splitRationale))
        {
            throw new ArgumentOutOfRangeException(nameof(splitRationale));
        }
        if (splitRationale == MomentEpisodeSplitRationale.None &&
                parentEpisodeId is not null ||
            splitRationale != MomentEpisodeSplitRationale.None &&
                string.IsNullOrWhiteSpace(parentEpisodeId))
        {
            throw new ArgumentException(
                "Only validated split episodes may identify a parent.");
        }

        MomentEventEpisodePhase[] phaseSnapshot = phases
            .OrderBy(static item => item.Start)
            .ThenBy(static item => item.Kind)
            .ToArray();
        if (phaseSnapshot.Any(static item => item is null) ||
            phaseSnapshot.Any(item => item.Start < start || item.End > end) ||
            phaseSnapshot
                .GroupBy(static item => item.Kind)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Episode phases must be unique and bounded.",
                nameof(phases));
        }

        MomentEventEpisodePhase? core = phaseSnapshot.FirstOrDefault(
            static item => item.Kind == MomentEventEpisodePhaseKind.Core);
        if (core is null ||
            primaryPeakTimestamp < core.Start ||
            primaryPeakTimestamp > core.End)
        {
            throw new ArgumentException(
                "The primary peak must lie inside the Core phase.",
                nameof(phases));
        }

        string[] warningSnapshot = warnings?
            .Select(static item => item?.Trim() ?? string.Empty)
            .Where(static item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        Id = id.Trim();
        Start = start;
        OnsetTimestamp = onsetTimestamp;
        PrimaryPeakTimestamp = primaryPeakTimestamp;
        End = end;
        PeakActivation = peakActivation;
        IntegratedActivation = integratedActivation;
        ActivationOccupancy = activationOccupancy;
        LocalBaselineBefore = localBaselineBefore;
        LocalRecoveryAfter = localRecoveryAfter;
        EvidenceSummary = evidenceSummary;
        Rationale = rationale.Trim();
        ParentEpisodeId = parentEpisodeId?.Trim();
        SplitRationale = splitRationale;
        _phases = Array.AsReadOnly(phaseSnapshot);
        _warnings = Array.AsReadOnly(warningSnapshot);
    }

    public string Id { get; }
    public TimeSpan Start { get; }
    public TimeSpan OnsetTimestamp { get; }
    public TimeSpan PrimaryPeakTimestamp { get; }
    public TimeSpan End { get; }
    public TimeSpan Duration => End - Start;
    public double PeakActivation { get; }
    public double IntegratedActivation { get; }
    public double ActivationOccupancy { get; }
    public double? LocalBaselineBefore { get; }
    public double? LocalRecoveryAfter { get; }
    public MomentEpisodeEvidenceSummary EvidenceSummary { get; }
    public IReadOnlyList<MomentEventEpisodePhase> Phases => _phases;
    public string Rationale { get; }
    public string? ParentEpisodeId { get; }
    public string CohesionIdentity => ParentEpisodeId ?? Id;
    public MomentEpisodeSplitRationale SplitRationale { get; }
    public IReadOnlyList<string> Warnings => _warnings;

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateOptionalRatio(double? value, string name)
    {
        if (value is not null)
        {
            ValidateRatio(value.Value, name);
        }
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
