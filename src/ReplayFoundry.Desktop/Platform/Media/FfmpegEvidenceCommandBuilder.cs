using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Visual;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceCommandBuilder
{
    internal const string VisualTargetMetadataKey =
        "replayfoundry.visual_target";

    internal const string RecordKindMetadataKey =
        "replayfoundry.record_kind";

    internal const string AudioStreamMetadataKey =
        "replayfoundry.audio_stream";

    internal const string SceneRecordKind =
        "scene";

    internal const string VisualSignalRecordKind =
        "visual_signal";

    internal const string BlackRecordKind =
        "black_interval";

    internal const string FreezeRecordKind =
        "freeze_interval";

    internal const string SilenceRecordKind =
        "silence_interval";

    internal const string AudioSignalRecordKind =
        "audio_signal";

    public static IReadOnlyList<string> BuildSceneDetectionArguments(
        MediaEvidenceAnalysisRequest request,
        IReadOnlyList<VisualEvidenceTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(request);
        FfmpegEvidenceArgumentBuilder.ValidateTargets(
            request,
            targets);

        string graph =
            FfmpegSceneFilterGraphBuilder.Build(
                request,
                targets);

        IReadOnlyList<string> outputLabels =
        [
            FfmpegEvidenceFilterLabels.SceneOutput(
                targets.Single(
                    static target =>
                        target.Kind ==
                        VisualEvidenceTargetKind.FullFrame)
                    .TargetKey),
        ];

        return FfmpegEvidenceArgumentBuilder.BuildVideoArguments(
            graph,
            outputLabels);
    }

    public static IReadOnlyList<string> BuildVisualIntervalArguments(
        MediaEvidenceAnalysisRequest request,
        IReadOnlyList<VisualEvidenceTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(request);
        FfmpegEvidenceArgumentBuilder.ValidateTargets(
            request,
            targets);

        string graph =
            FfmpegVisualIntervalFilterGraphBuilder.Build(
                request,
                targets);

        IReadOnlyList<string> outputLabels =
        [
            FfmpegEvidenceFilterLabels.VisualOutput(
                targets.Single(
                    static target =>
                        target.Kind ==
                        VisualEvidenceTargetKind.FullFrame)
                    .TargetKey),
        ];

        return FfmpegEvidenceArgumentBuilder.BuildVideoArguments(
            graph,
            outputLabels);
    }

    public static IReadOnlyList<string> BuildAudioEvidenceArguments(
        AudioStreamInfo audioStream,
        MediaEvidenceAnalysisOptions options) =>
        FfmpegAudioEvidenceCommandBuilder.BuildArguments(
            audioStream,
            options);

    internal static FfmpegAudioWindowSpecification
        CreateAudioWindowSpecification(
            AudioStreamInfo audioStream,
            TimeSpan requestedDuration) =>
        FfmpegAudioEvidenceCommandBuilder.CreateWindowSpecification(
            audioStream,
            requestedDuration);

    public static IReadOnlyList<string> BindInputPath(
        IReadOnlyList<string> template,
        string fullPath)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "FFmpeg analysis requires a source path.",
                nameof(fullPath));
        }

        var arguments =
            new List<string>(
                template.Count);

        foreach (string argument in template)
        {
            arguments.Add(
                string.Equals(
                    argument,
                    "{INPUT}",
                    StringComparison.Ordinal)
                    ? fullPath
                    : argument);
        }

        return arguments;
    }
}
