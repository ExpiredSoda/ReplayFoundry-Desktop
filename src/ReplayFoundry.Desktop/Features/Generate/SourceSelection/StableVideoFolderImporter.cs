using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

public sealed record StableVideoFolderImport(IReadOnlyList<string> ReadyFiles, IReadOnlyList<string> SkippedFiles);
public sealed class StableVideoFolderImporter
{
    public async Task<StableVideoFolderImport> ScanAsync(string folder, CancellationToken cancellationToken,
        TimeSpan? stabilityWindow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string root = Path.GetFullPath(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing recording folder.");
        var validator = new VideoSourceValidator();
        var before = new Dictionary<string, (long Length, DateTime Modified)>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();
        string[] files = Directory.EnumerateFiles(root).Take(1001).ToArray();
        if (files.Length > 1000) throw new InvalidOperationException("Choose a folder containing at most 1,000 files.");
        foreach (string path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!validator.TryValidate(path, out _, out _)) continue;
            try
            {
                var file = new FileInfo(path);
                if (file.Length == 0 || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) { skipped.Add(path); continue; }
                before[path] = (file.Length, file.LastWriteTimeUtc);
            }
            catch (IOException) { skipped.Add(path); }
            catch (UnauthorizedAccessException) { skipped.Add(path); }
        }
        TimeSpan delay = stabilityWindow ?? TimeSpan.FromSeconds(2);
        if (delay < TimeSpan.Zero || delay > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(stabilityWindow));
        await Task.Delay(delay, cancellationToken);
        var ready = new List<string>();
        foreach (var pair in before)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Exclusive read prevents admitting a recording that still has an
                // active writer even when its size temporarily stops changing.
                using var file = new FileStream(pair.Key, FileMode.Open, FileAccess.Read, FileShare.None);
                if (file.Length != pair.Value.Length || File.GetLastWriteTimeUtc(pair.Key) != pair.Value.Modified)
                    skipped.Add(pair.Key);
                else ready.Add(pair.Key);
            }
            catch (IOException) { skipped.Add(pair.Key); }
            catch (UnauthorizedAccessException) { skipped.Add(pair.Key); }
        }
        return new(ready.Order(StringComparer.OrdinalIgnoreCase).ToArray(), skipped);
    }
}
