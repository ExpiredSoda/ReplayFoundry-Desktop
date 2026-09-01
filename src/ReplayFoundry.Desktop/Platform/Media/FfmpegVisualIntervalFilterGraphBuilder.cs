using System.Collections.Generic;
using System.Text;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegVisualIntervalFilterGraphBuilder
{
    private static readonly string[] VisualIntervalMetadataKeys =
    [
        "lavfi.black_start",
        "lavfi.black_end",
        "lavfi.freezedetect.freeze_start",
        "lavfi.freezedetect.freeze_end",
    ];
    internal static string Build(
        MediaEvidenceAnalysisRequest request,
        IReadOnlyList<VisualEvidenceTarget> targets)
    {
        var graph =
            new StringBuilder();

        FfmpegVisualTargetFilterGraphBuilder.AppendNormalizedSplit(
            graph,
            request,
            targets);

        foreach (VisualEvidenceTarget target in targets)
        {
            graph.Append(';');
            graph.Append(
                FfmpegEvidenceFilterLabels.Input(
                    target.TargetKey));

            FfmpegVisualTargetFilterGraphBuilder.AppendTargetScope(
                graph,
                target);

            graph.Append(
                "blackdetect=" +
                $"d={FfmpegEvidenceFilterLabels.Format(request.Options.MinimumBlackDuration.TotalSeconds)}:" +
                $"pix_th={FfmpegEvidenceFilterLabels.Format(request.Options.BlackPixelThreshold)}:" +
                $"pic_th={FfmpegEvidenceFilterLabels.Format(request.Options.BlackPictureRatio)},");

            graph.Append(
                "freezedetect=" +
                $"n={FfmpegEvidenceFilterLabels.Format(request.Options.FreezeNoiseToleranceDb)}dB:" +
                $"d={FfmpegEvidenceFilterLabels.Format(request.Options.MinimumFreezeDuration.TotalSeconds)},");

            graph.Append(
                $"split={VisualIntervalMetadataKeys.Length + 1}");

            for (int eventIndex = 0;
                 eventIndex <
                 VisualIntervalMetadataKeys.Length;
                 eventIndex++)
            {
                graph.Append(
                    FfmpegEvidenceFilterLabels.VisualEventInput(
                        target.TargetKey,
                        eventIndex));
            }

            graph.Append(
                FfmpegEvidenceFilterLabels.VisualSinkInput(
                    target.TargetKey));

            for (int eventIndex = 0;
                 eventIndex <
                 VisualIntervalMetadataKeys.Length;
                 eventIndex++)
            {
                graph.Append(';');
                graph.Append(
                    FfmpegEvidenceFilterLabels.VisualEventInput(
                        target.TargetKey,
                        eventIndex));

                graph.Append(
                    "metadata=mode=select:key=");

                graph.Append(
                    VisualIntervalMetadataKeys[
                        eventIndex]);

                graph.Append(',');

                AppendDeleteOtherVisualMetadata(
                    graph,
                    eventIndex);

                FfmpegVisualTargetFilterGraphBuilder.AppendTargetAttributionAndPrint(
                    graph,
                    target.TargetKey,
                    eventIndex < 2
                        ? FfmpegEvidenceCommandBuilder.BlackRecordKind
                        : FfmpegEvidenceCommandBuilder.FreezeRecordKind);

                graph.Append(
                    ",nullsink");
            }

            graph.Append(';');
            graph.Append(
                FfmpegEvidenceFilterLabels.VisualSinkInput(
                    target.TargetKey));

            if (target.Kind ==
                VisualEvidenceTargetKind.FullFrame)
            {
                graph.Append(
                    "null");
                graph.Append(
                    FfmpegEvidenceFilterLabels.VisualOutput(
                        target.TargetKey));
            }
            else
            {
                graph.Append(
                    "nullsink");
            }
        }

        return graph.ToString();
    }

    private static void AppendDeleteOtherVisualMetadata(
        StringBuilder graph,
        int retainedEventIndex)
    {
        for (int index = 0; index < VisualIntervalMetadataKeys.Length; index++)
        {
            if (index == retainedEventIndex)
            {
                continue;
            }

            graph.Append("metadata=mode=delete:key=");
            graph.Append(VisualIntervalMetadataKeys[index]);
            graph.Append(',');
        }
    }

    /*
     * Event-only branches terminate in nullsink after metadata printing.
     * Mapping them would make FFmpeg fail when a target has no matching event.
     * The guaranteed full-frame passthrough is the sole mapped null output.
     */
}
