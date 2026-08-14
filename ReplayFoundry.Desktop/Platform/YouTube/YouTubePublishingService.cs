using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.YouTube;

internal sealed class YouTubePublishingService :
    IYouTubePublishingService,
    IDisposable
{
    private readonly IYouTubeAuthorizationService _authorization;
    private readonly IYouTubeDataApiClient _api;
    private readonly IYouTubePublishHistoryStore _history;
    private readonly IYouTubeConnectionPermission? _connectionPermission;
    private readonly IDisposable? _ownedResource;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private bool _disposed;

    public YouTubePublishingService(
        IYouTubeAuthorizationService authorization,
        IYouTubeDataApiClient api,
        IYouTubePublishHistoryStore history,
        IYouTubeConnectionPermission? connectionPermission = null,
        IDisposable? ownedResource = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(history);
        _authorization = authorization;
        _api = api;
        _history = history;
        _connectionPermission = connectionPermission;
        _ownedResource = ownedResource;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _publishGate.Dispose();
        (_authorization as IDisposable)?.Dispose();
        _ownedResource?.Dispose();
    }

    public bool IsConfigured => true;
    public IReadOnlyList<YouTubePublishHistoryEntry> History =>
        _history.Current;

    public async Task<YouTubeAccountConnection?> GetConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (_connectionPermission?.IsEnabled == false)
        {
            return null;
        }
        try
        {
            YouTubeAccessCredential? credential = await _authorization
                .GetAccessCredentialAsync(
                    forceRefresh: false,
                    cancellationToken)
                .ConfigureAwait(false);
            return credential is null
                ? null
                : await _api.GetChannelAsync(
                        credential.AccessToken,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            throw TranslateInfrastructureFailure(exception);
        }
    }

    public async Task<YouTubeAccountConnection> ConnectAsync(
        CancellationToken cancellationToken)
    {
        RequireConnectionPermission();
        try
        {
            YouTubeAccessCredential credential = await _authorization
                .ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            return await _api.GetChannelAsync(
                    credential.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (YouTubePublishingException)
        {
            try
            {
                await _authorization.DisconnectAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original channel-validation failure.
            }
            throw;
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            try
            {
                await _authorization.DisconnectAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // The original connect failure remains authoritative.
            }
            throw TranslateInfrastructureFailure(exception);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _authorization.DisconnectAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            throw TranslateInfrastructureFailure(exception);
        }
    }

    public async Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(
        CancellationToken cancellationToken)
    {
        RequireConnectionPermission();
        try
        {
            YouTubeAccessCredential credential = await RequireCredentialAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            return await _api.GetPlaylistsAsync(
                    credential.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            throw TranslateInfrastructureFailure(exception);
        }
    }

    public async Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(
        CancellationToken cancellationToken)
    {
        RequireConnectionPermission();
        try
        {
            YouTubeAccessCredential credential = await RequireCredentialAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            return await _api.GetCategoriesAsync(
                    credential.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            throw TranslateInfrastructureFailure(exception);
        }
    }

    public async Task<YouTubePublishResult> PublishAsync(
        YouTubePublishRequest request,
        IProgress<YouTubePublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireConnectionPermission();
        if (!await _publishGate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Only one YouTube upload can run at a time.");
        }

        string historyId = CreateHistoryId(request);
        try
        {
            YouTubeAccessCredential credential = await RequireCredentialAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            string videoId = await _api.UploadVideoAsync(
                    credential.AccessToken,
                    request,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            var warnings = new List<string>();
            bool thumbnailApplied = false;
            if (request.ThumbnailFullPath is not null)
            {
                progress?.Report(new YouTubePublishProgress(
                    YouTubePublishPhase.SettingThumbnail,
                    "Setting the thumbnail",
                    "The video is uploaded; Replay Foundry is applying your custom thumbnail.",
                    0,
                    null));
                try
                {
                    await _api.SetThumbnailAsync(
                            credential.AccessToken,
                            videoId,
                            request.ThumbnailFullPath,
                            cancellationToken)
                        .ConfigureAwait(false);
                    thumbnailApplied = true;
                }
                catch (YouTubePublishingException exception)
                {
                    warnings.Add(exception.Message);
                }
            }
            if (request.PlaylistId is not null)
            {
                progress?.Report(new YouTubePublishProgress(
                    YouTubePublishPhase.AddingToPlaylist,
                    "Adding the playlist",
                    "The video is uploaded; Replay Foundry is adding it to your selected playlist.",
                    0,
                    null));
                try
                {
                    await _api.AddToPlaylistAsync(
                            credential.AccessToken,
                            videoId,
                            request.PlaylistId,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (YouTubePublishingException exception)
                {
                    warnings.Add(exception.Message);
                }
            }

            YouTubePublishOutcome outcome = request.Timing switch
            {
                YouTubePublishTiming.Schedule =>
                    YouTubePublishOutcome.Scheduled,
                _ when request.Visibility == YouTubeVideoVisibility.Public =>
                    YouTubePublishOutcome.Published,
                _ when request.Visibility == YouTubeVideoVisibility.Unlisted =>
                    YouTubePublishOutcome.UploadedUnlisted,
                _ => YouTubePublishOutcome.UploadedPrivate,
            };
            var result = new YouTubePublishResult(
                videoId,
                "https://youtu.be/" + Uri.EscapeDataString(videoId),
                outcome,
                request.Visibility,
                DateTimeOffset.UtcNow,
                request.ScheduledForUtc,
                request.PlaylistId,
                thumbnailApplied,
                warnings);
            _history.Append(new YouTubePublishHistoryEntry(
                historyId,
                request.Asset.Id,
                request.Title,
                result.VideoId,
                result.VideoUrl,
                result.Outcome,
                result.Visibility,
                result.CompletedAtUtc,
                result.ScheduledForUtc));
            progress?.Report(new YouTubePublishProgress(
                YouTubePublishPhase.Completed,
                outcome == YouTubePublishOutcome.Scheduled
                    ? "YouTube release scheduled"
                    : "YouTube upload complete",
                warnings.Count == 0
                    ? "The video and requested metadata were accepted by YouTube."
                    : "The video was accepted with follow-up warnings.",
                request.Asset.OutputFullPath is null
                    ? 0
                    : new FileInfo(request.Asset.OutputFullPath).Length,
                request.Asset.OutputFullPath is null
                    ? null
                    : new FileInfo(request.Asset.OutputFullPath).Length));
            return result;
        }
        catch (OperationCanceledException)
        {
            _history.Append(new YouTubePublishHistoryEntry(
                historyId,
                request.Asset.Id,
                request.Title,
                videoId: null,
                videoUrl: null,
                YouTubePublishOutcome.Cancelled,
                request.Visibility,
                DateTimeOffset.UtcNow,
                request.ScheduledForUtc,
                "youtube.publish.cancelled",
                "The upload was cancelled before Replay Foundry received a completed video."));
            throw;
        }
        catch (YouTubePublishingException exception)
        {
            _history.Append(new YouTubePublishHistoryEntry(
                historyId,
                request.Asset.Id,
                request.Title,
                videoId: null,
                videoUrl: null,
                YouTubePublishOutcome.Failed,
                request.Visibility,
                DateTimeOffset.UtcNow,
                request.ScheduledForUtc,
                exception.DiagnosticCode,
                exception.Message));
            throw;
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            YouTubePublishingException translated =
                TranslateInfrastructureFailure(exception);
            _history.Append(new YouTubePublishHistoryEntry(
                historyId,
                request.Asset.Id,
                request.Title,
                videoId: null,
                videoUrl: null,
                YouTubePublishOutcome.Failed,
                request.Visibility,
                DateTimeOffset.UtcNow,
                request.ScheduledForUtc,
                translated.DiagnosticCode,
                translated.Message));
            throw translated;
        }
        finally
        {
            _publishGate.Release();
        }
    }

    public void ClearHistory() => _history.Clear();

    public async Task<int> ReconcileHistoryAsync(
        CancellationToken cancellationToken)
    {
        RequireConnectionPermission();
        if (!await _publishGate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "YouTube history cannot be checked while another publish operation is active.");
        }
        try
        {
            YouTubeAccessCredential credential = await RequireCredentialAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            YouTubePublishHistoryEntry[] original = _history.Current.ToArray();
            string[] ids = original
                .Select(static entry => entry.VideoId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var existing = new HashSet<string>(StringComparer.Ordinal);
            for (int offset = 0; offset < ids.Length; offset += 50)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] batch = ids.Skip(offset).Take(50).ToArray();
                IReadOnlySet<string> result = await _api.GetExistingVideoIdsAsync(
                        credential.AccessToken,
                        batch,
                        cancellationToken)
                    .ConfigureAwait(false);
                existing.UnionWith(result);
            }

            DateTimeOffset checkedAtUtc = DateTimeOffset.UtcNow;
            YouTubePublishHistoryEntry[] updated = original
                .Select(entry => entry.VideoId is null
                    ? entry
                    : entry.WithRemoteStatus(
                        existing.Contains(entry.VideoId)
                            ? YouTubeRemoteVideoStatus.Exists
                            : YouTubeRemoteVideoStatus.NotFoundOrInaccessible,
                        checkedAtUtc))
                .ToArray();
            _history.Replace(updated);
            return updated.Count(static entry =>
                entry.RemoteStatus ==
                YouTubeRemoteVideoStatus.NotFoundOrInaccessible);
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            throw TranslateInfrastructureFailure(exception);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private void RequireConnectionPermission()
    {
        if (_connectionPermission?.IsEnabled == false)
        {
            throw new YouTubePublishingException(
                "Enable YouTube connections in Settings before using this feature.",
                "youtube.connection.disabled");
        }
    }

    private async Task<YouTubeAccessCredential> RequireCredentialAsync(
        CancellationToken cancellationToken) =>
        await _authorization.GetAccessCredentialAsync(
                forceRefresh: false,
                cancellationToken)
            .ConfigureAwait(false) ??
        throw new YouTubePublishingException(
            "Connect a YouTube channel before publishing.",
            "youtube.oauth.connection-required");

    private static string CreateHistoryId(YouTubePublishRequest request)
    {
        string value = string.Join(
            '|',
            request.Asset.Id,
            request.Title,
            request.CreatedAtUtc.ToString("O"));
        return "yt-" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24]
            .ToLowerInvariant();
    }

    private static bool IsInfrastructureFailure(Exception exception) =>
        exception is HttpRequestException or
            IOException or
            InvalidDataException or
            UnauthorizedAccessException or
            Win32Exception or
            JsonException;

    private static YouTubePublishingException TranslateInfrastructureFailure(
        Exception exception) =>
        new(
            exception is HttpRequestException
                ? "Replay Foundry could not reach Google or YouTube. Check your connection and try again."
                : "Replay Foundry could not access the local YouTube publishing data.",
            exception is HttpRequestException
                ? "youtube.network.request-failed"
                : "youtube.local-storage.failed",
            exception.Message,
            exception);
}
