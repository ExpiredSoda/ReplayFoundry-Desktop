using System.Collections.ObjectModel;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Diagnostics;

public enum UserReportKind
{
    ManualFeedback,
    Crash,
}

public enum UserReportDisposition
{
    AwaitingReview,
    ReadyToSend,
    Sent,
    Failed,
}

public enum UserReportSubmissionCode
{
    ConsentRequired,
    EndpointUnavailable,
    Sent,
    Failed,
}

public sealed class UserReportConsentSnapshot
{
    public const string CurrentNoticeVersion = "bug-report-consent-1.0";

    public UserReportConsentSnapshot(
        bool isEnabled,
        DateTimeOffset? enabledAtUtc,
        string noticeVersion = CurrentNoticeVersion)
    {
        if (string.IsNullOrWhiteSpace(noticeVersion) ||
            isEnabled != enabledAtUtc.HasValue ||
            enabledAtUtc is { } enabled && enabled.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Bug-report delivery requires an explicit versioned UTC consent state.");
        }

        IsEnabled = isEnabled;
        EnabledAtUtc = enabledAtUtc;
        NoticeVersion = noticeVersion.Trim();
    }

    public static UserReportConsentSnapshot Disabled { get; } =
        new(false, null);

    public bool IsEnabled { get; }
    public DateTimeOffset? EnabledAtUtc { get; }
    public string NoticeVersion { get; }
}

public interface IUserReportConsentStore
{
    bool IsPersistent { get; }
    UserReportConsentSnapshot Current { get; }
    void Replace(UserReportConsentSnapshot value);
}

public sealed class InMemoryUserReportConsentStore : IUserReportConsentStore
{
    private UserReportConsentSnapshot _current;

    public InMemoryUserReportConsentStore(
        UserReportConsentSnapshot? initial = null)
    {
        _current = initial ?? UserReportConsentSnapshot.Disabled;
    }

    public bool IsPersistent => false;
    public UserReportConsentSnapshot Current => _current;
    public void Replace(UserReportConsentSnapshot value) =>
        _current = value ?? throw new ArgumentNullException(nameof(value));
}

public sealed class UserReportConsentState
{
    private readonly IUserReportConsentStore _store;
    private UserReportConsentSnapshot _current;

    public UserReportConsentState(IUserReportConsentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = store.Current;
    }

    public event EventHandler? Changed;

    public bool IsPersistent => _store.IsPersistent;
    public bool IsEnabled => _current.IsEnabled;
    public DateTimeOffset? EnabledAtUtc => _current.EnabledAtUtc;
    public string NoticeVersion => _current.NoticeVersion;

    public void Enable(DateTimeOffset enabledAtUtc) => Replace(
        new UserReportConsentSnapshot(true, enabledAtUtc));

    public void Disable() => Replace(UserReportConsentSnapshot.Disabled);

    private void Replace(UserReportConsentSnapshot value)
    {
        _store.Replace(value);
        _current = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class UserReportAttachment
{
    public const int MaximumContentLength = 64 * 1024;

    public UserReportAttachment(
        string fileName,
        string mediaType,
        string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(content);
        int size = System.Text.Encoding.UTF8.GetByteCount(content);
        if (Path.GetFileName(fileName) != fileName ||
            fileName.Contains("..", StringComparison.Ordinal) ||
            size > MaximumContentLength)
        {
            throw new ArgumentException(
                "A diagnostic attachment must be a bounded relative file name.");
        }

        FileName = fileName;
        MediaType = mediaType.Trim();
        Content = content;
        Size = size;
        Sha256 = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content)));
    }

    public string FileName { get; }
    public string MediaType { get; }
    public string Content { get; }
    public long Size { get; }
    public string Sha256 { get; }
}

public sealed class UserReportDraft
{
    private readonly ReadOnlyCollection<UserReportAttachment> _attachments;

    public UserReportDraft(
        string reportId,
        UserReportKind kind,
        string summary,
        string details,
        string applicationVersion,
        DateTimeOffset createdAtUtc,
        IEnumerable<UserReportAttachment>? attachments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        UserReportAttachment[] snapshot = attachments?.ToArray() ?? [];
        if (!Enum.IsDefined(kind) ||
            reportId.Length != 32 ||
            reportId.Any(static value => !Uri.IsHexDigit(value)) ||
            summary.Length > 160 ||
            details.Length > 4_000 ||
            applicationVersion.Length > 128 ||
            createdAtUtc.Offset != TimeSpan.Zero ||
            snapshot.Length > 4 ||
            snapshot.Any(static value => value is null) ||
            snapshot.Sum(static value => value.Size) > 256 * 1024 ||
            snapshot.Select(static value => value.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "A user report requires bounded text, UTC time, and bounded unique attachments.");
        }

        ReportId = reportId.ToUpperInvariant();
        Kind = kind;
        Summary = summary.Trim();
        Details = details.Trim();
        ApplicationVersion = applicationVersion.Trim();
        CreatedAtUtc = createdAtUtc;
        _attachments = Array.AsReadOnly(snapshot);
    }

    public const string SchemaVersion = "user-report-1.0";
    public string ReportId { get; }
    public UserReportKind Kind { get; }
    public string Summary { get; }
    public string Details { get; }
    public string ApplicationVersion { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public IReadOnlyList<UserReportAttachment> Attachments => _attachments;
}

public sealed class StoredUserReport
{
    public StoredUserReport(
        UserReportDraft draft,
        UserReportDisposition disposition,
        DateTimeOffset updatedAtUtc,
        string? failureCode = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!Enum.IsDefined(disposition) ||
            updatedAtUtc.Offset != TimeSpan.Zero ||
            failureCode?.Length > 120 ||
            disposition == UserReportDisposition.Failed !=
                !string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException(
                "A stored report requires a typed disposition and UTC update time.");
        }

        Draft = draft;
        Disposition = disposition;
        UpdatedAtUtc = updatedAtUtc;
        FailureCode = failureCode?.Trim();
    }

    public UserReportDraft Draft { get; }
    public UserReportDisposition Disposition { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public string? FailureCode { get; }
}

public interface IUserReportOutbox
{
    IReadOnlyList<StoredUserReport> Current { get; }
    void Upsert(StoredUserReport report);
    void Remove(string reportId);
    void Clear();
}

public sealed class InMemoryUserReportOutbox : IUserReportOutbox
{
    private readonly Dictionary<string, StoredUserReport> _reports =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<StoredUserReport> Current => Array.AsReadOnly(
        _reports.Values
            .OrderByDescending(static report => report.Draft.CreatedAtUtc)
            .ToArray());

    public void Upsert(StoredUserReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _reports[report.Draft.ReportId] = report;
    }

    public void Remove(string reportId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        _reports.Remove(reportId);
    }

    public void Clear() => _reports.Clear();
}

public sealed record UserReportSubmissionResult(
    UserReportSubmissionCode Code,
    string Message);

public interface IUserReportTransport
{
    bool IsConfigured { get; }
    string DestinationDisplayName { get; }
    Task SendAsync(UserReportDraft report, CancellationToken cancellationToken);
}

public interface IUserReportDiagnosticCollector
{
    UserReportAttachment Collect(Exception? exception = null);
}

public interface IUserReportTextSanitizer
{
    string Sanitize(string value, int maximumLength);
}
