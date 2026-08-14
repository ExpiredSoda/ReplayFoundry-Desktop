using System.Windows.Input;
using System.IO;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Settings;

public sealed class LocalDataSettingsViewModel : ObservableObject
{
    private readonly IReplayFoundryLocalDataMaintenance _maintenance;
    private readonly ILocalDataCleanupConfirmation? _confirmation;
    private readonly AsyncDelegateCommand _clearCacheCommand;
    private readonly DelegateCommand _scheduleResetCommand;
    private bool _includeDiagnostics;
    private bool _includeLibraryCatalog;
    private bool _includeStudioProjects;
    private string _notice = string.Empty;
    private string _cacheUsage = "Not measured";

    public LocalDataSettingsViewModel()
        : this(
            new UnavailableReplayFoundryLocalDataMaintenance(),
            confirmation: null)
    {
    }

    public LocalDataSettingsViewModel(
        IReplayFoundryLocalDataMaintenance maintenance,
        ILocalDataCleanupConfirmation? confirmation)
    {
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _confirmation = confirmation;
        _clearCacheCommand = new AsyncDelegateCommand(ClearCacheAsync);
        _scheduleResetCommand = new DelegateCommand(
            ScheduleReset,
            () => _confirmation is not null);
        RefreshUsage();
    }

    public bool IncludeDiagnostics
    {
        get => _includeDiagnostics;
        set => SetProperty(ref _includeDiagnostics, value);
    }

    public bool IncludeLibraryCatalog
    {
        get => _includeLibraryCatalog;
        set => SetProperty(ref _includeLibraryCatalog, value);
    }

    public bool IncludeStudioProjects
    {
        get => _includeStudioProjects;
        set => SetProperty(ref _includeStudioProjects, value);
    }

    public string CacheUsage
    {
        get => _cacheUsage;
        private set => SetProperty(ref _cacheUsage, value);
    }

    public string Notice
    {
        get => _notice;
        private set
        {
            if (!SetProperty(ref _notice, value)) return;
            OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public event EventHandler<ReplayFoundryLocalDataResetRequest>? ResetScheduled;
    public ICommand ClearCacheCommand => _clearCacheCommand;
    public ICommand ScheduleResetCommand => _scheduleResetCommand;

    private async Task ClearCacheAsync()
    {
        ReplayFoundryLocalDataCleanupResult result =
            await _maintenance.ClearDerivedCachesAsync();
        Notice = result.Succeeded
            ? $"Cleared {FormatBytes(result.DeletedBytes)} from derived " +
              "previews, game lookup cache, downloaded installer copies, " +
              "and abandoned temporary workspaces. Installed tools, " +
              "models, projects, Library records, and finished videos " +
              "were kept."
            : "Some cache files were in use and could not be removed. " +
              string.Join(" · ", result.Warnings);
        RefreshUsage();
    }

    private void ScheduleReset()
    {
        var kinds = new List<ReplayFoundryLocalDataKind>
        {
            ReplayFoundryLocalDataKind.PreferencesAndHistory,
        };
        if (IncludeDiagnostics)
            kinds.Add(ReplayFoundryLocalDataKind.DiagnosticsAndReports);
        if (IncludeLibraryCatalog)
            kinds.Add(ReplayFoundryLocalDataKind.LibraryCatalog);
        if (IncludeStudioProjects)
            kinds.Add(ReplayFoundryLocalDataKind.StudioProjects);
        var request = new ReplayFoundryLocalDataResetRequest(kinds);
        if (_confirmation?.Confirm(request) != true) return;
        try
        {
            _maintenance.ScheduleReset(request);
            ResetScheduled?.Invoke(this, request);
            Notice =
                "The selected local data will be removed before Replay " +
                "Foundry loads it on the next start. Finished video files " +
                "and installed tools/models are never part of this reset.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            Notice = "Replay Foundry could not schedule the reset: " + exception.Message;
        }
    }

    private void RefreshUsage()
    {
        ReplayFoundryLocalDataUsage? cache = _maintenance.Inspect()
            .FirstOrDefault(static item =>
                item.Kind == ReplayFoundryLocalDataKind.DerivedCaches);
        CacheUsage = cache is null
            ? "Cache size unavailable"
            : $"{FormatBytes(cache.Bytes)} across {cache.FileCount} file{(cache.FileCount == 1 ? string.Empty : "s")}";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024L => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} bytes",
    };
}
