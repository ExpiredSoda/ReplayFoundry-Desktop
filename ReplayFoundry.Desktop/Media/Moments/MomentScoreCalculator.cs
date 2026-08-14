namespace ReplayFoundry.Desktop.Media.Moments;

internal sealed record ScoredMomentProposal(
    MomentScore Score,
    double FullFrameBlackRatio,
    double FullFrameFreezeRatio,
    double GameplayBlackRatio,
    double GameplayFreezeRatio,
    IReadOnlyList<MomentEvidenceReference> IntegrityReferences,
    MomentEpisodeFeatureVector? EpisodeFeatures,
    StandaloneEpisodeFeatureVector? StandaloneFeatures,
    MontageSegmentFeatureVector? MontageFeatures);

internal static class MomentScoreCalculator
{
    public static ScoredMomentProposal Score(
        MediaMomentFindingRequest request,
        NormalizedMomentSignals signals,
        MomentActivationSeries activation,
        ProposedMomentWindow proposal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MomentSignalScoreMeasurements signalMeasurements =
            MomentScoreMeasurementCalculator.MeasureSignals(
                request,
                signals,
                proposal);
        MomentIntegrityScoreMeasurements integrityMeasurements =
            MomentScoreMeasurementCalculator.MeasureIntegrity(
                request,
                signals,
                proposal);
        MomentEpisodeScoreMeasurements episodeMeasurements =
            MomentScoreMeasurementCalculator.MeasureEpisode(
                request,
                signals,
                activation,
                proposal);
        var components = new List<MomentScoreComponent>();

        MomentSignalScoreComponentBuilder.AddComponents(
            components,
            request,
            proposal,
            signalMeasurements,
            integrityMeasurements);
        MomentEpisodeScoreComponentBuilder.AddComponents(
            components,
            request,
            proposal,
            signalMeasurements,
            episodeMeasurements);

        MomentEvidenceReference[] integrityReferences = components
            .Where(static component =>
                component.Code is
                    MomentScoreComponentCode.FullFrameBlackPenalty or
                    MomentScoreComponentCode.FullFrameFreezePenalty)
            .SelectMany(static component => component.EvidenceReferences)
            .ToArray();

        return new ScoredMomentProposal(
            new MomentScore(components),
            integrityMeasurements.FullFrameBlack,
            integrityMeasurements.FullFrameFreeze,
            integrityMeasurements.GameplayBlack,
            integrityMeasurements.GameplayFreeze,
            integrityReferences,
            episodeMeasurements.Features,
            episodeMeasurements.StandaloneFeatures,
            episodeMeasurements.MontageFeatures);
    }
}
