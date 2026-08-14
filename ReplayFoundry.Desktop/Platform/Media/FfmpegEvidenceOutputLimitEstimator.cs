using System;
using System.Collections.Generic;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceOutputLimitEstimator
{
    private const int VisualFloorCharacters =
        32 * 1024 * 1024;

    private const int AudioFloorCharacters =
        16 * 1024 * 1024;

    private const int VisualCharactersPerSample =
        512;

    private const int AudioCharactersPerWindow =
        384;

    public static int EstimateVisualOutputLimit(
        IReadOnlyList<VisualEvidenceTarget> targets,
        MediaEvidenceAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(options);

        long sampleCount = 0;

        foreach (VisualEvidenceTarget target in targets)
        {
            sampleCount =
                checked(
                    sampleCount +
                    (long)Math.Ceiling(
                        target.Duration.Ticks /
                        (double)options
                            .VisualSignalSampleInterval
                            .Ticks));
        }

        long estimate =
            checked(
                VisualFloorCharacters +
                sampleCount *
                VisualCharactersPerSample);

        return ValidateBoundedEstimate(
            estimate,
            MediaSignalEvidencePolicy
                .MaximumVisualOutputCharacters,
            "visual signal");
    }

    public static int EstimateAudioOutputLimit(
        TimeSpan sourceDuration,
        AudioStreamInfo audioStream,
        MediaEvidenceAnalysisOptions options)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Audio output estimation requires a positive source duration.");
        }

        ArgumentNullException.ThrowIfNull(audioStream);
        ArgumentNullException.ThrowIfNull(options);

        FfmpegAudioWindowSpecification window =
            FfmpegEvidenceCommandBuilder
                .CreateAudioWindowSpecification(
                    audioStream,
                    options.AudioSignalWindowDuration);

        long windowCount =
            checked(
                (long)Math.Ceiling(
                    sourceDuration.Ticks /
                    (double)window
                        .ActualWindowDuration
                        .Ticks));

        long estimate =
            checked(
                AudioFloorCharacters +
                windowCount *
                AudioCharactersPerWindow);

        return ValidateBoundedEstimate(
            estimate,
            MediaSignalEvidencePolicy
                .MaximumAudioOutputCharacters,
            $"audio stream {audioStream.Index} signal");
    }

    private static int ValidateBoundedEstimate(
        long estimate,
        int maximum,
        string evidenceName)
    {
        if (estimate >
            maximum)
        {
            throw new MediaEvidenceAnalysisException(
                $"The configured {evidenceName} cadence would exceed Replay Foundry's safe in-memory metadata limit. Increase the sampling interval or analyze a shorter source.");
        }

        return checked((int)estimate);
    }
}
