namespace ReplayFoundry.Desktop.Features.Library;

public sealed class LibraryGridEntry
{
    private LibraryGridEntry(string groupName, LibraryItem? item)
    {
        GroupName = groupName;
        Item = item;
    }

    public string GroupName { get; }
    public LibraryItem? Item { get; }
    public bool IsHeader => Item is null;
    public bool IsCard => Item is not null;

    internal static LibraryGridEntry Header(string groupName) =>
        new(groupName, null);

    internal static LibraryGridEntry Card(LibraryItem item) =>
        new(item.OrganizationGroup, item);
}

internal static class LibraryGridProjection
{
    internal static IReadOnlyList<LibraryGridEntry> Create(
        IReadOnlyList<LibraryItem> items)
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
            entries.Add(LibraryGridEntry.Header(group.Key));
            entries.AddRange(group.Select(LibraryGridEntry.Card));
        }
        return entries.ToArray();
    }
}
