namespace ReplayFoundry.Desktop.Media.Moments;

public sealed class MomentEpisodePolicy
{
    public const string CurrentVersion = "1.3";

    public MomentEpisodePolicy(
        double episodeStartActivationThreshold,
        double episodeContinueActivationThreshold,
        double episodeEndActivationThreshold,
        TimeSpan minimumEpisodeDuration,
        TimeSpan maximumEpisodeDuration,
        TimeSpan maximumEpisodeBridgeGap,
        double minimumEpisodeIntegratedActivation,
        double minimumEpisodePeakActivation,
        TimeSpan minimumRecoveryDuration,
        double recoveryActivationThreshold,
        TimeSpan episodeSmoothingHalfWindow,
        double splitValleyActivationThreshold,
        TimeSpan minimumSplitValleyDuration,
        string version = CurrentVersion)
    {
        ValidateRatio(episodeStartActivationThreshold, nameof(episodeStartActivationThreshold));
        ValidateRatio(episodeContinueActivationThreshold, nameof(episodeContinueActivationThreshold));
        ValidateRatio(episodeEndActivationThreshold, nameof(episodeEndActivationThreshold));
        ValidateRatio(minimumEpisodeIntegratedActivation, nameof(minimumEpisodeIntegratedActivation));
        ValidateRatio(minimumEpisodePeakActivation, nameof(minimumEpisodePeakActivation));
        ValidateRatio(recoveryActivationThreshold, nameof(recoveryActivationThreshold));
        ValidateRatio(splitValleyActivationThreshold, nameof(splitValleyActivationThreshold));

        if (episodeStartActivationThreshold < episodeContinueActivationThreshold ||
            episodeContinueActivationThreshold < episodeEndActivationThreshold ||
            splitValleyActivationThreshold > episodeContinueActivationThreshold)
        {
            throw new ArgumentException("Episode hysteresis and split thresholds are not ordered.");
        }

        ValidatePositive(minimumEpisodeDuration, nameof(minimumEpisodeDuration));
        ValidatePositive(maximumEpisodeDuration, nameof(maximumEpisodeDuration));
        ValidateNonNegative(maximumEpisodeBridgeGap, nameof(maximumEpisodeBridgeGap));
        ValidateNonNegative(minimumRecoveryDuration, nameof(minimumRecoveryDuration));
        ValidateNonNegative(episodeSmoothingHalfWindow, nameof(episodeSmoothingHalfWindow));
        ValidatePositive(minimumSplitValleyDuration, nameof(minimumSplitValleyDuration));

        if (minimumEpisodeDuration > maximumEpisodeDuration)
        {
            throw new ArgumentException("Minimum episode duration cannot exceed the maximum.");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Episode policy version cannot be blank.", nameof(version));
        }

        EpisodeStartActivationThreshold = episodeStartActivationThreshold;
        EpisodeContinueActivationThreshold = episodeContinueActivationThreshold;
        EpisodeEndActivationThreshold = episodeEndActivationThreshold;
        MinimumEpisodeDuration = minimumEpisodeDuration;
        MaximumEpisodeDuration = maximumEpisodeDuration;
        MaximumEpisodeBridgeGap = maximumEpisodeBridgeGap;
        MinimumEpisodeIntegratedActivation = minimumEpisodeIntegratedActivation;
        MinimumEpisodePeakActivation = minimumEpisodePeakActivation;
        MinimumRecoveryDuration = minimumRecoveryDuration;
        RecoveryActivationThreshold = recoveryActivationThreshold;
        EpisodeSmoothingHalfWindow = episodeSmoothingHalfWindow;
        SplitValleyActivationThreshold = splitValleyActivationThreshold;
        MinimumSplitValleyDuration = minimumSplitValleyDuration;
        Version = version.Trim();
    }

    public double EpisodeStartActivationThreshold { get; }
    public double EpisodeContinueActivationThreshold { get; }
    public double EpisodeEndActivationThreshold { get; }
    public TimeSpan MinimumEpisodeDuration { get; }
    public TimeSpan MaximumEpisodeDuration { get; }
    public TimeSpan MaximumEpisodeBridgeGap { get; }
    public double MinimumEpisodeIntegratedActivation { get; }
    public double MinimumEpisodePeakActivation { get; }
    public TimeSpan MinimumRecoveryDuration { get; }
    public double RecoveryActivationThreshold { get; }
    public TimeSpan EpisodeSmoothingHalfWindow { get; }
    public double SplitValleyActivationThreshold { get; }
    public TimeSpan MinimumSplitValleyDuration { get; }
    public string Version { get; }

    public static MomentEpisodePolicy CreateDefaults() =>
        new(
            0.18,
            0.10,
            0.06,
            TimeSpan.FromSeconds(1.5),
            TimeSpan.FromSeconds(90),
            TimeSpan.FromSeconds(2),
            0.08,
            0.20,
            TimeSpan.FromSeconds(2),
            0.08,
            TimeSpan.FromSeconds(1),
            0.05,
            TimeSpan.FromSeconds(1));

    private static void ValidateRatio(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidateNonNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
