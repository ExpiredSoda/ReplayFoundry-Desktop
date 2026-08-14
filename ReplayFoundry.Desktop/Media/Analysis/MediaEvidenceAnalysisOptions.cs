using System;

namespace ReplayFoundry.Desktop.Media.Analysis;

public sealed class MediaEvidenceAnalysisOptions
{
    public static readonly TimeSpan MinimumSignalCadence =
        TimeSpan.FromMilliseconds(100);

    public static readonly TimeSpan MaximumSignalCadence =
        TimeSpan.FromSeconds(5);

    public static readonly TimeSpan DefaultVisualSignalSampleInterval =
        TimeSpan.FromMilliseconds(500);

    public static readonly TimeSpan DefaultAudioSignalWindowDuration =
        TimeSpan.FromMilliseconds(500);

    public MediaEvidenceAnalysisOptions(
        double sceneThresholdPercent,
        TimeSpan minimumBlackDuration,
        double blackPixelThreshold,
        double blackPictureRatio,
        TimeSpan minimumFreezeDuration,
        double freezeNoiseToleranceDb,
        TimeSpan minimumSilenceDuration,
        double silenceNoiseThresholdDb,
        TimeSpan processTimeout,
        TimeSpan visualSignalSampleInterval,
        TimeSpan audioSignalWindowDuration)
    {
        if (!double.IsFinite(sceneThresholdPercent) ||
            sceneThresholdPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sceneThresholdPercent),
                sceneThresholdPercent,
                "Scene threshold must be between 0 and 100 percent.");
        }

        if (minimumBlackDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumBlackDuration),
                minimumBlackDuration,
                "Minimum black duration must be greater than zero.");
        }

        if (!double.IsFinite(blackPixelThreshold) ||
            blackPixelThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blackPixelThreshold),
                blackPixelThreshold,
                "Black pixel threshold must be between 0 and 1.");
        }

        if (!double.IsFinite(blackPictureRatio) ||
            blackPictureRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blackPictureRatio),
                blackPictureRatio,
                "Black picture ratio must be between 0 and 1.");
        }

        if (minimumFreezeDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumFreezeDuration),
                minimumFreezeDuration,
                "Minimum freeze duration must be greater than zero.");
        }

        ValidateDecibelThreshold(
            freezeNoiseToleranceDb,
            nameof(freezeNoiseToleranceDb));

        if (minimumSilenceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSilenceDuration),
                minimumSilenceDuration,
                "Minimum silence duration must be greater than zero.");
        }

        ValidateDecibelThreshold(
            silenceNoiseThresholdDb,
            nameof(silenceNoiseThresholdDb));

        if (processTimeout < TimeSpan.FromMinutes(1) ||
            processTimeout > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(
                nameof(processTimeout),
                processTimeout,
                "Analysis process timeout must be between one minute and 24 hours.");
        }

        ValidateSignalCadence(
            visualSignalSampleInterval,
            nameof(visualSignalSampleInterval));
        ValidateSignalCadence(
            audioSignalWindowDuration,
            nameof(audioSignalWindowDuration));

        SceneThresholdPercent = sceneThresholdPercent;
        MinimumBlackDuration = minimumBlackDuration;
        BlackPixelThreshold = blackPixelThreshold;
        BlackPictureRatio = blackPictureRatio;
        MinimumFreezeDuration = minimumFreezeDuration;
        FreezeNoiseToleranceDb = freezeNoiseToleranceDb;
        MinimumSilenceDuration = minimumSilenceDuration;
        SilenceNoiseThresholdDb = silenceNoiseThresholdDb;
        ProcessTimeout = processTimeout;
        VisualSignalSampleInterval =
            visualSignalSampleInterval;
        AudioSignalWindowDuration =
            audioSignalWindowDuration;
    }

    public double SceneThresholdPercent { get; }

    public TimeSpan MinimumBlackDuration { get; }

    public double BlackPixelThreshold { get; }

    public double BlackPictureRatio { get; }

    public TimeSpan MinimumFreezeDuration { get; }

    public double FreezeNoiseToleranceDb { get; }

    public TimeSpan MinimumSilenceDuration { get; }

    public double SilenceNoiseThresholdDb { get; }

    public TimeSpan ProcessTimeout { get; }

    public TimeSpan VisualSignalSampleInterval { get; }

    public TimeSpan AudioSignalWindowDuration { get; }

    public MediaEvidenceAnalysisOptions WithSceneThresholdPercent(
        double sceneThresholdPercent)
    {
        return new MediaEvidenceAnalysisOptions(
            sceneThresholdPercent,
            MinimumBlackDuration,
            BlackPixelThreshold,
            BlackPictureRatio,
            MinimumFreezeDuration,
            FreezeNoiseToleranceDb,
            MinimumSilenceDuration,
            SilenceNoiseThresholdDb,
            ProcessTimeout,
            VisualSignalSampleInterval,
            AudioSignalWindowDuration);
    }

    public MediaEvidenceAnalysisOptions WithSignalSampling(
        TimeSpan visualSignalSampleInterval,
        TimeSpan audioSignalWindowDuration)
    {
        return new MediaEvidenceAnalysisOptions(
            SceneThresholdPercent,
            MinimumBlackDuration,
            BlackPixelThreshold,
            BlackPictureRatio,
            MinimumFreezeDuration,
            FreezeNoiseToleranceDb,
            MinimumSilenceDuration,
            SilenceNoiseThresholdDb,
            ProcessTimeout,
            visualSignalSampleInterval,
            audioSignalWindowDuration);
    }

    public static MediaEvidenceAnalysisOptions CreateFullPrecisionDefaults()
    {
        return new MediaEvidenceAnalysisOptions(
            sceneThresholdPercent: 10,
            minimumBlackDuration: TimeSpan.FromSeconds(0.5),
            blackPixelThreshold: 0.10,
            blackPictureRatio: 0.98,
            minimumFreezeDuration: TimeSpan.FromSeconds(2),
            freezeNoiseToleranceDb: -60,
            minimumSilenceDuration: TimeSpan.FromSeconds(0.5),
            silenceNoiseThresholdDb: -50,
            processTimeout: TimeSpan.FromHours(8),
            visualSignalSampleInterval:
                DefaultVisualSignalSampleInterval,
            audioSignalWindowDuration:
                DefaultAudioSignalWindowDuration);
    }

    public static void ValidateSignalCadence(
        TimeSpan value,
        string parameterName)
    {
        if (value < MinimumSignalCadence ||
            value > MaximumSignalCadence)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Signal cadence must be between {MinimumSignalCadence} and {MaximumSignalCadence}.");
        }
    }

    private static void ValidateDecibelThreshold(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value is < -160 or > 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Decibel threshold must be finite and between -160 dB and 0 dB.");
        }
    }
}
