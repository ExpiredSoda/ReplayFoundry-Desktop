using System;

namespace ReplayFoundry.Desktop.Media.Analysis.Summaries;

public sealed class MediaEvidenceSummaryOptions
{
    public const string CurrentSignalSummaryPolicyVersion =
        "1.0";

    public MediaEvidenceSummaryOptions(
        TimeSpan sceneClusterMaximumGap,
        TimeSpan sceneDensityBucketDuration,
        TimeSpan silenceMergeTolerance,
        TimeSpan shortSilenceMaximum,
        TimeSpan longSilenceMinimum,
        double darkLumaThreshold,
        double brightLumaThreshold,
        string signalSummaryPolicyVersion =
            CurrentSignalSummaryPolicyVersion)
    {
        if (sceneClusterMaximumGap < TimeSpan.Zero ||
            sceneClusterMaximumGap > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sceneClusterMaximumGap),
                sceneClusterMaximumGap,
                "Scene-cluster gap must be between zero and ten minutes.");
        }

        if (sceneDensityBucketDuration < TimeSpan.FromSeconds(1) ||
            sceneDensityBucketDuration > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sceneDensityBucketDuration),
                sceneDensityBucketDuration,
                "Scene-density bucket duration must be between one second and 24 hours.");
        }

        if (silenceMergeTolerance < TimeSpan.Zero ||
            silenceMergeTolerance > TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(silenceMergeTolerance),
                silenceMergeTolerance,
                "Silence merge tolerance must be between zero and five seconds.");
        }

        if (shortSilenceMaximum <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shortSilenceMaximum),
                shortSilenceMaximum,
                "Short-silence maximum must be greater than zero.");
        }

        if (longSilenceMinimum <= shortSilenceMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longSilenceMinimum),
                longSilenceMinimum,
                "Long-silence minimum must be greater than the short-silence maximum.");
        }

        ValidateNormalizedThreshold(
            darkLumaThreshold,
            nameof(darkLumaThreshold));
        ValidateNormalizedThreshold(
            brightLumaThreshold,
            nameof(brightLumaThreshold));

        if (brightLumaThreshold <=
            darkLumaThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(brightLumaThreshold),
                brightLumaThreshold,
                "Bright-luma threshold must exceed the dark-luma threshold.");
        }

        if (string.IsNullOrWhiteSpace(
                signalSummaryPolicyVersion))
        {
            throw new ArgumentException(
                "Signal summaries require a policy version.",
                nameof(signalSummaryPolicyVersion));
        }

        SceneClusterMaximumGap = sceneClusterMaximumGap;
        SceneDensityBucketDuration = sceneDensityBucketDuration;
        SilenceMergeTolerance = silenceMergeTolerance;
        ShortSilenceMaximum = shortSilenceMaximum;
        LongSilenceMinimum = longSilenceMinimum;
        DarkLumaThreshold = darkLumaThreshold;
        BrightLumaThreshold = brightLumaThreshold;
        SignalSummaryPolicyVersion =
            signalSummaryPolicyVersion.Trim();
    }

    public TimeSpan SceneClusterMaximumGap { get; }

    public TimeSpan SceneDensityBucketDuration { get; }

    public TimeSpan SilenceMergeTolerance { get; }

    public TimeSpan ShortSilenceMaximum { get; }

    public TimeSpan LongSilenceMinimum { get; }

    public double DarkLumaThreshold { get; }

    public double BrightLumaThreshold { get; }

    public string SignalSummaryPolicyVersion { get; }

    public static MediaEvidenceSummaryOptions CreateDefault()
    {
        return new MediaEvidenceSummaryOptions(
            sceneClusterMaximumGap: TimeSpan.FromSeconds(10),
            sceneDensityBucketDuration: TimeSpan.FromMinutes(5),
            silenceMergeTolerance: TimeSpan.FromMilliseconds(50),
            shortSilenceMaximum: TimeSpan.FromSeconds(2),
            longSilenceMinimum: TimeSpan.FromSeconds(10),
            darkLumaThreshold: 0.10,
            brightLumaThreshold: 0.85);
    }

    private static void ValidateNormalizedThreshold(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Signal-summary luma thresholds must be finite and between zero and one.");
        }
    }
}
