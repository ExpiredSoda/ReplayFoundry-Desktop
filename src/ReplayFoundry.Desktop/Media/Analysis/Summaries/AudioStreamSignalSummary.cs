using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class AudioStreamSignalSummary
{
    public AudioStreamSignalSummary(
        int audioStreamIndex,
        string policyVersion,
        int sampleCount,
        TimeSpan totalCoveredDuration,
        double? meanRmsLevelDbfs,
        double? medianRmsLevelDbfs,
        double? rmsLevelP10Dbfs,
        double? rmsLevelP90Dbfs,
        double? maximumPeakLevelDbfs,
        int digitalSilenceWindowCount,
        double? digitalSilenceWindowPercentage,
        TimeSpan maximumInterWindowGap)
    {
        if (audioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex),
                audioStreamIndex,
                "Audio stream index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException(
                "An audio signal summary requires a policy version.",
                nameof(policyVersion));
        }

        if (sampleCount < 0 ||
            digitalSilenceWindowCount < 0 ||
            digitalSilenceWindowCount > sampleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                "Audio signal summary counts are inconsistent.");
        }

        if (totalCoveredDuration < TimeSpan.Zero ||
            maximumInterWindowGap < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalCoveredDuration),
                "Audio signal summary durations cannot be negative.");
        }

        ValidateDbfs(
            meanRmsLevelDbfs,
            nameof(meanRmsLevelDbfs));
        ValidateDbfs(
            medianRmsLevelDbfs,
            nameof(medianRmsLevelDbfs));
        ValidateDbfs(
            rmsLevelP10Dbfs,
            nameof(rmsLevelP10Dbfs));
        ValidateDbfs(
            rmsLevelP90Dbfs,
            nameof(rmsLevelP90Dbfs));
        ValidateDbfs(
            maximumPeakLevelDbfs,
            nameof(maximumPeakLevelDbfs));

        if (digitalSilenceWindowPercentage is double percentage &&
            (!double.IsFinite(percentage) ||
             percentage is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(digitalSilenceWindowPercentage),
                digitalSilenceWindowPercentage,
                "Digital-silence percentage must be finite and between zero and 100.");
        }

        if (sampleCount == 0 &&
            digitalSilenceWindowPercentage is not null)
        {
            throw new ArgumentException(
                "An empty audio signal summary cannot claim a digital-silence percentage.");
        }

        AudioStreamIndex = audioStreamIndex;
        PolicyVersion = policyVersion.Trim();
        SampleCount = sampleCount;
        TotalCoveredDuration =
            totalCoveredDuration;
        MeanRmsLevelDbfs = meanRmsLevelDbfs;
        MedianRmsLevelDbfs =
            medianRmsLevelDbfs;
        RmsLevelP10Dbfs = rmsLevelP10Dbfs;
        RmsLevelP90Dbfs = rmsLevelP90Dbfs;
        MaximumPeakLevelDbfs =
            maximumPeakLevelDbfs;
        DigitalSilenceWindowCount =
            digitalSilenceWindowCount;
        DigitalSilenceWindowPercentage =
            digitalSilenceWindowPercentage;
        MaximumInterWindowGap =
            maximumInterWindowGap;
    }

    public int AudioStreamIndex { get; }

    public string PolicyVersion { get; }

    public int SampleCount { get; }

    public TimeSpan TotalCoveredDuration { get; }

    public double? MeanRmsLevelDbfs { get; }

    public double? MedianRmsLevelDbfs { get; }

    public double? RmsLevelP10Dbfs { get; }

    public double? RmsLevelP90Dbfs { get; }

    public double? MaximumPeakLevelDbfs { get; }

    public int DigitalSilenceWindowCount { get; }

    public double? DigitalSilenceWindowPercentage { get; }

    public TimeSpan MaximumInterWindowGap { get; }

    private static void ValidateDbfs(
        double? value,
        string parameterName)
    {
        if (value is double actual &&
            (!double.IsFinite(actual) ||
             actual > 0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Finite summary audio levels must be at or below 0 dBFS.");
        }
    }
}
