using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Media.Analysis;

public sealed class MediaEvidenceResult
{
    private readonly ReadOnlyCollection<VisualTargetEvidenceResult>
        _regionVisualResults;

    private readonly ReadOnlyCollection<SilenceInterval>
        _silenceIntervals;

    private readonly ReadOnlyCollection<AudioSignalSample>
        _audioSignalSamples;

    private readonly ReadOnlyCollection<AudioSignalCoverage>
        _audioSignalCoverages;

    private readonly ReadOnlyCollection<MediaEvidenceWarning>
        _warnings;

    public MediaEvidenceResult(
        string fullPath,
        TimeSpan sourceDuration,
        VisualTargetEvidenceResult fullFrame,
        IEnumerable<VisualTargetEvidenceResult> regionVisualResults,
        IEnumerable<SilenceInterval> silenceIntervals,
        IEnumerable<AudioSignalSample> audioSignalSamples,
        IEnumerable<AudioSignalCoverage> audioSignalCoverages,
        MediaEvidenceAnalysisManifest manifest,
        IEnumerable<MediaEvidenceWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "An evidence result requires a source path.",
                nameof(fullPath));
        }

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "Evidence source path must be fully qualified.",
                nameof(fullPath));
        }

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Evidence source duration must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(fullFrame);
        ArgumentNullException.ThrowIfNull(regionVisualResults);
        ArgumentNullException.ThrowIfNull(silenceIntervals);
        ArgumentNullException.ThrowIfNull(audioSignalSamples);
        ArgumentNullException.ThrowIfNull(audioSignalCoverages);
        ArgumentNullException.ThrowIfNull(manifest);

        if (fullFrame.Target.Kind !=
                VisualEvidenceTargetKind.FullFrame ||
            fullFrame.Target.Start != TimeSpan.Zero ||
            fullFrame.Target.End != sourceDuration)
        {
            throw new ArgumentException(
                "The root evidence result requires one full-duration full-frame target.",
                nameof(fullFrame));
        }

        VisualTargetEvidenceResult[] regionSnapshot =
            regionVisualResults
                .OrderBy(
                    static result =>
                        result.Target.Start)
                .ThenBy(
                    static result =>
                        result.Target.IntervalIndex)
                .ThenBy(
                    static result =>
                        result.Target.Role)
                .ThenBy(
                    static result =>
                        result.Target.RegionId,
                    StringComparer.Ordinal)
                .ThenBy(
                    static result =>
                        result.Target.TargetKey,
                    StringComparer.Ordinal)
                .ToArray();

        SilenceInterval[] silenceSnapshot =
            silenceIntervals
                .OrderBy(
                    static item =>
                        item.AudioStreamIndex)
                .ThenBy(
                    static item =>
                        item.Start)
                .ThenBy(
                    static item =>
                        item.End)
                .ToArray();

        AudioSignalSample[] audioSignalSnapshot =
            audioSignalSamples
                .OrderBy(
                    static item =>
                        item.AudioStreamIndex)
                .ThenBy(
                    static item =>
                        item.Start)
                .ThenBy(
                    static item =>
                        item.End)
                .ToArray();

        AudioSignalCoverage[] audioCoverageSnapshot =
            audioSignalCoverages
                .OrderBy(
                    static item =>
                        item.AudioStreamIndex)
                .ToArray();

        MediaEvidenceWarning[] warningSnapshot =
            warnings?.ToArray() ??
            [];

        RejectNullItems(
            regionSnapshot,
            nameof(regionVisualResults));
        RejectNullItems(
            silenceSnapshot,
            nameof(silenceIntervals));
        RejectNullItems(
            audioSignalSnapshot,
            nameof(audioSignalSamples));
        RejectNullItems(
            audioCoverageSnapshot,
            nameof(audioSignalCoverages));
        RejectNullItems(
            warningSnapshot,
            nameof(warnings));

        if (regionSnapshot.Any(
                static result =>
                    result.Target.Kind !=
                    VisualEvidenceTargetKind
                        .CompositionRegion))
        {
            throw new ArgumentException(
                "Region visual results must use composition-region targets.",
                nameof(regionVisualResults));
        }

        VisualTargetEvidenceResult[] allTargets =
        [
            fullFrame,
            .. regionSnapshot,
        ];

        if (allTargets
            .GroupBy(
                static result =>
                    result.Target.TargetKey,
                StringComparer.Ordinal)
            .Any(
                static group =>
                    group.Count() > 1))
        {
            throw new ArgumentException(
                "Evidence target keys must be unique.",
                nameof(regionVisualResults));
        }

        if (regionSnapshot.Any(
                result =>
                    result.Target.Start < TimeSpan.Zero ||
                    result.Target.End >
                    sourceDuration))
        {
            throw new ArgumentException(
                "Region targets must remain within the source duration.",
                nameof(regionVisualResults));
        }

        if (silenceSnapshot.Any(
                interval =>
                    interval.End >
                    sourceDuration))
        {
            throw new ArgumentException(
                "Audio evidence cannot extend beyond the source duration.",
                nameof(silenceIntervals));
        }

        if (audioSignalSnapshot.Any(
                sample =>
                    sample.End >
                    sourceDuration))
        {
            throw new ArgumentException(
                "Audio signal evidence cannot extend beyond the source duration.",
                nameof(audioSignalSamples));
        }

        if (audioCoverageSnapshot
            .GroupBy(
                static item =>
                    item.AudioStreamIndex)
            .Any(
                static group =>
                    group.Count() > 1))
        {
            throw new ArgumentException(
                "Audio signal coverage must be unique per absolute stream index.",
                nameof(audioSignalCoverages));
        }

        foreach (IGrouping<int, AudioSignalSample> streamGroup in
                 audioSignalSnapshot.GroupBy(
                     static item =>
                         item.AudioStreamIndex))
        {
            AudioSignalSample[] streamSamples =
                streamGroup.ToArray();

            if (streamSamples
                .Zip(
                    streamSamples.Skip(1),
                    static (left, right) =>
                        right.Start < left.End)
                .Any(static overlaps => overlaps))
            {
                throw new ArgumentException(
                    "Audio signal windows must be ordered and non-overlapping.",
                    nameof(audioSignalSamples));
            }
        }

        foreach (AudioSignalCoverage coverage in
                 audioCoverageSnapshot)
        {
            AudioSignalSample[] streamSamples =
                audioSignalSnapshot
                    .Where(
                        sample =>
                            sample.AudioStreamIndex ==
                            coverage.AudioStreamIndex)
                    .ToArray();

            TimeSpan totalCovered =
                TimeSpan.FromTicks(
                    streamSamples.Sum(
                        static sample =>
                            sample.Duration.Ticks));

            if (coverage.SourceDuration !=
                    sourceDuration ||
                coverage.ActualWindowCount !=
                    streamSamples.Length ||
                coverage.TotalCoveredDuration !=
                    totalCovered)
            {
                throw new ArgumentException(
                    "Audio signal coverage does not match its authoritative sample collection.",
                    nameof(audioSignalCoverages));
            }
        }

        int[] coverageStreamIndices =
            audioCoverageSnapshot
                .Select(
                    static item =>
                        item.AudioStreamIndex)
                .OrderBy(static index => index)
                .ToArray();

        if (audioSignalSnapshot.Any(
                sample =>
                    Array.BinarySearch(
                        coverageStreamIndices,
                        sample.AudioStreamIndex) < 0))
        {
            throw new ArgumentException(
                "Every audio stream with signal samples requires exactly one coverage record.",
                nameof(audioSignalCoverages));
        }

        string[] resultKeys =
            allTargets
                .Select(
                    static result =>
                        result.Target.TargetKey)
                .OrderBy(
                    static key =>
                        key,
                    StringComparer.Ordinal)
                .ToArray();

        string[] manifestKeys =
            manifest.VisualTargets
                .Select(
                    static target =>
                        target.TargetKey)
                .OrderBy(
                    static key =>
                        key,
                    StringComparer.Ordinal)
                .ToArray();

        if (!resultKeys.SequenceEqual(
                manifestKeys,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Every evidence target key must be present in the analysis manifest mapping.",
                nameof(manifest));
        }

        int visualSignalCount =
            allTargets.Sum(
                static target =>
                    target.SignalSamples.Count);

        if (manifest.VisualSignalSampleCount !=
                visualSignalCount ||
            manifest.AudioSignalSampleCount !=
                audioSignalSnapshot.Length)
        {
            throw new ArgumentException(
                "Manifest signal counts must match the authoritative result collections.",
                nameof(manifest));
        }

        FullPath = fullPath;
        SourceDuration = sourceDuration;
        FullFrame = fullFrame;
        Manifest = manifest;
        _regionVisualResults =
            Array.AsReadOnly(
                regionSnapshot);
        _silenceIntervals =
            Array.AsReadOnly(
                silenceSnapshot);
        _audioSignalSamples =
            Array.AsReadOnly(
                audioSignalSnapshot);
        _audioSignalCoverages =
            Array.AsReadOnly(
                audioCoverageSnapshot);
        _warnings =
            Array.AsReadOnly(
                warningSnapshot);
    }

    public string FullPath { get; }

    public TimeSpan SourceDuration { get; }

    public VisualTargetEvidenceResult FullFrame { get; }

    public IReadOnlyList<VisualTargetEvidenceResult>
        RegionVisualResults =>
        _regionVisualResults;

    /// <summary>
    /// Convenience projection of the authoritative full-frame target result.
    /// </summary>
    public IReadOnlyList<SceneBoundary> SceneBoundaries =>
        FullFrame.SceneBoundaries;

    /// <summary>
    /// Convenience projection of the authoritative full-frame target result.
    /// </summary>
    public IReadOnlyList<BlackInterval> BlackIntervals =>
        FullFrame.BlackIntervals;

    /// <summary>
    /// Convenience projection of the authoritative full-frame target result.
    /// </summary>
    public IReadOnlyList<FreezeInterval> FreezeIntervals =>
        FullFrame.FreezeIntervals;

    public IReadOnlyList<SilenceInterval> SilenceIntervals =>
        _silenceIntervals;

    public IReadOnlyList<AudioSignalSample> AudioSignalSamples =>
        _audioSignalSamples;

    public IReadOnlyList<AudioSignalCoverage>
        AudioSignalCoverages =>
        _audioSignalCoverages;

    public MediaEvidenceAnalysisManifest Manifest { get; }

    public IReadOnlyList<MediaEvidenceWarning> Warnings =>
        _warnings;

    private static void RejectNullItems<TValue>(
        IReadOnlyList<TValue> values,
        string parameterName)
        where TValue : class
    {
        if (values.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Evidence collections cannot contain null values.",
                parameterName);
        }
    }
}
