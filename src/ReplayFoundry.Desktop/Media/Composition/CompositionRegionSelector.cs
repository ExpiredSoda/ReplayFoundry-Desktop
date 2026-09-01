namespace ReplayFoundry.Desktop.Media.Composition;

public static class CompositionRegionSelector
{
    public static CompositionRegion? FindPrimary(
        CompositionLayoutInterval layout,
        CompositionRegionRole role)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        return layout.Regions
            .Where(region => region.Role == role)
            .OrderByDescending(static region =>
                region.Geometry.Width * region.Geometry.Height)
            .ThenBy(static region => region.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
