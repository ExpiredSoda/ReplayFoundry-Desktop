namespace ReplayFoundry.Desktop.Media.Moments;

internal static class StandaloneEpisodeRanker
{
    public static StandaloneEpisodeFeatureVector Rank(
        ProposedMomentWindow proposal,
        MomentEpisodeFeatureVector shared)
    {
        MomentEventEpisode episode =
            proposal.Episode ??
            throw new ArgumentException(
                "Standalone ranking requires an episode.",
                nameof(proposal));
        double completeness =
            Math.Clamp(
                OverlapSeconds(
                    proposal.Window.Start,
                    proposal.Window.End,
                    episode.Start,
                    episode.End) /
                episode.Duration.TotalSeconds,
                0,
                1);
        double onsetContext =
            proposal.Window.Start <= episode.OnsetTimestamp
                ? 1
                : 0;
        double recoveryContext =
            proposal.Window.End >= episode.End
                ? 1
                : 0;
        double contextAvailability =
            proposal.ContextAllocation is null
                ? (onsetContext + recoveryContext) / 2
                : (
                    Fraction(
                        proposal.ContextAllocation.AchievedLeadIn,
                        proposal.ContextAllocation.RequestedLeadIn) +
                    Fraction(
                        proposal.ContextAllocation.AchievedRecovery,
                        proposal.ContextAllocation.RequestedRecovery)
                ) / 2;
        double lowContinuous =
            1 - shared.ContinuousUniformityPenalty;
        double ranking =
            Math.Clamp(
                shared.Distinctiveness * 0.32 +
                completeness * 0.24 +
                shared.OnsetStrength * 0.12 +
                shared.RecoverySupport * 0.10 +
                contextAvailability * 0.10 +
                shared.SceneClusterSupport * 0.06 +
                lowContinuous * 0.06,
                0,
                1);
        return new StandaloneEpisodeFeatureVector(
            completeness,
            onsetContext,
            recoveryContext,
            contextAvailability,
            shared.SceneClusterSupport,
            lowContinuous,
            ranking);
    }

    private static double Fraction(
        TimeSpan achieved,
        TimeSpan requested) =>
        requested <= TimeSpan.Zero
            ? 1
            : Math.Clamp(
                achieved.TotalSeconds /
                requested.TotalSeconds,
                0,
                1);

    private static double OverlapSeconds(
        TimeSpan leftStart,
        TimeSpan leftEnd,
        TimeSpan rightStart,
        TimeSpan rightEnd)
    {
        TimeSpan start =
            leftStart > rightStart ? leftStart : rightStart;
        TimeSpan end =
            leftEnd < rightEnd ? leftEnd : rightEnd;
        return end <= start ? 0 : (end - start).TotalSeconds;
    }
}
