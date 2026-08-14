using System.Windows.Input;
using System.IO;
using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Settings;

public sealed record UserReportListItem(
    string ReportId,
    UserReportKind Kind,
    string Summary,
    string Details,
    string AttachmentSummary,
    string DiagnosticPreview,
    UserReportDisposition Disposition,
    DateTimeOffset CreatedAtUtc)
{
    public string KindText => Kind == UserReportKind.Crash
        ? "Crash report"
        : "Feedback";
    public string CreatedText => CreatedAtUtc.ToLocalTime().ToString("g");
    public string StatusText => Disposition switch
    {
        UserReportDisposition.AwaitingReview => "Waiting for your review",
        UserReportDisposition.ReadyToSend => "Ready to send",
        UserReportDisposition.Sent => "Sent",
        UserReportDisposition.Failed => "Send failed · kept locally",
        _ => "Unknown",
    };
}
public sealed class BugReportSettingsViewModel : ObservableObject, IDisposable
{
    private readonly UserReportConsentState? _consent;
    private readonly IUserReportOutbox? _outbox;
    private readonly UserReportCoordinator? _coordinator;
    private readonly DelegateCommand _enableCommand;
    private readonly DelegateCommand _disableCommand;
    private readonly DelegateCommand _saveManualCommand;
    private readonly AsyncDelegateCommand _sendSelectedCommand;
    private readonly DelegateCommand _deleteSelectedCommand;
    private readonly DelegateCommand _deleteAllCommand;
    private IReadOnlyList<UserReportListItem> _reports = [];
    private UserReportListItem? _selectedReport;
    private string _summary = string.Empty;
    private string _details = string.Empty;
    private string _notice = string.Empty;
    private bool _includeDiagnostics = true;
    private bool _deleteAllArmed;
    private bool _disposed;

    public BugReportSettingsViewModel()
    {
        _enableCommand = new DelegateCommand(() => { }, () => false);
        _disableCommand = new DelegateCommand(() => { }, () => false);
        _saveManualCommand = new DelegateCommand(() => { }, () => false);
        _sendSelectedCommand = new AsyncDelegateCommand(
            () => Task.CompletedTask,
            () => false);
        _deleteSelectedCommand = new DelegateCommand(() => { }, () => false);
        _deleteAllCommand = new DelegateCommand(() => { }, () => false);
    }

    public BugReportSettingsViewModel(
        UserReportConsentState consent,
        IUserReportOutbox outbox,
        UserReportCoordinator coordinator)
    {
        _consent = consent ?? throw new ArgumentNullException(nameof(consent));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _enableCommand = new DelegateCommand(
            Enable,
            () => !IsDeliveryAllowed && IsDeliveryConfigured);
        _disableCommand = new DelegateCommand(
            Disable,
            () => IsDeliveryAllowed);
        _saveManualCommand = new DelegateCommand(
            SaveManual,
            () => !string.IsNullOrWhiteSpace(Summary) &&
                  !string.IsNullOrWhiteSpace(Details));
        _sendSelectedCommand = new AsyncDelegateCommand(
            SendSelectedAsync,
            () => SelectedReport is not null &&
                  SelectedReport.Disposition != UserReportDisposition.Sent &&
                  IsDeliveryAllowed &&
                  IsDeliveryConfigured);
        _deleteSelectedCommand = new DelegateCommand(
            DeleteSelected,
            () => SelectedReport is not null);
        _deleteAllCommand = new DelegateCommand(
            DeleteAll,
            () => Reports.Count > 0);
        _consent.Changed += Consent_Changed;
        RefreshReports();
    }

    public bool IsAvailable => _coordinator is not null;
    public bool IsDeliveryAllowed => _consent?.IsEnabled == true;
    public bool IsDeliveryConfigured => _coordinator?.IsDeliveryConfigured == true;
    public string DeliveryStatus => !IsAvailable
        ? "Unavailable in this preview"
        : IsDeliveryAllowed
            ? IsDeliveryConfigured
                ? "Sending allowed · only when you click Send"
                : "Sending allowed · website endpoint not configured"
            : "Sending off";
    public string DeliveryDetail =>
        IsDeliveryConfigured
            ? $"Reviewed destination: {_coordinator!.DestinationDisplayName}. " +
              "Reports are separate from research sharing and YouTube. " +
              "Crash records stay local until you review and explicitly " +
              "send them. Replay Foundry never attaches media, titles, " +
              "transcripts, account tokens, or file paths."
            : "This build has no reviewed support-site endpoint, so delivery cannot be enabled. Reports can still be reviewed and kept or deleted locally.";
    public IReadOnlyList<UserReportListItem> Reports => _reports;
    public int ReportCount => Reports.Count;
    public string OutboxStatus => ReportCount == 0
        ? "No saved reports."
        : $"{ReportCount} report{(ReportCount == 1 ? string.Empty : "s")} stored locally.";

    public UserReportListItem? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (!SetProperty(ref _selectedReport, value)) return;
            _deleteAllArmed = false;
            OnPropertyChanged(nameof(SelectedDiagnosticPreview));
            OnPropertyChanged(nameof(DeleteAllLabel));
            RaiseCommandStates();
        }
    }

    public string Summary
    {
        get => _summary;
        set
        {
            if (!SetProperty(ref _summary, value ?? string.Empty)) return;
            _saveManualCommand.RaiseCanExecuteChanged();
        }
    }

    public string Details
    {
        get => _details;
        set
        {
            if (!SetProperty(ref _details, value ?? string.Empty)) return;
            _saveManualCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IncludeDiagnostics
    {
        get => _includeDiagnostics;
        set => SetProperty(ref _includeDiagnostics, value);
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
    public string SelectedDiagnosticPreview => SelectedReport?.DiagnosticPreview ??
        "No diagnostic attachment is selected.";
    public string DeleteAllLabel => _deleteAllArmed
        ? "Confirm delete all"
        : "Delete all reports";
    public ICommand EnableDeliveryCommand => _enableCommand;
    public ICommand DisableDeliveryCommand => _disableCommand;
    public ICommand SaveManualReportCommand => _saveManualCommand;
    public ICommand SendSelectedReportCommand => _sendSelectedCommand;
    public ICommand DeleteSelectedReportCommand => _deleteSelectedCommand;
    public ICommand DeleteAllReportsCommand => _deleteAllCommand;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_consent is not null) _consent.Changed -= Consent_Changed;
    }

    internal void DisableForLocalReset()
    {
        if (_consent?.IsEnabled == true) _consent.Disable();
    }

    private void Enable()
    {
        if (!IsDeliveryConfigured)
        {
            Notice = "Delivery cannot be enabled until this build identifies the reviewed Replay Foundry support endpoint.";
            return;
        }
        _consent!.Enable(DateTimeOffset.UtcNow);
        Notice = "Sending is allowed, but reports still require an explicit Send action.";
    }

    private void Disable()
    {
        _consent!.Disable();
        Notice = "Sending is off. Saved reports remain local until you delete them.";
    }

    private void SaveManual()
    {
        try
        {
            StoredUserReport report = _coordinator!.SaveManual(
                Summary,
                Details,
                IncludeDiagnostics);
            Summary = string.Empty;
            Details = string.Empty;
            RefreshReports(report.Draft.ReportId);
            Notice = "Feedback was saved locally. Review it below before sending.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or
            UnauthorizedAccessException or InvalidDataException)
        {
            Notice = "Replay Foundry could not save that report: " + exception.Message;
        }
    }

    private async Task SendSelectedAsync()
    {
        if (SelectedReport is null) return;
        string reportId = SelectedReport.ReportId;
        try
        {
            UserReportSubmissionResult result = await _coordinator!.SendAsync(
                reportId);
            RefreshReports(reportId);
            Notice = result.Message;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            RefreshReports(reportId);
            Notice = "Replay Foundry could not send that report: " + exception.Message;
        }
    }

    private void DeleteSelected()
    {
        if (SelectedReport is null) return;
        _outbox!.Remove(SelectedReport.ReportId);
        RefreshReports();
        Notice = "The selected local report was deleted.";
    }

    private void DeleteAll()
    {
        if (!_deleteAllArmed)
        {
            _deleteAllArmed = true;
            OnPropertyChanged(nameof(DeleteAllLabel));
            Notice = "Click Confirm delete all to permanently remove every local report and attachment.";
            return;
        }
        _outbox!.Clear();
        _deleteAllArmed = false;
        OnPropertyChanged(nameof(DeleteAllLabel));
        RefreshReports();
        Notice = "All local reports and their diagnostic attachments were deleted.";
    }

    private void RefreshReports(string? selectReportId = null)
    {
        _reports = _outbox?.Current.Select(static report =>
            new UserReportListItem(
                report.Draft.ReportId,
                report.Draft.Kind,
                report.Draft.Summary,
                report.Draft.Details,
                report.Draft.Attachments.Count == 0
                    ? "No diagnostic attachment"
                    : $"{report.Draft.Attachments.Count} sanitized diagnostic attachment{(report.Draft.Attachments.Count == 1 ? string.Empty : "s")}",
                report.Draft.Attachments.Count == 0
                    ? "No diagnostic attachment was included."
                    : string.Join(
                        Environment.NewLine + Environment.NewLine,
                        report.Draft.Attachments.Select(static attachment =>
                            $"--- {attachment.FileName} · {attachment.Size} bytes · SHA-256 {attachment.Sha256} ---{Environment.NewLine}{attachment.Content}")),
                report.Disposition,
                report.Draft.CreatedAtUtc)).ToArray() ?? [];
        OnPropertyChanged(nameof(Reports));
        OnPropertyChanged(nameof(ReportCount));
        OnPropertyChanged(nameof(OutboxStatus));
        SelectedReport = _reports.FirstOrDefault(report =>
            string.Equals(
                report.ReportId,
                selectReportId,
                StringComparison.OrdinalIgnoreCase)) ??
            _reports.FirstOrDefault();
        RaiseCommandStates();
    }

    private void Consent_Changed(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsDeliveryAllowed));
        OnPropertyChanged(nameof(DeliveryStatus));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        _enableCommand.RaiseCanExecuteChanged();
        _disableCommand.RaiseCanExecuteChanged();
        _saveManualCommand.RaiseCanExecuteChanged();
        _sendSelectedCommand.RaiseCanExecuteChanged();
        _deleteSelectedCommand.RaiseCanExecuteChanged();
        _deleteAllCommand.RaiseCanExecuteChanged();
    }
}
