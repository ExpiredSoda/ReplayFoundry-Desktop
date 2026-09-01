using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceCoverageBuilder;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceMetadataKeys;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceParseAccumulators;
using static ReplayFoundry.Desktop.Platform.Media.FfmpegEvidenceValueParser;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegAudioSignalRecordParser
{
    internal static void ParseAudioSignalRecord(
        FfmpegMetadataRecord record,
        int audioStreamIndex,
        int sampleRate,
        TimeSpan sourceDuration,
        ICollection<AudioSignalSample> samples,
        ICollection<MediaEvidenceWarning> warnings)
    {
        if (record.Timestamp is not TimeSpan start ||
            start < TimeSpan.Zero ||
            start >= sourceDuration)
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .EvidenceOutsideSourceDuration,
                    "Audio signal metadata did not report a valid source-relative start timestamp.",
                    audioStreamIndex));
            return;
        }

        if (!record.Tags.TryGetValue(
                AudioSampleCountKey,
                out string? sampleCountText) ||
            !TryParsePositiveInteger(
                sampleCountText,
                out int sampleCount))
        {
            warnings.Add(
                InvalidValueWarning(
                    AudioSampleCountKey,
                    sampleCountText ??
                    "<missing>",
                    streamIndex:
                        audioStreamIndex));
            return;
        }

        ParsedDbfs rms =
            ParseDbfs(
                record,
                AudioRmsKey);

        ParsedDbfs peak =
            ParseDbfs(
                record,
                AudioPeakKey);

        if (rms.Kind is DbfsValueKind.Missing or
                DbfsValueKind.Invalid ||
            peak.Kind is DbfsValueKind.Missing or
                DbfsValueKind.Invalid)
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .InvalidMetadataValue,
                    $"Audio signal metadata requires valid '{AudioRmsKey}' and '{AudioPeakKey}' values.",
                    audioStreamIndex));
            return;
        }

        bool isDigitalSilence =
            rms.Kind ==
                DbfsValueKind.NegativeInfinity &&
            peak.Kind ==
                DbfsValueKind.NegativeInfinity;

        if (!isDigitalSilence &&
            (rms.Kind ==
                 DbfsValueKind.NegativeInfinity ||
             peak.Kind ==
                 DbfsValueKind.NegativeInfinity))
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .InvalidMetadataValue,
                    "Audio RMS and peak metadata disagreed about digital silence.",
                    audioStreamIndex));
            return;
        }

        TimeSpan end =
            start +
            TimeSpan.FromSeconds(
                sampleCount /
                (double)sampleRate);

        if (end > sourceDuration)
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .EvidenceOutsideSourceDuration,
                    $"Audio signal window {start} through {end} exceeds the source duration.",
                    audioStreamIndex));
            return;
        }

        samples.Add(
            new AudioSignalSample(
                audioStreamIndex,
                start,
                end,
                sourceDuration,
                sampleCount,
                isDigitalSilence
                    ? null
                    : rms.Value,
                isDigitalSilence
                    ? null
                    : peak.Value,
                isDigitalSilence));
    }

    internal static IReadOnlyList<AudioSignalSample>
        NormalizeAudioSignalWindows(
            IEnumerable<AudioSignalSample> samples,
            int audioStreamIndex,
            ICollection<MediaEvidenceWarning> warnings)
    {
        AudioSignalSample[] ordered =
            samples
                .OrderBy(
                    static sample =>
                        sample.Start)
                .ThenBy(
                    static sample =>
                        sample.End)
                .ToArray();

        var accepted =
            new List<AudioSignalSample>(
                ordered.Length);

        foreach (AudioSignalSample sample in ordered)
        {
            AudioSignalSample? previous =
                accepted.LastOrDefault();
            if (previous is null)
            {
                accepted.Add(sample);
                continue;
            }

            if (sample.Start ==
                    previous.Start &&
                sample.End ==
                    previous.End)
            {
                warnings.Add(
                    new MediaEvidenceWarning(
                        MediaEvidenceWarningCode
                            .DuplicateAudioSignalWindow,
                        $"Duplicate audio signal window at {sample.Start} was ignored.",
                        audioStreamIndex));
                continue;
            }

            if (sample.Start <
                previous.End)
            {
                warnings.Add(
                    new MediaEvidenceWarning(
                        MediaEvidenceWarningCode
                            .OverlappingAudioSignalWindow,
                        $"Overlapping audio signal window at {sample.Start} was ignored.",
                        audioStreamIndex));
                continue;
            }

            accepted.Add(sample);
        }

        return accepted;
    }

    internal static AudioSignalCoverage CreateAudioCoverage(
        int audioStreamIndex,
        TimeSpan sourceDuration,
        TimeSpan requestedWindowDuration,
        FfmpegAudioWindowSpecification window,
        IReadOnlyList<AudioSignalSample> samples)
    {
        var warnings =
            new List<MediaEvidenceWarning>();

        TimeSpan totalCovered =
            TimeSpan.FromTicks(
                samples.Sum(
                    static sample =>
                        sample.Duration.Ticks));

        TimeSpan maximumGap =
            CalculateMaximumAudioGap(
                sourceDuration,
                samples);

        TimeSpan uncoveredTail =
            samples.Count == 0
                ? sourceDuration
                : sourceDuration -
                  samples[^1].End;

        int? expectedCount =
            TryCalculateExpectedCount(
                sourceDuration,
                window.ActualWindowDuration);

        if (samples.Count == 0 ||
            expectedCount is int expected &&
            samples.Count <
            Math.Max(
                expected -
                1,
                1))
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .MissingAudioSignalWindows,
                    $"Audio stream {audioStreamIndex} produced {samples.Count} sampled windows; expected approximately {expectedCount?.ToString(CultureInfo.InvariantCulture) ?? "an unbounded count"}.",
                    audioStreamIndex));
        }

        TimeSpan cadenceTolerance =
            TimeSpan.FromTicks(
                window.ActualWindowDuration.Ticks *
                3 /
                2);

        if (samples.Count > 0 &&
            maximumGap >
            cadenceTolerance)
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .IrregularAudioSignalCadence,
                    $"Audio stream {audioStreamIndex} has a maximum sampled-window gap of {maximumGap}.",
                    audioStreamIndex));
        }

        int? finalPartialCount =
            samples.LastOrDefault()
                ?.ActualSourceSampleCount is long finalCount &&
            finalCount <
            window.SamplesPerWindow
                ? checked((int)finalCount)
                : null;

        return new AudioSignalCoverage(
            audioStreamIndex,
            sourceDuration,
            requestedWindowDuration,
            window.ActualWindowDuration,
            window.SampleRate,
            window.SamplesPerWindow,
            samples.Count,
            totalCovered,
            maximumGap,
            AudioFinalPartialWindowPolicy
                .IncludeWithoutPadding,
            finalPartialCount,
            uncoveredTail,
            sourceTimelineTraversed: true,
            MediaSignalEvidencePolicy
                .CurrentSchemaVersion,
            warnings);
    }
}
