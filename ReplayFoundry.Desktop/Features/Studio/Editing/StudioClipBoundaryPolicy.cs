using ReplayFoundry.Desktop.Features.Generate.Handoff;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal static class StudioClipBoundaryPolicy
{
    internal static readonly TimeSpan MaximumAdjustment =
        TimeSpan.FromMinutes(1);

    internal static TimeSpan GetEarliestStart(GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Max(
            TimeSpan.Zero,
            asset.OriginalSourceStart - MaximumAdjustment);
    }

    internal static TimeSpan GetLatestStart(GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Min(
            asset.SourceDuration,
            asset.OriginalSourceStart + MaximumAdjustment);
    }

    internal static TimeSpan GetEarliestEnd(GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Max(
            TimeSpan.Zero,
            asset.OriginalSourceEnd - MaximumAdjustment);
    }

    internal static TimeSpan GetLatestEnd(GenerationOutputAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Min(
            asset.SourceDuration,
            asset.OriginalSourceEnd + MaximumAdjustment);
    }

    internal static bool IsValid(
        GenerationOutputAsset asset,
        TimeSpan sourceStart,
        TimeSpan sourceEnd)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return sourceStart >= GetEarliestStart(asset) &&
               sourceStart <= GetLatestStart(asset) &&
               sourceEnd >= GetEarliestEnd(asset) &&
               sourceEnd <= GetLatestEnd(asset) &&
               sourceEnd > sourceStart;
    }

    internal static void Validate(
        GenerationOutputAsset asset,
        TimeSpan sourceStart,
        TimeSpan sourceEnd)
    {
        if (!IsValid(asset, sourceStart, sourceEnd))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceStart),
                "Studio boundaries must remain inside the source, preserve a positive clip, and stay within one minute of the generated boundaries.");
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;
}
