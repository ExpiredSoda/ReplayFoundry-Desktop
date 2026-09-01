using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

public enum PublishHistoryStatusFilter
{
    All,
    Scheduled,
    Published,
    NeedsAttention,
}

public enum PublishHistorySortOrder
{
    NewestFirst,
    OldestFirst,
}

public enum PublishHistoryCategory
{
    Scheduled,
    Published,
    NeedsAttention,
}

public sealed record PublishHistoryChoice<T>(T Key, string Label)
    where T : struct, Enum
{
    public override string ToString() => Label;
}

public sealed record PublishHistoryListItem(
    string Id,
    string Title,
    string Status,
    string Detail,
    string DateGroup,
    DateTime LocalDate,
    DateTimeOffset AttemptedAtUtc,
    string? Url,
    PublishHistoryCategory Category)
{
    public bool HasUrl =>
        PublishHistoryLinkPolicy.TryCreateTrustedYouTubeUri(Url, out _);
    public bool IsScheduled => Category == PublishHistoryCategory.Scheduled;
    public bool IsPublished => Category == PublishHistoryCategory.Published;
    public bool NeedsAttention =>
        Category == PublishHistoryCategory.NeedsAttention;
    public string AccessibilityName => $"{Title}, {Status}. {Detail}";
    public string OpenActionName => $"Open {Title} on YouTube";
}

internal sealed record PublishHistoryProjection(
    IReadOnlyList<PublishHistoryListItem> RecentItems,
    IReadOnlyList<PublishHistoryListItem> PagedItems,
    int TotalCount,
    int ScheduledCount,
    int PublishedCount,
    int NeedsAttentionCount,
    int FilteredCount,
    bool HasMore,
    bool HasInvalidDateRange)
{
    public static PublishHistoryProjection Empty { get; } = new(
        [], [], 0, 0, 0, 0, 0, false, false);
}

internal static class PublishHistoryProjector
{
    public const int DashboardItemLimit = 5;
    public const int PageSize = 25;

    public static IReadOnlyList<
        PublishHistoryChoice<PublishHistoryStatusFilter>>
        CreateStatusFilters() =>
    [
        new(PublishHistoryStatusFilter.All, "All statuses"),
        new(PublishHistoryStatusFilter.Scheduled, "Scheduled"),
        new(PublishHistoryStatusFilter.Published, "Published"),
        new(PublishHistoryStatusFilter.NeedsAttention, "Needs attention"),
    ];

    public static IReadOnlyList<
        PublishHistoryChoice<PublishHistorySortOrder>>
        CreateSortOrders() =>
    [
        new(PublishHistorySortOrder.NewestFirst, "Newest first"),
        new(PublishHistorySortOrder.OldestFirst, "Oldest first"),
    ];

    public static PublishHistoryProjection Build(
        IReadOnlyList<YouTubePublishHistoryEntry> entries,
        string searchQuery,
        PublishHistoryStatusFilter statusFilter,
        DateTime? fromDate,
        DateTime? toDate,
        PublishHistorySortOrder sortOrder,
        int visibleLimit,
        DateTime localToday,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(searchQuery);
        ArgumentNullException.ThrowIfNull(timeZone);

        PublishHistoryListItem[] allItems = entries
            .Select(entry => CreateItem(entry, localToday, timeZone))
            .OrderByDescending(static item => item.AttemptedAtUtc)
            .ToArray();
        PublishHistoryListItem[] recentItems = allItems
            .Take(DashboardItemLimit)
            .ToArray();

        bool hasInvalidDateRange =
            fromDate.HasValue && toDate.HasValue &&
            fromDate.Value.Date > toDate.Value.Date;
        IEnumerable<PublishHistoryListItem> filtered = hasInvalidDateRange
            ? []
            : allItems.Where(item =>
                MatchesSearch(item, searchQuery) &&
                MatchesStatus(item, statusFilter) &&
                (!fromDate.HasValue ||
                 item.LocalDate >= fromDate.Value.Date) &&
                (!toDate.HasValue ||
                 item.LocalDate <= toDate.Value.Date));
        filtered = sortOrder == PublishHistorySortOrder.OldestFirst
            ? filtered.OrderBy(static item => item.AttemptedAtUtc)
            : filtered.OrderByDescending(static item => item.AttemptedAtUtc);
        PublishHistoryListItem[] filteredItems = filtered.ToArray();
        int safeVisibleLimit = Math.Max(PageSize, visibleLimit);

        return new PublishHistoryProjection(
            recentItems,
            filteredItems.Take(safeVisibleLimit).ToArray(),
            allItems.Length,
            allItems.Count(static item => item.IsScheduled),
            allItems.Count(static item => item.IsPublished),
            allItems.Count(static item => item.NeedsAttention),
            filteredItems.Length,
            filteredItems.Length > safeVisibleLimit,
            hasInvalidDateRange);
    }

    private static PublishHistoryListItem CreateItem(
        YouTubePublishHistoryEntry entry,
        DateTime localToday,
        TimeZoneInfo timeZone)
    {
        DateTime localDate = TimeZoneInfo.ConvertTime(
            entry.AttemptedAtUtc,
            timeZone).Date;
        PublishHistoryCategory category = GetCategory(entry);
        return new PublishHistoryListItem(
            entry.Id,
            entry.Title,
            PublishPresentationRules.FormatOutcome(entry.Outcome),
            PublishPresentationRules.BuildHistoryDetail(entry, timeZone),
            BuildDateGroup(localDate, localToday.Date),
            localDate,
            entry.AttemptedAtUtc,
            entry.VideoUrl,
            category);
    }

    private static PublishHistoryCategory GetCategory(
        YouTubePublishHistoryEntry entry)
    {
        if (entry.RemoteStatus ==
                YouTubeRemoteVideoStatus.NotFoundOrInaccessible ||
            entry.Outcome is YouTubePublishOutcome.Failed or
                YouTubePublishOutcome.Cancelled)
        {
            return PublishHistoryCategory.NeedsAttention;
        }

        return entry.Outcome == YouTubePublishOutcome.Scheduled
            ? PublishHistoryCategory.Scheduled
            : PublishHistoryCategory.Published;
    }

    private static bool MatchesSearch(
        PublishHistoryListItem item,
        string searchQuery) =>
        string.IsNullOrWhiteSpace(searchQuery) ||
        item.Title.Contains(
            searchQuery.Trim(),
            StringComparison.CurrentCultureIgnoreCase);

    private static bool MatchesStatus(
        PublishHistoryListItem item,
        PublishHistoryStatusFilter filter) => filter switch
        {
            PublishHistoryStatusFilter.Scheduled => item.IsScheduled,
            PublishHistoryStatusFilter.Published => item.IsPublished,
            PublishHistoryStatusFilter.NeedsAttention =>
                item.NeedsAttention,
            _ => true,
        };

    private static string BuildDateGroup(
        DateTime localDate,
        DateTime localToday)
    {
        if (localDate == localToday)
        {
            return "TODAY";
        }
        if (localDate == localToday.AddDays(-1))
        {
            return "YESTERDAY";
        }

        int daysSinceMonday = ((int)localToday.DayOfWeek + 6) % 7;
        DateTime weekStart = localToday.AddDays(-daysSinceMonday);
        if (localDate >= weekStart && localDate < localToday)
        {
            return "THIS WEEK";
        }
        if (localDate.Year == localToday.Year &&
            localDate.Month == localToday.Month)
        {
            return "THIS MONTH";
        }

        string format = localDate.Year == localToday.Year
            ? "MMMM"
            : "MMMM yyyy";
        return localDate.ToString(format).ToUpperInvariant();
    }
}
