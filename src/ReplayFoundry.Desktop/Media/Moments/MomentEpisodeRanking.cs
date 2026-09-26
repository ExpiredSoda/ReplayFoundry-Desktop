namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MomentEpisodeFeatureVectorBuilder
{
    public static MomentEpisodeFeatureVector Build(
        MediaMomentFindingRequest request,
        MomentEventEpisode episode,
        MomentActivationSeries activation,
        NormalizedMomentSignals signals)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(signals);

        MomentActivationSample[] samples =
            activation.Samples
                .Where(
                    sample =>
                        sample.Timestamp >= episode.Start &&
                        sample.Timestamp <= episode.End)
                .ToArray();
        if (samples.Length == 0)
        {
            return new MomentEpisodeFeatureVector(
                MomentEpisodeFeatureVector.CurrentVersion,
                0,
                0,
                0,
                0,
                0,
                episode.ActivationOccupancy,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                0,
                0);
        }

        double[] values =
            samples
                .Select(
                    static sample =>
                        Math.Max(
                            sample.RawCombinedActivation,
                            sample.SmoothedCombinedActivation))
                .ToArray();
        bool hasBaseline =
            episode.LocalBaselineBefore is not null;
        double baseline =
            episode.LocalBaselineBefore ?? 0;
        double peakSeparation =
            hasBaseline
                ? NormalizeSeparation(
                    episode.PeakActivation,
                    baseline)
                : 0;
        double mean = values.Average();
        double integratedSeparation =
            hasBaseline
                ? NormalizeSeparation(mean, baseline)
                : 0;
        double onset =
            samples
                .SelectMany(static sample => sample.Components)
                .Where(
                    static component =>
                        component.Code ==
                        MomentActivationComponentCode.GameplayOnset)
                .Select(static component => component.NormalizedValue ?? 0)
                .DefaultIfEmpty(0)
                .Max();

        MomentEventEpisodePhase corePhase =
            episode.Phases.Single(
                static phase =>
                    phase.Kind == MomentEventEpisodePhaseKind.Core);
        double coreMean =
            samples
                .Where(
                    sample =>
                        sample.Timestamp >= corePhase.Start &&
                        sample.Timestamp <= corePhase.End)
                .Select(
                    static sample =>
                        Math.Max(
                            sample.RawCombinedActivation,
                            sample.SmoothedCombinedActivation))
                .DefaultIfEmpty(episode.PeakActivation)
                .Average();
        double baselineCore =
            hasBaseline
                ? NormalizeSeparation(coreMean, baseline)
                : 0;
        double coreRecovery =
            episode.LocalRecoveryAfter is null
                ? 0
                : NormalizeSeparation(
                    coreMean,
                    episode.LocalRecoveryAfter.Value);
        double recoveryOrBoundary =
            episode.LocalRecoveryAfter is null
                ? peakSeparation * 0.35
                : coreRecovery;

        ActivityBurst[] gameplay =
            signals.GameplayBursts
                .Where(
                    burst =>
                        burst.Start <= episode.End &&
                        burst.End >= episode.Start)
                .ToArray();
        ActivityBurst[] presenter =
            signals.PresenterBursts
                .Where(
                    burst =>
                        burst.Start <= episode.End &&
                        burst.End >= episode.Start)
                .ToArray();
        double burstConcentration =
            gameplay
                .Select(static burst => burst.Concentration)
                .DefaultIfEmpty(0)
                .Max();
        double curveConcentration =
            episode.PeakActivation <= 0
                ? 0
                : Math.Clamp(
                    (episode.PeakActivation - mean) /
                    episode.PeakActivation,
                    0,
                    1);
        double concentration =
            Math.Max(burstConcentration, curveConcentration);

        double correlated =
            MomentVisualSupportCorrelation.Measure(
                gameplay,
                presenter,
                request.Options.CrossSignalAgreementWindow);
        double presenterIncremental =
            presenter
                .Select(
                    burst =>
                        burst.PeakProminence *
                        (1 - correlated))
                .DefaultIfEmpty(0)
                .Max();
        if (presenterIncremental <
            request.Options.DistinctivenessPolicy
                .MinimumIncrementalPresenterProminence)
        {
            presenterIncremental = 0;
        }

        var effectiveFamilies =
            episode.EvidenceSummary.DominantSignalFamilies
                .ToHashSet();
        if (correlated >=
                request.Options.DistinctivenessPolicy
                    .CorrelationThreshold &&
            presenterIncremental == 0)
        {
            effectiveFamilies.Remove(
                MomentSignalFamily.PresenterProminence);
        }

        double familyAgreement =
            Math.Clamp(
                (effectiveFamilies.Count - 1) / 3d,
                0,
                1);
        double singleFamily =
            effectiveFamilies.Count <= 1
                ? Math.Clamp(
                    Math.Max(peakSeparation, integratedSeparation),
                    0,
                    1)
                : 0;
        double sceneSupport =
            signals.GameplayScenes
                .Count(
                    scene =>
                        scene.Boundary.Timestamp >= episode.Start &&
                        scene.Boundary.Timestamp <= episode.End);
        sceneSupport = Math.Clamp(sceneSupport / 5d, 0, 1);
        double continuous =
            samples
                .SelectMany(static sample => sample.Components)
                .Where(
                    static component =>
                        component.Code ==
                        MomentActivationComponentCode
                            .ContinuousActivityPenalty)
                .Select(static component => component.NormalizedValue ?? 0)
                .DefaultIfEmpty(0)
                .Average();
        double occupancyExcess =
            Math.Clamp(
                (
                    episode.ActivationOccupancy -
                    request.Options.DistinctivenessPolicy
                        .UniformActivityOccupancyThreshold
                ) /
                Math.Max(
                    0.000001,
                    1 -
                    request.Options.DistinctivenessPolicy
                        .UniformActivityOccupancyThreshold),
                0,
                1);
        double continuousUniformity =
            Math.Clamp(
                Math.Max(continuous, occupancyExcess) *
                (
                    (1 - concentration) * 0.35 +
                    (1 - baselineCore) * 0.25 +
                    (1 - recoveryOrBoundary) * 0.20 +
                    (1 - familyAgreement) * 0.20
                ) *
                (0.50 + (1 - onset) * 0.50),
                0,
                1);
        double entropy = ActivationEntropy(values);

        MomentDistinctivenessPolicy policy =
            request.Options.DistinctivenessPolicy;
        double positive =
            peakSeparation * policy.PeakSeparationWeight +
            integratedSeparation *
                policy.IntegratedSeparationWeight +
            onset * policy.OnsetWeight +
            concentration * policy.ConcentrationWeight +
            familyAgreement * policy.FamilyAgreementWeight +
            recoveryOrBoundary *
                policy.RecoveryOrBoundaryWeight +
            baselineCore *
                policy.BaselineCoreSeparationWeight;
        double penalty =
            continuousUniformity *
                policy.ContinuousUniformityPenaltyWeight +
            singleFamily *
                policy.SingleFamilyDominancePenaltyWeight +
            correlated *
                policy.CorrelatedVisualSupportPenaltyWeight;
        double distinctiveness =
            Math.Clamp(positive - penalty, 0, 1);

        return new MomentEpisodeFeatureVector(
            MomentEpisodeFeatureVector.CurrentVersion,
            peakSeparation,
            integratedSeparation,
            onset,
            recoveryOrBoundary,
            concentration,
            episode.ActivationOccupancy,
            entropy,
            baselineCore,
            coreRecovery,
            familyAgreement,
            sceneSupport,
            Math.Max(continuous, occupancyExcess),
            continuousUniformity,
            singleFamily,
            correlated,
            presenterIncremental,
            distinctiveness);
    }

    private static double NormalizeSeparation(
        double high,
        double low) =>
        Math.Clamp(
            (high - low) /
            Math.Max(0.000001, 1 - low),
            0,
            1);

    private static double ActivationEntropy(
        IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        const int binCount = 8;
        int[] bins = new int[binCount];
        foreach (double value in values)
        {
            int index =
                Math.Min(
                    binCount - 1,
                    (int)Math.Floor(
                        Math.Clamp(value, 0, 1) * binCount));
            bins[index]++;
        }

        double entropy =
            bins
                .Where(static count => count > 0)
                .Sum(
                    count =>
                    {
                        double probability =
                            count / (double)values.Count;
                        return -probability * Math.Log(probability);
                    });
        double maximum =
            Math.Log(Math.Min(binCount, values.Count));
        return maximum <= 0
            ? 0
            : Math.Clamp(entropy / maximum, 0, 1);
    }
}
