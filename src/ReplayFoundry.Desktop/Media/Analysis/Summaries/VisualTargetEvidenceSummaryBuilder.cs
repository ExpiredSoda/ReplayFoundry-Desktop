using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

internal static class VisualTargetEvidenceSummaryBuilder
{
    internal static VisualTargetEvidenceSummary Build(
        VisualTargetEvidenceResult result,
        MediaEvidenceSummaryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        options ??=
            MediaEvidenceSummaryOptions.CreateDefault();

        SceneBoundary[] boundaries =
            result.SceneBoundaries
                .OrderBy(
                    static item =>
                        item.Timestamp)
                .ToArray();

        double[] scores =
            boundaries
                .Where(
                    static item =>
                        item.ScorePercent is not null)
                .Select(
                    static item =>
                        item.ScorePercent!.Value)
                .OrderBy(
                    static value =>
                        value)
                .ToArray();

        TimeSpan totalBlack =
            MediaEvidenceSummaryMath.SumDurations(
                result.BlackIntervals.Select(
                    static item =>
                        item.Duration));

        TimeSpan totalFreeze =
            MediaEvidenceSummaryMath.SumDurations(
                result.FreezeIntervals.Select(
                    static item =>
                        item.Duration));

        return new VisualTargetEvidenceSummary(
            result.Target,
            boundaries.Length,
            boundaries.Length == 0
                ? null
                : boundaries[0].Timestamp,
            boundaries.Length == 0
                ? null
                : boundaries[^1].Timestamp,
            scores.Length == 0
                ? null
                : scores[^1],
            scores.Length == 0
                ? null
                : scores.Average(),
            scores.Length == 0
                ? null
                : MediaEvidenceSummaryMath.CalculateMedian(scores),
            result.BlackIntervals.Count,
            totalBlack,
            result.BlackIntervals.Count == 0
                ? TimeSpan.Zero
                : result.BlackIntervals.Max(
                    static item =>
                        item.Duration),
            result.FreezeIntervals.Count,
            totalFreeze,
            result.FreezeIntervals.Count == 0
                ? TimeSpan.Zero
                : result.FreezeIntervals.Max(
                    static item =>
                        item.Duration),
            VisualTargetSignalSummaryBuilder.Build(
                result,
                options));
    }
}
