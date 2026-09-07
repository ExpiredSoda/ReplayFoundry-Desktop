using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Diagnostics;

public sealed class HttpsUserReportTransport :
    IUserReportTransport,
    IDisposable
{
    private readonly Uri _endpoint;
    private readonly HttpClient _client;
    private bool _disposed;

    public HttpsUserReportTransport(
        Uri endpoint,
        string destinationDisplayName)
        : this(
            endpoint,
            destinationDisplayName,
            new HttpClientHandler { AllowAutoRedirect = false })
    {
    }

    internal HttpsUserReportTransport(
        Uri endpoint,
        string destinationDisplayName,
        HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDisplayName);
        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "The bug-report endpoint must be one fixed HTTPS URL without credentials, query, or fragment.",
                nameof(endpoint));
        }

        _endpoint = endpoint;
        DestinationDisplayName = destinationDisplayName.Trim();
        _client = new HttpClient(
            handler ?? throw new ArgumentNullException(nameof(handler)),
            disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public bool IsConfigured => true;
    public string DestinationDisplayName { get; }

    public async Task SendAsync(
        UserReportDraft report,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(report);
        UserReportDraft outbound =
            UserReportSanitizer.SanitizeOutboundDraft(report);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(new
            {
                schemaVersion = UserReportDraft.SchemaVersion,
                reportId = outbound.ReportId,
                kind = outbound.Kind.ToString(),
                summary = outbound.Summary,
                details = outbound.Details,
                applicationVersion = outbound.ApplicationVersion,
                createdAtUtc = outbound.CreatedAtUtc,
                attachments = outbound.Attachments.Select(static attachment => new
                {
                    attachment.FileName,
                    attachment.MediaType,
                    attachment.Size,
                    attachment.Sha256,
                    attachment.Content,
                }),
            }),
        };
        using HttpResponseMessage response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new HttpRequestException(
                "The configured bug-report endpoint attempted an unapproved redirect.",
                inner: null,
                response.StatusCode);
        }
        if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.Accepted)
        {
            throw new HttpRequestException(
                "The configured bug-report endpoint rejected the report.",
                inner: null,
                response.StatusCode);
        }
        // A proxy error page or an unrelated successful request is not a receipt for this report.
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            response.Content.Headers.ContentLength is > 4096)
            throw new HttpRequestException("Support did not confirm receipt of this report.");
        using Stream body = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var receipt = new MemoryStream();
        var buffer = new byte[512];
        int count;
        while ((count = await body.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (receipt.Length + count > 4096)
                throw new HttpRequestException("The support receipt exceeded its supported size.");
            receipt.Write(buffer, 0, count);
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(receipt.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("accepted", out JsonElement accepted) || accepted.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("reportId", out JsonElement id) || id.ValueKind != JsonValueKind.String ||
                !string.Equals(id.GetString(), outbound.ReportId, StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException("Support did not confirm receipt of this report.");
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Support returned an unreadable receipt. The report remains on this PC.", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
    }
}
