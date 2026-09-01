using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

internal static class VisualTargetSignalSummaryBuilder
{
    internal static VisualTargetSignalSummary Build(
        VisualTargetEvidenceResult result,
        MediaEvidenceSummaryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        options ??=
            MediaEvidenceSummaryOptions.CreateDefault();

        VisualSignalSample[] samples =
            result.SignalSamples
                .OrderBy(
                    static sample =>
                        sample.Timestamp)
                .ToArray();

        double[] activity =
            samples
                .Where(
                    static sample =>
                        sample.NormalizedActivity is not null)
                .Select(
                    static sample =>
                        sample.NormalizedActivity!.Value)
                .OrderBy(
                    static value =>
                        value)
                .ToArray();

        if (samples.Length == 0)
        {
            return new VisualTargetSignalSummary(
                result.Target,
                options.SignalSummaryPolicyVersion,
                options.DarkLumaThreshold,
                options.BrightLumaThreshold,
                sampleCount: 0,
                meanNormalizedActivity: null,
                medianNormalizedActivity: null,
                maximumNormalizedActivity: null,
                meanNormalizedLuma: null,
                normalizedLumaStandardDeviation: null,
                meanNormalizedContrastSpan: null,
                meanNormalizedSaturation: null,
                darkestSampleTimestamp: null,
                darkestNormalizedMeanLuma: null,
                brightestSampleTimestamp: null,
                brightestNormalizedMeanLuma: null,
                darkSamplePercentage: null,
                brightSamplePercentage: null,
                result.SignalCoverage.MaximumObservedGap);
        }

        double meanLuma =
            samples.Average(
                static sample =>
                    sample.NormalizedMeanLuma);

        double variance =
            samples.Average(
                sample =>
                {
                    double difference =
                        sample.NormalizedMeanLuma -
                        meanLuma;

                    return difference *
                           difference;
                });

        VisualSignalSample darkest =
            samples
                .OrderBy(
                    static sample =>
                        sample.NormalizedMeanLuma)
                .ThenBy(
                    static sample =>
                        sample.Timestamp)
                .First();

        VisualSignalSample brightest =
            samples
                .OrderByDescending(
                    static sample =>
                        sample.NormalizedMeanLuma)
                .ThenBy(
                    static sample =>
                        sample.Timestamp)
                .First();

        return new VisualTargetSignalSummary(
            result.Target,
            options.SignalSummaryPolicyVersion,
            options.DarkLumaThreshold,
            options.BrightLumaThreshold,
            samples.Length,
            activity.Length == 0
                ? null
                : activity.Average(),
            activity.Length == 0
                ? null
                : MediaEvidenceSummaryMath.CalculateMedian(activity),
            activity.Length == 0
                ? null
                : activity[^1],
            meanLuma,
            Math.Sqrt(variance),
            samples.Average(
                static sample =>
                    sample.NormalizedLumaSpan),
            samples.Average(
                static sample =>
                    sample.NormalizedMeanSaturation),
            darkest.Timestamp,
            darkest.NormalizedMeanLuma,
            brightest.Timestamp,
            brightest.NormalizedMeanLuma,
            samples.Count(
                sample =>
                    sample.NormalizedMeanLuma <
                    options.DarkLumaThreshold) /
            (double)samples.Length *
            100,
            samples.Count(
                sample =>
                    sample.NormalizedMeanLuma >
                    options.BrightLumaThreshold) /
            (double)samples.Length *
            100,
            result.SignalCoverage.MaximumObservedGap);
    }
}
