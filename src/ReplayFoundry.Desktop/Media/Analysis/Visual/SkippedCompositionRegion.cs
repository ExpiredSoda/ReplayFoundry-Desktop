using System;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.Desktop.Media.Analysis.Visual;

public sealed class SkippedCompositionRegion
{
    public SkippedCompositionRegion(
        int intervalIndex,
        TimeSpan intervalStart,
        TimeSpan intervalEnd,
        string regionId,
        CompositionRegionRole role,
        string reason)
    {
        if (intervalIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalIndex),
                intervalIndex,
                "Skipped-region interval index cannot be negative.");
        }

        if (intervalStart < TimeSpan.Zero ||
            intervalEnd <= intervalStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalEnd),
                intervalEnd,
                "Skipped-region interval must be positive.");
        }

        if (string.IsNullOrWhiteSpace(regionId))
        {
            throw new ArgumentException(
                "A skipped composition region requires an identifier.",
                nameof(regionId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "The skipped composition role is not defined.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A skipped composition region requires a reason.",
                nameof(reason));
        }

        IntervalIndex = intervalIndex;
        IntervalStart = intervalStart;
        IntervalEnd = intervalEnd;
        RegionId = regionId.Trim();
        Role = role;
        Reason = reason.Trim();
    }

    public int IntervalIndex { get; }

    public TimeSpan IntervalStart { get; }

    public TimeSpan IntervalEnd { get; }

    public string RegionId { get; }

    public CompositionRegionRole Role { get; }

    public string Reason { get; }
}
