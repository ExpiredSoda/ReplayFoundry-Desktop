using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class MediaEvidenceSummary
{
    private readonly ReadOnlyCollection<AudioStreamSilenceSummary>
        _audioStreams;

    private readonly ReadOnlyCollection<VisualTargetEvidenceSummary>
        _regionTargets;

    private readonly ReadOnlyCollection<AudioStreamSignalSummary>
        _audioSignalStreams;

    public MediaEvidenceSummary(
        TimeSpan sourceDuration,
        MediaEvidenceSummaryOptions options,
        SceneEvidenceSummary scene,
        int blackIntervalCount,
        TimeSpan totalBlackDuration,
        int freezeIntervalCount,
        TimeSpan totalFreezeDuration,
        VisualTargetSignalSummary fullFrameSignals,
        IEnumerable<VisualTargetEvidenceSummary> regionTargets,
        IEnumerable<AudioStreamSilenceSummary> audioStreams,
        IEnumerable<AudioStreamSignalSummary> audioSignalStreams)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Evidence summary requires a positive source duration.");
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(fullFrameSignals);
        ArgumentNullException.ThrowIfNull(regionTargets);
        ArgumentNullException.ThrowIfNull(audioStreams);
        ArgumentNullException.ThrowIfNull(audioSignalStreams);

        if (fullFrameSignals.Target.Kind !=
            VisualEvidenceTargetKind.FullFrame)
        {
            throw new ArgumentException(
                "The full-frame signal summary must describe the full-frame target.",
                nameof(fullFrameSignals));
        }

        if (blackIntervalCount < 0 ||
            freezeIntervalCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blackIntervalCount),
                "Visual interval counts cannot be negative.");
        }

        ValidateDuration(totalBlackDuration, sourceDuration, nameof(totalBlackDuration));
        ValidateDuration(totalFreezeDuration, sourceDuration, nameof(totalFreezeDuration));

        AudioStreamSilenceSummary[] audioSnapshot =
            audioStreams
                .OrderBy(static item => item.AudioStreamIndex)
                .ToArray();

        VisualTargetEvidenceSummary[] regionSnapshot =
            regionTargets
                .OrderBy(
                    static item =>
                        item.Target.Start)
                .ThenBy(
                    static item =>
                        item.Target.IntervalIndex)
                .ThenBy(
                    static item =>
                        item.Target.Role)
                .ThenBy(
                    static item =>
                        item.Target.RegionId,
                    StringComparer.Ordinal)
                .ThenBy(
                    static item =>
                        item.Target.TargetKey,
                    StringComparer.Ordinal)
                .ToArray();

        AudioStreamSignalSummary[] audioSignalSnapshot =
            audioSignalStreams
                .OrderBy(
                    static item =>
                        item.AudioStreamIndex)
                .ToArray();

        if (audioSnapshot.Any(static item => item is null) ||
            regionSnapshot.Any(static item => item is null) ||
            audioSignalSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Audio summaries cannot contain null items.",
                nameof(audioStreams));
        }

        if (regionSnapshot.Any(
                static item =>
                    item.Target.Kind !=
                    VisualEvidenceTargetKind
                        .CompositionRegion))
        {
            throw new ArgumentException(
                "Region summaries must describe composition targets.",
                nameof(regionTargets));
        }

        if (regionSnapshot
            .GroupBy(
                static item =>
                    item.Target.TargetKey,
                StringComparer.Ordinal)
            .Any(
                static group =>
                    group.Count() > 1))
        {
            throw new ArgumentException(
                "Region summaries cannot contain duplicate target keys.",
                nameof(regionTargets));
        }

        if (audioSnapshot
            .GroupBy(static item => item.AudioStreamIndex)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Audio summaries cannot contain duplicate stream indices.",
                nameof(audioStreams));
        }

        if (audioSignalSnapshot
            .GroupBy(
                static item =>
                    item.AudioStreamIndex)
            .Any(
                static group =>
                    group.Count() > 1))
        {
            throw new ArgumentException(
                "Audio signal summaries cannot contain duplicate stream indices.",
                nameof(audioSignalStreams));
        }

        SourceDuration = sourceDuration;
        Options = options;
        Scene = scene;
        BlackIntervalCount = blackIntervalCount;
        TotalBlackDuration = totalBlackDuration;
        FreezeIntervalCount = freezeIntervalCount;
        TotalFreezeDuration = totalFreezeDuration;
        FullFrameSignals = fullFrameSignals;
        _regionTargets =
            Array.AsReadOnly(
                regionSnapshot);
        _audioStreams = Array.AsReadOnly(audioSnapshot);
        _audioSignalStreams =
            Array.AsReadOnly(
                audioSignalSnapshot);
    }

    public TimeSpan SourceDuration { get; }

    public MediaEvidenceSummaryOptions Options { get; }

    public SceneEvidenceSummary Scene { get; }

    public int BlackIntervalCount { get; }

    public TimeSpan TotalBlackDuration { get; }

    public int FreezeIntervalCount { get; }

    public TimeSpan TotalFreezeDuration { get; }

    public VisualTargetSignalSummary FullFrameSignals { get; }

    public IReadOnlyList<VisualTargetEvidenceSummary> RegionTargets =>
        _regionTargets;

    public IReadOnlyList<AudioStreamSilenceSummary> AudioStreams =>
        _audioStreams;

    public IReadOnlyList<AudioStreamSignalSummary>
        AudioSignalStreams =>
        _audioSignalStreams;

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
                "Summary duration must be within the source duration.");
        }
    }
}
