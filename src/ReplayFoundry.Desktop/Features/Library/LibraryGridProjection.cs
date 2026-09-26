namespace ReplayFoundry.Desktop.Features.Library;

public sealed class LibraryGridEntry
{
    private LibraryGridEntry(string groupName, LibraryItem? item, bool collapsed = false, int count = 0)
    {
        GroupName = groupName;
        Item = item;
        IsCollapsed = collapsed;
        Count = count;
    }

    public string GroupName { get; }
    public LibraryItem? Item { get; }
    public bool IsHeader => Item is null;
    public bool IsCard => Item is not null;
    public bool IsCollapsed { get; }
    public int Count { get; }
    public string HeaderLabel => $"{(IsCollapsed ? "▸" : "▾")}  {GroupName} · {Count} videos";

    internal static LibraryGridEntry Header(string groupName, bool collapsed = false, int count = 0) =>
        new(groupName, null, collapsed, count);

    internal static LibraryGridEntry Card(LibraryItem item) =>
        new(item.OrganizationGroup, item);
}

internal static class LibraryGridProjection
{
    internal static IReadOnlyList<LibraryGridEntry> Create(
        IReadOnlyList<LibraryItem> items, IReadOnlyDictionary<string, bool>? collapsedGroups = null, bool collapseOlder = false)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var entries = new List<LibraryGridEntry>(items.Count + 8);
        foreach (IGrouping<string, LibraryItem> group in items.GroupBy(
                     static item => item.OrganizationGroup,
                     StringComparer.Ordinal))
        {
            bool collapsed = collapsedGroups?.GetValueOrDefault(group.Key, collapseOlder && entries.Count > 0) ?? false;
            entries.Add(LibraryGridEntry.Header(group.Key, collapsed, group.Count()));
            if (!collapsed) entries.AddRange(group.Select(LibraryGridEntry.Card));
        }
        return entries.ToArray();
    }
}
