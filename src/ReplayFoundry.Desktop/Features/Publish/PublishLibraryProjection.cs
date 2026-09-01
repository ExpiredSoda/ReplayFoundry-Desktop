using System.IO;
using ReplayFoundry.Desktop.Features.Library;

namespace ReplayFoundry.Desktop.Features.Publish;

internal sealed record PublishLibraryProjection(
    IReadOnlyList<PublishLibraryFolderItem> FolderOptions,
    IReadOnlyList<PublishLibraryItem> Items,
    bool HasActiveFilters,
    DateTime LocalDate)
{
    public static PublishLibraryProjection Empty { get; } = new(
        [new PublishLibraryFolderItem(null, "All folders")],
        [],
        false,
        DateTime.MinValue);
}

internal static class PublishLibraryProjector
{
    public const string AnyDateFilter = "Any date";
    private const string TodayFilter = "Today";
    private const string ThisWeekFilter = "This week";
    private const string ThisMonthFilter = "This month";

    public static IReadOnlyList<string> CreateDateFilters() =>
        [AnyDateFilter, TodayFilter, ThisWeekFilter, ThisMonthFilter];

    public static PublishLibraryProjection Build(
        IReadOnlyList<LibraryMediaAsset> assets,
        string searchQuery,
        string dateFilter,
        string? selectedFolder,
        DateTime localDate,
        TimeZoneInfo timeZone,
        Func<LibraryMediaAsset, string> resolvePublishState)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(searchQuery);
        ArgumentNullException.ThrowIfNull(dateFilter);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(resolvePublishState);

        IEnumerable<LibraryMediaAsset> query = assets;
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string search = searchQuery.Trim();
            query = query.Where(asset =>
                asset.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(asset.OutputFullPath).Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ||
                GetFolderLabel(asset).Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase));
        }
        if (selectedFolder is not null)
        {
            query = query.Where(asset => string.Equals(
                Path.GetDirectoryName(asset.OutputFullPath),
                selectedFolder,
                StringComparison.OrdinalIgnoreCase));
        }
        query = dateFilter switch
        {
            TodayFilter => query.Where(asset =>
                GetLocalDate(asset, timeZone) == localDate.Date),
            ThisWeekFilter => query.Where(asset =>
                GetLocalDate(asset, timeZone) >= localDate.Date.AddDays(-6)),
            ThisMonthFilter => query.Where(asset =>
                GetLocalDate(asset, timeZone) >= localDate.Date.AddMonths(-1)),
            _ => query,
        };

        LibraryMediaAsset[] visibleAssets = query
            .OrderByDescending(static asset => asset.AddedAtUtc)
            .ThenBy(static asset => asset.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        PublishLibraryItem[] items = visibleAssets
            .Select(asset => new PublishLibraryItem(
                asset,
                asset.Title,
                $"{PublishPresentationRules.FormatDuration(asset.Duration)} · {asset.AspectRatioText}",
                BuildCollectionDetail(asset, timeZone),
                resolvePublishState(asset),
                asset.ThumbnailFullPath))
            .ToArray();
        bool hasActiveFilters =
            !string.IsNullOrWhiteSpace(searchQuery) ||
            !dateFilter.Equals(AnyDateFilter, StringComparison.Ordinal) ||
            selectedFolder is not null;
        return new PublishLibraryProjection(
            BuildFolderOptions(assets),
            items,
            hasActiveFilters,
            localDate.Date);
    }

    public static IReadOnlyList<PublishLibraryFolderItem> BuildFolderOptions(
        IReadOnlyList<LibraryMediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        string[] folders = assets
            .Select(static asset => Path.GetDirectoryName(asset.OutputFullPath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Dictionary<string, int> labelCounts = folders
            .GroupBy(GetFolderLeaf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        return
        [
            new PublishLibraryFolderItem(null, "All folders"),
            .. folders.Select(path =>
            {
                string leaf = GetFolderLeaf(path);
                string label = labelCounts[leaf] == 1
                    ? leaf
                    : $"{leaf} · {Path.GetDirectoryName(path)}";
                return new PublishLibraryFolderItem(path, label);
            }),
        ];
    }

    private static string BuildCollectionDetail(
        LibraryMediaAsset asset,
        TimeZoneInfo timeZone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(asset.AddedAtUtc, timeZone);
        return $"{GetFolderLabel(asset)} · {local:MMM d, yyyy}";
    }

    private static string GetFolderLabel(LibraryMediaAsset asset) =>
        GetFolderLeaf(Path.GetDirectoryName(asset.OutputFullPath) ?? string.Empty);

    private static DateTime GetLocalDate(
        LibraryMediaAsset asset,
        TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(asset.AddedAtUtc, timeZone).Date;

    private static string GetFolderLeaf(string path)
    {
        string trimmed = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }
}
