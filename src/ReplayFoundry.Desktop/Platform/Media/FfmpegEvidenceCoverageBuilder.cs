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

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceCoverageBuilder
{
    internal static VisualSignalCoverage CreateVisualCoverage(
        VisualEvidenceTarget target,
        TimeSpan requestedSampleInterval,
        IReadOnlyList<VisualSignalSample> samples)
    {
        int? expectedCount =
            TryCalculateExpectedCount(
                target.Duration,
                requestedSampleInterval);

        var warnings =
            new List<MediaEvidenceWarning>();

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
                        .MissingVisualSignalSamples,
                    $"Target '{target.TargetKey}' produced {samples.Count} samples; expected approximately {expectedCount?.ToString(CultureInfo.InvariantCulture) ?? "an unbounded count"}.",
                    targetKey:
                        target.TargetKey));
        }

        var coverage =
            new VisualSignalCoverage(
                target.TargetKey,
                target.Start,
                target.End,
                requestedSampleInterval,
                samples.Select(
                    static sample =>
                        sample.Timestamp),
                expectedCount,
                targetIntervalTraversed: true,
                MediaSignalEvidencePolicy
                    .CurrentSchemaVersion,
                warnings);

        TimeSpan tolerance =
            TimeSpan.FromTicks(
                requestedSampleInterval.Ticks *
                3 /
                2);

        if (samples.Count > 0 &&
            coverage.MaximumObservedGap >
            tolerance)
        {
            warnings.Add(
                new MediaEvidenceWarning(
                    MediaEvidenceWarningCode
                        .IrregularVisualSignalCadence,
                    $"Target '{target.TargetKey}' has a maximum sample gap of {coverage.MaximumObservedGap}.",
                    targetKey:
                        target.TargetKey));

            coverage =
                new VisualSignalCoverage(
                    target.TargetKey,
                    target.Start,
                    target.End,
                    requestedSampleInterval,
                    samples.Select(
                        static sample =>
                            sample.Timestamp),
                    expectedCount,
                    targetIntervalTraversed: true,
                    MediaSignalEvidencePolicy
                        .CurrentSchemaVersion,
                    warnings);
        }

        return coverage;
    }

    internal static int? TryCalculateExpectedCount(
        TimeSpan duration,
        TimeSpan cadence)
    {
        double expected =
            Math.Ceiling(
                duration.Ticks /
                (double)cadence.Ticks);

        return expected <= int.MaxValue
            ? checked((int)expected)
            : null;
    }

    internal static TimeSpan CalculateMaximumAudioGap(
        TimeSpan sourceDuration,
        IReadOnlyList<AudioSignalSample> samples)
    {
        if (samples.Count == 0)
        {
            return sourceDuration;
        }

        TimeSpan maximum =
            samples[0].Start;

        for (int index = 1;
             index < samples.Count;
             index++)
        {
            TimeSpan gap =
                samples[index].Start -
                samples[index - 1].End;

            if (gap > maximum)
            {
                maximum = gap;
            }
        }

        TimeSpan trailing =
            sourceDuration -
            samples[^1].End;

        return trailing > maximum
            ? trailing
            : maximum;
    }
}
