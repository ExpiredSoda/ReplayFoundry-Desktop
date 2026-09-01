using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReplayFoundry.Desktop.Media.Geometry;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

internal sealed class VisualEvidenceTargetPlan
{
    private readonly ReadOnlyCollection<VisualEvidenceTarget>
        _targets;

    private readonly ReadOnlyCollection<SkippedCompositionRegion>
        _skippedRegions;

    public VisualEvidenceTargetPlan(
        EffectiveDisplayGeometry displayGeometry,
        IEnumerable<VisualEvidenceTarget> targets,
        IEnumerable<SkippedCompositionRegion> skippedRegions)
    {
        ArgumentNullException.ThrowIfNull(displayGeometry);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(skippedRegions);

        VisualEvidenceTarget[] targetSnapshot =
            targets.ToArray();

        SkippedCompositionRegion[] skippedSnapshot =
            skippedRegions.ToArray();

        if (targetSnapshot.Length == 0 ||
            targetSnapshot.Any(static item => item is null) ||
            skippedSnapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Visual target plans require non-null targets and skipped-region records.");
        }

        if (targetSnapshot.Count(
                static target =>
                    target.Kind ==
                    VisualEvidenceTargetKind.FullFrame) != 1)
        {
            throw new ArgumentException(
                "A visual target plan requires exactly one full-frame target.",
                nameof(targets));
        }

        if (targetSnapshot
            .GroupBy(
                static target =>
                    target.TargetKey,
                StringComparer.Ordinal)
            .Any(
                static group =>
                    group.Count() > 1))
        {
            throw new ArgumentException(
                "Visual target keys must be unique.",
                nameof(targets));
        }

        DisplayGeometry = displayGeometry;
        _targets =
            Array.AsReadOnly(
                targetSnapshot);
        _skippedRegions =
            Array.AsReadOnly(
                skippedSnapshot);
    }

    public EffectiveDisplayGeometry DisplayGeometry { get; }

    public IReadOnlyList<VisualEvidenceTarget> Targets =>
        _targets;

    public IReadOnlyList<SkippedCompositionRegion> SkippedRegions =>
        _skippedRegions;
}
