using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using ReplayFoundry.Desktop.Features.Diagnostics;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Features.Generate.Progress;

namespace ReplayFoundry.PreparationTests;

internal static class ReportConnectionTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Report delivery requires a bounded receipt for the exact submitted report", Receipts),
        new("Report redaction removes standalone credentials, private keys and forward-slash local paths", Credentials),
        new("Handled generation failures create reviewable local reports without network delivery", GenerationFailures),
    ];

    private static Task GenerationFailures()
    {
        var outbox = new InMemoryUserReportOutbox();
        var reports = new UserReportCoordinator(new UserReportConsentState(new InMemoryUserReportConsentStore()),
            outbox, new ReplayFoundryDiagnosticCollector(), new UserReportSanitizer(), new UnavailableUserReportTransport());
        var progress = new GenerationProgressViewModel(() => { }, () => { });
        using (var connection = new GenerationFailureReporting(progress, reports))
        {
            var failure = new IOException("PRIVATE-SOURCE-MESSAGE C:/Users/Creator/video.mp4");
            progress.FailPreparation("Preparation stopped", failure);
            progress.FailEvidenceAnalysis("Scan stopped", failure);
            progress.Fail("Generation stopped", failure);
            TestAssert.Equal(3, outbox.Current.Count, "Every handled failure stage must reach the report outbox.");
            TestAssert.True(outbox.Current.All(x => x.Disposition == UserReportDisposition.AwaitingReview), "Reports require review before sending.");
            TestAssert.False(outbox.Current.Any(x => x.Draft.Attachments.Any(a => a.Content.Contains("PRIVATE-SOURCE-MESSAGE"))), "Exception messages cannot enter diagnostic attachments.");
            progress.Fail("Cancelled", new OperationCanceledException());
            TestAssert.Equal(3, outbox.Current.Count, "Cancelling is not a reportable failure.");
        }
        progress.Fail("Disconnected", new IOException());
        TestAssert.Equal(3, outbox.Current.Count, "Disposal must detach the failure subscription.");
        return Task.CompletedTask;
    }

    private static async Task Receipts()
    {
        var report = new UserReportDraft(Guid.NewGuid().ToString("N"), UserReportKind.ManualFeedback,
            "Connection check", "Synthetic test without personal data", "test", DateTimeOffset.UtcNow, []);
        using (var good = Transport(() => new(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new { accepted = true, reportId = report.ReportId.ToUpperInvariant() }),
        })) await good.SendAsync(report, CancellationToken.None);

        foreach (var makeResponse in new Func<HttpResponseMessage>[]
        {
            () => new(HttpStatusCode.OK) { Content = new StringContent("<html>Proxy login</html>") },
            () => new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { accepted = true, reportId = new string('A', 32) }) },
            () => new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { accepted = false, reportId = report.ReportId }) },
            () => new(HttpStatusCode.NoContent),
            () => new(HttpStatusCode.Redirect),
            () => new(HttpStatusCode.Accepted) { Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json") },
            () => new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { accepted = true, reportId = report.ReportId, padding = new string('x', 5000) }) },
        })
        {
            using var transport = Transport(makeResponse);
            await TestAssert.ThrowsAsync<HttpRequestException>(() => transport.SendAsync(report, CancellationToken.None),
                "An unconfirmed response must leave the report available to retry.");
        }
    }

    private static Task Credentials()
    {
        string[] secrets = ["github_" + "pat_" + new string('Z', 40), "gh" + "p_" + new string('R', 30),
            "cf" + "at_" + new string('C', 30), "ya" + "29." + new string('G', 30)];
        string text = string.Join('\n', secrets) + "\nhttps://user:private-password@service.example/path\n" +
            "token=opaque-private-value\nC:/Users/Creator/private.mov\n" +
            "-----BEGIN " + "PRIVATE KEY-----\nprivate-key-material\n-----END " + "PRIVATE KEY-----";
        string sanitized = new UserReportSanitizer().Sanitize(text, 8000);
        foreach (string secret in secrets.Concat(["private-password", "opaque-private-value", "Creator", "private-key-material"]))
            TestAssert.False(sanitized.Contains(secret, StringComparison.Ordinal), "Report text must not expose a pasted credential or local path.");
        return Task.CompletedTask;
    }

    private static HttpsUserReportTransport Transport(Func<HttpResponseMessage> response) =>
        new(new Uri("https://support.example.test/report"), "Test support", new Handler(response));
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }
}
