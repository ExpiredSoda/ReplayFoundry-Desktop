using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public static class MediaEvidenceSummaryBuilder
{
    public static AudioStreamSignalSummary BuildAudioSignalSummary(
        int audioStreamIndex,
        IEnumerable<AudioSignalSample> samples,
        AudioSignalCoverage? coverage,
        MediaEvidenceSummaryOptions? options = null) =>
        AudioStreamSignalSummaryBuilder.Build(
            audioStreamIndex,
            samples,
            coverage,
            options);

    public static AudioStreamSilenceSummary BuildAudioSummary(
        TimeSpan sourceDuration,
        int audioStreamIndex,
        IEnumerable<SilenceInterval> intervals,
        MediaEvidenceSummaryOptions? options = null) =>
        AudioStreamSilenceSummaryBuilder.Build(
            sourceDuration,
            audioStreamIndex,
            intervals,
            options);

    public static VisualTargetEvidenceSummary BuildVisualTargetSummary(
        VisualTargetEvidenceResult result,
        MediaEvidenceSummaryOptions? options = null) =>
        VisualTargetEvidenceSummaryBuilder.Build(
            result,
            options);

    public static VisualTargetSignalSummary BuildVisualSignalSummary(
        VisualTargetEvidenceResult result,
        MediaEvidenceSummaryOptions? options = null) =>
        VisualTargetSignalSummaryBuilder.Build(
            result,
            options);

    public static MediaEvidenceSummary Build(
        MediaProbeResult media,
        MediaEvidenceResult result,
        MediaEvidenceSummaryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(
                media.FullPath,
                result.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The media inspection and evidence result must describe the same source.",
                nameof(result));
        }

        if (media.Duration != result.SourceDuration)
        {
            throw new ArgumentException(
                "The media inspection and evidence result must use the same source duration.",
                nameof(result));
        }

        options ??=
            MediaEvidenceSummaryOptions.CreateDefault();

        SceneEvidenceSummary sceneSummary =
            SceneEvidenceSummaryBuilder.Build(
                result.SourceDuration,
                result.SceneBoundaries,
                options);

        IReadOnlyDictionary<int, SilenceInterval[]> intervalsByStream =
            result.SilenceIntervals
                .GroupBy(static interval => interval.AudioStreamIndex)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToArray());

        IReadOnlyDictionary<int, AudioSignalSample[]> signalsByStream =
            result.AudioSignalSamples
                .GroupBy(
                    static sample =>
                        sample.AudioStreamIndex)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group.ToArray());

        IReadOnlyDictionary<int, AudioSignalCoverage> coverageByStream =
            result.AudioSignalCoverages
                .ToDictionary(
                    static coverage =>
                        coverage.AudioStreamIndex);

        var knownAudioStreamIndices =
            new HashSet<int>(
                media.AudioStreams.Select(
                    static stream => stream.Index));

        int? unknownEvidenceStream =
            intervalsByStream.Keys
                .Concat(
                    signalsByStream.Keys)
                .Concat(
                    coverageByStream.Keys)
                .Distinct()
                .Cast<int?>()
                .FirstOrDefault(
                    streamIndex =>
                        streamIndex is int actual &&
                        !knownAudioStreamIndices.Contains(actual));

        if (unknownEvidenceStream is int unknownStreamIndex)
        {
            throw new ArgumentException(
                $"Silence evidence references audio stream {unknownStreamIndex}, " +
                "which is not present in structural media inspection.",
                nameof(result));
        }

        IReadOnlyList<AudioStreamSilenceSummary> audioSummaries =
            media.AudioStreams
                .OrderBy(static stream => stream.Index)
                .Select(
                    stream => BuildAudioSummary(
                        result.SourceDuration,
                        stream.Index,
                        intervalsByStream.TryGetValue(
                            stream.Index,
                            out SilenceInterval[]? intervals)
                                ? intervals
                                : [],
                        options))
                .ToArray();

        IReadOnlyList<VisualTargetEvidenceSummary> regionSummaries =
            result.RegionVisualResults
                .Select(
                    target =>
                        BuildVisualTargetSummary(
                            target,
                            options))
                .ToArray();

        IReadOnlyList<AudioStreamSignalSummary> audioSignalSummaries =
            media.AudioStreams
                .OrderBy(
                    static stream =>
                        stream.Index)
                .Select(
                    stream =>
                        BuildAudioSignalSummary(
                            stream.Index,
                            signalsByStream.TryGetValue(
                                stream.Index,
                                out AudioSignalSample[]? samples)
                                ? samples
                                : [],
                            coverageByStream.TryGetValue(
                                stream.Index,
                                out AudioSignalCoverage? coverage)
                                ? coverage
                                : null,
                            options))
                .ToArray();

        return new MediaEvidenceSummary(
            result.SourceDuration,
            options,
            sceneSummary,
            result.BlackIntervals.Count,
            MediaEvidenceSummaryMath.SumDurations(
                result.BlackIntervals.Select(static item => item.Duration)),
            result.FreezeIntervals.Count,
            MediaEvidenceSummaryMath.SumDurations(
                result.FreezeIntervals.Select(static item => item.Duration)),
            BuildVisualSignalSummary(
                result.FullFrame,
                options),
            regionSummaries,
            audioSummaries,
            audioSignalSummaries);
    }
}
