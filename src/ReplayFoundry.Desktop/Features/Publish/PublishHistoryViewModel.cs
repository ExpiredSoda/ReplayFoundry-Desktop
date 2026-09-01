using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Publish;

public sealed class PublishHistoryViewModel : ObservableObject
{
    private readonly IPublishHistoryDialogService? _dialog;
    private readonly IPublishHistoryLinkLauncher? _linkLauncher;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeZoneInfo _timeZone;
    private readonly DelegateCommand _openCommand;
    private readonly DelegateCommand _loadMoreCommand;
    private readonly DelegateCommand _clearFiltersCommand;
    private readonly DelegateCommand<PublishHistoryListItem>
        _openOnYouTubeCommand;
    private IReadOnlyList<YouTubePublishHistoryEntry> _entries = [];
    private PublishHistoryProjection _projection =
        PublishHistoryProjection.Empty;
    private string _searchQuery = string.Empty;
    private PublishHistoryStatusFilter _selectedStatusFilter;
    private PublishHistorySortOrder _selectedSortOrder;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private int _visibleLimit = PublishHistoryProjector.PageSize;
    private string _linkNotice = string.Empty;

    public PublishHistoryViewModel(
        IPublishHistoryDialogService? dialog = null,
        IPublishHistoryLinkLauncher? linkLauncher = null,
        Func<DateTimeOffset>? utcNow = null,
        TimeZoneInfo? timeZone = null)
    {
        _dialog = dialog;
        _linkLauncher = linkLauncher;
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        StatusFilters = PublishHistoryProjector.CreateStatusFilters();
        SortOrders = PublishHistoryProjector.CreateSortOrders();
        _openCommand = new DelegateCommand(
            () => _dialog?.Show(this),
            () => _dialog is not null && HasItems);
        _loadMoreCommand = new DelegateCommand(
            LoadMore,
            () => HasMoreItems);
        _clearFiltersCommand = new DelegateCommand(
            ClearFilters,
            () => HasActiveFilters);
        _openOnYouTubeCommand =
            new DelegateCommand<PublishHistoryListItem>(
                OpenOnYouTube,
                CanOpenOnYouTube);
    }

    public IReadOnlyList<
        PublishHistoryChoice<PublishHistoryStatusFilter>> StatusFilters
    { get; }
    public IReadOnlyList<PublishHistoryChoice<PublishHistorySortOrder>>
        SortOrders
    { get; }
    public IReadOnlyList<PublishHistoryListItem> RecentItems =>
        _projection.RecentItems;
    public IReadOnlyList<PublishHistoryListItem> PagedItems =>
        _projection.PagedItems;
    public int TotalCount => _projection.TotalCount;
    public int FilteredCount => _projection.FilteredCount;
    public bool HasItems => TotalCount > 0;
    public bool HasPagedItems => PagedItems.Count > 0;
    public bool HasMoreItems => _projection.HasMore;
    public bool HasAdditionalDashboardItems =>
        TotalCount > PublishHistoryProjector.DashboardItemLimit;
    public bool HasInvalidDateRange =>
        _projection.HasInvalidDateRange;
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchQuery) ||
        SelectedStatusFilter != PublishHistoryStatusFilter.All ||
        FromDate.HasValue ||
        ToDate.HasValue;
    public bool HasLinkNotice =>
        !string.IsNullOrWhiteSpace(LinkNotice);

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            string normalized = value ?? string.Empty;
            if (_searchQuery == normalized) return;
            _searchQuery = normalized;
            OnPropertyChanged();
            Rebuild(resetPage: true);
        }
    }

    public PublishHistoryStatusFilter SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException(
                    "The YouTube history status filter is not available.",
                    nameof(value));
            }
            if (_selectedStatusFilter == value) return;
            _selectedStatusFilter = value;
            OnPropertyChanged();
            Rebuild(resetPage: true);
        }
    }

    public PublishHistorySortOrder SelectedSortOrder
    {
        get => _selectedSortOrder;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException(
                    "The YouTube history sort order is not available.",
                    nameof(value));
            }
            if (_selectedSortOrder == value) return;
            _selectedSortOrder = value;
            OnPropertyChanged();
            Rebuild(resetPage: true);
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            DateTime? normalized = value?.Date;
            if (_fromDate == normalized) return;
            _fromDate = normalized;
            OnPropertyChanged();
            Rebuild(resetPage: true);
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            DateTime? normalized = value?.Date;
            if (_toDate == normalized) return;
            _toDate = normalized;
            OnPropertyChanged();
            Rebuild(resetPage: true);
        }
    }

    public string DashboardSummary => TotalCount == 0
        ? "No upload activity recorded yet."
        : $"{FormatCount(TotalCount, "record")} · " +
          $"{_projection.ScheduledCount} scheduled · " +
          $"{_projection.PublishedCount} published · " +
          (_projection.NeedsAttentionCount == 1
              ? "1 needs attention"
              : $"{_projection.NeedsAttentionCount} need attention");
    public string TotalBadge =>
        $"{TotalCount} RECORD{(TotalCount == 1 ? string.Empty : "S")}";
    public string DashboardRangeSummary => HasAdditionalDashboardItems
        ? $"Newest {PublishHistoryProjector.DashboardItemLimit} of {TotalCount}"
        : FormatCount(TotalCount, "record");
    public string ResultSummary => HasInvalidDateRange
        ? "Choose an end date on or after the start date."
        : FilteredCount == 0
            ? "No history matches these filters."
            : PagedItems.Count < FilteredCount
                ? $"Showing {PagedItems.Count} of {FilteredCount} matching records"
                : $"Showing all {FormatCount(FilteredCount, "matching record")}";
    public string LoadMoreText
    {
        get
        {
            int remaining = Math.Max(0, FilteredCount - PagedItems.Count);
            int next = Math.Min(PublishHistoryProjector.PageSize, remaining);
            return $"Load {next} more";
        }
    }
    public string LinkNotice
    {
        get => _linkNotice;
        private set
        {
            if (_linkNotice == value) return;
            _linkNotice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLinkNotice));
        }
    }

    public ICommand OpenCommand => _openCommand;
    public ICommand LoadMoreCommand => _loadMoreCommand;
    public ICommand ClearFiltersCommand => _clearFiltersCommand;
    public ICommand OpenOnYouTubeCommand => _openOnYouTubeCommand;

    public void Refresh(IReadOnlyList<YouTubePublishHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToArray();
        Rebuild(resetPage: true);
    }

    private void LoadMore()
    {
        _visibleLimit += PublishHistoryProjector.PageSize;
        Rebuild(resetPage: false);
    }

    private void ClearFilters()
    {
        _searchQuery = string.Empty;
        _selectedStatusFilter = PublishHistoryStatusFilter.All;
        _fromDate = null;
        _toDate = null;
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(SelectedStatusFilter));
        OnPropertyChanged(nameof(FromDate));
        OnPropertyChanged(nameof(ToDate));
        Rebuild(resetPage: true);
    }

    private bool CanOpenOnYouTube(PublishHistoryListItem? item) =>
        _linkLauncher is not null &&
        item is not null &&
        TryGetTrustedYouTubeUri(item.Url, out _);

    private void OpenOnYouTube(PublishHistoryListItem? item)
    {
        if (_linkLauncher is null ||
            item is null ||
            !TryGetTrustedYouTubeUri(item.Url, out Uri? uri) ||
            uri is null)
        {
            return;
        }

        try
        {
            _linkLauncher.Open(uri);
            LinkNotice = string.Empty;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            LinkNotice =
                "Replay Foundry could not open this YouTube page. Try again from your browser.";
        }
    }

    internal static bool TryGetTrustedYouTubeUri(
        string? value,
        out Uri? uri) =>
        PublishHistoryLinkPolicy.TryCreateTrustedYouTubeUri(value, out uri);

    private void Rebuild(bool resetPage)
    {
        if (resetPage)
        {
            _visibleLimit = PublishHistoryProjector.PageSize;
        }

        DateTime localToday = TimeZoneInfo.ConvertTime(
            _utcNow(),
            _timeZone).Date;
        _projection = PublishHistoryProjector.Build(
            _entries,
            _searchQuery,
            _selectedStatusFilter,
            _fromDate,
            _toDate,
            _selectedSortOrder,
            _visibleLimit,
            localToday,
            _timeZone);
        RaiseProjectionChanged();
    }

    private void RaiseProjectionChanged()
    {
        OnPropertyChanged(nameof(RecentItems));
        OnPropertyChanged(nameof(PagedItems));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasPagedItems));
        OnPropertyChanged(nameof(HasMoreItems));
        OnPropertyChanged(nameof(HasAdditionalDashboardItems));
        OnPropertyChanged(nameof(HasInvalidDateRange));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(DashboardSummary));
        OnPropertyChanged(nameof(TotalBadge));
        OnPropertyChanged(nameof(DashboardRangeSummary));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(LoadMoreText));
        _openCommand.RaiseCanExecuteChanged();
        _loadMoreCommand.RaiseCanExecuteChanged();
        _clearFiltersCommand.RaiseCanExecuteChanged();
        _openOnYouTubeCommand.RaiseCanExecuteChanged();
    }

    private static string FormatCount(int count, string singular) =>
        $"{count} {singular}{(count == 1 ? string.Empty : "s")}";
}
