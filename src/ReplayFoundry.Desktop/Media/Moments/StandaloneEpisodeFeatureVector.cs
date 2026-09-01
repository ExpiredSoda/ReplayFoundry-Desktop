namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class StandaloneEpisodeFeatureVector
{
    public StandaloneEpisodeFeatureVector(
        double episodeCompleteness,
        double onsetContext,
        double recoveryContext,
        double contextAvailability,
        double sceneCoherence,
        double lowContinuousActivityDominance,
        double rankingValue)
    {
        EpisodeCompleteness =
            Ratio(episodeCompleteness, nameof(episodeCompleteness));
        OnsetContext = Ratio(onsetContext, nameof(onsetContext));
        RecoveryContext = Ratio(recoveryContext, nameof(recoveryContext));
        ContextAvailability =
            Ratio(contextAvailability, nameof(contextAvailability));
        SceneCoherence = Ratio(sceneCoherence, nameof(sceneCoherence));
        LowContinuousActivityDominance =
            Ratio(
                lowContinuousActivityDominance,
                nameof(lowContinuousActivityDominance));
        RankingValue = Ratio(rankingValue, nameof(rankingValue));
    }

    public double EpisodeCompleteness { get; }
    public double OnsetContext { get; }
    public double RecoveryContext { get; }
    public double ContextAvailability { get; }
    public double SceneCoherence { get; }
    public double LowContinuousActivityDominance { get; }
    public double RankingValue { get; }

    private static double Ratio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}
