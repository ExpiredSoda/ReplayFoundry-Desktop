using System.Collections.Generic;
using System.Text;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegSceneFilterGraphBuilder
{
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

            graph.Append("split=2");
            graph.Append(
                FfmpegEvidenceFilterLabels.SceneDetectorInput(
                    target.TargetKey));
            graph.Append(
                FfmpegEvidenceFilterLabels.VisualSignalInput(
                    target.TargetKey));
            graph.Append(';');

            graph.Append(
                FfmpegEvidenceFilterLabels.SceneDetectorInput(
                    target.TargetKey));

            graph.Append(
                "scdet=" +
                $"threshold={FfmpegEvidenceFilterLabels.Format(request.Options.SceneThresholdPercent)}:" +
                "sc_pass=0,split=2");

            graph.Append(
                FfmpegEvidenceFilterLabels.SceneEventInput(
                    target.TargetKey));

            graph.Append(
                FfmpegEvidenceFilterLabels.SceneSinkInput(
                    target.TargetKey));

            graph.Append(';');
            graph.Append(
                FfmpegEvidenceFilterLabels.SceneEventInput(
                    target.TargetKey));

            graph.Append(
                "metadata=mode=select:key=lavfi.scd.time,");

            FfmpegVisualTargetFilterGraphBuilder.AppendTargetAttributionAndPrint(
                graph,
                target.TargetKey,
                FfmpegEvidenceCommandBuilder.SceneRecordKind);

            graph.Append(
                ",nullsink;");

            graph.Append(
                FfmpegEvidenceFilterLabels.SceneSinkInput(
                    target.TargetKey));

            if (target.Kind ==
                VisualEvidenceTargetKind.FullFrame)
            {
                graph.Append(
                    "null");

                graph.Append(
                    FfmpegEvidenceFilterLabels.SceneOutput(
                        target.TargetKey));
            }
            else
            {
                graph.Append(
                    "nullsink");
            }

            graph.Append(';');
            graph.Append(
                FfmpegEvidenceFilterLabels.VisualSignalInput(
                    target.TargetKey));
            FfmpegVisualTargetFilterGraphBuilder.AppendVisualSignalBranch(
                graph,
                request,
                target);
        }

        return graph.ToString();
    }
}
