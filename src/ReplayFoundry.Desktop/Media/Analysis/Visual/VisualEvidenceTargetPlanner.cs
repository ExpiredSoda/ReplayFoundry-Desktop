using System;
using System.Collections.Generic;
using System.Linq;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Geometry;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

internal static class VisualEvidenceTargetPlanner
{
    public static VisualEvidenceTargetPlan Create(
        MediaEvidenceAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        EffectiveDisplayGeometry display =
            EffectiveDisplayGeometryCalculator
                .Calculate(
                    request.Media.PrimaryVideoStream);

        var targets =
            new List<VisualEvidenceTarget>
            {
                new(
                    CreateTargetKey(0),
                    VisualEvidenceTargetKind.FullFrame,
                    TimeSpan.Zero,
                    request.Media.Duration,
                    display.Width,
                    display.Height),
            };

        var skipped =
            new List<SkippedCompositionRegion>();

        if (request.Composition is null)
        {
            return new VisualEvidenceTargetPlan(
                display,
                targets,
                skipped);
        }

        var includedRoles =
            request.IncludedRegionRoles
                .ToHashSet();

        var included =
            new List<RegionPlanItem>();

        for (int intervalIndex = 0;
             intervalIndex <
             request.Composition.Intervals.Count;
             intervalIndex++)
        {
            CompositionLayoutInterval interval =
                request.Composition.Intervals[
                    intervalIndex];

            foreach (CompositionRegion region in
                     interval.Regions)
            {
                if (!includedRoles.Contains(
                        region.Role))
                {
                    skipped.Add(
                        new SkippedCompositionRegion(
                            intervalIndex,
                            interval.Start,
                            interval.End,
                            region.Id,
                            region.Role,
                            "The region role was not requested for deterministic region evidence."));

                    continue;
                }

                included.Add(
                    new RegionPlanItem(
                        intervalIndex,
                        interval,
                        region));
            }
        }

        RegionPlanItem[] ordered =
            included
                .OrderBy(
                    static item =>
                        item.Interval.Start)
                .ThenBy(
                    static item =>
                        item.IntervalIndex)
                .ThenBy(
                    static item =>
                        item.Region.Role)
                .ThenBy(
                    static item =>
                        item.Region.Id,
                    StringComparer.Ordinal)
                .ToArray();

        for (int index = 0;
             index < ordered.Length;
             index++)
        {
            RegionPlanItem item =
                ordered[index];

            PixelRectangle crop =
                EffectiveDisplayGeometryCalculator
                    .CalculateCrop(
                        display,
                        item.Region.Geometry);

            targets.Add(
                new VisualEvidenceTarget(
                    CreateTargetKey(index + 1),
                    VisualEvidenceTargetKind
                        .CompositionRegion,
                    item.Interval.Start,
                    item.Interval.End,
                    display.Width,
                    display.Height,
                    item.IntervalIndex,
                    item.Region.Id,
                    item.Region.Role,
                    item.Region.Traits,
                    item.Region.Geometry,
                    crop,
                    item.Region.GeometryConfidence,
                    item.Region.RoleConfidence,
                    item.Region.GeometrySource,
                    item.Region.RoleSource));
        }

        skipped.Sort(
            static (left, right) =>
            {
                int result =
                    left.IntervalStart.CompareTo(
                        right.IntervalStart);

                if (result != 0)
                {
                    return result;
                }

                result =
                    left.IntervalIndex.CompareTo(
                        right.IntervalIndex);

                if (result != 0)
                {
                    return result;
                }

                result =
                    left.Role.CompareTo(
                        right.Role);

                return result != 0
                    ? result
                    : StringComparer.Ordinal.Compare(
                        left.RegionId,
                        right.RegionId);
            });

        return new VisualEvidenceTargetPlan(
            display,
            targets,
            skipped);
    }

    private static string CreateTargetKey(
        int index)
    {
        return $"t{index:0000}";
    }

    private sealed record RegionPlanItem(
        int IntervalIndex,
        CompositionLayoutInterval Interval,
        CompositionRegion Region);
}
