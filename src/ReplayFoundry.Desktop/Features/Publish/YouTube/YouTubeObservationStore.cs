using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

internal interface IYouTubeObservationStore
{
    YouTubeSavedObservations.LoadResult Load();
    Task SaveAsync(IReadOnlyList<YouTubeVideoObservation> observations, CancellationToken cancellationToken);
    void Clear();
}

internal static class YouTubeObservationStoreFactory
{
    internal static IYouTubeObservationStore Create(string? path) => new FileYouTubeObservationStore(path);
}
