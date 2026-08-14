using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;

public sealed class VisualSignalCoverage
{
    private readonly ReadOnlyCollection<TimeSpan>
        _actualSampleTimestamps;

    private readonly ReadOnlyCollection<MediaEvidenceWarning>
        _warnings;

    public VisualSignalCoverage(
        string targetKey,
        TimeSpan targetStart,
        TimeSpan targetEnd,
        TimeSpan requestedSampleInterval,
        IEnumerable<TimeSpan> actualSampleTimestamps,
        int? expectedSampleCount,
        bool targetIntervalTraversed,
        string samplingPolicyVersion,
        IEnumerable<MediaEvidenceWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            throw new ArgumentException(
                "Visual signal coverage requires a target key.",
                nameof(targetKey));
        }

        if (targetStart < TimeSpan.Zero ||
            targetEnd <= targetStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetEnd),
                targetEnd,
                "Visual signal coverage requires a positive target interval.");
        }

        MediaEvidenceAnalysisOptions
            .ValidateSignalCadence(
                requestedSampleInterval,
                nameof(requestedSampleInterval));

        ArgumentNullException.ThrowIfNull(
            actualSampleTimestamps);

        if (expectedSampleCount is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedSampleCount),
                expectedSampleCount,
                "Expected visual sample count cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(
                samplingPolicyVersion))
        {
            throw new ArgumentException(
                "Visual signal coverage requires a sampling policy version.",
                nameof(samplingPolicyVersion));
        }

        TimeSpan[] timestampSnapshot =
            actualSampleTimestamps
                .OrderBy(static value => value)
                .ToArray();

        if (timestampSnapshot.Any(
                timestamp =>
                    timestamp < targetStart ||
                    timestamp >= targetEnd))
        {
            throw new ArgumentException(
                "Visual signal timestamps must remain inside the half-open target interval.",
                nameof(actualSampleTimestamps));
        }

        if (timestampSnapshot
            .Distinct()
            .Count() !=
            timestampSnapshot.Length)
        {
            throw new ArgumentException(
                "Visual signal coverage cannot contain duplicate timestamps.",
                nameof(actualSampleTimestamps));
        }

        MediaEvidenceWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (warningSnapshot.Any(
                warning =>
                    warning is null ||
                    warning.TargetKey is not null &&
                    !string.Equals(
                        warning.TargetKey,
                        targetKey,
                        StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Visual coverage warnings must identify their owning target.",
                nameof(warnings));
        }

        TargetKey = targetKey.Trim();
        TargetStart = targetStart;
        TargetEnd = targetEnd;
        RequestedSampleInterval =
            requestedSampleInterval;
        ExpectedSampleCount = expectedSampleCount;
        TargetIntervalTraversed =
            targetIntervalTraversed;
        SamplingPolicyVersion =
            samplingPolicyVersion.Trim();
        _actualSampleTimestamps =
            Array.AsReadOnly(
                timestampSnapshot);
        _warnings =
            Array.AsReadOnly(
                warningSnapshot);
        MaximumObservedGap =
            CalculateMaximumGap(
                targetStart,
                targetEnd,
                timestampSnapshot);
    }

    public string TargetKey { get; }

    public TimeSpan TargetStart { get; }

    public TimeSpan TargetEnd { get; }

    public TimeSpan RequestedSampleInterval { get; }

    public IReadOnlyList<TimeSpan>
        ActualSampleTimestamps =>
        _actualSampleTimestamps;

    public int? ExpectedSampleCount { get; }

    public int ActualSampleCount =>
        _actualSampleTimestamps.Count;

    public TimeSpan MaximumObservedGap { get; }

    public bool TargetIntervalTraversed { get; }

    public string SamplingPolicyVersion { get; }

    public IReadOnlyList<MediaEvidenceWarning> Warnings =>
        _warnings;

    private static TimeSpan CalculateMaximumGap(
        TimeSpan start,
        TimeSpan end,
        IReadOnlyList<TimeSpan> timestamps)
    {
        if (timestamps.Count == 0)
        {
            return end - start;
        }

        TimeSpan maximum =
            timestamps[0] -
            start;

        for (int index = 1;
             index < timestamps.Count;
             index++)
        {
            TimeSpan gap =
                timestamps[index] -
                timestamps[index - 1];

            if (gap > maximum)
            {
                maximum = gap;
            }
        }

        TimeSpan trailing =
            end -
            timestamps[^1];

        return trailing > maximum
            ? trailing
            : maximum;
    }
}
