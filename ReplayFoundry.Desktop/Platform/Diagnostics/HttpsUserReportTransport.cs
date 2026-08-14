using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            _endpoint,
            new
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
            },
            cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            throw new HttpRequestException(
                "The configured bug-report endpoint attempted an unapproved redirect.",
                inner: null,
                response.StatusCode);
        }
        if (response.StatusCode is not HttpStatusCode.OK and
            not HttpStatusCode.Accepted and
            not HttpStatusCode.NoContent)
        {
            throw new HttpRequestException(
                "The configured bug-report endpoint rejected the report.",
                inner: null,
                response.StatusCode);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
    }
}
