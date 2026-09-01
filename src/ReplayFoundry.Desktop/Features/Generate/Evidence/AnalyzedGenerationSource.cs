using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;

namespace ReplayFoundry.Desktop.Features.Generate.Evidence;

public sealed class AnalyzedGenerationSource
{
    public AnalyzedGenerationSource(
        PreparedGenerationSource preparedSource,
        PreparedSourceCompositionPlan compositionPlan,
        MediaEvidenceResult evidence,
        MediaEvidenceSummary summary,
        GenerationEvidenceAnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(preparedSource);
        ArgumentNullException.ThrowIfNull(compositionPlan);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(settings);

        if (!ReferenceEquals(
                preparedSource,
                compositionPlan.PreparedSource))
        {
            throw new ArgumentException(
                "The analyzed source and composition plan must preserve prepared-source identity.",
                nameof(compositionPlan));
        }

        if (!string.Equals(
                preparedSource.Media.FullPath,
                evidence.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Evidence must describe the prepared source path.",
                nameof(evidence));
        }

        if (preparedSource.Media.Duration !=
                evidence.SourceDuration ||
            compositionPlan.Plan.SourceDuration !=
                evidence.SourceDuration)
        {
            throw new ArgumentException(
                "Evidence duration must exactly match the prepared media and composition plan.",
                nameof(evidence));
        }

        if (!evidence.Manifest.CompositionPlanSupplied)
        {
            throw new ArgumentException(
                "Desktop generation requires composition-aware evidence.",
                nameof(evidence));
        }

        if (!string.Equals(
                evidence.Manifest.SignalSchemaVersion,
                MediaSignalEvidencePolicy
                    .CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Evidence uses an unsupported continuous-signal schema.",
                nameof(evidence));
        }

        if (!evidence.Manifest
            .RequestedIncludedRegionRoles
            .SequenceEqual(
                settings.IncludedRegionRoles))
        {
            throw new ArgumentException(
                "Evidence requested roles do not match the generation evidence settings.",
                nameof(evidence));
        }

        if (!AnalysisOptionsMatch(
                evidence.Manifest.Options,
                settings.Options))
        {
            throw new ArgumentException(
                "Evidence analysis options do not match the generation evidence settings.",
                nameof(evidence));
        }

        if (!string.Equals(
                evidence.Manifest.CompositionSchemaVersion,
                compositionPlan.Plan.Manifest.SchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                evidence.Manifest.CompositionCoordinateSpaceVersion,
                compositionPlan.Plan.Manifest.CoordinateSpaceVersion,
                StringComparison.Ordinal) ||
            evidence.Manifest.CompositionPlanOrigin !=
                compositionPlan.Plan.Manifest.Origin)
        {
            throw new ArgumentException(
                "Evidence composition provenance does not match the confirmed plan.",
                nameof(evidence));
        }

        if (summary.SourceDuration !=
            evidence.SourceDuration)
        {
            throw new ArgumentException(
                "The evidence summary duration must match its evidence result.",
                nameof(summary));
        }

        if (!SummaryOptionsMatch(
                summary.Options,
                settings.SummaryOptions))
        {
            throw new ArgumentException(
                "Evidence summary options do not match the generation evidence settings.",
                nameof(summary));
        }

        string[] evidenceRegionKeys =
            evidence.RegionVisualResults
                .Select(
                    static item =>
                        item.Target.TargetKey)
                .ToArray();

        string[] summaryRegionKeys =
            summary.RegionTargets
                .Select(
                    static item =>
                        item.Target.TargetKey)
                .ToArray();

        if (!evidenceRegionKeys.SequenceEqual(
                summaryRegionKeys,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Every region evidence target requires one matching summary.",
                nameof(summary));
        }

        if (!ReferenceEquals(
                summary.FullFrameSignals.Target,
                evidence.FullFrame.Target) ||
            summary.FullFrameSignals.SampleCount !=
                evidence.FullFrame.SignalSamples.Count ||
            summary.RegionTargets.Any(
                regionSummary =>
                    regionSummary.Signals.SampleCount !=
                    evidence.RegionVisualResults
                        .Single(
                            result =>
                                string.Equals(
                                    result.Target.TargetKey,
                                    regionSummary.Target.TargetKey,
                                    StringComparison.Ordinal))
                        .SignalSamples.Count))
        {
            throw new ArgumentException(
                "Visual signal summaries must match the authoritative target samples.",
                nameof(summary));
        }

        int[] expectedAudioIndices =
            preparedSource.Media.AudioStreams
                .Select(
                    static stream =>
                        stream.Index)
                .OrderBy(
                    static index =>
                        index)
                .ToArray();

        int[] summaryAudioIndices =
            summary.AudioSignalStreams
                .Select(
                    static stream =>
                        stream.AudioStreamIndex)
                .OrderBy(
                    static index =>
                        index)
                .ToArray();

        if (!expectedAudioIndices.SequenceEqual(
                summaryAudioIndices) ||
            summary.AudioSignalStreams.Any(
                signalSummary =>
                    signalSummary.SampleCount !=
                    evidence.AudioSignalSamples.Count(
                        sample =>
                            sample.AudioStreamIndex ==
                            signalSummary.AudioStreamIndex)))
        {
            throw new ArgumentException(
                "Audio signal summaries must match every prepared audio stream and its authoritative samples.",
                nameof(summary));
        }

        PreparedSource = preparedSource;
        CompositionPlan = compositionPlan;
        Evidence = evidence;
        Summary = summary;
    }

    public PreparedGenerationSource PreparedSource { get; }

    public PreparedSourceCompositionPlan CompositionPlan { get; }

    public MediaEvidenceResult Evidence { get; }

    public MediaEvidenceSummary Summary { get; }

    private static bool AnalysisOptionsMatch(
        MediaEvidenceAnalysisOptions left,
        MediaEvidenceAnalysisOptions right)
    {
        return left.SceneThresholdPercent ==
                   right.SceneThresholdPercent &&
               left.MinimumBlackDuration ==
                   right.MinimumBlackDuration &&
               left.BlackPixelThreshold ==
                   right.BlackPixelThreshold &&
               left.BlackPictureRatio ==
                   right.BlackPictureRatio &&
               left.MinimumFreezeDuration ==
                   right.MinimumFreezeDuration &&
               left.FreezeNoiseToleranceDb ==
                   right.FreezeNoiseToleranceDb &&
               left.MinimumSilenceDuration ==
                   right.MinimumSilenceDuration &&
               left.SilenceNoiseThresholdDb ==
                   right.SilenceNoiseThresholdDb &&
               left.ProcessTimeout ==
                   right.ProcessTimeout &&
               left.VisualSignalSampleInterval ==
                   right.VisualSignalSampleInterval &&
               left.AudioSignalWindowDuration ==
                   right.AudioSignalWindowDuration;
    }

    private static bool SummaryOptionsMatch(
        MediaEvidenceSummaryOptions left,
        MediaEvidenceSummaryOptions right)
    {
        return left.SceneClusterMaximumGap ==
                   right.SceneClusterMaximumGap &&
               left.SceneDensityBucketDuration ==
                   right.SceneDensityBucketDuration &&
               left.SilenceMergeTolerance ==
                   right.SilenceMergeTolerance &&
               left.ShortSilenceMaximum ==
                   right.ShortSilenceMaximum &&
               left.LongSilenceMinimum ==
                   right.LongSilenceMinimum &&
               left.DarkLumaThreshold ==
                   right.DarkLumaThreshold &&
               left.BrightLumaThreshold ==
                   right.BrightLumaThreshold &&
               string.Equals(
                   left.SignalSummaryPolicyVersion,
                   right.SignalSummaryPolicyVersion,
                   StringComparison.Ordinal);
    }
}
