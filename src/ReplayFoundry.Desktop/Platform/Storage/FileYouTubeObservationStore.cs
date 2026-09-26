using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Platform.Storage;

internal sealed class FileYouTubeObservationStore : IYouTubeObservationStore
{
    private readonly string _path;

    internal FileYouTubeObservationStore(string? path) => _path = ReplayFoundryLocalDataPaths.Resolve(
        path, Path.Combine("Analytics", "youtube-observations.json"));

    public YouTubeSavedObservations.LoadResult Load() => File.Exists(_path) && new FileInfo(_path).Length <= 4 * 1024 * 1024
        ? YouTubeSavedObservations.Parse(File.ReadAllText(_path))
        : new([], 0);

    public async Task SaveAsync(IReadOnlyList<YouTubeVideoObservation> observations, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(observations), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
