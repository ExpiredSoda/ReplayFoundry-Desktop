using System.Collections.Generic;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.Desktop.Features.Library.DesignTime;

public sealed class LibraryDesignViewModel
{
    public LibraryDesignViewModel()
    {
        Categories = new[]
        {
            new LibraryCategoryItem(LibraryCategory.Projects, "Projects", "Icon.Project"),
            new LibraryCategoryItem(LibraryCategory.GeneratedClips, "Generated Clips", "Icon.Spark"),
            new LibraryCategoryItem(LibraryCategory.Montages, "Montages", "Icon.Grid")
        };
        Items = new[]
        {
            new LibraryItem("Clutch finish / vertical", "Generated clip", "00:18", "Today · 10:42", "Ready", "9:16", "From Generate · Individual Clips", "Icon.Play"),
            new LibraryItem("Arena round-up", "Montage", "01:24", "Yesterday · 18:05", "Draft", "16:9", "4 source videos", "Icon.Grid"),
            new LibraryItem("Creator cam reference", "Source media", "12:08", "Jul 28 · 09:10", "Indexed", "16:9", "Local source · design preview", "Icon.Media")
        };
        foreach (LibraryItem item in Items)
        {
            item.OrganizationGroup = "Today";
        }
    }
    public IReadOnlyList<LibraryCategoryItem> Categories { get; }
    public IReadOnlyList<string> Modes { get; } = new[] { "All modes", "Individual clips", "Montage" };
    public IReadOnlyList<string> Statuses { get; } = new[] { "All statuses", "Ready", "Missing locally" };
    public IReadOnlyList<string> Dates { get; } = new[] { "Any date", "Today", "This week", "This month" };
    public IReadOnlyList<string> SortOptions { get; } = new[] { "Recently modified", "Name", "Duration", "Status" };
    public IReadOnlyList<LibraryOrganizationOption> OrganizationOptions { get; } =
    [
        new(LibraryOrganizationMode.Date, "Date"),
        new(LibraryOrganizationMode.Folder, "Folder"),
        new(LibraryOrganizationMode.Project, "Project"),
    ];
    public IReadOnlyList<LibraryItem> Items { get; }
    public LibraryCategory SelectedCategory => LibraryCategory.Projects;
    public LibraryViewMode ViewMode => LibraryViewMode.Grid;
    public LibraryOrganizationMode OrganizationMode => LibraryOrganizationMode.Date;
    public bool IsGridView => true;
    public bool IsListView => false;
    public bool IsEmpty => false;
    public bool HasNoVisibleItems => false;
    public WorkspaceSurfaceState SurfaceState => WorkspaceSurfaceState.ContentReady;
    public bool IsContentReady => true;
    public bool IsLoading => false;
    public bool IsError => false;
    public bool IsUnavailable => false;
    public bool HasSelection => true;
    public bool CanRelinkSelected => false;
    public bool HasActiveFilters => false;
    public string SearchQuery => string.Empty;
    public string ModeFilter => "All modes";
    public string StatusFilter => "All statuses";
    public string DateFilter => "Any date";
    public string SortBy => "Recently modified";
    public string ResultSummary => "3 items";
    public string FilterSummary => "All available items";
    public string OrganizationSummary => "Grouped by date";
    public string EmptyTitle => "No items match these filters";
    public string EmptyDescription => "Clear a filter or search term to see the full category.";
    public string SelectedCategoryLabel => "Projects";
    public string StatusText => "Design preview · runtime index unavailable";
    public string ErrorSummary => "The library could not load its content.";
    public string LibraryNotice => string.Empty;
    public bool HasLibraryNotice => false;
    public LibraryItem SelectedItem => Items[0];
    public LibraryPlaybackViewModel Playback { get; } = new();
    public ICommand? ClearFiltersCommand => null;
    public ICommand? SetGridViewCommand => null;
    public ICommand? SetListViewCommand => null;
    public ICommand? RefreshLibraryCommand => null;
    public ICommand? OpenSelectedFolderCommand => null;
    public ICommand? RelinkMissingFileCommand => null;
}
