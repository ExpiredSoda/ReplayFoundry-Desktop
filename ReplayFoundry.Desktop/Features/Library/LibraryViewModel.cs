using System;
using System.Collections.Generic;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Library;

public enum LibraryCategory
{
    Projects,
    GeneratedClips,
    Montages,
}

public enum LibraryViewMode
{
    Grid,
    List,
}

public enum LibraryOrganizationMode
{
    Date,
    Folder,
    Project,
}

public sealed record LibraryCategoryItem(LibraryCategory Key, string Label, string Glyph);
public sealed record LibraryOrganizationOption(
    LibraryOrganizationMode Key,
    string Label);
public sealed class LibraryItem : ObservableObject
{
    private bool _isMarked;
    private string _organizationGroup = string.Empty;

    public LibraryItem(
        string title,
        string type,
        string duration,
        string modified,
        string status,
        string aspectRatio,
        string detail,
        string glyph,
        string? thumbnailFullPath = null,
        LibraryMediaAsset? asset = null)
    {
        Title = title;
        Type = type;
        Duration = duration;
        Modified = modified;
        Status = status;
        AspectRatio = aspectRatio;
        Detail = detail;
        Glyph = glyph;
        ThumbnailFullPath = thumbnailFullPath;
        Asset = asset;
    }

    public string Title { get; }
    public string Type { get; }
    public string Duration { get; }
    public string Modified { get; }
    public string Status { get; }
    public string AspectRatio { get; }
    public string Detail { get; }
    public string Glyph { get; }
    public string? ThumbnailFullPath { get; }
    public LibraryMediaAsset? Asset { get; }
    public bool HasThumbnail =>
        ThumbnailFullPath is not null &&
        File.Exists(ThumbnailFullPath);
    public string OrganizationGroup
    {
        get => _organizationGroup;
        internal set
        {
            if (_organizationGroup == value) return;
            _organizationGroup = value;
            OnPropertyChanged();
        }
    }

    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value) return;
            _isMarked = value;
            OnPropertyChanged();
        }
    }
}

public sealed class LibraryViewModel : ObservableObject, IWorkspaceChromeSource, IDisposable
{
    private LibraryCategory _selectedCategory = LibraryCategory.Projects;
    private LibraryViewMode _viewMode = LibraryViewMode.Grid;
    private string _searchQuery = string.Empty;
    private string _modeFilter = "All modes";
    private string _statusFilter = "All statuses";
    private string _dateFilter = "Any date";
    private string _sortBy = "Recently modified";
    private LibraryOrganizationMode _organizationMode =
        LibraryOrganizationMode.Date;
    private LibraryItem? _selectedItem;
    private readonly ILibraryCatalog _catalog;
    private readonly ILibraryAssetRelinker? _assetRelinker;
    private readonly ILibraryMediaFilePicker? _mediaFilePicker;
    private readonly ILocalFolderLauncher? _folderLauncher;
    private readonly ILibraryAssetRemover? _assetRemover;
    private readonly ILibraryRemovalConfirmation? _removalConfirmation;
    private readonly DelegateCommand _refreshLibraryCommand;
    private readonly DelegateCommand _openSelectedFolderCommand;
    private readonly DelegateCommand _relinkMissingFileCommand;
    private readonly DelegateCommand _removeSelectedCommand;
    private readonly DelegateCommand _beginSelectionCommand;
    private readonly DelegateCommand _cancelSelectionCommand;
    private readonly DelegateCommand _selectAllVisibleCommand;
    private readonly DelegateCommand _removeMarkedCommand;
    private readonly DelegateCommand<LibraryItem> _toggleMarkedCommand;
    private readonly DelegateCommand<LibraryItem> _removeItemCommand;
    private string _libraryNotice = string.Empty;
    private WorkspaceSurfaceState _surfaceState;
    private IReadOnlyList<LibraryItem> _allItems = [];
    private IReadOnlyList<LibraryItem> _items = [];
    private bool _isDisposed;
    private bool _isSelectionMode;

    public LibraryViewModel()
        : this(
            EmptyLibraryCatalog.Instance,
            WorkspaceSurfaceState.Empty,
            assetRelinker: null,
            mediaFilePicker: null,
            folderLauncher: null,
            assetRemover: null,
            removalConfirmation: null)
    {
    }

    public LibraryViewModel(ILibraryCatalog catalog)
        : this(
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            catalog.Assets.Count > 0
                ? WorkspaceSurfaceState.ContentReady
                : WorkspaceSurfaceState.Empty,
            catalog as ILibraryAssetRelinker,
            mediaFilePicker: null,
            folderLauncher: null,
            catalog as ILibraryAssetRemover,
            removalConfirmation: null)
    {
    }

    public LibraryViewModel(
        ILibraryCatalog catalog,
        ILibraryAssetRelinker assetRelinker,
        ILibraryMediaFilePicker mediaFilePicker,
        ILocalFolderLauncher folderLauncher,
        ILibraryAssetRemover? assetRemover = null,
        ILibraryRemovalConfirmation? removalConfirmation = null)
        : this(
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            catalog.Assets.Count > 0
                ? WorkspaceSurfaceState.ContentReady
                : WorkspaceSurfaceState.Empty,
            assetRelinker ?? throw new ArgumentNullException(nameof(assetRelinker)),
            mediaFilePicker ?? throw new ArgumentNullException(nameof(mediaFilePicker)),
            folderLauncher ?? throw new ArgumentNullException(nameof(folderLauncher)),
            assetRemover,
            removalConfirmation)
    {
    }

    private LibraryViewModel(
        ILibraryCatalog catalog,
        WorkspaceSurfaceState surfaceState,
        ILibraryAssetRelinker? assetRelinker,
        ILibraryMediaFilePicker? mediaFilePicker,
        ILocalFolderLauncher? folderLauncher,
        ILibraryAssetRemover? assetRemover,
        ILibraryRemovalConfirmation? removalConfirmation)
    {
        _catalog = catalog;
        _assetRelinker = assetRelinker;
        _mediaFilePicker = mediaFilePicker;
        _folderLauncher = folderLauncher;
        _assetRemover = assetRemover;
        _removalConfirmation = removalConfirmation;
        _surfaceState = surfaceState;
        Playback = new LibraryPlaybackViewModel();
        Categories = new[]
        {
            new LibraryCategoryItem(LibraryCategory.Projects, "Projects", "Icon.Project"),
            new LibraryCategoryItem(LibraryCategory.GeneratedClips, "Generated Clips", "Icon.Spark"),
            new LibraryCategoryItem(LibraryCategory.Montages, "Montages", "Icon.Grid"),
        };
        Modes = new[] { "All modes", "Individual clips", "Montage" };
        Statuses = new[] { "All statuses", "Ready", "Missing locally" };
        Dates = new[] { "Any date", "Today", "This week", "This month" };
        SortOptions = new[] { "Recently modified", "Name", "Duration", "Status" };
        OrganizationOptions = new[]
        {
            new LibraryOrganizationOption(LibraryOrganizationMode.Date, "Date"),
            new LibraryOrganizationOption(LibraryOrganizationMode.Folder, "Folder"),
            new LibraryOrganizationOption(LibraryOrganizationMode.Project, "Project"),
        };
        ClearFiltersCommand = new DelegateCommand(ClearFilters, () => HasActiveFilters);
        SetGridViewCommand = new DelegateCommand(() => ViewMode = LibraryViewMode.Grid);
        SetListViewCommand = new DelegateCommand(() => ViewMode = LibraryViewMode.List);
        _refreshLibraryCommand = new DelegateCommand(
            RefreshKnownAssets,
            () => _catalog.Assets.Count > 0);
        _openSelectedFolderCommand = new DelegateCommand(
            OpenSelectedFolder,
            CanOpenSelectedFolder);
        _relinkMissingFileCommand = new DelegateCommand(
            RelinkMissingFile,
            () => CanRelinkSelected);
        _removeSelectedCommand = new DelegateCommand(
            RemoveSelectedFromLibrary,
            () => CanRemoveSelected);
        _beginSelectionCommand = new DelegateCommand(
            BeginSelection,
            () => Items.Any(static item => item.Asset is not null));
        _cancelSelectionCommand = new DelegateCommand(
            CancelSelection,
            () => IsSelectionMode);
        _selectAllVisibleCommand = new DelegateCommand(
            SelectAllVisible,
            () => IsSelectionMode && Items.Any(static item => item.Asset is not null));
        _removeMarkedCommand = new DelegateCommand(
            RemoveMarkedFromLibrary,
            () => CanRemoveMarked);
        _toggleMarkedCommand = new DelegateCommand<LibraryItem>(ToggleMarked);
        _removeItemCommand = new DelegateCommand<LibraryItem>(
            RemoveItemFromLibrary,
            item => item.Asset is not null &&
                _assetRemover is not null &&
                _removalConfirmation is not null);

        _allItems = BuildItems(_catalog.Assets);
        RefreshView();

        _catalog.Changed += Catalog_Changed;
    }

    internal LibraryViewModel(WorkspaceSurfaceState surfaceState)
        : this(
            EmptyLibraryCatalog.Instance,
            surfaceState,
            assetRelinker: null,
            mediaFilePicker: null,
            folderLauncher: null,
            assetRemover: null,
            removalConfirmation: null)
    {
    }

    public IReadOnlyList<LibraryCategoryItem> Categories { get; }
    public IReadOnlyList<string> Modes { get; }
    public IReadOnlyList<string> Statuses { get; }
    public IReadOnlyList<string> Dates { get; }
    public IReadOnlyList<string> SortOptions { get; }
    public IReadOnlyList<LibraryOrganizationOption> OrganizationOptions { get; }
    public LibraryPlaybackViewModel Playback { get; }
    public IReadOnlyList<LibraryItem> Items => _items;
    public WorkspaceSurfaceState SurfaceState => _surfaceState;
    public bool IsEmpty => SurfaceState == WorkspaceSurfaceState.Empty;
    public bool HasNoVisibleItems =>
        Items.Count == 0 && (IsEmpty || IsContentReady);
    public bool IsContentReady => SurfaceState == WorkspaceSurfaceState.ContentReady;
    public bool IsLoading => SurfaceState == WorkspaceSurfaceState.Loading;
    public bool IsError => SurfaceState == WorkspaceSurfaceState.Error;
    public bool IsUnavailable => SurfaceState == WorkspaceSurfaceState.Unavailable;
    public bool ShouldShowPlaceholder => IsUnavailable || IsError;
    public bool IsGridView => ViewMode == LibraryViewMode.Grid;
    public bool IsListView => ViewMode == LibraryViewMode.List;
    public bool HasSelection => SelectedItem is not null;
    public bool CanRelinkSelected =>
        SelectedItem?.Asset is { IsAvailable: false } &&
        _assetRelinker is not null &&
        _mediaFilePicker is not null;
    public bool CanRemoveSelected =>
        SelectedItem?.Asset is not null &&
        _assetRemover is not null &&
        _removalConfirmation is not null;
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        private set
        {
            if (_isSelectionMode == value) return;
            _isSelectionMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSingleItemMode));
            RaiseSelectionCommandStates();
        }
    }
    public bool IsSingleItemMode => !IsSelectionMode;
    public int MarkedCount => Items.Count(static item => item.IsMarked);
    public bool HasMarkedItems => MarkedCount > 0;
    public bool CanRemoveMarked =>
        HasMarkedItems &&
        _assetRemover is not null &&
        _removalConfirmation is not null;
    public string SelectionSummary => MarkedCount == 1
        ? "1 video selected"
        : $"{MarkedCount} videos selected";
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchQuery) ||
        ModeFilter != Modes[0] ||
        StatusFilter != Statuses[0] ||
        DateFilter != Dates[0];
    public string ResultSummary => Items.Count == 0 ? "0 items" : $"{Items.Count} items";
    public string FilterSummary => HasActiveFilters ? "Filtered view" : "All available items";
    public string OrganizationSummary => OrganizationMode switch
    {
        LibraryOrganizationMode.Date => "Grouped by date",
        LibraryOrganizationMode.Folder => "Grouped by output folder",
        LibraryOrganizationMode.Project => "Grouped by render project",
        _ => "Organized Library",
    };
    public string EmptyTitle => HasActiveFilters ? "No items match these filters" : $"No {SelectedCategoryLabel.ToLowerInvariant()} yet";
    public string EmptyDescription => HasActiveFilters ? "Clear a filter or search term to see the full category." : GetFutureWorkflowMessage();
    public string SelectedCategoryLabel => GetCategoryLabel(SelectedCategory);
    public string StatusText => IsUnavailable ? "Library not connected" : ResultSummary;
    public string WorkspaceEyebrow => "LIBRARY / ORGANIZE";
    public string WorkspaceTitle => "Find the next cut";
    public string WorkspaceDescription =>
        "Browse finished clips and projects, filter the collection, and inspect the next asset.";
    public string ErrorSummary => "The library could not load its content.";
    public string LibraryNotice => _libraryNotice;
    public bool HasLibraryNotice =>
        !string.IsNullOrWhiteSpace(LibraryNotice);

    public LibraryCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value) return;
            _selectedCategory = value;
            SelectedItem = null;
            RaiseDerivedProperties();
        }
    }

    public LibraryViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value) return;
            _viewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGridView));
            OnPropertyChanged(nameof(IsListView));
        }
    }

    public LibraryOrganizationMode OrganizationMode
    {
        get => _organizationMode;
        set
        {
            if (_organizationMode == value) return;
            _organizationMode = value;
            RefreshView();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrganizationSummary));
        }
    }

    public string SearchQuery { get => _searchQuery; set { if (_searchQuery == value) return; _searchQuery = value; RaiseDerivedProperties(); } }
    public string ModeFilter { get => _modeFilter; set { if (_modeFilter == value) return; _modeFilter = value; RaiseDerivedProperties(); } }
    public string StatusFilter { get => _statusFilter; set { if (_statusFilter == value) return; _statusFilter = value; RaiseDerivedProperties(); } }
    public string DateFilter { get => _dateFilter; set { if (_dateFilter == value) return; _dateFilter = value; RaiseDerivedProperties(); } }
    public string SortBy { get => _sortBy; set { if (_sortBy == value) return; _sortBy = value; RefreshView(); OnPropertyChanged(); } }
    public LibraryItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value)) return;
            _selectedItem = value;
            Playback.Load(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanRelinkSelected));
            OnPropertyChanged(nameof(CanRemoveSelected));
            _openSelectedFolderCommand.RaiseCanExecuteChanged();
            _relinkMissingFileCommand.RaiseCanExecuteChanged();
            _removeSelectedCommand.RaiseCanExecuteChanged();
        }
    }
    public ICommand ClearFiltersCommand { get; }
    public ICommand SetGridViewCommand { get; }
    public ICommand SetListViewCommand { get; }
    public ICommand RefreshLibraryCommand => _refreshLibraryCommand;
    public ICommand OpenSelectedFolderCommand => _openSelectedFolderCommand;
    public ICommand RelinkMissingFileCommand => _relinkMissingFileCommand;
    public ICommand RemoveSelectedFromLibraryCommand => _removeSelectedCommand;
    public ICommand BeginSelectionCommand => _beginSelectionCommand;
    public ICommand CancelSelectionCommand => _cancelSelectionCommand;
    public ICommand SelectAllVisibleCommand => _selectAllVisibleCommand;
    public ICommand RemoveMarkedCommand => _removeMarkedCommand;
    public ICommand ToggleMarkedCommand => _toggleMarkedCommand;
    public ICommand RemoveItemCommand => _removeItemCommand;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _catalog.Changed -= Catalog_Changed;
    }

    private void ClearFilters()
    {
        SearchQuery = string.Empty;
        ModeFilter = Modes[0];
        StatusFilter = Statuses[0];
        DateFilter = Dates[0];
    }

    private string GetFutureWorkflowMessage() => SelectedCategory switch
    {
        LibraryCategory.Projects => "Finished Studio outputs will appear here after they are rendered.",
        LibraryCategory.GeneratedClips => "Generated clips will appear here after they are selected and rendered in Studio.",
        LibraryCategory.Montages => "Montages will appear here after they are finalized and rendered in Studio.",
        _ => "Finished Studio outputs will appear here after they are rendered.",
    };

    private void RaiseDerivedProperties()
    {
        RefreshView();
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(ModeFilter));
        OnPropertyChanged(nameof(StatusFilter));
        OnPropertyChanged(nameof(DateFilter));
        OnPropertyChanged(nameof(SelectedCategoryLabel));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(StatusText));
        if (ClearFiltersCommand is DelegateCommand clear) clear.RaiseCanExecuteChanged();
    }

    private string GetCategoryLabel(LibraryCategory category)
    {
        foreach (LibraryCategoryItem item in Categories)
            if (item.Key == category) return item.Label;
        throw new InvalidOperationException("The selected library category is not defined.");
    }

    private void Catalog_Changed(object? sender, EventArgs e)
    {
        string? selectedId = SelectedItem?.Asset?.Id;
        _allItems = BuildItems(_catalog.Assets);
        _surfaceState = _allItems.Count == 0
            ? WorkspaceSurfaceState.Empty
            : WorkspaceSurfaceState.ContentReady;
        RefreshView();
        RestoreSelection(selectedId);
        NotifyCatalogChanged();
        _refreshLibraryCommand.RaiseCanExecuteChanged();
    }

    private void RefreshKnownAssets()
    {
        string? selectedId = SelectedItem?.Asset?.Id;
        _allItems = BuildItems(_catalog.Assets);
        RefreshView();
        RestoreSelection(selectedId);
        SetLibraryNotice(
            "Known Library entries were refreshed. Moved files remain missing until you relink them explicitly.");
        NotifyCatalogChanged();
    }

    private bool CanOpenSelectedFolder()
    {
        string? path = SelectedItem?.Asset?.OutputFullPath;
        string? parent = string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetDirectoryName(path);
        return _folderLauncher is not null &&
            parent is not null &&
            Directory.Exists(parent);
    }

    private void OpenSelectedFolder()
    {
        string? parent = Path.GetDirectoryName(
            SelectedItem?.Asset?.OutputFullPath ?? string.Empty);
        if (parent is null || _folderLauncher is null)
        {
            return;
        }

        try
        {
            _folderLauncher.OpenFolder(parent);
            SetLibraryNotice("Opened the selected clip's folder.");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            System.ComponentModel.Win32Exception)
        {
            SetLibraryNotice("The output folder could not be opened: " + exception.Message);
        }
    }

    private void RelinkMissingFile()
    {
        LibraryMediaAsset? asset = SelectedItem?.Asset;
        if (asset is null || _assetRelinker is null || _mediaFilePicker is null)
        {
            return;
        }

        try
        {
            string? replacement = _mediaFilePicker.PickReplacementMedia(asset);
            if (replacement is null)
            {
                return;
            }
            _assetRelinker.RelinkMissingAsset(asset.Id, replacement);
            SetLibraryNotice(
                "The moved clip was relinked without changing its Library identity or metadata.");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            SetLibraryNotice("The clip could not be relinked: " + exception.Message);
        }
    }

    private void RemoveSelectedFromLibrary()
    {
        LibraryMediaAsset? asset = SelectedItem?.Asset;
        if (asset is null ||
            _assetRemover is null ||
            _removalConfirmation is null ||
            !_removalConfirmation.ConfirmRemoveFromLibrary([asset]))
        {
            return;
        }

        try
        {
            _assetRemover.RemoveAsset(asset.Id);
            SetLibraryNotice(
                "The entry was removed from Library. Its rendered video remains on disk.");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            SetLibraryNotice("The Library entry could not be removed: " + exception.Message);
        }
    }

    private void BeginSelection()
    {
        ClearMarks();
        IsSelectionMode = true;
    }

    private void CancelSelection()
    {
        ClearMarks();
        IsSelectionMode = false;
    }

    private void SelectAllVisible()
    {
        foreach (LibraryItem item in Items.Where(static item => item.Asset is not null))
        {
            item.IsMarked = true;
        }
        NotifyMarkedSelectionChanged();
    }

    private void ToggleMarked(LibraryItem item)
    {
        if (!IsSelectionMode || item.Asset is null)
        {
            return;
        }
        item.IsMarked = !item.IsMarked;
        NotifyMarkedSelectionChanged();
    }

    private void RemoveItemFromLibrary(LibraryItem item)
    {
        if (item.Asset is null)
        {
            return;
        }
        RemoveAssetsFromLibrary([item.Asset]);
    }

    private void RemoveMarkedFromLibrary()
    {
        LibraryMediaAsset[] marked = Items
            .Where(static item => item.IsMarked && item.Asset is not null)
            .Select(static item => item.Asset!)
            .ToArray();
        if (marked.Length == 0)
        {
            return;
        }
        RemoveAssetsFromLibrary(marked);
    }

    private void RemoveAssetsFromLibrary(IReadOnlyList<LibraryMediaAsset> assets)
    {
        if (_assetRemover is null ||
            _removalConfirmation is null ||
            !_removalConfirmation.ConfirmRemoveFromLibrary(assets))
        {
            return;
        }

        try
        {
            _assetRemover.RemoveAssets(assets.Select(static asset => asset.Id).ToArray());
            ClearMarks();
            IsSelectionMode = false;
            SetLibraryNotice(
                assets.Count == 1
                    ? "The entry was removed from Library. Its rendered video remains on disk."
                    : $"{assets.Count} entries were removed from Library. Their rendered videos remain on disk.");
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            SetLibraryNotice("The Library entries could not be removed: " + exception.Message);
        }
    }

    private void ClearMarks()
    {
        foreach (LibraryItem item in _allItems)
        {
            item.IsMarked = false;
        }
        NotifyMarkedSelectionChanged();
    }

    private void NotifyMarkedSelectionChanged()
    {
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(HasMarkedItems));
        OnPropertyChanged(nameof(CanRemoveMarked));
        OnPropertyChanged(nameof(SelectionSummary));
        RaiseSelectionCommandStates();
    }

    private void RaiseSelectionCommandStates()
    {
        _beginSelectionCommand.RaiseCanExecuteChanged();
        _cancelSelectionCommand.RaiseCanExecuteChanged();
        _selectAllVisibleCommand.RaiseCanExecuteChanged();
        _removeMarkedCommand.RaiseCanExecuteChanged();
    }

    private void RestoreSelection(string? assetId)
    {
        if (assetId is null)
        {
            return;
        }
        SelectedItem = _items.FirstOrDefault(item =>
            item.Asset?.Id.Equals(assetId, StringComparison.Ordinal) == true) ??
            SelectedItem;
    }

    private void SetLibraryNotice(string value)
    {
        _libraryNotice = value;
        OnPropertyChanged(nameof(LibraryNotice));
        OnPropertyChanged(nameof(HasLibraryNotice));
    }

    private void RefreshView()
    {
        IEnumerable<LibraryItem> query = _allItems;
        query = SelectedCategory switch
        {
            LibraryCategory.GeneratedClips => query.Where(item =>
                item.Asset?.Mode == GenerationMode.IndividualClips),
            LibraryCategory.Montages => query.Where(item =>
                item.Asset?.Mode == GenerationMode.Montage),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string search = SearchQuery.Trim();
            query = query.Where(item =>
                item.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Detail.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Type.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        if (ModeFilter == "Individual clips")
        {
            query = query.Where(item =>
                item.Asset?.Mode == GenerationMode.IndividualClips);
        }
        else if (ModeFilter == "Montage")
        {
            query = query.Where(item =>
                item.Asset?.Mode == GenerationMode.Montage);
        }
        if (StatusFilter != Statuses[0])
        {
            query = query.Where(item =>
                item.Status.Equals(StatusFilter, StringComparison.Ordinal));
        }
        DateTime today = DateTime.Today;
        query = DateFilter switch
        {
            "Today" => query.Where(item =>
                item.Asset?.AddedAtUtc.ToLocalTime().Date == today),
            "This week" => query.Where(item =>
                item.Asset?.AddedAtUtc.ToLocalTime().Date >= today.AddDays(-6)),
            "This month" => query.Where(item =>
                item.Asset?.AddedAtUtc.ToLocalTime().Date >= today.AddMonths(-1)),
            _ => query,
        };
        query = SortBy switch
        {
            "Name" => query.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            "Duration" => query.OrderByDescending(item => item.Asset?.Duration),
            "Status" => query.OrderBy(item => item.Status, StringComparer.Ordinal),
            _ => query.OrderByDescending(item => item.Asset?.AddedAtUtc),
        };
        _items = query.ToArray();
        ApplyOrganizationGroups(_items);
        if (SelectedItem is null || !_items.Contains(SelectedItem))
        {
            SelectedItem = _items.FirstOrDefault();
        }
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoVisibleItems));
        NotifyMarkedSelectionChanged();
    }

    private void NotifyCatalogChanged()
    {
        foreach (string propertyName in new[]
        {
            nameof(Items), nameof(SurfaceState), nameof(IsEmpty),
            nameof(IsContentReady), nameof(ResultSummary), nameof(StatusText),
            nameof(EmptyTitle), nameof(EmptyDescription),
        })
        {
            OnPropertyChanged(propertyName);
        }
    }

    private static IReadOnlyList<LibraryItem> BuildItems(
        IReadOnlyList<LibraryMediaAsset> assets) =>
        assets.Select(asset => new LibraryItem(
                asset.Title,
                asset.Mode == GenerationMode.Montage
                    ? "Montage"
                    : "Generated Clip",
                FormatDuration(asset.Duration),
                asset.AddedAtUtc.LocalDateTime.ToString("g"),
                asset.IsAvailable ? "Ready" : "Missing locally",
                asset.AspectRatioText,
                asset.Mode == GenerationMode.Montage
                    ? $"{asset.ContributingCandidateCount} selected segments"
                    : "Final Studio render",
                asset.Mode == GenerationMode.Montage
                    ? "Icon.Grid"
                    : "Icon.Spark",
                asset.ThumbnailFullPath,
                asset))
            .ToArray();

    private void ApplyOrganizationGroups(IReadOnlyList<LibraryItem> items)
    {
        IReadOnlyDictionary<string, string> projectLabels =
            BuildProjectLabels(_allItems);
        foreach (LibraryItem item in items)
        {
            LibraryMediaAsset? asset = item.Asset;
            item.OrganizationGroup = OrganizationMode switch
            {
                LibraryOrganizationMode.Folder => GetFolderLabel(
                    asset?.OutputFullPath),
                LibraryOrganizationMode.Project when asset is not null =>
                    projectLabels[asset.ProjectId],
                _ => GetDateLabel(asset?.AddedAtUtc),
            };
        }
    }

    private static IReadOnlyDictionary<string, string> BuildProjectLabels(
        IReadOnlyList<LibraryItem> items) =>
        items
            .Where(static item => item.Asset is not null)
            .GroupBy(
                static item => item.Asset!.ProjectId,
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    LibraryMediaAsset newest = group
                        .Select(static item => item.Asset!)
                        .OrderByDescending(static asset => asset.AddedAtUtc)
                        .First();
                    int count = group.Count();
                    return $"Render · {newest.AddedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt} · " +
                        (count == 1 ? "1 video" : $"{count} videos");
                },
                StringComparer.Ordinal);

    private static string GetFolderLabel(string? mediaFullPath)
    {
        string? directory = string.IsNullOrWhiteSpace(mediaFullPath)
            ? null
            : Path.GetDirectoryName(mediaFullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return "Folder unavailable";
        }

        string? leaf = Path.GetFileName(directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(leaf)
            ? directory
            : leaf;
    }

    private static string GetDateLabel(DateTimeOffset? addedAtUtc)
    {
        if (addedAtUtc is null)
        {
            return "Date unavailable";
        }

        DateTime local = addedAtUtc.Value.LocalDateTime;
        DateTime today = DateTime.Today;
        if (local.Date == today)
        {
            return "Today";
        }
        if (local.Date == today.AddDays(-1))
        {
            return "Yesterday";
        }
        return local.ToString("dddd, MMMM d, yyyy");
    }

    private static string FormatDuration(TimeSpan duration) =>
        MediaTimeFormatter.Format(duration);

}
