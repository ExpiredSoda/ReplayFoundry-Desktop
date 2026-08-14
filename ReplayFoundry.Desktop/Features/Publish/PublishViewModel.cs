using System.Globalization;
using System.IO;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.Desktop.Features.Publish;

public sealed class PublishViewModel : ObservableObject, IWorkspaceChromeSource, IDisposable
{
    private static readonly TimeSpan MinimumScheduleLeadTime =
        TimeSpan.FromMinutes(30);

    private readonly ILibraryCatalog _libraryCatalog;
    private readonly PublishYouTubeOperationController? _youtubeOperations;
    private readonly IYouTubeConnectionPermission? _connectionPermission;
    private readonly IYouTubePublishPreferencesStore _preferences;
    private readonly IYouTubePublishDraftStore _drafts;
    private readonly IThumbnailFilePicker? _thumbnailPicker;
    private readonly IPublishPreparationDialogService? _preparationDialog;
    private readonly IPublishBulkConfirmation? _bulkConfirmation;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeZoneInfo _timeZone;
    private readonly AsyncDelegateCommand _connectCommand;
    private readonly AsyncDelegateCommand _disconnectCommand;
    private readonly AsyncDelegateCommand _refreshYouTubeCommand;
    private readonly AsyncDelegateCommand _reconcileYouTubeHistoryCommand;
    private readonly AsyncDelegateCommand _publishCommand;
    private readonly AsyncDelegateCommand _publishAllNowCommand;
    private readonly DelegateCommand _cancelPublishCommand;
    private readonly DelegateCommand _pickThumbnailCommand;
    private readonly DelegateCommand _clearThumbnailCommand;
    private readonly DelegateCommand _addPreferredSlotCommand;
    private readonly DelegateCommand<YouTubePreferredScheduleSlot>
        _removePreferredSlotCommand;
    private readonly DelegateCommand _useNextPreferredSlotCommand;
    private readonly DelegateCommand _clearHistoryCommand;
    private readonly DelegateCommand _saveDraftCommand;
    private readonly DelegateCommand _createPlanCommand;
    private readonly DelegateCommand<LibraryMediaAsset> _prepareAssetCommand;
    private readonly DelegateCommand _clearLibraryFiltersCommand;

    private WorkspaceSurfaceState _surfaceState;
    private PublishCalendarMode _selectedCalendarMode =
        PublishCalendarMode.Month;
    private PublishCalendarPlatform _selectedCalendarPlatform =
        PublishCalendarPlatform.All;
    private DateTime _calendarAnchor;
    private IReadOnlyList<PublishCalendarDay> _calendarDays = [];
    private PublishCalendarDay? _selectedCalendarDay;
    private LibraryMediaAsset? _selectedAsset;
    private IReadOnlyList<LibraryMediaAsset> _availableAssets = [];
    private string _librarySearchQuery = string.Empty;
    private string _libraryDateFilter = "Any date";
    private string? _selectedLibraryFolder;
    private YouTubeAccountConnection? _connection;
    private IReadOnlyList<YouTubePlaylist> _playlists = [];
    private IReadOnlyList<YouTubeVideoCategory> _categories = [];
    private string? _selectedPlaylistId;
    private string? _selectedCategoryId;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _tags = string.Empty;
    private YouTubeVideoVisibility _visibility =
        YouTubeVideoVisibility.Private;
    private YouTubePublishTiming _timing =
        YouTubePublishTiming.PublishNow;
    private YouTubeAudience _audience =
        YouTubeAudience.NotMadeForKids;
    private bool _containsSyntheticMedia;
    private bool _notifySubscribers = true;
    private DateTime? _scheduledDate;
    private string _scheduledTimeText = "6:00 PM";
    private DayOfWeek _preferredDay = DayOfWeek.Friday;
    private string _preferredTimeText = "6:00 PM";
    private string? _thumbnailFullPath;
    private bool _isInitializing;
    private bool _isPublishing;
    private string _operationTitle = string.Empty;
    private string _operationDetail = string.Empty;
    private double? _operationPercentage;
    private string _notice = string.Empty;
    private string _technicalDetails = string.Empty;
    private YouTubePublishResult? _lastResult;
    private bool _isInitialized;
    private bool _isDisposed;

    public PublishViewModel()
        : this(
            EmptyLibraryCatalog.Instance,
            youtube: null,
            new InMemoryYouTubePublishPreferencesStore(),
            thumbnailPicker: null,
            WorkspaceSurfaceState.Empty,
            static () => DateTimeOffset.UtcNow,
            TimeZoneInfo.Local,
            drafts: new InMemoryYouTubePublishDraftStore())
    {
    }

    public PublishViewModel(
        ILibraryCatalog libraryCatalog,
        IYouTubePublishingService? youtube,
        IYouTubePublishPreferencesStore preferences,
        IThumbnailFilePicker thumbnailPicker,
        IYouTubeConnectionPermission? connectionPermission = null,
        IYouTubePublishDraftStore? drafts = null,
        IPublishPreparationDialogService? preparationDialog = null,
        IPublishBulkConfirmation? bulkConfirmation = null,
        IPublishEditorialMetadataService? editorialMetadata = null,
        IEditorialRerollPreference? editorialRerollPreference = null)
        : this(
            libraryCatalog ?? throw new ArgumentNullException(nameof(libraryCatalog)),
            youtube,
            preferences ?? throw new ArgumentNullException(nameof(preferences)),
            thumbnailPicker ?? throw new ArgumentNullException(nameof(thumbnailPicker)),
            libraryCatalog.Assets.Count > 0
                ? WorkspaceSurfaceState.ContentReady
                : WorkspaceSurfaceState.Empty,
            static () => DateTimeOffset.UtcNow,
            TimeZoneInfo.Local,
            connectionPermission,
            drafts,
            preparationDialog,
            bulkConfirmation,
            editorialMetadata,
            editorialRerollPreference)
    {
    }

    internal PublishViewModel(WorkspaceSurfaceState surfaceState)
        : this(
            EmptyLibraryCatalog.Instance,
            youtube: null,
            new InMemoryYouTubePublishPreferencesStore(),
            thumbnailPicker: null,
            surfaceState,
            static () => DateTimeOffset.UtcNow,
            TimeZoneInfo.Local,
            drafts: new InMemoryYouTubePublishDraftStore())
    {
    }

    internal PublishViewModel(
        ILibraryCatalog libraryCatalog,
        IYouTubePublishingService? youtube,
        IYouTubePublishPreferencesStore preferences,
        IThumbnailFilePicker? thumbnailPicker,
        WorkspaceSurfaceState surfaceState,
        Func<DateTimeOffset> utcNow,
        TimeZoneInfo timeZone,
        IYouTubeConnectionPermission? connectionPermission = null,
        IYouTubePublishDraftStore? drafts = null,
        IPublishPreparationDialogService? preparationDialog = null,
        IPublishBulkConfirmation? bulkConfirmation = null,
        IPublishEditorialMetadataService? editorialMetadata = null,
        IEditorialRerollPreference? editorialRerollPreference = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(utcNow);
        ArgumentNullException.ThrowIfNull(timeZone);
        _libraryCatalog = libraryCatalog ??
            throw new ArgumentNullException(nameof(libraryCatalog));
        _youtubeOperations = youtube is null
            ? null
            : new PublishYouTubeOperationController(youtube);
        _connectionPermission = connectionPermission;
        _preferences = preferences;
        _drafts = drafts ?? new InMemoryYouTubePublishDraftStore();
        _thumbnailPicker = thumbnailPicker;
        _preparationDialog = preparationDialog;
        _bulkConfirmation = bulkConfirmation;
        _surfaceState = surfaceState;
        _utcNow = utcNow;
        _timeZone = timeZone;
        DateTime localToday = TimeZoneInfo.ConvertTime(
            utcNow(),
            timeZone).Date;
        _calendarAnchor = new DateTime(
            localToday.Year,
            localToday.Month,
            1);
        _scheduledDate = localToday.AddDays(1);

        CalendarModes =
        [
            new PublishCalendarModeItem(
                PublishCalendarMode.Month,
                "Month"),
            new PublishCalendarModeItem(
                PublishCalendarMode.Week,
                "Week"),
        ];
        CalendarPlatformFilters =
        [
            new PublishCalendarPlatformItem(
                PublishCalendarPlatform.All,
                "All YouTube plans",
                "Icon.Grid"),
            new PublishCalendarPlatformItem(
                PublishCalendarPlatform.YouTube,
                "Scheduled videos",
                "Icon.Play"),
        ];
        VisibilityOptions = Enum.GetValues<YouTubeVideoVisibility>()
            .Select(static value => new PublishChoiceItem<YouTubeVideoVisibility>(
                value,
                value switch
                {
                    YouTubeVideoVisibility.Public => "Public",
                    YouTubeVideoVisibility.Unlisted => "Unlisted",
                    _ => "Private",
                },
                value switch
                {
                    YouTubeVideoVisibility.Public =>
                        "Anyone can watch when the upload is ready.",
                    YouTubeVideoVisibility.Unlisted =>
                        "Only people with the link can watch.",
                    _ => "Only you and invited viewers can watch.",
                }))
            .ToArray();
        TimingOptions =
        [
            new PublishChoiceItem<YouTubePublishTiming>(
                YouTubePublishTiming.PublishNow,
                "Upload now",
                "YouTube applies the visibility you choose."),
            new PublishChoiceItem<YouTubePublishTiming>(
                YouTubePublishTiming.Schedule,
                "Schedule release",
                "Upload privately now and let YouTube publish it later."),
        ];
        AudienceOptions =
        [
            new PublishChoiceItem<YouTubeAudience>(
                YouTubeAudience.NotMadeForKids,
                "No, it is not made for kids",
                "Choose this only when it accurately describes the video."),
            new PublishChoiceItem<YouTubeAudience>(
                YouTubeAudience.MadeForKids,
                "Yes, it is made for kids",
                "YouTube limits some features on child-directed videos."),
        ];
        PreferredDays = Enum.GetValues<DayOfWeek>();
        LibraryDateFilters = ["Any date", "Today", "This week", "This month"];

        PreviousCalendarCommand = new DelegateCommand(
            () => MoveCalendar(-1));
        NextCalendarCommand = new DelegateCommand(
            () => MoveCalendar(1));
        TodayCalendarCommand = new DelegateCommand(ReturnCalendarToToday);
        _createPlanCommand = new DelegateCommand(
            OpenSchedulePreparation,
            () => HasAsset && !IsBusy);
        _connectCommand = new AsyncDelegateCommand(
            ConnectAsync,
            () => IsYouTubeConfigured && IsOnlineConnectionEnabled &&
                  !IsBusy && !IsConnected);
        _disconnectCommand = new AsyncDelegateCommand(
            DisconnectAsync,
            () => IsYouTubeConfigured && !IsBusy && IsConnected);
        _refreshYouTubeCommand = new AsyncDelegateCommand(
            RefreshYouTubeAsync,
            () => IsYouTubeConfigured && IsOnlineConnectionEnabled &&
                  !IsBusy && IsConnected);
        _reconcileYouTubeHistoryCommand = new AsyncDelegateCommand(
            ReconcileYouTubeHistoryAsync,
            () => IsYouTubeConfigured && IsOnlineConnectionEnabled &&
                  !IsBusy && IsConnected &&
                  (_youtubeOperations?.History.Any(
                      static entry => entry.VideoId is not null) == true));
        _publishCommand = new AsyncDelegateCommand(
            PublishAsync,
            () => CanPublish);
        _publishAllNowCommand = new AsyncDelegateCommand(
            PublishAllNowAsync,
            () => CanPublishAllNow);
        _cancelPublishCommand = new DelegateCommand(
            CancelPublish,
            () => IsPublishing);
        _pickThumbnailCommand = new DelegateCommand(
            PickThumbnail,
            () => _thumbnailPicker is not null && HasAsset && !IsBusy);
        _clearThumbnailCommand = new DelegateCommand(
            () => ThumbnailFullPath = null,
            () => ThumbnailFullPath is not null && !IsBusy);
        _addPreferredSlotCommand = new DelegateCommand(
            AddPreferredSlot,
            () => PublishPresentationRules.TryParseTime(PreferredTimeText, out _));
        _removePreferredSlotCommand =
            new DelegateCommand<YouTubePreferredScheduleSlot>(
                RemovePreferredSlot);
        _useNextPreferredSlotCommand = new DelegateCommand(
            UseNextPreferredSlot,
            () => PreferredSlots.Count > 0);
        _clearHistoryCommand = new DelegateCommand(
            ClearHistory,
            () => HistoryItems.Count > 0 && !IsBusy);
        _saveDraftCommand = new DelegateCommand(
            SaveDraft,
            () => CanSaveDraft);
        _prepareAssetCommand = new DelegateCommand<LibraryMediaAsset>(
            PrepareAsset,
            asset => AvailableAssets.Any(value => ReferenceEquals(value, asset)) && !IsBusy);
        _clearLibraryFiltersCommand = new DelegateCommand(
            ClearLibraryFilters,
            () => HasActiveLibraryFilters);
        Editorial = new PublishEditorialMetadataViewModel(
            editorialMetadata,
            result =>
            {
                Title = result.Title;
                Description = result.Description;
                Tags = result.Tags;
            },
            () => new PublishEditorialMetadataSnapshot(
                Title,
                Description,
                Tags),
            () =>
            {
                if (CanSaveDraft)
                {
                    SaveDraft(announce: false);
                }
            },
            () => IsInitializing || IsPublishing,
            editorialRerollPreference);
        Editorial.PropertyChanged += Editorial_PropertyChanged;

        _libraryCatalog.Changed += LibraryCatalog_Changed;
        if (_connectionPermission is not null)
        {
            _connectionPermission.Changed +=
                YouTubeConnectionPermission_Changed;
        }
        BindCurrentProject();
        RebuildCalendar();
    }


    public IReadOnlyList<PublishCalendarModeItem> CalendarModes { get; }
    public IReadOnlyList<PublishCalendarPlatformItem>
        CalendarPlatformFilters
    { get; }
    public IReadOnlyList<string> CalendarWeekdayHeaders { get; } =
        ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];
    public IReadOnlyList<PublishChoiceItem<YouTubeVideoVisibility>>
        VisibilityOptions
    { get; }
    public IReadOnlyList<PublishChoiceItem<YouTubePublishTiming>>
        TimingOptions
    { get; }
    public IReadOnlyList<PublishChoiceItem<YouTubeAudience>>
        AudienceOptions
    { get; }
    public IReadOnlyList<DayOfWeek> PreferredDays { get; }
    public IReadOnlyList<string> LibraryDateFilters { get; }

    public WorkspaceSurfaceState SurfaceState => _surfaceState;
    public IReadOnlyList<LibraryMediaAsset> AvailableAssets =>
        _availableAssets;
    public IReadOnlyList<LibraryMediaAsset> VisibleLibraryAssets =>
        FilterLibraryAssets();
    public IReadOnlyList<PublishLibraryFolderItem> LibraryFolderOptions =>
        BuildLibraryFolderOptions();
    public IReadOnlyList<PublishLibraryItem> LibraryItems =>
        VisibleLibraryAssets.Select(asset => new PublishLibraryItem(
            asset,
            asset.Title,
            $"{PublishPresentationRules.FormatDuration(asset.Duration)} · {asset.AspectRatioText}",
            BuildLibraryCollectionDetail(asset),
            GetAssetPublishState(asset),
            asset.ThumbnailFullPath)).ToArray();
    public string LibrarySearchQuery
    {
        get => _librarySearchQuery;
        set
        {
            value ??= string.Empty;
            if (_librarySearchQuery == value) return;
            _librarySearchQuery = value;
            RaiseLibraryProjectionChanged();
        }
    }
    public string LibraryDateFilter
    {
        get => _libraryDateFilter;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!LibraryDateFilters.Contains(value, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    "The Publish Library date filter is not defined.",
                    nameof(value));
            }
            if (_libraryDateFilter == value) return;
            _libraryDateFilter = value;
            RaiseLibraryProjectionChanged();
        }
    }
    public string? SelectedLibraryFolder
    {
        get => _selectedLibraryFolder;
        set
        {
            string? normalized = string.IsNullOrWhiteSpace(value)
                ? null
                : Path.GetFullPath(value);
            if (normalized is not null &&
                !LibraryFolderOptions.Any(item =>
                    string.Equals(
                        item.FullPath,
                        normalized,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "The Publish Library folder filter is not available.",
                    nameof(value));
            }
            if (string.Equals(
                    _selectedLibraryFolder,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _selectedLibraryFolder = normalized;
            RaiseLibraryProjectionChanged();
        }
    }
    public bool HasActiveLibraryFilters =>
        !string.IsNullOrWhiteSpace(LibrarySearchQuery) ||
        !LibraryDateFilter.Equals(
            LibraryDateFilters[0],
            StringComparison.Ordinal) ||
        SelectedLibraryFolder is not null;
    public bool HasVisibleLibraryItems => LibraryItems.Count > 0;
    public string LibraryResultSummary =>
        LibraryItems.Count == 1
            ? "1 finished video"
            : $"{LibraryItems.Count} finished videos";
    public string LibraryEmptyTitle => HasActiveLibraryFilters
        ? "No finished videos match"
        : "No finished videos yet";
    public string LibraryEmptyDescription => HasActiveLibraryFilters
        ? "Clear a filter or search term to see every Library video."
        : "Render a clip in Studio and it will be ready to schedule here.";
    public bool IsEmpty => SurfaceState == WorkspaceSurfaceState.Empty;
    public bool IsContentReady =>
        SurfaceState == WorkspaceSurfaceState.ContentReady;
    public bool IsLoading => SurfaceState == WorkspaceSurfaceState.Loading;
    public bool IsError => SurfaceState == WorkspaceSurfaceState.Error;
    public bool IsUnavailable =>
        SurfaceState == WorkspaceSurfaceState.Unavailable;
    public bool ShouldShowPlaceholder => IsUnavailable || IsError;
    public bool HasAsset => SelectedAsset is not null;
    public bool IsAssetMissing => !HasAsset;
    public bool IsBusy =>
        IsInitializing || IsPublishing || Editorial.IsGenerating;
    public bool IsYouTubeConfigured =>
        _youtubeOperations?.IsConfigured == true;
    public bool IsOnlineConnectionEnabled =>
        _connectionPermission?.IsEnabled ?? true;
    public bool IsConnected => Connection is not null;
    public bool HasThumbnail => ThumbnailFullPath is not null;
    public bool IsScheduled => Timing == YouTubePublishTiming.Schedule;
    public bool HasPreferredSlots => PreferredSlots.Count > 0;
    public bool HasQueueItems => QueueItems.Count > 0;
    public bool HasHistoryItems => HistoryItems.Count > 0;
    public bool HasDrafts => _drafts.Current.Count > 0;
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public bool HasTechnicalDetails =>
        !string.IsNullOrWhiteSpace(TechnicalDetails);
    public bool HasLastResult => LastResult is not null;

    public LibraryMediaAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (ReferenceEquals(_selectedAsset, value))
            {
                return;
            }
            if (value is not null &&
                !AvailableAssets.Any(asset => ReferenceEquals(asset, value)))
            {
                throw new ArgumentException(
                    "The selected Publish asset must belong to the finalized Studio project.",
                    nameof(value));
            }
            _selectedAsset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAsset));
            OnPropertyChanged(nameof(IsAssetMissing));
            OnPropertyChanged(nameof(AssetTitle));
            OnPropertyChanged(nameof(AssetDetail));
            OnPropertyChanged(nameof(AssetResolution));
            LoadAssetMetadata();
            YouTubePublishDraft? retainedDraft = FindDraft(value);
            Editorial.BindAsset(
                value,
                retainedDraft?.LastCompletedEditorialRerollAttempt,
                retainedDraft?.PriorAcceptedTitles);
            RaiseCommandStates();
        }
    }

    public YouTubeAccountConnection? Connection
    {
        get => _connection;
        private set
        {
            if (ReferenceEquals(_connection, value)) return;
            _connection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatus));
            OnPropertyChanged(nameof(ConnectionTitle));
            OnPropertyChanged(nameof(ConnectionDetail));
            OnPropertyChanged(nameof(Destinations));
            OnPropertyChanged(nameof(Checklist));
            RaiseCommandStates();
        }
    }

    public IReadOnlyList<YouTubePlaylist> Playlists
    {
        get => _playlists;
        private set
        {
            _playlists = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaylistOptions));
        }
    }

    public IReadOnlyList<YouTubeVideoCategory> Categories
    {
        get => _categories;
        private set
        {
            _categories = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCategories));
        }
    }

    public bool HasCategories => Categories.Count > 0;

    public IReadOnlyList<PublishPlaylistItem> PlaylistOptions =>
        [
            new PublishPlaylistItem(null, "No playlist", false),
            .. Playlists.Select(static playlist =>
                new PublishPlaylistItem(
                    playlist.Id,
                    playlist.DisplayLabel,
                    playlist.IsPrivate)),
        ];

    public string? SelectedPlaylistId
    {
        get => _selectedPlaylistId;
        set
        {
            if (_selectedPlaylistId == value) return;
            if (value is not null &&
                !Playlists.Any(playlist =>
                    playlist.Id.Equals(value, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "The selected playlist was not returned by the connected channel.",
                    nameof(value));
            }
            _selectedPlaylistId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Checklist));
        }
    }

    public string? SelectedCategoryId
    {
        get => _selectedCategoryId;
        set
        {
            if (_selectedCategoryId == value) return;
            if (value is not null &&
                !Categories.Any(category =>
                    category.Id.Equals(value, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "The selected category was not returned by YouTube.",
                    nameof(value));
            }
            _selectedCategoryId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Checklist));
            RaiseCommandStates();
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            value ??= string.Empty;
            if (_title == value) return;
            _title = value;
            RaiseMetadataChanged();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            value ??= string.Empty;
            if (_description == value) return;
            _description = value;
            RaiseMetadataChanged();
        }
    }

    public string Tags
    {
        get => _tags;
        set
        {
            value ??= string.Empty;
            if (_tags == value) return;
            _tags = value;
            RaiseMetadataChanged();
        }
    }


    public YouTubeVideoVisibility Visibility
    {
        get => _visibility;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_visibility == value) return;
            _visibility = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Checklist));
            RaiseCommandStates();
        }
    }

    public YouTubePublishTiming Timing
    {
        get => _timing;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_timing == value) return;
            _timing = value;
            if (value == YouTubePublishTiming.Schedule)
            {
                _visibility = YouTubeVideoVisibility.Public;
                OnPropertyChanged(nameof(Visibility));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsScheduled));
            OnPropertyChanged(nameof(ScheduleSummary));
            OnPropertyChanged(nameof(PublishCommandText));
            OnPropertyChanged(nameof(Checklist));
            RaiseCommandStates();
        }
    }

    public YouTubeAudience Audience
    {
        get => _audience;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_audience == value) return;
            _audience = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Checklist));
            RaiseCommandStates();
        }
    }

    public bool ContainsSyntheticMedia
    {
        get => _containsSyntheticMedia;
        set
        {
            if (_containsSyntheticMedia == value) return;
            _containsSyntheticMedia = value;
            OnPropertyChanged();
        }
    }

    public bool NotifySubscribers
    {
        get => _notifySubscribers;
        set
        {
            if (_notifySubscribers == value) return;
            _notifySubscribers = value;
            OnPropertyChanged();
        }
    }

    public DateTime? ScheduledDate
    {
        get => _scheduledDate;
        set
        {
            if (_scheduledDate == value) return;
            _scheduledDate = value?.Date;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScheduleSummary));
            OnPropertyChanged(nameof(Checklist));
            RaiseCommandStates();
        }
    }

    public string ScheduledTimeText
    {
        get => _scheduledTimeText;
        set
        {
            value ??= string.Empty;
            if (_scheduledTimeText == value) return;
            _scheduledTimeText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScheduleSummary));
            OnPropertyChanged(nameof(Checklist));
            RaiseCommandStates();
        }
    }

    public DayOfWeek PreferredDay
    {
        get => _preferredDay;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_preferredDay == value) return;
            _preferredDay = value;
            OnPropertyChanged();
        }
    }

    public string PreferredTimeText
    {
        get => _preferredTimeText;
        set
        {
            value ??= string.Empty;
            if (_preferredTimeText == value) return;
            _preferredTimeText = value;
            OnPropertyChanged();
            _addPreferredSlotCommand.RaiseCanExecuteChanged();
        }
    }

    public string? ThumbnailFullPath
    {
        get => _thumbnailFullPath;
        set
        {
            string? normalized = string.IsNullOrWhiteSpace(value)
                ? null
                : Path.GetFullPath(value);
            if (_thumbnailFullPath == normalized) return;
            _thumbnailFullPath = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
            OnPropertyChanged(nameof(ThumbnailName));
            OnPropertyChanged(nameof(ThumbnailValidationMessage));
            OnPropertyChanged(nameof(Checklist));
            _clearThumbnailCommand.RaiseCanExecuteChanged();
            RaiseCommandStates();
        }
    }

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (_isInitializing == value) return;
            _isInitializing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            RaiseCommandStates();
        }
    }

    public bool IsPublishing
    {
        get => _isPublishing;
        private set
        {
            if (_isPublishing == value) return;
            _isPublishing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(QueueItems));
            OnPropertyChanged(nameof(HasQueueItems));
            RaiseCommandStates();
        }
    }

    public string OperationTitle
    {
        get => _operationTitle;
        private set { _operationTitle = value; OnPropertyChanged(); }
    }

    public string OperationDetail
    {
        get => _operationDetail;
        private set { _operationDetail = value; OnPropertyChanged(); }
    }

    public double? OperationPercentage
    {
        get => _operationPercentage;
        private set
        {
            _operationPercentage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOperationIndeterminate));
        }
    }

    public bool IsOperationIndeterminate => OperationPercentage is null;

    public string Notice
    {
        get => _notice;
        private set
        {
            _notice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNotice));
        }
    }

    public string TechnicalDetails
    {
        get => _technicalDetails;
        private set
        {
            _technicalDetails = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTechnicalDetails));
        }
    }

    public YouTubePublishResult? LastResult
    {
        get => _lastResult;
        private set
        {
            _lastResult = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLastResult));
            OnPropertyChanged(nameof(LastResultUrl));
        }
    }

    public PublishCalendarMode SelectedCalendarMode
    {
        get => _selectedCalendarMode;
        set
        {
            if (_selectedCalendarMode == value) return;
            DateTime focus = SelectedCalendarDay?.Date ?? LocalToday;
            _selectedCalendarMode = value;
            _calendarAnchor = value == PublishCalendarMode.Month
                ? new DateTime(focus.Year, focus.Month, 1)
                : focus;
            OnPropertyChanged();
            RebuildCalendar(focus);
            OnPropertyChanged(nameof(CalendarRangeTitle));
            OnPropertyChanged(nameof(CalendarCellMinimumHeight));
        }
    }

    public PublishCalendarPlatform SelectedCalendarPlatform
    {
        get => _selectedCalendarPlatform;
        set
        {
            if (_selectedCalendarPlatform == value) return;
            _selectedCalendarPlatform = value;
            OnPropertyChanged();
            RebuildCalendar();
        }
    }

    public PublishCalendarDay? SelectedCalendarDay
    {
        get => _selectedCalendarDay;
        set
        {
            if (ReferenceEquals(_selectedCalendarDay, value)) return;
            _selectedCalendarDay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCalendarDayTitle));
            OnPropertyChanged(nameof(SelectedCalendarDaySlots));
            OnPropertyChanged(nameof(HasSelectedCalendarDaySlots));
            OnPropertyChanged(nameof(SelectedCalendarDaySummary));
        }
    }

    public IReadOnlyList<PublishCalendarDay> CalendarDays =>
        _calendarDays;
    public IReadOnlyList<YouTubePreferredScheduleSlot> PreferredSlots =>
        _preferences.PreferredSlots;
    public IReadOnlyList<PublishPlanningItem> PlanningBacklog =>
        AvailableAssets.Select(asset => new PublishPlanningItem(
            asset.Title,
            $"{PublishPresentationRules.FormatDuration(asset.Duration)} · finalized in Studio",
            _drafts.Current.Any(draft => draft.AssetId.Equals(asset.Id, StringComparison.Ordinal))
                ? "DRAFT SAVED"
                : GetAssetPublishState(asset).ToUpperInvariant(),
            "Icon.Media",
            asset)).ToArray();
    public IReadOnlyList<PublishDestinationItem> Destinations =>
    [
        new PublishDestinationItem(
            PublishDestination.YouTube,
            "YouTube",
            ConnectionStatus,
            !IsOnlineConnectionEnabled
                ? "Enable optional YouTube connections in Settings before connecting or publishing."
                : IsYouTubeConfigured
                ? "Connect in your browser, then upload or schedule without leaving Replay Foundry."
                : "Replay Foundry's Google Desktop OAuth credentials must be included in the build.",
            "Icon.Play",
            IsConnected),
    ];
    public PublishDestination SelectedDestination =>
        PublishDestination.YouTube;

    public IReadOnlyList<PublishChecklistItem> Checklist =>
        BuildChecklist();
    public IReadOnlyList<PublishJobItem> QueueItems =>
        IsPublishing
            ? [new PublishJobItem(
                Title.Length == 0 ? "YouTube upload" : Title,
                OperationTitle,
                OperationDetail)]
            : [];
    public IReadOnlyList<PublishJobItem> HistoryItems =>
        (_youtubeOperations?.History ?? [])
            .Select(static entry => new PublishJobItem(
                entry.Title,
                PublishPresentationRules.FormatOutcome(entry.Outcome),
                PublishPresentationRules.BuildHistoryDetail(entry),
                entry.VideoUrl))
            .ToArray();

    public string ConnectionStatus => !IsOnlineConnectionEnabled
        ? "Off in Settings"
        : !IsYouTubeConfigured
            ? "Setup required"
            : IsConnected ? "Connected" : "Not connected";
    public string ConnectionTitle => Connection?.ChannelTitle ??
        (!IsOnlineConnectionEnabled
            ? "YouTube connections are disabled"
            : IsYouTubeConfigured
            ? "Connect your YouTube channel"
            : "YouTube app configuration required");
    public string ConnectionDetail => Connection is not null
        ? $"Channel ID {Connection.ChannelId} · token protected by Windows Credential Manager"
        : !IsOnlineConnectionEnabled
            ? "Enable YouTube connections under Settings → Privacy & connections. Enabling permission alone does not contact Google."
        : IsYouTubeConfigured
            ? "A browser window will ask you to choose a Google account and approve YouTube access."
            : "Set the Replay Foundry Google Desktop OAuth client ID and its paired desktop client secret for development, or include both in the release build.";
    public string SelectedDestinationLabel => "YouTube";
    public string SelectedDestinationStatus => ConnectionStatus;
    public string SelectedDestinationDescription =>
        Destinations[0].Description;
    public bool IsSelectedDestinationConnected => IsConnected;
    public string AssetTitle => SelectedAsset is null
        ? "No finalized video selected"
        : Path.GetFileNameWithoutExtension(SelectedAsset.OutputFullPath!);
    public string AssetDetail => SelectedAsset is null
        ? "Complete a Studio render before publishing."
        : $"{PublishPresentationRules.FormatDuration(SelectedAsset.Duration)} · " +
          GetAssetPublishState(SelectedAsset);
    public string AssetResolution => SelectedAsset is null
        ? "Awaiting Studio"
        : GetAssetPublishState(SelectedAsset);
    public string ThumbnailName => ThumbnailFullPath is null
        ? "Use YouTube's generated thumbnail"
        : Path.GetFileName(ThumbnailFullPath);
    public string ThumbnailValidationMessage =>
        PublishPresentationRules.ValidateThumbnail(ThumbnailFullPath);
    public string TitleCharacterCount => $"{Title.Length}/100";
    public string DescriptionCharacterCount =>
        $"{Description.Length}/5000";
    public string TagsCharacterCount => $"{Tags.Length}/500";
    public bool IsMetadataWithinLimits =>
        !string.IsNullOrWhiteSpace(Title) &&
        Title.Length <= 100 &&
        Description.Length <= 5_000 &&
        Tags.Length <= 500;
    public string PresentationValidationMessage =>
        string.IsNullOrWhiteSpace(Title)
            ? "Add a specific title before publishing."
            : IsMetadataWithinLimits
                ? "Metadata fits YouTube's current limits."
                : "Shorten the highlighted metadata before publishing.";
    public string ScheduleSummary
    {
        get
        {
            if (!IsScheduled)
            {
                return Visibility switch
                {
                    YouTubeVideoVisibility.Public =>
                        "The video becomes public after YouTube accepts and processes it.",
                    YouTubeVideoVisibility.Unlisted =>
                        "The video is available to anyone with the link.",
                    _ => "The video remains private in YouTube Studio.",
                };
            }
            return TryGetScheduledUtc(out DateTimeOffset scheduled, out string error)
                ? $"YouTube will publish at {TimeZoneInfo.ConvertTime(scheduled, _timeZone):f} ({_timeZone.DisplayName})."
                : error;
        }
    }
    public string TimeZoneLabel => _timeZone.DisplayName;
    public string PublishCommandText => IsScheduled
        ? "Upload and schedule"
        : Visibility == YouTubeVideoVisibility.Public
            ? "Upload and publish"
            : "Upload to YouTube";
    public string ReadinessSummary => CanPublish
        ? "Everything required for this YouTube handoff is ready."
        : "Finish each required item before starting the upload.";
    public string CalendarRangeTitle =>
        SelectedCalendarMode == PublishCalendarMode.Month
            ? _calendarAnchor.ToString(
                "MMMM yyyy",
                CultureInfo.CurrentCulture)
            : PublishCalendarProjector.FormatWeekRange(
                PublishCalendarProjector.GetWeekStart(_calendarAnchor));
    public string CalendarTimeZoneLabel =>
        $"{_timeZone.StandardName} · UTC{PublishCalendarProjector.FormatUtcOffset(_timeZone.GetUtcOffset(_utcNow()))}";
    public string CalendarIntegrityText =>
        "USER-CHOSEN PREFERRED TIMES + YOUTUBE-ACCEPTED RELEASES · NO AUTOMATIC PEAK-TIME CLAIM";
    public double CalendarCellMinimumHeight =>
        SelectedCalendarMode == PublishCalendarMode.Month ? 108d : 210d;
    public string SelectedCalendarDayTitle =>
        SelectedCalendarDay?.Date.ToString(
            "dddd, MMMM d",
            CultureInfo.CurrentCulture) ?? "Choose a day";
    public IReadOnlyList<PublishCalendarSlot> SelectedCalendarDaySlots =>
        SelectedCalendarDay?.Slots ?? [];
    public bool HasSelectedCalendarDaySlots =>
        SelectedCalendarDaySlots.Count > 0;
    public string SelectedCalendarDaySummary =>
        HasSelectedCalendarDaySlots
            ? $"{SelectedCalendarDaySlots.Count} release plan{(SelectedCalendarDaySlots.Count == 1 ? string.Empty : "s")}"
            : "No preferred time or scheduled upload is recorded for this day.";
    public string PlanningHorizonSummary =>
        $"{PublishPresentationRules.FormatCount(PreferredSlots.Count, "preferred weekly time")} · " +
        PublishPresentationRules.FormatCount(HistoryItems.Count, "history item");
    public string StatusText => IsPublishing
        ? OperationTitle
        : IsConnected
            ? $"Connected to {Connection!.ChannelTitle}"
            : ConnectionStatus;
    public string WorkspaceEyebrow => "PUBLISH / YOUTUBE";
    public string WorkspaceTitle => "Release with intention";
    public string WorkspaceDescription =>
        "Choose a Library video, review its YouTube details, then upload now or let YouTube publish it later.";
    public string ErrorSummary =>
        "Publish could not load the selected finalized video.";
    public string QueueEmptyTitle => "No active YouTube upload";
    public string QueueEmptyDescription =>
        "Uploads appear here while Replay Foundry transfers a finalized Studio video.";
    public string HistoryEmptyTitle => "No YouTube publish history yet";
    public string HistoryEmptyDescription =>
        "Completed, scheduled, failed, and cancelled uploads are recorded locally here.";
    public string LastResultUrl => LastResult?.VideoUrl ?? string.Empty;
    public bool CanPublish =>
        _youtubeOperations is not null &&
        IsOnlineConnectionEnabled &&
        IsConnected &&
        HasAsset &&
        SelectedAsset!.OutputFullPath is not null &&
        File.Exists(SelectedAsset.OutputFullPath) &&
        IsMetadataWithinLimits &&
        SelectedCategoryId is not null &&
        (PublishPresentationRules.ValidateThumbnail(ThumbnailFullPath) is "Thumbnail ready." or
            "YouTube will choose a thumbnail.") &&
        (!IsScheduled || TryGetScheduledUtc(out _, out _)) &&
        !IsBusy;
    public bool CanSaveDraft =>
        HasAsset &&
        IsMetadataWithinLimits &&
        (!IsScheduled || TryGetScheduledUtc(out _, out _)) &&
        !IsBusy;
    public bool CanPublishAllNow =>
        _youtubeOperations is not null &&
        IsOnlineConnectionEnabled &&
        IsConnected &&
        _drafts.Current.Count > 0 &&
        !IsBusy;
    public string DraftSummary => _drafts.Current.Count == 1
        ? "1 saved upload draft"
        : $"{_drafts.Current.Count} saved upload drafts";

    public ICommand PreviousCalendarCommand { get; }
    public ICommand NextCalendarCommand { get; }
    public ICommand TodayCalendarCommand { get; }
    public ICommand CreatePlanCommand => _createPlanCommand;
    public ICommand ConnectCommand => _connectCommand;
    public ICommand DisconnectCommand => _disconnectCommand;
    public ICommand RefreshYouTubeCommand => _refreshYouTubeCommand;
    public ICommand ReconcileYouTubeHistoryCommand =>
        _reconcileYouTubeHistoryCommand;
    public ICommand PublishCommand => _publishCommand;
    public ICommand CancelPublishCommand => _cancelPublishCommand;
    public ICommand PickThumbnailCommand => _pickThumbnailCommand;
    public ICommand ClearThumbnailCommand => _clearThumbnailCommand;
    public ICommand AddPreferredSlotCommand => _addPreferredSlotCommand;
    public ICommand RemovePreferredSlotCommand =>
        _removePreferredSlotCommand;
    public ICommand UseNextPreferredSlotCommand =>
        _useNextPreferredSlotCommand;
    public ICommand ClearHistoryCommand => _clearHistoryCommand;
    public ICommand SaveDraftCommand => _saveDraftCommand;
    public ICommand PrepareAssetCommand => _prepareAssetCommand;
    public ICommand PublishAllNowCommand => _publishAllNowCommand;
    public ICommand ClearLibraryFiltersCommand =>
        _clearLibraryFiltersCommand;
    public PublishEditorialMetadataViewModel Editorial { get; }
    public async Task InitializeAsync()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }
        _isInitialized = true;
        if (_youtubeOperations is null)
        {
            Notice =
                "YouTube publishing is ready in code, but this build has no complete Google Desktop OAuth configuration.";
            return;
        }
        if (!IsOnlineConnectionEnabled)
        {
            Notice =
                "YouTube connections are off. Enable them under Settings → Privacy & connections when you want to connect or publish.";
            RefreshHistoryAndCalendar();
            return;
        }

        IsInitializing = true;
        try
        {
            YouTubeAccountConnection? connection =
                await _youtubeOperations.RunAsync(
                    static (youtube, cancellationToken) =>
                        youtube.GetConnectionAsync(cancellationToken));
            Connection = IsOnlineConnectionEnabled
                ? connection
                : null;
            if (Connection is not null && IsOnlineConnectionEnabled)
            {
                await LoadYouTubeChoicesAsync();
            }
        }
        catch (YouTubePublishingException exception)
        {
            Connection = null;
            Notice = exception.Message;
            TechnicalDetails =
                exception.DiagnosticCode + Environment.NewLine +
                exception.TechnicalDetails;
        }
        catch (OperationCanceledException)
        {
            Connection = null;
            if (!_isDisposed)
            {
                Notice = "YouTube connection was cancelled.";
            }
        }
        finally
        {
            IsInitializing = false;
            RefreshHistoryAndCalendar();
        }
    }

    public void PrepareAssetForDate(
        LibraryMediaAsset asset,
        DateTime date)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!AvailableAssets.Any(value => ReferenceEquals(value, asset)))
        {
            throw new ArgumentException(
                "The publishing asset must come from the current Library.",
                nameof(asset));
        }
        if (date.Date < LocalToday)
        {
            Notice = "Choose today or a future date.";
            return;
        }
        ScheduledDate = date.Date;
        PrepareAsset(asset);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Editorial.PropertyChanged -= Editorial_PropertyChanged;
        Editorial.Dispose();
        _youtubeOperations?.Dispose();
        _libraryCatalog.Changed -= LibraryCatalog_Changed;
        if (_connectionPermission is not null)
        {
            _connectionPermission.Changed -=
                YouTubeConnectionPermission_Changed;
        }
    }

    private async Task ConnectAsync()
    {
        if (_youtubeOperations is null || !IsOnlineConnectionEnabled) return;
        IsInitializing = true;
        Notice = "Opening your browser to connect YouTube…";
        TechnicalDetails = string.Empty;
        try
        {
            YouTubeAccountConnection connection =
                await _youtubeOperations.RunAsync(
                    static (youtube, cancellationToken) =>
                        youtube.ConnectAsync(cancellationToken));
            if (!IsOnlineConnectionEnabled)
            {
                await _youtubeOperations.RunAsync(
                    static (youtube, cancellationToken) =>
                        youtube.DisconnectAsync(cancellationToken));
                return;
            }
            Connection = connection;
            await LoadYouTubeChoicesAsync();
            Notice = $"Connected to {Connection.ChannelTitle}.";
        }
        catch (YouTubePublishingException exception)
        {
            Connection = null;
            Notice = exception.Message;
            TechnicalDetails =
                exception.DiagnosticCode + Environment.NewLine +
                exception.TechnicalDetails;
        }
        catch (OperationCanceledException)
        {
            Connection = null;
            if (!_isDisposed)
            {
                Notice = "YouTube connection was cancelled.";
            }
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task DisconnectAsync()
    {
        if (_youtubeOperations is null) return;
        IsInitializing = true;
        try
        {
            await _youtubeOperations.RunAsync(
                static (youtube, cancellationToken) =>
                    youtube.DisconnectAsync(cancellationToken));
            Connection = null;
            Playlists = [];
            Categories = [];
            SelectedPlaylistId = null;
            SelectedCategoryId = null;
            Notice =
                "YouTube was disconnected and the local Windows credential was removed.";
            TechnicalDetails = string.Empty;
        }
        catch (YouTubePublishingException exception)
        {
            if (exception.DiagnosticCode.Equals(
                    "youtube.oauth.revoke-failed",
                    StringComparison.Ordinal))
            {
                Connection = null;
                Playlists = [];
                Categories = [];
                SelectedPlaylistId = null;
                SelectedCategoryId = null;
            }
            Notice = exception.Message;
            TechnicalDetails =
                exception.DiagnosticCode + Environment.NewLine +
                exception.TechnicalDetails;
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                Notice = "YouTube disconnection was cancelled.";
            }
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task RefreshYouTubeAsync()
    {
        if (_youtubeOperations is null || !IsOnlineConnectionEnabled) return;
        IsInitializing = true;
        try
        {
            YouTubeAccountConnection? connection =
                await _youtubeOperations.RunAsync(
                    static (youtube, cancellationToken) =>
                        youtube.GetConnectionAsync(cancellationToken));
            Connection = IsOnlineConnectionEnabled
                ? connection
                : null;
            if (Connection is null)
            {
                Notice = "Connect YouTube again to refresh channel details.";
                return;
            }
            await LoadYouTubeChoicesAsync();
            Notice = "Channel, playlist, and category details are current.";
            TechnicalDetails = string.Empty;
        }
        catch (YouTubePublishingException exception)
        {
            Notice = exception.Message;
            TechnicalDetails =
                exception.DiagnosticCode + Environment.NewLine +
                exception.TechnicalDetails;
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                Notice = "The YouTube refresh was cancelled.";
            }
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task ReconcileYouTubeHistoryAsync()
    {
        if (_youtubeOperations is null || !IsOnlineConnectionEnabled) return;
        IsInitializing = true;
        Notice = "Checking only the YouTube video IDs previously recorded by Replay Foundry…";
        TechnicalDetails = string.Empty;
        try
        {
            int flagged = await _youtubeOperations.RunAsync(
                static (youtube, cancellationToken) =>
                    youtube.ReconcileHistoryAsync(cancellationToken));
            Notice = flagged == 0
                ? "Every recorded YouTube video is still accessible to the connected channel."
                : $"{flagged} recorded YouTube video{(flagged == 1 ? " is" : "s are")} no longer returned to this channel and may have been removed or become inaccessible.";
        }
        catch (YouTubePublishingException exception)
        {
            Notice = exception.Message;
            TechnicalDetails =
                exception.DiagnosticCode + Environment.NewLine +
                exception.TechnicalDetails;
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                Notice = "The YouTube history check was cancelled.";
            }
        }
        finally
        {
            IsInitializing = false;
            RefreshHistoryAndCalendar();
        }
    }

    private async Task LoadYouTubeChoicesAsync()
    {
        if (_youtubeOperations is null)
        {
            return;
        }

        (IReadOnlyList<YouTubePlaylist> playlists,
            IReadOnlyList<YouTubeVideoCategory> categories) =
            await _youtubeOperations.RunAsync(
                async (youtube, cancellationToken) =>
                {
                    IReadOnlyList<YouTubePlaylist> loadedPlaylists =
                        await youtube.GetPlaylistsAsync(cancellationToken);
                    IReadOnlyList<YouTubeVideoCategory> loadedCategories =
                        await youtube.GetCategoriesAsync(cancellationToken);
                    return (loadedPlaylists, loadedCategories);
                });
        Playlists = playlists;
        Categories = categories;
        SelectedCategoryId = Categories.FirstOrDefault(category =>
                category.Title.Equals(
                    "Gaming",
                    StringComparison.OrdinalIgnoreCase))?.Id ??
            Categories.FirstOrDefault()?.Id;
    }

    private async Task PublishAsync()
    {
        if (_youtubeOperations is null || SelectedAsset is null ||
            !IsOnlineConnectionEnabled) return;
        DateTimeOffset? scheduled = null;
        if (IsScheduled)
        {
            if (!TryGetScheduledUtc(out DateTimeOffset value, out string error))
            {
                Notice = error;
                return;
            }
            scheduled = value;
        }
        var request = new YouTubePublishRequest(
            SelectedAsset,
            Title,
            Description,
            PublishPresentationRules.ParseTags(Tags),
            SelectedCategoryId!,
            Visibility,
            Timing,
            Audience,
            ContainsSyntheticMedia,
            NotifySubscribers,
            scheduled,
            SelectedPlaylistId,
            ThumbnailFullPath,
            _utcNow());

        IsPublishing = true;
        LastResult = null;
        Notice = string.Empty;
        TechnicalDetails = string.Empty;
        OperationTitle = "Preparing YouTube upload";
        OperationDetail =
            "Replay Foundry is validating the finalized Studio video.";
        OperationPercentage = null;
        var progress = new Progress<YouTubePublishProgress>(update =>
        {
            OperationTitle = update.Title;
            OperationDetail = update.Detail;
            OperationPercentage = update.Percentage;
        });
        try
        {
            LastResult = await _youtubeOperations.RunAsync(
                (youtube, cancellationToken) => youtube.PublishAsync(
                    request,
                    progress,
                    cancellationToken));
            _drafts.Remove(SelectedAsset.Id);
            NotifyDraftsChanged();
            Notice = LastResult.Outcome == YouTubePublishOutcome.Scheduled
                ? "YouTube accepted the upload and its public release time. Replay Foundry does not need to remain open."
                : "YouTube accepted the upload.";
            if (LastResult.Warnings.Count > 0)
            {
                TechnicalDetails = string.Join(
                    Environment.NewLine,
                    LastResult.Warnings);
            }
        }
        catch (OperationCanceledException)
        {
            Notice =
                "The YouTube upload was cancelled. A resumable session may remain temporarily on Google's servers, but no completed video was recorded by Replay Foundry.";
        }
        catch (YouTubePublishingException exception)
        {
            Notice = exception.Message;
            TechnicalDetails =
                exception.DiagnosticCode + Environment.NewLine +
                exception.TechnicalDetails;
        }
        finally
        {
            IsPublishing = false;
            RefreshHistoryAndCalendar();
        }
    }

    private async Task PublishAllNowAsync()
    {
        YouTubePublishDraft[] drafts = _drafts.Current.ToArray();
        if (drafts.Length == 0 ||
            _bulkConfirmation is null ||
            !_bulkConfirmation.ConfirmPublishAllNow(drafts.Length))
        {
            return;
        }

        foreach (YouTubePublishDraft draft in drafts)
        {
            LibraryMediaAsset? asset = AvailableAssets.FirstOrDefault(value =>
                value.Id.Equals(draft.AssetId, StringComparison.Ordinal));
            if (asset is null || !asset.IsAvailable)
            {
                Notice = $"Stopped before ‘{draft.Title}’ because its Library video is unavailable.";
                break;
            }

            SelectedAsset = asset;
            ApplyDraft(draft);
            Timing = YouTubePublishTiming.PublishNow;
            Visibility = YouTubeVideoVisibility.Public;
            await PublishAsync();
            if (LastResult is null)
            {
                break;
            }
        }
        RefreshHistoryAndCalendar();
    }

    private void CancelPublish() => _youtubeOperations?.CancelActive();

    private void PickThumbnail()
    {
        string? selected = _thumbnailPicker?.PickThumbnail();
        if (selected is not null)
        {
            ThumbnailFullPath = selected;
        }
    }

    private void AddPreferredSlot()
    {
        if (!PublishPresentationRules.TryParseTime(PreferredTimeText, out TimeOnly time))
        {
            Notice = "Enter a preferred time such as 6:00 PM.";
            return;
        }
        _preferences.Replace(
            [
                .. PreferredSlots,
                new YouTubePreferredScheduleSlot(PreferredDay, time),
            ]);
        OnPreferredSlotsChanged();
        Notice =
            "Preferred times are a planning aid you control; Replay Foundry does not infer channel peak performance.";
    }

    private void RemovePreferredSlot(YouTubePreferredScheduleSlot slot)
    {
        if (slot is null) return;
        _preferences.Replace(PreferredSlots.Where(value => value != slot));
        OnPreferredSlotsChanged();
    }

    private void UseNextPreferredSlot()
    {
        DateTimeOffset? next = YouTubeSchedulePlanner.FindNext(
            PreferredSlots,
            _utcNow(),
            _timeZone,
            MinimumScheduleLeadTime);
        if (next is null)
        {
            Notice = "No valid preferred release time is available in the next two weeks.";
            return;
        }
        DateTimeOffset local = TimeZoneInfo.ConvertTime(next.Value, _timeZone);
        Timing = YouTubePublishTiming.Schedule;
        ScheduledDate = local.Date;
        ScheduledTimeText = local.ToString(
            "h:mm tt",
            CultureInfo.CurrentCulture);
    }

    private void OnPreferredSlotsChanged()
    {
        OnPropertyChanged(nameof(PreferredSlots));
        OnPropertyChanged(nameof(HasPreferredSlots));
        OnPropertyChanged(nameof(PlanningHorizonSummary));
        _useNextPreferredSlotCommand.RaiseCanExecuteChanged();
        RebuildCalendar();
    }

    private void ClearHistory()
    {
        _youtubeOperations?.ClearHistory();
        Notice = "Local YouTube publish history was cleared.";
        RefreshHistoryAndCalendar();
    }

    private void OpenSchedulePreparation()
    {
        if (SelectedAsset is null) return;
        PrepareAsset(SelectedAsset);
    }

    private void PrepareAsset(LibraryMediaAsset asset)
    {
        SelectedAsset = asset;
        Timing = YouTubePublishTiming.Schedule;
        if (SelectedCalendarDay is { } day && day.Date >= LocalToday)
        {
            ScheduledDate = day.Date;
        }
        else if (PreferredSlots.Count > 0)
        {
            UseNextPreferredSlot();
        }
        _preparationDialog?.Show(this);
    }

    private void SaveDraft() => SaveDraft(announce: true);

    private void SaveDraft(bool announce)
    {
        if (SelectedAsset is null || !CanSaveDraft) return;
        DateTimeOffset? scheduled = null;
        if (IsScheduled)
        {
            if (!TryGetScheduledUtc(out DateTimeOffset value, out string error))
            {
                Notice = error;
                return;
            }
            scheduled = value;
        }
        try
        {
            YouTubePublishDraft? replacedDraft = FindDraft(SelectedAsset);
            if (replacedDraft is not null &&
                !replacedDraft.Title.Equals(Title, StringComparison.Ordinal))
            {
                Editorial.RetainReplacedTitle(replacedDraft.Title);
            }
            _drafts.Upsert(new YouTubePublishDraft(
                SelectedAsset.Id,
                Title,
                Description,
                Tags,
                Visibility,
                Timing,
                Audience,
                ContainsSyntheticMedia,
                NotifySubscribers,
                scheduled,
                SelectedPlaylistId,
                SelectedCategoryId,
                ThumbnailFullPath,
                _utcNow(),
                Editorial.AudienceAddress,
                Editorial.NamingGuidance,
                Editorial.DescriptionSignature,
                Editorial.LastCompletedAttempt,
                Editorial.PriorAcceptedTitles));
            if (announce)
            {
                Notice = IsScheduled
                    ? "Draft saved on this PC. Choose Upload and schedule when you are ready for YouTube to receive it."
                    : "Draft saved on this PC. Nothing was uploaded.";
            }
            NotifyDraftsChanged();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            Notice = "The local upload draft could not be saved: " + exception.Message;
        }
    }

    private void LibraryCatalog_Changed(object? sender, EventArgs e)
    {
        _surfaceState = _libraryCatalog.Assets.Count > 0
            ? WorkspaceSurfaceState.ContentReady
            : WorkspaceSurfaceState.Empty;
        OnPropertyChanged(nameof(SurfaceState));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsContentReady));
        OnPropertyChanged(nameof(ShouldShowPlaceholder));
        BindCurrentProject();
    }

    private void BindCurrentProject()
    {
        string? selectedId = SelectedAsset?.Id;
        _availableAssets = _libraryCatalog.Assets
            .Where(static asset => asset.IsAvailable)
            .ToArray();
        OnPropertyChanged(nameof(AvailableAssets));
        if (_selectedLibraryFolder is not null &&
            !BuildLibraryFolderOptions().Any(item => string.Equals(
                item.FullPath,
                _selectedLibraryFolder,
                StringComparison.OrdinalIgnoreCase)))
        {
            _selectedLibraryFolder = null;
            OnPropertyChanged(nameof(SelectedLibraryFolder));
        }
        RaiseLibraryProjectionChanged();
        SelectedAsset = AvailableAssets.FirstOrDefault(asset =>
                asset.Id.Equals(selectedId, StringComparison.Ordinal)) ??
            AvailableAssets.FirstOrDefault();
        OnPropertyChanged(nameof(PlanningBacklog));
        RebuildCalendar();
    }

    private void ClearLibraryFilters()
    {
        _librarySearchQuery = string.Empty;
        _libraryDateFilter = LibraryDateFilters[0];
        _selectedLibraryFolder = null;
        RaiseLibraryProjectionChanged();
    }

    private IReadOnlyList<LibraryMediaAsset> FilterLibraryAssets()
    {
        IEnumerable<LibraryMediaAsset> query = AvailableAssets;
        if (!string.IsNullOrWhiteSpace(LibrarySearchQuery))
        {
            string search = LibrarySearchQuery.Trim();
            query = query.Where(asset =>
                asset.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(asset.OutputFullPath).Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ||
                GetLibraryFolderLabel(asset).Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedLibraryFolder is not null)
        {
            query = query.Where(asset => string.Equals(
                Path.GetDirectoryName(asset.OutputFullPath),
                SelectedLibraryFolder,
                StringComparison.OrdinalIgnoreCase));
        }

        DateTime today = TimeZoneInfo.ConvertTime(_utcNow(), _timeZone).Date;
        query = LibraryDateFilter switch
        {
            "Today" => query.Where(asset =>
                TimeZoneInfo.ConvertTime(asset.AddedAtUtc, _timeZone).Date == today),
            "This week" => query.Where(asset =>
                TimeZoneInfo.ConvertTime(asset.AddedAtUtc, _timeZone).Date >=
                today.AddDays(-6)),
            "This month" => query.Where(asset =>
                TimeZoneInfo.ConvertTime(asset.AddedAtUtc, _timeZone).Date >=
                today.AddMonths(-1)),
            _ => query,
        };
        return query
            .OrderByDescending(static asset => asset.AddedAtUtc)
            .ThenBy(static asset => asset.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<PublishLibraryFolderItem>
        BuildLibraryFolderOptions()
    {
        string[] folders = AvailableAssets
            .Select(static asset => Path.GetDirectoryName(asset.OutputFullPath))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var labels = folders
            .GroupBy(
                static path => GetFolderLeaf(path),
                StringComparer.OrdinalIgnoreCase)
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
                string label = labels[leaf] == 1
                    ? leaf
                    : $"{leaf} · {Path.GetDirectoryName(path)}";
                return new PublishLibraryFolderItem(path, label);
            }),
        ];
    }

    private string BuildLibraryCollectionDetail(LibraryMediaAsset asset)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(
            asset.AddedAtUtc,
            _timeZone);
        return $"{GetLibraryFolderLabel(asset)} · {local:MMM d, yyyy}";
    }

    private static string GetLibraryFolderLabel(LibraryMediaAsset asset) =>
        GetFolderLeaf(Path.GetDirectoryName(asset.OutputFullPath) ?? string.Empty);

    private static string GetFolderLeaf(string path)
    {
        string trimmed = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }

    private void RaiseLibraryProjectionChanged()
    {
        OnPropertyChanged(nameof(LibrarySearchQuery));
        OnPropertyChanged(nameof(LibraryDateFilter));
        OnPropertyChanged(nameof(SelectedLibraryFolder));
        OnPropertyChanged(nameof(LibraryFolderOptions));
        OnPropertyChanged(nameof(VisibleLibraryAssets));
        OnPropertyChanged(nameof(LibraryItems));
        OnPropertyChanged(nameof(HasActiveLibraryFilters));
        OnPropertyChanged(nameof(HasVisibleLibraryItems));
        OnPropertyChanged(nameof(LibraryResultSummary));
        OnPropertyChanged(nameof(LibraryEmptyTitle));
        OnPropertyChanged(nameof(LibraryEmptyDescription));
        _clearLibraryFiltersCommand.RaiseCanExecuteChanged();
    }

    private void LoadAssetMetadata()
    {
        if (SelectedAsset is { } asset)
        {
            YouTubePublishDraft? draft = FindDraft(asset);
            if (draft is not null)
            {
                ApplyDraft(draft);
                return;
            }
            Editorial.ResetContextToProfile();
            Title = asset.Title;
            Description = asset.Description;
            Tags = string.Join(", ", asset.Tags);
        }
        else
        {
            Editorial.ResetContextToProfile();
            Title = string.Empty;
            Description = string.Empty;
            Tags = string.Empty;
        }
        ThumbnailFullPath = SelectedAsset?.ThumbnailFullPath;
    }

    private YouTubePublishDraft? FindDraft(LibraryMediaAsset? asset) =>
        asset is null
            ? null
            : _drafts.Current.FirstOrDefault(value =>
                value.AssetId.Equals(asset.Id, StringComparison.Ordinal));

    private void ApplyDraft(YouTubePublishDraft draft)
    {
        Title = draft.Title;
        Description = draft.Description;
        Tags = draft.Tags;
        Editorial.LoadDraftContext(
            draft.AudienceAddress,
            draft.NamingGuidance,
            draft.DescriptionSignature);
        Visibility = draft.Visibility;
        Timing = draft.Timing;
        Audience = draft.Audience;
        ContainsSyntheticMedia = draft.ContainsSyntheticMedia;
        NotifySubscribers = draft.NotifySubscribers;
        ThumbnailFullPath = draft.ThumbnailFullPath;
        if (draft.ScheduledForUtc is { } scheduled)
        {
            DateTimeOffset local = TimeZoneInfo.ConvertTime(scheduled, _timeZone);
            ScheduledDate = local.Date;
            ScheduledTimeText = local.ToString("h:mm tt", CultureInfo.CurrentCulture);
        }
        SelectedPlaylistId = draft.PlaylistId is not null && Playlists.Any(value =>
            value.Id.Equals(draft.PlaylistId, StringComparison.Ordinal))
                ? draft.PlaylistId
                : null;
        SelectedCategoryId = draft.CategoryId is not null && Categories.Any(value =>
            value.Id.Equals(draft.CategoryId, StringComparison.Ordinal))
                ? draft.CategoryId
                : SelectedCategoryId;
    }

    private bool TryGetScheduledUtc(
        out DateTimeOffset scheduledUtc,
        out string error)
    {
        scheduledUtc = default;
        if (ScheduledDate is null)
        {
            error = "Choose a release date.";
            return false;
        }
        if (!PublishPresentationRules.TryParseTime(ScheduledTimeText, out TimeOnly time))
        {
            error = "Enter a release time such as 6:00 PM.";
            return false;
        }
        try
        {
            scheduledUtc = YouTubeSchedulePlanner.ToUtc(
                DateOnly.FromDateTime(ScheduledDate.Value),
                time,
                _timeZone);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
        if (scheduledUtc < _utcNow() + MinimumScheduleLeadTime)
        {
            error =
                "Choose a release at least 30 minutes from now so YouTube has time to receive and process the video.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void RefreshHistoryAndCalendar()
    {
        OnPropertyChanged(nameof(HistoryItems));
        OnPropertyChanged(nameof(HasHistoryItems));
        OnPropertyChanged(nameof(PlanningHorizonSummary));
        OnPropertyChanged(nameof(PlanningBacklog));
        OnPropertyChanged(nameof(LibraryItems));
        OnPropertyChanged(nameof(AssetDetail));
        OnPropertyChanged(nameof(AssetResolution));
        _clearHistoryCommand.RaiseCanExecuteChanged();
        RebuildCalendar();
    }

    private void RebuildCalendar(DateTime? preferredDate = null)
    {
        PublishCalendarProjection projection = PublishCalendarProjector.Build(
            SelectedCalendarMode,
            SelectedCalendarPlatform,
            _calendarAnchor,
            LocalToday,
            BuildCalendarSlots(),
            preferredDate ?? SelectedCalendarDay?.Date);
        _calendarDays = projection.Days;
        _selectedCalendarDay = projection.SelectedDay;
        OnPropertyChanged(nameof(CalendarDays));
        OnPropertyChanged(nameof(SelectedCalendarDay));
        OnPropertyChanged(nameof(SelectedCalendarDayTitle));
        OnPropertyChanged(nameof(SelectedCalendarDaySlots));
        OnPropertyChanged(nameof(HasSelectedCalendarDaySlots));
        OnPropertyChanged(nameof(SelectedCalendarDaySummary));
    }

    private IReadOnlyList<PublishCalendarSlot> BuildCalendarSlots()
    {
        var slots = new List<PublishCalendarSlot>();
        foreach (YouTubePublishHistoryEntry entry in
                 _youtubeOperations?.History ?? [])
        {
            DateTimeOffset? time = entry.ScheduledForUtc;
            if (time is null) continue;
            DateTime local = TimeZoneInfo.ConvertTime(
                time.Value,
                _timeZone).DateTime;
            slots.Add(PublishCalendarProjector.CreatePreviewSlot(
                local,
                PublishCalendarPlatform.YouTube,
                "YouTube",
                entry.Title,
                PublishPresentationRules.FormatOutcome(entry.Outcome),
                "Icon.Play"));
        }

        foreach (YouTubePublishDraft draft in _drafts.Current)
        {
            if (draft.ScheduledForUtc is not { } scheduledUtc) continue;
            DateTime local = TimeZoneInfo.ConvertTime(scheduledUtc, _timeZone).DateTime;
            slots.Add(PublishCalendarProjector.CreatePreviewSlot(
                local,
                PublishCalendarPlatform.YouTube,
                "Saved draft",
                draft.Title,
                "Not uploaded",
                "Icon.Edit"));
        }

        DateTime rangeStart = SelectedCalendarMode == PublishCalendarMode.Month
            ? new DateTime(_calendarAnchor.Year, _calendarAnchor.Month, 1)
                .AddDays(-7)
            : PublishCalendarProjector.GetWeekStart(_calendarAnchor);
        DateTime rangeEnd = rangeStart.AddDays(
            SelectedCalendarMode == PublishCalendarMode.Month ? 56 : 7);
        for (DateTime date = rangeStart.Date;
             date < rangeEnd.Date;
             date = date.AddDays(1))
        {
            foreach (YouTubePreferredScheduleSlot preferred in
                     PreferredSlots.Where(value => value.Day == date.DayOfWeek))
            {
                slots.Add(PublishCalendarProjector.CreatePreviewSlot(
                    date.Add(preferred.LocalTime.ToTimeSpan()),
                    PublishCalendarPlatform.All,
                    "Preferred time",
                    "User-chosen release window",
                    "Planning preference",
                    "Icon.Clock"));
            }
        }
        return slots;
    }

    private void MoveCalendar(int direction)
    {
        _calendarAnchor = SelectedCalendarMode == PublishCalendarMode.Month
            ? _calendarAnchor.AddMonths(direction)
            : _calendarAnchor.AddDays(direction * 7);
        RebuildCalendar();
        OnPropertyChanged(nameof(CalendarRangeTitle));
    }

    private void ReturnCalendarToToday()
    {
        _calendarAnchor = SelectedCalendarMode == PublishCalendarMode.Month
            ? new DateTime(LocalToday.Year, LocalToday.Month, 1)
            : LocalToday;
        RebuildCalendar(LocalToday);
        OnPropertyChanged(nameof(CalendarRangeTitle));
    }

    private DateTime LocalToday =>
        TimeZoneInfo.ConvertTime(_utcNow(), _timeZone).Date;

    private void RaiseMetadataChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TitleCharacterCount));
        OnPropertyChanged(nameof(DescriptionCharacterCount));
        OnPropertyChanged(nameof(TagsCharacterCount));
        OnPropertyChanged(nameof(IsMetadataWithinLimits));
        OnPropertyChanged(nameof(PresentationValidationMessage));
        OnPropertyChanged(nameof(Checklist));
        RaiseCommandStates();
    }

    private void Editorial_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Editorial.IsGenerating))
        {
            OnPropertyChanged(nameof(IsBusy));
            RaiseCommandStates();
        }
    }

    private IReadOnlyList<PublishChecklistItem> BuildChecklist() =>
    [
        new(
            "Final Studio video",
            HasAsset ? AssetTitle : "Complete Studio rendering",
            HasAsset ? "Ready" : "Waiting"),
        new(
            "YouTube channel",
            Connection?.ChannelTitle ?? ConnectionStatus,
            IsConnected ? "Ready" : "Waiting"),
        new(
            "Title and metadata",
            PresentationValidationMessage,
            IsMetadataWithinLimits ? "Ready" : "Waiting"),
        new(
            "Audience",
            Audience == YouTubeAudience.MadeForKids
                ? "Made for kids"
                : "Not made for kids",
            "Ready"),
        new(
            "Category",
            Categories.FirstOrDefault(category =>
                category.Id == SelectedCategoryId)?.Title ??
                "Choose a category",
            SelectedCategoryId is null ? "Waiting" : "Ready"),
        new(
            "Release",
            ScheduleSummary,
            !IsScheduled || TryGetScheduledUtc(out _, out _)
                ? "Ready"
                : "Waiting"),
        new(
            "Thumbnail",
            ThumbnailValidationMessage,
            ThumbnailValidationMessage is
                "Thumbnail ready." or
                "YouTube will choose a thumbnail."
                ? "Ready"
                : "Waiting"),
    ];

    private void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(ReadinessSummary));
        OnPropertyChanged(nameof(StatusText));
        Editorial.RefreshHostState();
        _connectCommand.RaiseCanExecuteChanged();
        _disconnectCommand.RaiseCanExecuteChanged();
        _refreshYouTubeCommand.RaiseCanExecuteChanged();
        _reconcileYouTubeHistoryCommand.RaiseCanExecuteChanged();
        _publishCommand.RaiseCanExecuteChanged();
        _cancelPublishCommand.RaiseCanExecuteChanged();
        _pickThumbnailCommand.RaiseCanExecuteChanged();
        _clearThumbnailCommand.RaiseCanExecuteChanged();
        _clearHistoryCommand.RaiseCanExecuteChanged();
        _saveDraftCommand.RaiseCanExecuteChanged();
        _createPlanCommand.RaiseCanExecuteChanged();
        _publishAllNowCommand.RaiseCanExecuteChanged();
        _prepareAssetCommand.RaiseCanExecuteChanged();
    }

    private void NotifyDraftsChanged()
    {
        OnPropertyChanged(nameof(HasDrafts));
        OnPropertyChanged(nameof(DraftSummary));
        OnPropertyChanged(nameof(PlanningBacklog));
        OnPropertyChanged(nameof(LibraryItems));
        RaiseCommandStates();
        RebuildCalendar();
    }

    private string GetAssetPublishState(LibraryMediaAsset asset)
    {
        YouTubePublishHistoryEntry? uploaded =
            (_youtubeOperations?.History ?? [])
            .FirstOrDefault(entry =>
                entry.AssetId.Equals(asset.Id, StringComparison.Ordinal) &&
                entry.VideoId is not null);
        if (uploaded is not null)
        {
            if (uploaded.RemoteStatus ==
                YouTubeRemoteVideoStatus.NotFoundOrInaccessible)
            {
                return "YouTube copy needs attention";
            }
            return uploaded.Outcome == YouTubePublishOutcome.Scheduled
                ? "Scheduled on YouTube"
                : "Uploaded to YouTube";
        }

        bool failed = (_youtubeOperations?.History ?? []).Any(entry =>
            entry.AssetId.Equals(asset.Id, StringComparison.Ordinal) &&
            entry.Outcome == YouTubePublishOutcome.Failed);
        return failed
            ? "Last upload failed"
            : "Not uploaded";
    }

    private void YouTubeConnectionPermission_Changed(
        object? sender,
        EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(IsOnlineConnectionEnabled));
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(ConnectionTitle));
        OnPropertyChanged(nameof(ConnectionDetail));
        OnPropertyChanged(nameof(Destinations));
        OnPropertyChanged(nameof(SelectedDestinationStatus));
        OnPropertyChanged(nameof(SelectedDestinationDescription));
        OnPropertyChanged(nameof(Checklist));
        OnPropertyChanged(nameof(StatusText));

        if (!IsOnlineConnectionEnabled)
        {
            _youtubeOperations?.CancelActive();
            Connection = null;
            Playlists = [];
            Categories = [];
            SelectedPlaylistId = null;
            SelectedCategoryId = null;
            Notice =
                "YouTube network access is off. Replay Foundry is removing the local YouTube connection.";
            _ = DisconnectAfterPermissionDisabledAsync();
        }
        else
        {
            Notice =
                "YouTube network access is enabled. Choose Connect when you want to authorize a channel.";
        }

        RaiseCommandStates();
    }

    private async Task DisconnectAfterPermissionDisabledAsync()
    {
        if (_youtubeOperations is null)
        {
            return;
        }

        try
        {
            await _youtubeOperations.RunAsync(
                static (youtube, cancellationToken) =>
                    youtube.DisconnectAsync(cancellationToken));
            Notice =
                "YouTube network access is off and the local Windows credential was removed.";
            TechnicalDetails = string.Empty;
        }
        catch (YouTubePublishingException exception)
        {
            Notice =
                "YouTube network access is off and the local credential " +
                "was removed. Google could not confirm remote revocation; " +
                "you can also remove Replay Foundry in your Google Account " +
                "connections.";
            TechnicalDetails =
                exception.DiagnosticCode + Environment.NewLine +
                exception.TechnicalDetails;
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                Notice =
                    "YouTube connection cleanup was cancelled before it completed.";
            }
        }
        catch (Exception exception)
        {
            Notice =
                "YouTube network access is off, but Replay Foundry could " +
                "not confirm local connection cleanup. Review the technical " +
                "details and remove Replay Foundry in your Google Account " +
                "connections.";
            TechnicalDetails = exception.ToString();
        }
    }

}
