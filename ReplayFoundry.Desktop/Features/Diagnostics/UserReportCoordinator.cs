using System.Reflection;
using System.Net.Http;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Diagnostics;

public sealed class UserReportCoordinator
{
    private readonly UserReportConsentState _consent;
    private readonly IUserReportOutbox _outbox;
    private readonly IUserReportDiagnosticCollector _diagnostics;
    private readonly IUserReportTextSanitizer _sanitizer;
    private readonly IUserReportTransport _transport;

    public UserReportCoordinator(
        UserReportConsentState consent,
        IUserReportOutbox outbox,
        IUserReportDiagnosticCollector diagnostics,
        IUserReportTextSanitizer sanitizer,
        IUserReportTransport transport)
    {
        _consent = consent ?? throw new ArgumentNullException(nameof(consent));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _diagnostics = diagnostics ??
            throw new ArgumentNullException(nameof(diagnostics));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public bool IsDeliveryConfigured => _transport.IsConfigured;
    public string DestinationDisplayName => _transport.DestinationDisplayName;

    public StoredUserReport SaveManual(
        string summary,
        string details,
        bool includeDiagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(details);
        UserReportDraft draft = CreateDraft(
            UserReportKind.ManualFeedback,
            _sanitizer.Sanitize(summary, 160),
            _sanitizer.Sanitize(details, 4_000),
            includeDiagnostics ? [_diagnostics.Collect()] : []);
        var stored = new StoredUserReport(
            draft,
            UserReportDisposition.AwaitingReview,
            DateTimeOffset.UtcNow);
        _outbox.Upsert(stored);
        return stored;
    }

    public StoredUserReport CaptureCrash(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        UserReportDraft draft = CreateDraft(
            UserReportKind.Crash,
            "Replay Foundry closed unexpectedly",
            "A sanitized local crash record is waiting for review. " +
            "It will not be sent automatically.",
            [_diagnostics.Collect(exception)]);
        var stored = new StoredUserReport(
            draft,
            UserReportDisposition.AwaitingReview,
            DateTimeOffset.UtcNow);
        _outbox.Upsert(stored);
        return stored;
    }

    public bool TryCaptureCrash(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _ = CaptureCrash(exception);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<UserReportSubmissionResult> SendAsync(
        string reportId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        StoredUserReport? stored = _outbox.Current.FirstOrDefault(
            report => string.Equals(
                report.Draft.ReportId,
                reportId,
                StringComparison.OrdinalIgnoreCase));
        if (stored is null)
        {
            throw new ArgumentException(
                "The selected report is no longer in the local outbox.",
                nameof(reportId));
        }
        if (!_consent.IsEnabled)
        {
            return new UserReportSubmissionResult(
                UserReportSubmissionCode.ConsentRequired,
                "Turn on bug-report delivery before sending this reviewed report.");
        }
        if (!_transport.IsConfigured)
        {
            return new UserReportSubmissionResult(
                UserReportSubmissionCode.EndpointUnavailable,
                "Bug-report delivery is not configured in this build. The report remains local.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _outbox.Upsert(new StoredUserReport(
            stored.Draft,
            UserReportDisposition.ReadyToSend,
            DateTimeOffset.UtcNow));
        try
        {
            await _transport.SendAsync(stored.Draft, cancellationToken);
            _outbox.Upsert(new StoredUserReport(
                stored.Draft,
                UserReportDisposition.Sent,
                DateTimeOffset.UtcNow));
            return new UserReportSubmissionResult(
                UserReportSubmissionCode.Sent,
                $"The reviewed report was sent to {_transport.DestinationDisplayName}.");
        }
        catch (OperationCanceledException)
        {
            _outbox.Upsert(stored);
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TimeoutException)
        {
            _outbox.Upsert(new StoredUserReport(
                stored.Draft,
                UserReportDisposition.Failed,
                DateTimeOffset.UtcNow,
                exception.GetType().Name));
            return new UserReportSubmissionResult(
                UserReportSubmissionCode.Failed,
                "The report could not be sent and remains in the local outbox.");
        }
    }

    private static UserReportDraft CreateDraft(
        UserReportKind kind,
        string summary,
        string details,
        IEnumerable<UserReportAttachment> attachments) => new(
            Guid.NewGuid().ToString("N"),
            kind,
            summary,
            details,
            CurrentApplicationVersion(),
            DateTimeOffset.UtcNow,
            attachments);

    internal static string CurrentApplicationVersion() =>
            typeof(UserReportCoordinator).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ??
            typeof(UserReportCoordinator).Assembly.GetName().Version?.ToString() ??
            "development";
}

public sealed class UnavailableUserReportTransport : IUserReportTransport
{
    public bool IsConfigured => false;
    public string DestinationDisplayName => "Replay Foundry support";

    public Task SendAsync(
        UserReportDraft report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Bug-report delivery is not configured in this build.");
    }
}
