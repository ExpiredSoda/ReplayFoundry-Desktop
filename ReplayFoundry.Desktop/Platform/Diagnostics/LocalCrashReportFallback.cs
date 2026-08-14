using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Diagnostics;

/// <summary>
/// Captures a minimal local crash record when composition failed before the
/// normal report coordinator was available. It never performs network I/O.
/// </summary>
internal static class LocalCrashReportFallback
{
    internal static bool TryCapture(
        Exception exception,
        string? outboxRoot = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var draft = new UserReportDraft(
                Guid.NewGuid().ToString("N"),
                UserReportKind.Crash,
                "Replay Foundry stopped unexpectedly",
                "A startup failure was captured locally. Review this report before choosing whether to send it.",
                UserReportCoordinator.CurrentApplicationVersion(),
                now,
                [new ReplayFoundryDiagnosticCollector().Collect(exception)]);
            new JsonUserReportOutbox(outboxRoot).Upsert(
                new StoredUserReport(
                    draft,
                    UserReportDisposition.AwaitingReview,
                    now));
            return true;
        }
        catch
        {
            // A crash recorder is strictly best effort and must never replace
            // the original fatal exception or attempt an online fallback.
            return false;
        }
    }
}
