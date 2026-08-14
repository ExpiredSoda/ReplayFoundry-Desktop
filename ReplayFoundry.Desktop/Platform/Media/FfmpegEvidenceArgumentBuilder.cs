using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Visual;

namespace ReplayFoundry.Desktop.Platform.Media;

internal static class FfmpegEvidenceArgumentBuilder
{
    internal static IReadOnlyList<string> BuildVideoArguments(
        string filterGraph,
        IReadOnlyList<string> outputLabels)
    {
        var arguments =
            new List<string>
            {
                "-hide_banner",
                "-nostdin",
                "-nostats",
                "-v",
                "error",
                "-i",
                "{INPUT}",
                "-filter_complex",
                filterGraph,
            };

        foreach (string label in outputLabels)
        {
            arguments.Add("-map");
            arguments.Add(label);
        }

        arguments.Add("-an");
        arguments.Add("-sn");
        arguments.Add("-dn");
        arguments.Add("-f");
        arguments.Add("null");
        arguments.Add("-");

        return arguments;
    }

    internal static void ValidateTargets(
        MediaEvidenceAnalysisRequest request,
        IReadOnlyList<VisualEvidenceTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0 ||
            targets.Any(static target => target is null))
        {
            throw new ArgumentException(
                "Evidence command building requires at least one visual target.",
                nameof(targets));
        }

        if (targets.Count(
                static target =>
                    target.Kind ==
                    VisualEvidenceTargetKind.FullFrame) != 1)
        {
            throw new ArgumentException(
                "Evidence commands require exactly one full-frame target.",
                nameof(targets));
        }

        if (targets.Any(
                target =>
                    target.End >
                    request.Media.Duration))
        {
            throw new ArgumentException(
                "Evidence command targets must remain within the source duration.",
                nameof(targets));
        }

        if (targets
            .GroupBy(
                static target =>
                    target.TargetKey,
                StringComparer.Ordinal)
            .Any(
                static group =>
                    group.Count() > 1))
        {
            throw new ArgumentException(
                "Evidence command target keys must be unique.",
                nameof(targets));
        }
    }
}
