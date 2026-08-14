using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.YouTube;

internal interface IYouTubeDataApiClient
{
    Task<YouTubeAccountConnection> GetChannelAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<string> UploadVideoAsync(
        string accessToken,
        YouTubePublishRequest request,
        IProgress<YouTubePublishProgress>? progress,
        CancellationToken cancellationToken);

    Task SetThumbnailAsync(
        string accessToken,
        string videoId,
        string thumbnailFullPath,
        CancellationToken cancellationToken);

    Task AddToPlaylistAsync(
        string accessToken,
        string videoId,
        string playlistId,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> GetExistingVideoIdsAsync(
        string accessToken,
        IReadOnlyList<string> videoIds,
        CancellationToken cancellationToken);
}

internal sealed class YouTubeDataApiClient : IYouTubeDataApiClient
{
    private const int UploadChunkSize = 8 * 1024 * 1024;
    private const int MaximumChunkAttempts = 4;
    private const int MaximumErrorCharacters = 64 * 1024;
    private static readonly Uri ApiRoot =
        new("https://www.googleapis.com/youtube/v3/");
    private static readonly Uri UploadRoot =
        new("https://www.googleapis.com/upload/youtube/v3/");
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;

    public YouTubeDataApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<YouTubeAccountConnection> GetChannelAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Get,
                new Uri(ApiRoot, "channels?part=id%2Csnippet&mine=true"),
                accessToken,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "youtube.channel.read-failed",
                "Replay Foundry could not read the connected YouTube channel.",
                cancellationToken)
            .ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        ChannelListResponse? payload = await JsonSerializer
            .DeserializeAsync<ChannelListResponse>(
                stream,
                JsonReadOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        ChannelItem? channel = payload?.Items?.SingleOrDefault();
        if (channel is null ||
            string.IsNullOrWhiteSpace(channel.Id) ||
            string.IsNullOrWhiteSpace(channel.Snippet?.Title))
        {
            throw new YouTubePublishingException(
                "The selected Google account does not expose exactly one YouTube channel.",
                "youtube.channel.missing");
        }
        return new YouTubeAccountConnection(
            channel.Id,
            channel.Snippet.Title,
            DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlySet<string>> GetExistingVideoIdsAsync(
        string accessToken,
        IReadOnlyList<string> videoIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videoIds);
        string[] snapshot = videoIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (snapshot.Length is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(videoIds),
                "YouTube status checks accept 1 through 50 recorded video IDs per request.");
        }
        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Get,
                new Uri(
                    ApiRoot,
                    "videos?part=id&id=" +
                    Uri.EscapeDataString(string.Join(',', snapshot))),
                accessToken,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "youtube.videos.reconcile-failed",
                "Replay Foundry could not check the recorded YouTube videos.",
                cancellationToken)
            .ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        VideoListResponse? payload = await JsonSerializer.DeserializeAsync<VideoListResponse>(
                stream,
                JsonReadOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new HashSet<string>(
            payload?.Items?.Select(static item => item.Id)
                .Where(static id => !string.IsNullOrWhiteSpace(id)) ?? [],
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var result = new List<YouTubePlaylist>();
        string? pageToken = null;
        do
        {
            string query =
                "playlists?part=id%2Csnippet%2Cstatus&mine=true&maxResults=50" +
                (pageToken is null
                    ? string.Empty
                    : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using HttpResponseMessage response = await SendAsync(
                    HttpMethod.Get,
                    new Uri(ApiRoot, query),
                    accessToken,
                    content: null,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(
                    response,
                    "youtube.playlists.read-failed",
                    "Replay Foundry could not load your YouTube playlists.",
                    cancellationToken)
                .ConfigureAwait(false);
            await using Stream stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            PlaylistListResponse? payload = await JsonSerializer
                .DeserializeAsync<PlaylistListResponse>(
                    stream,
                    JsonReadOptions,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (PlaylistItem item in payload?.Items ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Snippet?.Title))
                {
                    throw new YouTubePublishingException(
                        "YouTube returned an incomplete playlist record.",
                        "youtube.playlists.invalid-response");
                }
                result.Add(new YouTubePlaylist(
                    item.Id,
                    item.Snippet.Title,
                    item.Status?.PrivacyStatus?.Equals(
                        "private",
                        StringComparison.OrdinalIgnoreCase) == true));
            }
            pageToken = string.IsNullOrWhiteSpace(payload?.NextPageToken)
                ? null
                : payload.NextPageToken;
        }
        while (pageToken is not null);

        return Array.AsReadOnly(result
            .OrderBy(static value => value.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static value => value.Id, StringComparer.Ordinal)
            .ToArray());
    }

    public async Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        RegionInfo region = RegionInfo.CurrentRegion;
        string regionCode = region.TwoLetterISORegionName.Length == 2
            ? region.TwoLetterISORegionName.ToUpperInvariant()
            : "US";
        var uri = new Uri(
            ApiRoot,
            "videoCategories?part=id%2Csnippet&regionCode=" +
            Uri.EscapeDataString(regionCode));
        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Get,
                uri,
                accessToken,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "youtube.categories.read-failed",
                "Replay Foundry could not load YouTube video categories.",
                cancellationToken)
            .ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        CategoryListResponse? payload = await JsonSerializer
            .DeserializeAsync<CategoryListResponse>(
                stream,
                JsonReadOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        YouTubeVideoCategory[] categories = (payload?.Items ?? [])
            .Where(static item =>
                item.Snippet?.Assignable == true &&
                !string.IsNullOrWhiteSpace(item.Id) &&
                !string.IsNullOrWhiteSpace(item.Snippet.Title))
            .Select(static item =>
                new YouTubeVideoCategory(item.Id, item.Snippet!.Title))
            .OrderBy(static item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (categories.Length == 0)
        {
            throw new YouTubePublishingException(
                "YouTube returned no assignable video categories.",
                "youtube.categories.empty-response");
        }
        return Array.AsReadOnly(categories);
    }

    public async Task<string> UploadVideoAsync(
        string accessToken,
        YouTubePublishRequest request,
        IProgress<YouTubePublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var file = new FileInfo(request.Asset.OutputFullPath!);
        if (!file.Exists || file.Length <= 0)
        {
            throw new YouTubePublishingException(
                "The finalized Studio video no longer exists or is empty.",
                "youtube.upload.asset-missing");
        }
        long originalLength = file.Length;
        DateTime originalWriteUtc = file.LastWriteTimeUtc;

        progress?.Report(new YouTubePublishProgress(
            YouTubePublishPhase.Preparing,
            "Preparing YouTube upload",
            "Creating a resumable YouTube upload session.",
            0,
            file.Length));
        Uri uploadSession = await CreateUploadSessionAsync(
                accessToken,
                request,
                file,
                cancellationToken)
            .ConfigureAwait(false);

        long offset = 0;
        string? videoId = null;
        while (offset < file.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            file.Refresh();
            if (!file.Exists ||
                file.Length != originalLength ||
                file.LastWriteTimeUtc != originalWriteUtc)
            {
                throw new YouTubePublishingException(
                    "The finalized video changed while it was being uploaded.",
                    "youtube.upload.asset-changed");
            }
            long length = Math.Min(UploadChunkSize, file.Length - offset);
            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    using HttpResponseMessage response = await SendChunkAsync(
                            uploadSession,
                            accessToken,
                            file,
                            offset,
                            length,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (IsResumeIncomplete(response))
                    {
                        long resumed = ParseResumeOffset(response, offset);
                        EnsureResumeOffset(resumed, file.Length);
                        if (resumed <= offset)
                        {
                            throw new YouTubePublishingException(
                                "YouTube did not advance the resumable upload.",
                                "youtube.upload.no-progress");
                        }
                        offset = resumed;
                        break;
                    }
                    if (IsTransient(response.StatusCode) &&
                        attempt < MaximumChunkAttempts)
                    {
                        await DelayBeforeRetryAsync(
                                response,
                                attempt,
                                cancellationToken)
                            .ConfigureAwait(false);
                        UploadStatus status = await QueryUploadStatusAsync(
                                uploadSession,
                                accessToken,
                                file.Length,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (status.VideoId is not null)
                        {
                            videoId = status.VideoId;
                            offset = file.Length;
                            break;
                        }
                        offset = status.Offset;
                        length = Math.Min(
                            UploadChunkSize,
                            file.Length - offset);
                        continue;
                    }
                    await EnsureSuccessAsync(
                            response,
                            "youtube.upload.transfer-failed",
                            "YouTube could not receive the selected video.",
                            cancellationToken)
                        .ConfigureAwait(false);
                    videoId = await ReadVideoIdAsync(response, cancellationToken)
                        .ConfigureAwait(false);
                    offset = file.Length;
                    break;
                }
                catch (HttpRequestException) when (
                    attempt < MaximumChunkAttempts)
                {
                    await Task.Delay(
                            TimeSpan.FromSeconds(1 << (attempt - 1)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    UploadStatus status = await QueryUploadStatusAsync(
                            uploadSession,
                            accessToken,
                            file.Length,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (status.VideoId is not null)
                    {
                        videoId = status.VideoId;
                        offset = file.Length;
                        break;
                    }
                    offset = status.Offset;
                    length = Math.Min(UploadChunkSize, file.Length - offset);
                }
            }
        }

        file.Refresh();
        if (!file.Exists ||
            file.Length != originalLength ||
            file.LastWriteTimeUtc != originalWriteUtc)
        {
            throw new YouTubePublishingException(
                "The finalized video changed while it was being uploaded.",
                "youtube.upload.asset-changed");
        }
        if (string.IsNullOrWhiteSpace(videoId))
        {
            throw new YouTubePublishingException(
                "YouTube completed the transfer without returning a video identifier.",
                "youtube.upload.missing-video-id");
        }
        return videoId;
    }

    public async Task SetThumbnailAsync(
        string accessToken,
        string videoId,
        string thumbnailFullPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId) ||
            string.IsNullOrWhiteSpace(thumbnailFullPath) ||
            !Path.IsPathFullyQualified(thumbnailFullPath))
        {
            throw new ArgumentException(
                "A YouTube thumbnail requires a video and full file path.");
        }
        var file = new FileInfo(thumbnailFullPath);
        if (!file.Exists || file.Length is <= 0 or > 2_000_000)
        {
            throw new YouTubePublishingException(
                "The custom thumbnail must be a nonempty JPEG or PNG no larger than 2 MB.",
                "youtube.thumbnail.invalid-file");
        }
        string extension = file.Extension.ToLowerInvariant();
        string mediaType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => throw new YouTubePublishingException(
                "YouTube custom thumbnails must be JPEG or PNG files.",
                "youtube.thumbnail.unsupported-format"),
        };
        await using FileStream stream = file.OpenRead();
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        Uri uri = new(
            UploadRoot,
            "thumbnails/set?uploadType=media&videoId=" +
            Uri.EscapeDataString(videoId));
        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Post,
                uri,
                accessToken,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "youtube.thumbnail.upload-failed",
                "The video was uploaded, but YouTube could not apply the custom thumbnail.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddToPlaylistAsync(
        string accessToken,
        string videoId,
        string playlistId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId) ||
            string.IsNullOrWhiteSpace(playlistId))
        {
            throw new ArgumentException(
                "Adding a YouTube playlist item requires both identifiers.");
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            snippet = new
            {
                playlistId,
                resourceId = new
                {
                    kind = "youtube#video",
                    videoId,
                },
            },
        });
        using var content = new ByteArrayContent(json);
        content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Post,
                new Uri(ApiRoot, "playlistItems?part=snippet"),
                accessToken,
                content,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "youtube.playlist.add-failed",
                "The video was uploaded, but YouTube could not add it to the selected playlist.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Uri> CreateUploadSessionAsync(
        string accessToken,
        YouTubePublishRequest request,
        FileInfo file,
        CancellationToken cancellationToken)
    {
        string effectivePrivacy = request.Timing == YouTubePublishTiming.Schedule
            ? "private"
            : request.Visibility.ToString().ToLowerInvariant();
        var status = new Dictionary<string, object>
        {
            ["privacyStatus"] = effectivePrivacy,
            ["selfDeclaredMadeForKids"] =
                request.Audience == YouTubeAudience.MadeForKids,
            ["containsSyntheticMedia"] = request.ContainsSyntheticMedia,
        };
        if (request.Timing == YouTubePublishTiming.Schedule)
        {
            status["publishAt"] = request.ScheduledForUtc!.Value
                .ToString("O", CultureInfo.InvariantCulture);
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            snippet = new
            {
                title = request.Title,
                description = request.Description,
                tags = request.Tags,
                categoryId = request.CategoryId,
            },
            status,
        });
        using var content = new ByteArrayContent(json);
        content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        Uri uri = new(
            UploadRoot,
            "videos?uploadType=resumable&part=snippet%2Cstatus" +
            "&notifySubscribers=" +
            request.NotifySubscribers.ToString().ToLowerInvariant());
        using var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = content,
        };
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.TryAddWithoutValidation(
            "X-Upload-Content-Length",
            file.Length.ToString(CultureInfo.InvariantCulture));
        message.Headers.TryAddWithoutValidation(
            "X-Upload-Content-Type",
            GetVideoMediaType(file.Extension));
        using HttpResponseMessage response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                response,
                "youtube.upload.session-failed",
                "YouTube could not start a resumable upload.",
                cancellationToken)
            .ConfigureAwait(false);
        Uri? location = response.Headers.Location;
        if (location is null ||
            location.Scheme != Uri.UriSchemeHttps ||
            !(location.Host.Equals(
                  "www.googleapis.com",
                  StringComparison.OrdinalIgnoreCase) ||
              location.Host.EndsWith(
                  ".googleapis.com",
                  StringComparison.OrdinalIgnoreCase)))
        {
            throw new YouTubePublishingException(
                "YouTube returned an invalid resumable-upload location.",
                "youtube.upload.invalid-session-location");
        }
        return location;
    }

    private async Task<HttpResponseMessage> SendChunkAsync(
        Uri uploadSession,
        string accessToken,
        FileInfo file,
        long offset,
        long length,
        IProgress<YouTubePublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        var content = new FileRangeHttpContent(
            file.FullName,
            offset,
            length,
            sent => progress?.Report(new YouTubePublishProgress(
                YouTubePublishPhase.Uploading,
                "Uploading to YouTube",
                $"Transferred {FormatBytes(offset + sent)} of {FormatBytes(file.Length)}.",
                offset + sent,
                file.Length)));
        content.Headers.ContentType = new MediaTypeHeaderValue(
            GetVideoMediaType(file.Extension));
        content.Headers.ContentRange = new ContentRangeHeaderValue(
            offset,
            offset + length - 1,
            file.Length);
        var message = new HttpRequestMessage(HttpMethod.Put, uploadSession)
        {
            Content = content,
        };
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            return await _httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            message.Dispose();
        }
    }

    private async Task<UploadStatus> QueryUploadStatusAsync(
        Uri uploadSession,
        string accessToken,
        long totalLength,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentLength = 0;
        content.Headers.TryAddWithoutValidation(
            "Content-Range",
            $"bytes */{totalLength.ToString(CultureInfo.InvariantCulture)}");
        using var message = new HttpRequestMessage(HttpMethod.Put, uploadSession)
        {
            Content = content,
        };
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (IsResumeIncomplete(response))
        {
            long offset = ParseResumeOffset(response, 0);
            EnsureResumeOffset(offset, totalLength);
            return new UploadStatus(offset, null);
        }
        await EnsureSuccessAsync(
                response,
                "youtube.upload.status-failed",
                "Replay Foundry could not resume the interrupted YouTube upload.",
                cancellationToken)
            .ConfigureAwait(false);
        return new UploadStatus(
            totalLength,
            await ReadVideoIdAsync(response, cancellationToken)
                .ConfigureAwait(false));
    }

    private static bool IsResumeIncomplete(HttpResponseMessage response) =>
        (int)response.StatusCode == 308;

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode == 429 ||
        (int)statusCode is >= 500 and <= 599;

    private static async Task DelayBeforeRetryAsync(
        HttpResponseMessage response,
        int attempt,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = response.Headers.RetryAfter?.Delta ??
            TimeSpan.FromSeconds(1 << (attempt - 1));
        await Task.Delay(
                delay > TimeSpan.FromSeconds(30)
                    ? TimeSpan.FromSeconds(30)
                    : delay,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string GetVideoMediaType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream",
        };

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        string accessToken,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException(
                "YouTube API access requires an OAuth access token.",
                nameof(accessToken));
        }
        using var message = new HttpRequestMessage(method, uri)
        {
            Content = content,
        };
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static long ParseResumeOffset(
        HttpResponseMessage response,
        long fallback)
    {
        if (!response.Headers.TryGetValues("Range", out var values))
        {
            return fallback;
        }
        string value = values.Single();
        int dash = value.LastIndexOf('-');
        return dash >= 0 &&
               long.TryParse(
                   value[(dash + 1)..],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out long last)
            ? last + 1
            : throw new YouTubePublishingException(
                "YouTube returned an invalid resumable-upload range.",
                "youtube.upload.invalid-resume-range",
                value);
    }

    private static void EnsureResumeOffset(long offset, long totalLength)
    {
        if (offset < 0 || offset > totalLength)
        {
            throw new YouTubePublishingException(
                "YouTube returned a resumable-upload position outside the selected video.",
                "youtube.upload.invalid-resume-offset",
                $"Offset {offset}; total {totalLength}.");
        }
    }

    private static async Task<string> ReadVideoIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken)
            .ConfigureAwait(false);
        VideoInsertResponse? payload = await JsonSerializer
            .DeserializeAsync<VideoInsertResponse>(
                stream,
                JsonReadOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(payload?.Id)
            ? throw new YouTubePublishingException(
                "YouTube returned no video identifier after upload.",
                "youtube.upload.invalid-response")
            : payload.Id;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string diagnosticCode,
        string message,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        string body = await response.Content.ReadAsStringAsync(
                cancellationToken)
            .ConfigureAwait(false);
        if (body.Length > MaximumErrorCharacters)
        {
            body = body[..MaximumErrorCharacters] + "…";
        }
        throw new YouTubePublishingException(
            message,
            diagnosticCode,
            $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
    }

    private static string FormatBytes(long value) =>
        value >= 1_000_000_000
            ? $"{value / 1_000_000_000d:0.0} GB"
            : value >= 1_000_000
                ? $"{value / 1_000_000d:0.0} MB"
                : $"{value / 1_000d:0.0} KB";

    private sealed class FileRangeHttpContent : HttpContent
    {
        private readonly string _path;
        private readonly long _offset;
        private readonly long _length;
        private readonly Action<long>? _progress;

        public FileRangeHttpContent(
            string path,
            long offset,
            long length,
            Action<long>? progress)
        {
            _path = path;
            _offset = offset;
            _length = length;
            _progress = progress;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await SerializeToStreamAsync(
                    stream,
                    context,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            await using var source = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                useAsync: true);
            source.Position = _offset;
            byte[] buffer = new byte[128 * 1024];
            long remaining = _length;
            long written = 0;
            while (remaining > 0)
            {
                int read = await source.ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "The video ended before the requested upload range.");
                }
                await stream.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken)
                    .ConfigureAwait(false);
                remaining -= read;
                written += read;
                _progress?.Invoke(written);
            }
        }
    }

    private sealed record UploadStatus(long Offset, string? VideoId);

    private sealed class ChannelListResponse
    {
        public ChannelItem[]? Items { get; set; }
    }

    private sealed class ChannelItem
    {
        public string Id { get; set; } = string.Empty;
        public Snippet? Snippet { get; set; }
    }

    private sealed class PlaylistListResponse
    {
        public PlaylistItem[]? Items { get; set; }
        public string? NextPageToken { get; set; }
    }

    private sealed class PlaylistItem
    {
        public string Id { get; set; } = string.Empty;
        public Snippet? Snippet { get; set; }
        public Status? Status { get; set; }
    }

    private sealed class CategoryListResponse
    {
        public CategoryItem[]? Items { get; set; }
    }

    private sealed class CategoryItem
    {
        public string Id { get; set; } = string.Empty;
        public CategorySnippet? Snippet { get; set; }
    }

    private sealed class CategorySnippet
    {
        public string Title { get; set; } = string.Empty;
        public bool Assignable { get; set; }
    }

    private sealed class Snippet
    {
        public string Title { get; set; } = string.Empty;
    }

    private sealed class Status
    {
        public string? PrivacyStatus { get; set; }
    }

    private sealed class VideoInsertResponse
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class VideoListResponse
    {
        public VideoIdentity[]? Items { get; set; }
    }

    private sealed class VideoIdentity
    {
        public string Id { get; set; } = string.Empty;
    }
}
