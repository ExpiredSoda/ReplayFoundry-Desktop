using ReplayFoundry.Desktop.Media.Analysis.Signals.Audio;
using ReplayFoundry.Desktop.Media.Analysis.Signals.Visual;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentSignalNormalizer
{
    public static NormalizedMomentSignals Normalize(
        MediaMomentFindingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        NormalizedVisualMomentSample[] gameplay =
            BuildVisual(
                request,
                CompositionRegionRole.Gameplay,
                cancellationToken);
        NormalizedVisualMomentSample[] presenter =
            request.Options.SignalAblation is
                MomentSignalAblation.NoPresenter or
                MomentSignalAblation.GameplayOnly
                ? []
                : BuildVisual(
                    request,
                    CompositionRegionRole.Presenter,
                    cancellationToken);
        NormalizedAudioMomentSample[] audio =
            request.Options.SignalAblation is
                MomentSignalAblation.NoAudio or
                MomentSignalAblation.GameplayOnly
                ? []
                : BuildAudio(
                    request,
                    cancellationToken);

        ActivityBurst[] gameplayBursts =
            ActivityBurstDetector.Detect(
                request,
                gameplay,
                cancellationToken);
        ActivityBurst[] presenterBursts =
            ActivityBurstDetector.Detect(
                request,
                presenter,
                cancellationToken);
        AudioNoveltyEvent[] audioEvents =
            AudioNoveltyDetector.Detect(
                request,
                audio,
                cancellationToken);

        return new NormalizedMomentSignals(
            gameplay,
            presenter,
            audio,
            request.Options.SignalAblation ==
                MomentSignalAblation.NoScene
                ? []
                : MomentGameplaySceneBoundaryCollector.Collect(
                    request.Evidence,
                    cancellationToken),
            gameplayBursts,
            presenterBursts,
            audioEvents);
    }

    private static NormalizedVisualMomentSample[] BuildVisual(
        MediaMomentFindingRequest request,
        CompositionRegionRole role,
        CancellationToken cancellationToken)
    {
        var output =
            new List<NormalizedVisualMomentSample>();

        foreach (var result in
                 request.Evidence.RegionVisualResults
                     .Where(result => result.Target.Role == role)
                     .OrderBy(static result => result.Target.Start)
                     .ThenBy(static result => result.Target.TargetKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            VisualSignalSample[] samples =
                result.SignalSamples
                    .Where(static sample => sample.NormalizedActivity is not null)
                    .OrderBy(static sample => sample.Timestamp)
                    .ToArray();
            IReadOnlyList<LocalSignalContext> contexts =
                LocalProminenceCalculator.Calculate(
                    samples.Select(
                        static sample =>
                            new LocalSignalSample(
                                sample.Timestamp,
                                sample.NormalizedActivity!.Value)),
                    request.Options.CalibrationPolicy,
                    cancellationToken);

            for (int index = 0; index < samples.Length; index++)
            {
                output.Add(
                    new NormalizedVisualMomentSample(
                        samples[index],
                        result.Target.RegionId!,
                        result.Target.IntervalIndex!.Value,
                        role,
                        contexts[index]));
            }
        }

        return output
            .OrderBy(static sample => sample.Sample.Timestamp)
            .ThenBy(static sample => sample.Sample.TargetKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static NormalizedAudioMomentSample[] BuildAudio(
        MediaMomentFindingRequest request,
        CancellationToken cancellationToken)
    {
        var output =
            new List<NormalizedAudioMomentSample>();

        foreach (IGrouping<int, AudioSignalSample> stream in
                 request.Evidence.AudioSignalSamples
                     .GroupBy(static sample => sample.AudioStreamIndex)
                     .OrderBy(static group => group.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AudioSignalSample[] samples =
                stream
                    .OrderBy(static sample => sample.Start)
                    .ToArray();
            AudioSignalSample[] finite =
                samples
                    .Where(
                        static sample =>
                            !sample.IsDigitalSilence &&
                            sample.RmsLevelDbfs is not null)
                    .ToArray();

            // dBFS uses a different physical unit than normalized visual
            // activity. A two-decibel robust floor prevents tiny encoder
            // fluctuations from becoming novelty events.
            MomentCalibrationPolicy audioPolicy =
                CreateAudioPolicy(
                    request.Options.CalibrationPolicy);
            IReadOnlyList<LocalSignalContext> contexts =
                LocalProminenceCalculator.Calculate(
                    finite.Select(
                        static sample =>
                            new LocalSignalSample(
                                sample.Start,
                                sample.RmsLevelDbfs!.Value)),
                    audioPolicy,
                    cancellationToken);
            var byTimestamp =
                contexts.ToDictionary(
                    static context => context.Timestamp);

            output.AddRange(
                samples.Select(
                    sample =>
                        new NormalizedAudioMomentSample(
                            sample,
                            byTimestamp.GetValueOrDefault(sample.Start))));
        }

        return output
            .OrderBy(static sample => sample.Sample.Start)
            .ThenBy(static sample => sample.Sample.AudioStreamIndex)
            .ToArray();
    }

    private static MomentCalibrationPolicy CreateAudioPolicy(
        MomentCalibrationPolicy basis) =>
        new(
            basis.LocalBaselineHalfWindow,
            basis.LocalBaselineGuardHalfWindow,
            basis.OnsetLookback,
            Math.Max(2, basis.ProminenceSpreadFloor),
            basis.ProminenceSaturationMultiple,
            basis.MinimumBurstProminence,
            basis.MinimumBurstOnset,
            basis.BurstStartThreshold,
            basis.BurstEndThreshold,
            basis.MinimumBurstDuration,
            basis.MaximumBurstMergeGap,
            basis.ContinuousActivityPenaltyWindow,
            basis.ContinuousActivityOccupancyThreshold,
            basis.EventNeighborhoodMaximumGap,
            basis.NeighborhoodValleyProminenceThreshold,
            basis.MinimumNeighborhoodValleyDuration,
            basis.MontageMinimumCooldown,
            basis.ClusterLeadInShare,
            basis.BurstLeadInShare,
            basis.MinimumLeadInContext,
            basis.MinimumPayoffContext,
            basis.SourceEdgeReallocationPolicyVersion,
            basis.Version);
}
