using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// A time interval during which one composition layout is applicable.
/// </summary>
public sealed class CompositionLayoutInterval
{
    private readonly ReadOnlyCollection<CompositionRegion> _regions;

    public CompositionLayoutInterval(
        TimeSpan start,
        TimeSpan end,
        IEnumerable<CompositionRegion> regions)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A composition layout interval cannot start before the source begins.");
        }

        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A composition layout interval must end after it starts.");
        }

        ArgumentNullException.ThrowIfNull(regions);

        CompositionRegion[] regionSnapshot =
            regions.ToArray();

        if (regionSnapshot.Length == 0)
        {
            throw new ArgumentException(
                "A composition layout interval requires at least one region.",
                nameof(regions));
        }

        if (regionSnapshot.Any(
                static region =>
                    region is null))
        {
            throw new ArgumentException(
                "A composition layout interval cannot contain null regions.",
                nameof(regions));
        }

        string? duplicateId =
            regionSnapshot
                .GroupBy(
                    static region =>
                        region.Id,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(
                    static group =>
                        group.Count() > 1)
                ?.Key;

        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Composition region identifier '{duplicateId}' is duplicated " +
                "within the same layout interval.",
                nameof(regions));
        }

        Start = start;
        End = end;
        _regions =
            Array.AsReadOnly(
                regionSnapshot);
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration =>
        End - Start;

    public IReadOnlyList<CompositionRegion> Regions =>
        _regions;

    public CompositionRegion? FindRegion(
        string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A composition region lookup requires an identifier.",
                nameof(id));
        }

        return _regions.FirstOrDefault(
            region =>
                string.Equals(
                    region.Id,
                    id.Trim(),
                    StringComparison.OrdinalIgnoreCase));
    }

    public bool Contains(
        TimeSpan timestamp,
        bool includeEnd = false)
    {
        if (timestamp < TimeSpan.Zero)
        {
            return false;
        }

        return includeEnd
            ? timestamp >= Start &&
              timestamp <= End
            : timestamp >= Start &&
              timestamp < End;
    }
}
