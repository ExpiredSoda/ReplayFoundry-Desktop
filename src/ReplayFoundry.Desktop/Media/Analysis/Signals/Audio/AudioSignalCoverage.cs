using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;

public sealed class AudioSignalCoverage
{
    private readonly ReadOnlyCollection<MediaEvidenceWarning>
        _warnings;

    public AudioSignalCoverage(
        int audioStreamIndex,
        TimeSpan sourceDuration,
        TimeSpan requestedWindowDuration,
        TimeSpan actualWindowDuration,
        int sampleRate,
        int samplesPerCompleteWindow,
        int actualWindowCount,
        TimeSpan totalCoveredDuration,
        TimeSpan maximumObservedGap,
        AudioFinalPartialWindowPolicy finalPartialWindowPolicy,
        int? finalPartialWindowSampleCount,
        TimeSpan uncoveredTailDuration,
        bool sourceTimelineTraversed,
        string samplingPolicyVersion,
        IEnumerable<MediaEvidenceWarning>? warnings = null)
    {
        if (audioStreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audioStreamIndex),
                audioStreamIndex,
                "Audio stream index cannot be negative.");
        }

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Audio coverage requires a positive source duration.");
        }

        MediaEvidenceAnalysisOptions
            .ValidateSignalCadence(
                requestedWindowDuration,
                nameof(requestedWindowDuration));
        MediaEvidenceAnalysisOptions
            .ValidateSignalCadence(
                actualWindowDuration,
                nameof(actualWindowDuration));

        if (sampleRate <= 0 ||
            samplesPerCompleteWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                "Audio coverage requires a positive sample rate and window sample count.");
        }

        TimeSpan derivedWindowDuration =
            TimeSpan.FromSeconds(
                samplesPerCompleteWindow /
                (double)sampleRate);

        if (actualWindowDuration !=
            derivedWindowDuration)
        {
            throw new ArgumentException(
                "Actual audio window duration must be derived exactly from its integer sample count and sample rate.",
                nameof(actualWindowDuration));
        }

        if (actualWindowCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualWindowCount),
                actualWindowCount,
                "Actual audio window count cannot be negative.");
        }

        ValidateDuration(
            totalCoveredDuration,
            sourceDuration,
            nameof(totalCoveredDuration));
        ValidateDuration(
            maximumObservedGap,
            sourceDuration,
            nameof(maximumObservedGap));
        ValidateDuration(
            uncoveredTailDuration,
            sourceDuration,
            nameof(uncoveredTailDuration));

        if (!Enum.IsDefined(
                finalPartialWindowPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(finalPartialWindowPolicy),
                finalPartialWindowPolicy,
                "The final partial-window policy is not defined.");
        }

        if (finalPartialWindowSampleCount is <= 0 ||
            finalPartialWindowSampleCount >=
            samplesPerCompleteWindow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(finalPartialWindowSampleCount),
                finalPartialWindowSampleCount,
                "A reported partial window must contain fewer positive samples than a complete window.");
        }

        if (string.IsNullOrWhiteSpace(
                samplingPolicyVersion))
        {
            throw new ArgumentException(
                "Audio coverage requires a sampling policy version.",
                nameof(samplingPolicyVersion));
        }

        MediaEvidenceWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        if (warningSnapshot.Any(
                warning =>
                    warning is null ||
                    warning.StreamIndex is not null &&
                    warning.StreamIndex !=
                    audioStreamIndex))
        {
            throw new ArgumentException(
                "Audio coverage warnings must identify their owning stream.",
                nameof(warnings));
        }

        AudioStreamIndex = audioStreamIndex;
        SourceDuration = sourceDuration;
        RequestedWindowDuration =
            requestedWindowDuration;
        ActualWindowDuration =
            actualWindowDuration;
        SampleRate = sampleRate;
        SamplesPerCompleteWindow =
            samplesPerCompleteWindow;
        ActualWindowCount = actualWindowCount;
        TotalCoveredDuration =
            totalCoveredDuration;
        MaximumObservedGap =
            maximumObservedGap;
        FinalPartialWindowPolicy =
            finalPartialWindowPolicy;
        FinalPartialWindowSampleCount =
            finalPartialWindowSampleCount;
        UncoveredTailDuration =
            uncoveredTailDuration;
        SourceTimelineTraversed =
            sourceTimelineTraversed;
        SamplingPolicyVersion =
            samplingPolicyVersion.Trim();
        _warnings =
            Array.AsReadOnly(
                warningSnapshot);
    }

    public int AudioStreamIndex { get; }

    public TimeSpan SourceDuration { get; }

    public TimeSpan RequestedWindowDuration { get; }

    public TimeSpan ActualWindowDuration { get; }

    public int SampleRate { get; }

    public int SamplesPerCompleteWindow { get; }

    public int ActualWindowCount { get; }

    public TimeSpan TotalCoveredDuration { get; }

    public TimeSpan MaximumObservedGap { get; }

    public AudioFinalPartialWindowPolicy
        FinalPartialWindowPolicy
    { get; }

    public int? FinalPartialWindowSampleCount { get; }

    public TimeSpan UncoveredTailDuration { get; }

    public bool SourceTimelineTraversed { get; }

    public string SamplingPolicyVersion { get; }

    public IReadOnlyList<MediaEvidenceWarning> Warnings =>
        _warnings;

    private static void ValidateDuration(
        TimeSpan value,
        TimeSpan sourceDuration,
        string parameterName)
    {
        if (value < TimeSpan.Zero ||
            value > sourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Audio coverage durations must remain within the source duration.");
        }
    }
}
