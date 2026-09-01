using System.IO;
using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record DiagnosticReportingServices(
    UserReportConsentState Consent,
    IUserReportOutbox Outbox,
    IUserReportTransport Transport,
    UserReportCoordinator Coordinator);

internal static class DiagnosticReportingComposition
{
    public static DiagnosticReportingServices Create()
    {
        UserReportConsentState consent = CreateConsent();
        IUserReportOutbox outbox = CreateOutbox();
        IUserReportTransport transport = CreateTransport();
        var coordinator = new UserReportCoordinator(
            consent,
            outbox,
            new ReplayFoundryDiagnosticCollector(),
            new UserReportSanitizer(),
            transport);
        return new(consent, outbox, transport, coordinator);
    }

    private static UserReportConsentState CreateConsent()
    {
        try
        {
            return new UserReportConsentState(
                new JsonUserReportConsentStore());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Bug-report consent storage is unavailable",
                exception);
            return new UserReportConsentState(
                new InMemoryUserReportConsentStore());
        }
    }

    private static IUserReportOutbox CreateOutbox()
    {
        try
        {
            return new JsonUserReportOutbox();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "Bug-report outbox storage is unavailable",
                exception);
            return new InMemoryUserReportOutbox();
        }
    }

    private static IUserReportTransport CreateTransport()
    {
        try
        {
            return UserReportTransportFactory.CreateFromAssembly(
                typeof(App).Assembly);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            SafeDiagnosticTrace.Write(
                "Bug-report delivery is unavailable",
                exception);
            return new UnavailableUserReportTransport();
        }
    }
}
