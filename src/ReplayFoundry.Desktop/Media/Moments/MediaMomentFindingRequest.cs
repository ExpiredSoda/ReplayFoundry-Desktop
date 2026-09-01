using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;
using System.IO;

namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MediaMomentFindingRequest
{
    public MediaMomentFindingRequest(
        MediaProbeResult media,
        CompositionPlan composition,
        MediaEvidenceResult evidence,
        MediaEvidenceSummary summary,
        MediaMomentFindingOptions options,
        MediaMomentGuidance? guidance = null)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(options);

        if (!PathsMatch(media.FullPath, composition.SourcePath) ||
            !PathsMatch(media.FullPath, evidence.FullPath))
        {
            throw new ArgumentException(
                "Media, composition, and evidence paths must match.");
        }

        if (media.Container.Duration != composition.SourceDuration ||
            media.Container.Duration != evidence.SourceDuration ||
            media.Container.Duration != summary.SourceDuration)
        {
            throw new ArgumentException(
                "Media, composition, evidence, and summary durations must match exactly.");
        }

        if (composition.CoordinateSpace !=
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop)
        {
            throw new ArgumentException(
                "Moment finding requires effective-display normalized composition geometry.",
                nameof(composition));
        }

        if (!composition.Intervals
            .SelectMany(static interval => interval.Regions)
            .Any(static region => region.Role == CompositionRegionRole.Gameplay))
        {
            throw new ArgumentException(
                "Moment finding requires at least one confirmed Gameplay region.",
                nameof(composition));
        }

        if (evidence.Manifest.Coverage != AnalysisCoverage.FullTimeline ||
            string.IsNullOrWhiteSpace(
                evidence.Manifest.CompositionSchemaVersion))
        {
            throw new ArgumentException(
                "Moment finding requires composition-aware deterministic evidence.",
                nameof(evidence));
        }

        if (string.IsNullOrWhiteSpace(evidence.Manifest.AnalyzerName) ||
            string.IsNullOrWhiteSpace(evidence.Manifest.AnalyzerVersion))
        {
            throw new ArgumentException(
                "Evidence analyzer identity must be present.",
                nameof(evidence));
        }

        if (evidence.Manifest.CompositionSchemaVersion != composition.Manifest.SchemaVersion ||
            evidence.Manifest.CompositionCoordinateSpaceVersion != composition.Manifest.CoordinateSpaceVersion ||
            evidence.Manifest.CompositionPlanOrigin != composition.Manifest.Origin)
        {
            throw new ArgumentException(
                "Evidence composition provenance does not match the supplied plan.",
                nameof(evidence));
        }

        ValidateCompositionTargets(
            media,
            composition,
            evidence);

        if (!evidence.Manifest.RequestedIncludedRegionRoles.Contains(
                CompositionRegionRole.Gameplay))
        {
            throw new ArgumentException(
                "Evidence must include Gameplay in its requested region roles.",
                nameof(evidence));
        }

        string[] evidenceKeys =
            evidence.RegionVisualResults
                .Select(static result => result.Target.TargetKey)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();

        string[] summaryKeys =
            summary.RegionTargets
                .Select(static result => result.Target.TargetKey)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();

        if (!evidenceKeys.SequenceEqual(summaryKeys, StringComparer.Ordinal) ||
            summary.FullFrameSignals.Target.TargetKey !=
                evidence.FullFrame.Target.TargetKey ||
            !SummaryMatchesEvidence(
                media,
                evidence,
                summary))
        {
            throw new ArgumentException(
                "Evidence-summary targets do not match the evidence result.",
                nameof(summary));
        }

        string[] evidenceCoverageKeys =
            evidence
                .RegionVisualResults
                .Prepend(evidence.FullFrame)
                .Select(static result => result.SignalCoverage.TargetKey)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();

        string[] manifestCoverageKeys =
            evidence.Manifest.VisualSignalCoverages
                .Select(static coverage => coverage.TargetKey)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();

        if (!evidenceCoverageKeys.SequenceEqual(
                manifestCoverageKeys,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Visual signal coverage does not match the evidence targets.",
                nameof(evidence));
        }

        int[] resultAudioCoverage =
            evidence.AudioSignalCoverages
                .Select(static coverage => coverage.AudioStreamIndex)
                .OrderBy(static index => index)
                .ToArray();

        int[] manifestAudioCoverage =
            evidence.Manifest.AudioSignalCoverages
                .Select(static coverage => coverage.AudioStreamIndex)
                .OrderBy(static index => index)
                .ToArray();

        if (!resultAudioCoverage.SequenceEqual(manifestAudioCoverage))
        {
            throw new ArgumentException(
                "Audio signal coverage does not match the evidence manifest.",
                nameof(evidence));
        }

        Media = media;
        Composition = composition;
        Evidence = evidence;
        Summary = summary;
        Options = options;
        Guidance = guidance ?? MediaMomentGuidance.Empty;
        if (Guidance.Items.Any(item => item.End > media.Duration))
        {
            throw new ArgumentException(
                "Human moment guidance must stay inside the retained source duration.",
                nameof(guidance));
        }
    }

    public MediaProbeResult Media { get; }
    public CompositionPlan Composition { get; }
    public MediaEvidenceResult Evidence { get; }
    public MediaEvidenceSummary Summary { get; }
    public MediaMomentFindingOptions Options { get; }
    public MediaMomentGuidance Guidance { get; }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateCompositionTargets(
        MediaProbeResult media,
        CompositionPlan composition,
        MediaEvidenceResult evidence)
    {
        MediaEvidenceAnalysisRequest expectedRequest =
            MediaEvidenceAnalysisRequest.CreateCompositionAware(
                media,
                composition,
                evidence.Manifest.Options,
                evidence.Manifest.RequestedIncludedRegionRoles);
        VisualEvidenceTargetPlan expected =
            VisualEvidenceTargetPlanner.Create(
                expectedRequest);
        VisualEvidenceTarget[] actual =
            evidence.RegionVisualResults
                .Prepend(evidence.FullFrame)
                .Select(
                    static result =>
                        result.Target)
                .ToArray();

        if (expected.Targets.Count != actual.Length ||
            expected.Targets
                .Zip(actual)
                .Any(
                    pair =>
                        !TargetsMatch(
                            pair.First,
                            pair.Second)))
        {
            throw new ArgumentException(
                "Evidence visual targets do not match the supplied composition plan.",
                nameof(evidence));
        }
    }

    private static bool TargetsMatch(
        VisualEvidenceTarget expected,
        VisualEvidenceTarget actual) =>
        expected.TargetKey == actual.TargetKey &&
        expected.Kind == actual.Kind &&
        expected.Start == actual.Start &&
        expected.End == actual.End &&
        expected.EffectiveDisplayWidth ==
            actual.EffectiveDisplayWidth &&
        expected.EffectiveDisplayHeight ==
            actual.EffectiveDisplayHeight &&
        expected.IntervalIndex == actual.IntervalIndex &&
        string.Equals(
            expected.RegionId,
            actual.RegionId,
            StringComparison.Ordinal) &&
        expected.Role == actual.Role &&
        expected.Traits == actual.Traits &&
        RectanglesMatch(
            expected.RequestedRectangle,
            actual.RequestedRectangle) &&
        expected.ActualPixelCrop ==
            actual.ActualPixelCrop &&
        expected.GeometryConfidence ==
            actual.GeometryConfidence &&
        expected.RoleConfidence ==
            actual.RoleConfidence &&
        expected.GeometrySource ==
            actual.GeometrySource &&
        expected.RoleSource ==
            actual.RoleSource;

    private static bool RectanglesMatch(
        NormalizedRectangle? expected,
        NormalizedRectangle? actual) =>
        expected is null
            ? actual is null
            : actual is not null &&
              expected.X == actual.X &&
              expected.Y == actual.Y &&
              expected.Width == actual.Width &&
              expected.Height == actual.Height;

    private static bool SummaryMatchesEvidence(
        MediaProbeResult media,
        MediaEvidenceResult evidence,
        MediaEvidenceSummary summary)
    {
        if (summary.Scene.BoundaryCount !=
                evidence.FullFrame.SceneBoundaries.Count ||
            summary.BlackIntervalCount !=
                evidence.FullFrame.BlackIntervals.Count ||
            summary.TotalBlackDuration !=
                SumDurations(
                    evidence.FullFrame.BlackIntervals.Select(
                        static interval =>
                            interval.Duration)) ||
            summary.FreezeIntervalCount !=
                evidence.FullFrame.FreezeIntervals.Count ||
            summary.TotalFreezeDuration !=
                SumDurations(
                    evidence.FullFrame.FreezeIntervals.Select(
                        static interval =>
                            interval.Duration)) ||
            summary.FullFrameSignals.SampleCount !=
                evidence.FullFrame.SignalSamples.Count)
        {
            return false;
        }

        IReadOnlyDictionary<string, VisualTargetEvidenceSummary>
            regionSummaries =
            summary.RegionTargets.ToDictionary(
                static item =>
                    item.Target.TargetKey,
                StringComparer.Ordinal);

        foreach (VisualTargetEvidenceResult result in
                 evidence.RegionVisualResults)
        {
            if (!regionSummaries.TryGetValue(
                    result.Target.TargetKey,
                    out VisualTargetEvidenceSummary? targetSummary) ||
                targetSummary.SceneBoundaryCount !=
                    result.SceneBoundaries.Count ||
                targetSummary.BlackIntervalCount !=
                    result.BlackIntervals.Count ||
                targetSummary.TotalBlackDuration !=
                    SumDurations(
                        result.BlackIntervals.Select(
                            static interval =>
                                interval.Duration)) ||
                targetSummary.FreezeIntervalCount !=
                    result.FreezeIntervals.Count ||
                targetSummary.TotalFreezeDuration !=
                    SumDurations(
                        result.FreezeIntervals.Select(
                            static interval =>
                                interval.Duration)) ||
                targetSummary.Signals.SampleCount !=
                    result.SignalSamples.Count)
            {
                return false;
            }
        }

        int[] mediaAudioIndices =
            media.AudioStreams
                .Select(
                    static stream =>
                        stream.Index)
                .OrderBy(
                    static index =>
                        index)
                .ToArray();
        int[] silenceSummaryIndices =
            summary.AudioStreams
                .Select(
                    static stream =>
                        stream.AudioStreamIndex)
                .OrderBy(
                    static index =>
                        index)
                .ToArray();
        int[] signalSummaryIndices =
            summary.AudioSignalStreams
                .Select(
                    static stream =>
                        stream.AudioStreamIndex)
                .OrderBy(
                    static index =>
                        index)
                .ToArray();

        if (!mediaAudioIndices.SequenceEqual(
                silenceSummaryIndices) ||
            !mediaAudioIndices.SequenceEqual(
                signalSummaryIndices))
        {
            return false;
        }

        return summary.AudioStreams.All(
                   stream =>
                       stream.RawIntervalCount ==
                       evidence.SilenceIntervals.Count(
                           interval =>
                               interval.AudioStreamIndex ==
                               stream.AudioStreamIndex)) &&
               summary.AudioSignalStreams.All(
                   stream =>
                       stream.SampleCount ==
                       evidence.AudioSignalSamples.Count(
                           sample =>
                               sample.AudioStreamIndex ==
                               stream.AudioStreamIndex));
    }

    private static TimeSpan SumDurations(
        IEnumerable<TimeSpan> durations)
    {
        long ticks = 0;

        foreach (TimeSpan duration in durations)
        {
            ticks =
                checked(
                    ticks +
                    duration.Ticks);
        }

        return TimeSpan.FromTicks(ticks);
    }
}
