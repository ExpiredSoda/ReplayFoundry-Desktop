using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Moments;

public enum MediaMomentGuidanceKind
{
    PriorityPoint,
    PriorityRange,
}

public sealed record MediaMomentGuidanceItem
{
    public MediaMomentGuidanceItem(
        string id,
        MediaMomentGuidanceKind kind,
        TimeSpan start,
        TimeSpan end,
        bool reservesCandidateSearch)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !Enum.IsDefined(kind) ||
            start < TimeSpan.Zero ||
            end < start ||
            (kind == MediaMomentGuidanceKind.PriorityPoint && end != start) ||
            (kind == MediaMomentGuidanceKind.PriorityRange && end == start) ||
            (reservesCandidateSearch && kind != MediaMomentGuidanceKind.PriorityRange))
        {
            throw new ArgumentException("Media moment guidance is invalid.");
        }
        Id = id.Trim();
        Kind = kind;
        Start = start;
        End = end;
        ReservesCandidateSearch = reservesCandidateSearch;
    }

    public string Id { get; }
    public MediaMomentGuidanceKind Kind { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public TimeSpan Duration => End - Start;
    public bool ReservesCandidateSearch { get; }
}

public sealed class MediaMomentGuidance
{
    private readonly ReadOnlyCollection<MediaMomentGuidanceItem> _items;

    public MediaMomentGuidance(
        IEnumerable<MediaMomentGuidanceItem>? items = null)
    {
        MediaMomentGuidanceItem[] snapshot = items?
            .OrderBy(static item => item.Start)
            .ThenBy(static item => item.End)
            .ThenBy(static item => item.Kind)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray() ?? [];
        if (snapshot.Any(static item => item is null) ||
            snapshot.GroupBy(static item => item.Id, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Media moment guidance must be immutable, non-null, and uniquely identified.",
                nameof(items));
        }
        _items = Array.AsReadOnly(snapshot);
    }

    public static MediaMomentGuidance Empty { get; } = new();
    public IReadOnlyList<MediaMomentGuidanceItem> Items => _items;
    public bool IsEmpty => _items.Count == 0;
}
