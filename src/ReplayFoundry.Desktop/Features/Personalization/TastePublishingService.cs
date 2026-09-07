using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Personalization;

internal sealed class TastePublishingService(IYouTubePublishingService inner, TasteInteractionRecorder recorder) : IYouTubePublishingService, IDisposable
{
    public bool IsConfigured => inner.IsConfigured;
    public IReadOnlyList<YouTubePublishHistoryEntry> History => inner.History;
    public Task<YouTubeAccountConnection?> GetConnectionAsync(CancellationToken cancellationToken) => inner.GetConnectionAsync(cancellationToken);
    public Task<YouTubeAccountConnection> ConnectAsync(CancellationToken cancellationToken) => inner.ConnectAsync(cancellationToken);
    public Task DisconnectAsync(CancellationToken cancellationToken) => inner.DisconnectAsync(cancellationToken);
    public Task<IReadOnlyList<YouTubePlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken) => inner.GetPlaylistsAsync(cancellationToken);
    public Task<IReadOnlyList<YouTubeVideoCategory>> GetCategoriesAsync(CancellationToken cancellationToken) => inner.GetCategoriesAsync(cancellationToken);
    public async Task<YouTubePublishResult> PublishAsync(YouTubePublishRequest request, IProgress<YouTubePublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = await inner.PublishAsync(request, progress, cancellationToken).ConfigureAwait(false);
        recorder.Published(request.Asset, result.Outcome);
        return result;
    }
    public async Task<int> ReconcileHistoryAsync(CancellationToken cancellationToken)
    {
        int count = await inner.ReconcileHistoryAsync(cancellationToken).ConfigureAwait(false);
        recorder.ImportPublishHistory(inner.History); return count;
    }
    public void ClearHistory() => inner.ClearHistory();
    public void Dispose() { if (inner is IDisposable disposable) disposable.Dispose(); }
}
