using System;
using System.Collections.Generic;
using System.Text;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Signals;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegVisualTargetFilterGraphBuilder
{
    internal static void AppendNormalizedSplit(
        StringBuilder graph,
        MediaEvidenceAnalysisRequest request,
        IReadOnlyList<VisualEvidenceTarget> targets)
    {
        VisualEvidenceTarget firstTarget =
            targets[0];

        graph.Append(
            $"[0:{request.Media.PrimaryVideoStream.Index}]");

        graph.Append(
            $"scale={firstTarget.EffectiveDisplayWidth}:" +
            $"{firstTarget.EffectiveDisplayHeight}:flags=lanczos,");

        graph.Append(
            "setsar=1,metadata=mode=delete,");

        graph.Append(
            $"split={targets.Count}");

        foreach (VisualEvidenceTarget target in targets)
        {
            graph.Append(
                FfmpegEvidenceFilterLabels.Input(
                    target.TargetKey));
        }
    }

    internal static void AppendTargetScope(
        StringBuilder graph,
        VisualEvidenceTarget target)
    {
        if (target.Kind ==
            VisualEvidenceTargetKind.FullFrame)
        {
            return;
        }

        PixelRectangle crop =
            target.ActualPixelCrop ??
            throw new InvalidOperationException(
                "A composition target requires crop geometry.");

        graph.Append(
            $"trim=start={FfmpegEvidenceFilterLabels.Format(target.Start.TotalSeconds)}:" +
            $"end={FfmpegEvidenceFilterLabels.Format(target.End.TotalSeconds)},");

        // trim intentionally preserves absolute PTS. Do not add setpts.
        graph.Append(
            $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y},");
    }

    internal static void AppendTargetAttributionAndPrint(
        StringBuilder graph,
        string targetKey,
        string recordKind)
    {
        graph.Append(
            $"metadata=mode=add:key={FfmpegEvidenceCommandBuilder.RecordKindMetadataKey}:" +
            $"value={recordKind},");

        graph.Append(
            $"metadata=mode=add:key={FfmpegEvidenceCommandBuilder.VisualTargetMetadataKey}:" +
            $"value={targetKey},");

        graph.Append(
            "metadata=mode=print:file=-:direct=1");
    }

    internal static void AppendVisualSignalBranch(
        StringBuilder graph,
        MediaEvidenceAnalysisRequest request,
        VisualEvidenceTarget target)
    {
        graph.Append(
            "fps=" +
            $"fps={TimeSpan.TicksPerSecond}/" +
            $"{request.Options.VisualSignalSampleInterval.Ticks}:" +
            $"start_time={FfmpegEvidenceFilterLabels.Format(target.Start.TotalSeconds)}:" +
            "round=up:eof_action=pass,");

        graph.Append(
            $"format=pix_fmts={MediaSignalEvidencePolicy.VisualAnalysisPixelFormat},");

        graph.Append("signalstats,");

        string[] removedKeys =
        [
            "YMIN",
            "YMAX",
            "UMIN",
            "ULOW",
            "UAVG",
            "UHIGH",
            "UMAX",
            "VMIN",
            "VLOW",
            "VAVG",
            "VHIGH",
            "VMAX",
            "SATMIN",
            "SATLOW",
            "SATHIGH",
            "SATMAX",
            "HUEMED",
            "HUEAVG",
            "UDIF",
            "VDIF",
            "YBITDEPTH",
            "UBITDEPTH",
            "VBITDEPTH",
        ];

        foreach (string key in removedKeys)
        {
            graph.Append(
                $"metadata=mode=delete:key=lavfi.signalstats.{key},");
        }

        FfmpegVisualTargetFilterGraphBuilder.AppendTargetAttributionAndPrint(
            graph,
            target.TargetKey,
            FfmpegEvidenceCommandBuilder.VisualSignalRecordKind);

        graph.Append(",nullsink");
    }
}
