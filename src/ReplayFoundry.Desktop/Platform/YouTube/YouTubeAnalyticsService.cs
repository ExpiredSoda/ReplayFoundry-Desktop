using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.YouTube;

internal sealed class YouTubeAnalyticsService(
    IYouTubeAuthorizationService authorization,
    HttpClient httpClient,
    IYouTubeConnectionPermission permission) : IYouTubeAnalyticsService, IDisposable
{
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        RequirePermission();
        var credential = await authorization.ConnectAsync(cancellationToken).ConfigureAwait(false);
        RequireScope(credential);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) => authorization.DisconnectAsync(cancellationToken);

    public async Task<IReadOnlyList<YouTubeVideoObservation>> ReadAsync(
        IReadOnlyList<YouTubePublishHistoryEntry> history, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        RequirePermission();
        if (from > to || to > DateOnly.FromDateTime(DateTime.UtcNow) || to.DayNumber - from.DayNumber > 366)
            throw new ArgumentException("Choose a valid reporting period of at most one year.");
        var credential = await authorization.GetAccessCredentialAsync(false, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connect read-only YouTube Analytics first.");
        RequireScope(credential);
        var entries = history.Where(entry => !string.IsNullOrWhiteSpace(entry.VideoId))
            .OrderByDescending(entry => entry.AttemptedAtUtc).DistinctBy(entry => entry.VideoId).Take(100).ToArray();
        if (entries.Length == 0) return [];
        string ids = string.Join(',', entries.Select(entry => entry.VideoId));
        // The channel report permits video as a dimension when the same filter is supplied.
        // https://developers.google.com/youtube/analytics/channel_reports#retrieving-a-report
        string query = "ids=channel%3D%3DMINE&dimensions=video&metrics=views,engagedViews,averageViewDuration,averageViewPercentage,shares,subscribersGained" +
            "&filters=" + Uri.EscapeDataString("video==" + ids) + "&startDate=" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "&endDate=" + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://youtubeanalytics.googleapis.com/v2/reports?" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"YouTube Analytics returned {(int)response.StatusCode}. Check the read-only connection and Analytics API configuration.");
        const int maximumBytes = 4 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("Analytics response exceeded the local report limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        while (await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false) is int count && count > 0)
        {
            if (buffer.Length + count > maximumBytes)
                throw new InvalidDataException("Analytics response exceeded the local report limit.");
            buffer.Write(chunk, 0, count);
        }
        using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
        return Parse(document.RootElement, entries, from, to, DateTimeOffset.UtcNow);
    }

    internal static IReadOnlyList<YouTubeVideoObservation> Parse(JsonElement root,
        IReadOnlyList<YouTubePublishHistoryEntry> entries, DateOnly from, DateOnly to, DateTimeOffset retrieved)
    {
        if (!root.TryGetProperty("columnHeaders", out var headerArray) || headerArray.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Analytics report has no column definitions.");
        var headers = headerArray.EnumerateArray().Select((value, index) => new { name = value.GetProperty("name").GetString()!, index })
            .ToDictionary(value => value.name, value => value.index, StringComparer.Ordinal);
        if (!headers.TryGetValue("video", out int idIndex)) throw new InvalidDataException("Analytics report is not grouped by video.");
        var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("rows", out var values) && values.ValueKind == JsonValueKind.Array)
            foreach (var row in values.EnumerateArray())
            {
                if (row.GetArrayLength() != headers.Count) throw new InvalidDataException("Analytics row has an unexpected shape.");
                string id = row[idIndex].GetString() ?? throw new InvalidDataException("Missing video identity.");
                if (!rows.TryAdd(id, row)) throw new InvalidDataException("Duplicate analytics video row.");
            }
        return entries.Select(entry =>
        {
            bool hasData = rows.TryGetValue(entry.VideoId!, out var row);
            double? Read(string key)
            {
                if (!hasData || !headers.TryGetValue(key, out int index) || row[index].ValueKind == JsonValueKind.Null) return null;
                if (!row[index].TryGetDouble(out double value) || !double.IsFinite(value) || value < 0)
                    throw new InvalidDataException("Analytics metric is not a nonnegative number.");
                return value;
            }
            return new YouTubeVideoObservation(entry.VideoId!, entry.AssetId, entry.Title, from, to,
                Read("views"), Read("engagedViews"), Read("averageViewDuration"), Read("averageViewPercentage"),
                Read("shares"), Read("subscribersGained"), retrieved, entry.Provenance,
                entry.AttemptedAtUtc, entry.ScheduledForUtc);
        }).ToArray();
    }

    private void RequirePermission()
    {
        if (!permission.IsEnabled) throw new InvalidOperationException("Enable the YouTube connection in Settings first.");
    }

    private static void RequireScope(YouTubeAccessCredential credential)
    {
        if (!credential.Scopes.Contains(YouTubeOAuthClientConfiguration.AnalyticsReadOnlyScope, StringComparer.Ordinal))
            throw new InvalidOperationException("YouTube Analytics read-only access was not granted.");
    }

    public void Dispose() => (authorization as IDisposable)?.Dispose();
}
