namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

public interface IYouTubePublishingService
{
    bool IsConfigured { get; }
    IReadOnlyList<YouTubePublishHistoryEntry> History { get; }

    Task<YouTubeAccountConnection?> GetConnectionAsync(
        CancellationToken cancellationToken);

    Task<YouTubeAccountConnection> ConnectAsync(
        CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(
        CancellationToken cancellationToken);

    Task<YouTubePublishResult> PublishAsync(
        YouTubePublishRequest request,
        IProgress<YouTubePublishProgress>? progress,
        CancellationToken cancellationToken);

    Task<int> ReconcileHistoryAsync(
        CancellationToken cancellationToken);

    void ClearHistory();
}
