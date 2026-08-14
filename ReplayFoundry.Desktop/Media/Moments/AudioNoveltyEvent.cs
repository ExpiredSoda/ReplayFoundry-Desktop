using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class AudioNoveltyEvent
{
    private readonly ReadOnlyCollection<int> _audioStreamIndices;
    private readonly ReadOnlyCollection<MomentEvidenceReference> _evidenceReferences;

    public AudioNoveltyEvent(
        string id,
        IEnumerable<int> audioStreamIndices,
        TimeSpan start,
        TimeSpan peakTimestamp,
        TimeSpan end,
        double localBaselineDbfs,
        double localSpreadDb,
        double peakFiniteRmsDbfs,
        double normalizedProminence,
        double onsetStrength,
        double peakLiftDb,
        TimeSpan durationAboveBaseline,
        bool isSilenceReentry,
        double returnToBaseline,
        IEnumerable<MomentEvidenceReference> evidenceReferences)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("An audio novelty event requires a stable identifier.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(audioStreamIndices);
        int[] streams = audioStreamIndices.Distinct().OrderBy(static index => index).ToArray();
        if (streams.Length == 0 || streams.Any(static index => index < 0))
        {
            throw new ArgumentException("Audio novelty events require defined absolute stream indices.", nameof(audioStreamIndices));
        }

        if (start < TimeSpan.Zero || peakTimestamp < start || end <= start || peakTimestamp > end)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        ValidateDbfs(localBaselineDbfs, nameof(localBaselineDbfs));
        ValidatePositive(localSpreadDb, nameof(localSpreadDb));
        ValidateDbfs(peakFiniteRmsDbfs, nameof(peakFiniteRmsDbfs));
        ValidateRatio(normalizedProminence, nameof(normalizedProminence));
        ValidateRatio(onsetStrength, nameof(onsetStrength));
        ValidateNonNegative(peakLiftDb, nameof(peakLiftDb));
        if (durationAboveBaseline < TimeSpan.Zero || durationAboveBaseline > end - start)
        {
            throw new ArgumentOutOfRangeException(nameof(durationAboveBaseline));
        }
        ValidateRatio(returnToBaseline, nameof(returnToBaseline));

        ArgumentNullException.ThrowIfNull(evidenceReferences);
        MomentEvidenceReference[] references =
            evidenceReferences
                .OrderBy(static reference => reference.Start)
                .ThenBy(static reference => reference.AudioStreamIndex)
                .ToArray();
        if (references.Length == 0 || references.Any(static reference => reference is null))
        {
            throw new ArgumentException("An audio novelty event requires attributed evidence.", nameof(evidenceReferences));
        }

        Id = id.Trim();
        Start = start;
        PeakTimestamp = peakTimestamp;
        End = end;
        LocalBaselineDbfs = localBaselineDbfs;
        LocalSpreadDb = localSpreadDb;
        PeakFiniteRmsDbfs = peakFiniteRmsDbfs;
        NormalizedProminence = normalizedProminence;
        OnsetStrength = onsetStrength;
        PeakLiftDb = peakLiftDb;
        DurationAboveBaseline = durationAboveBaseline;
        IsSilenceReentry = isSilenceReentry;
        ReturnToBaseline = returnToBaseline;
        _audioStreamIndices = Array.AsReadOnly(streams);
        _evidenceReferences = Array.AsReadOnly(references);
    }

    public string Id { get; }
    public IReadOnlyList<int> AudioStreamIndices => _audioStreamIndices;
    public TimeSpan Start { get; }
    public TimeSpan PeakTimestamp { get; }
    public TimeSpan End { get; }
    public TimeSpan Duration => End - Start;
    public double LocalBaselineDbfs { get; }
    public double LocalSpreadDb { get; }
    public double PeakFiniteRmsDbfs { get; }
    public double NormalizedProminence { get; }
    public double OnsetStrength { get; }
    public double PeakLiftDb { get; }
    public TimeSpan DurationAboveBaseline { get; }
    public bool IsSilenceReentry { get; }
    public double ReturnToBaseline { get; }
    public IReadOnlyList<MomentEvidenceReference> EvidenceReferences => _evidenceReferences;

    private static void ValidateDbfs(double value, string name)
    {
        if (!double.IsFinite(value) || value > 0)
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
