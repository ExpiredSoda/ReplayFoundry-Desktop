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
using static ReplayFoundry.Desktop.Platform.Media.FfmpegVisualIntervalPairing;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceParseAccumulators
{
    internal sealed class TargetAccumulator
    {
        public TargetAccumulator(
            VisualEvidenceTarget target)
        {
            Target = target;
        }

        public VisualEvidenceTarget Target { get; }

        public SortedDictionary<TimeSpan, SceneBoundary>
            Scenes
        { get; } =
            [];

        public SortedDictionary<TimeSpan, VisualSignalSample>
            Signals
        { get; } =
            [];

        public List<IntervalEvent> BlackEvents { get; } =
            [];

        public List<IntervalEvent> FreezeEvents { get; } =
            [];

        public List<MediaEvidenceWarning> Warnings { get; } =
            [];

        public VisualTargetEvidenceResult Build(
            TimeSpan visualSignalSampleInterval)
        {
            IReadOnlyList<BlackInterval> blackIntervals =
                PairTargetIntervals(
                    BlackEvents,
                    this,
                    "black",
                    static (start, end) =>
                        new BlackInterval(
                            start,
                            end));

            IReadOnlyList<FreezeInterval> freezeIntervals =
                PairTargetIntervals(
                    FreezeEvents,
                    this,
                    "freeze",
                    static (start, end) =>
                        new FreezeInterval(
                            start,
                            end));

            VisualSignalSample[] signals =
                Signals.Values
                    .Select(
                        static (sample, index) =>
                            index == 0 &&
                            sample.NormalizedActivity is not null
                                ? new VisualSignalSample(
                                    sample.TargetKey,
                                    sample.Timestamp,
                                    sample.NormalizedMeanLuma,
                                    sample.NormalizedLowLuma,
                                    sample.NormalizedHighLuma,
                                    sample.NormalizedMeanSaturation,
                                    normalizedActivity: null,
                                    sample.SignalBitDepth)
                                : sample)
                    .ToArray();

            VisualSignalCoverage signalCoverage =
                CreateVisualCoverage(
                    Target,
                    visualSignalSampleInterval,
                    signals);

            return new VisualTargetEvidenceResult(
                Target,
                Scenes.Values,
                blackIntervals,
                freezeIntervals,
                signals,
                signalCoverage,
                Warnings);
        }
    }

    internal sealed record IntervalEvent(
        TimeSpan Timestamp,
        bool IsStart);

    internal sealed record ParsedDbfs(
        DbfsValueKind Kind,
        double? Value);

    internal enum DbfsValueKind
    {
        Missing,
        Finite,
        NegativeInfinity,
        Invalid,
    }
}
