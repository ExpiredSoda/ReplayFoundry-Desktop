namespace ReplayFoundry.Desktop.Media.Moments;

internal static class MontageSegmentRanker
{
    public static MontageSegmentFeatureVector Rank(
        ProposedMomentWindow proposal,
        MomentEpisodeFeatureVector shared)
    {
        MomentEventEpisode episode =
            proposal.Episode ??
            throw new ArgumentException(
                "Montage ranking requires an episode.",
                nameof(proposal));
        double representative =
            proposal.MontageObjective?.ObjectiveScore ?? 0;
        double peak =
            proposal.Window.Contains(
                episode.PrimaryPeakTimestamp)
                ? 1
                : 0;
        double density =
            Math.Clamp(
                episode.IntegratedActivation /
                Math.Max(
                    0.000001,
                    proposal.Window.Duration.TotalSeconds),
                0,
                1);
        double familyDensity =
            Math.Clamp(
                shared.IndependentFamilyAgreement *
                (0.5 + density * 0.5),
                0,
                1);
        double ranking =
            Math.Clamp(
                representative * 0.32 +
                shared.Distinctiveness * 0.24 +
                density * 0.18 +
                peak * 0.12 +
                familyDensity * 0.10 +
                (1 - shared.CorrelatedVisualSupport) * 0.04,
                0,
                1);
        return new MontageSegmentFeatureVector(
            representative,
            peak,
            density,
            familyDensity,
            ranking);
    }
}
