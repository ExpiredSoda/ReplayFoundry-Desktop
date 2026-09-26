using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Media;

/// <summary>Only fully encoded and validated segments enter the resumable cache.</summary>
internal sealed class StudioRenderCheckpointCache
{
    private static readonly object Gate = new();
    private readonly string _root;
    private readonly long _maximumBytes;
    internal StudioRenderCheckpointCache(string? root = null, long maximumBytes = 5L * 1024 * 1024 * 1024)
    {
        _root = ReplayFoundryLocalDataPaths.Resolve(root, Path.Combine("Cache", "StudioRenders"));
        _maximumBytes = maximumBytes > 0 ? maximumBytes : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
    }
    internal static string CreateKey(FfmpegClipRenderCommand command, string executable)
    {
        var canonical = new StringBuilder("studio-render-checkpoint-2\n");
        canonical.AppendJoin('\n', command.Arguments.Take(command.Arguments.Count - 1));
        foreach (string input in command.Arguments.Where(File.Exists).Append(executable).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(input);
            canonical.Append('\n').Append(info.FullName).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
        }
        foreach (string argument in command.Arguments)
            foreach (Match match in Regex.Matches(argument, "ass=filename='([^']+)'"))
                canonical.Append('\n').Append(File.ReadAllText(Path.Combine(command.WorkingDirectory!, match.Groups[1].Value)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
    internal Task<bool> TryRestoreAsync(string key, string destination, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            string root = Entry(key), video = Path.Combine(root, "segment.mp4"), receipt = Path.Combine(root, "sha256.txt");
            if (!File.Exists(video) || !File.Exists(receipt))
            {
                DiscardEntry(root);
                return false;
            }
            string temporary = destination + ".restoring-" + Guid.NewGuid().ToString("N");
            try
            {
                bool intact;
                using (FileStream stream = File.OpenRead(video))
                    intact = Convert.ToHexString(SHA256.HashData(stream)).Equals(File.ReadAllText(receipt), StringComparison.Ordinal);
                if (!intact)
                {
                    DiscardEntry(root);
                    return false;
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(video, temporary, overwrite: false);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.SetLastWriteTimeUtc(root, DateTime.UtcNow);
                File.Move(temporary, destination, overwrite: false);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }, cancellationToken);

    internal Task StoreAsync(string key, string source, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            string destination = Entry(key);
            if (Directory.Exists(destination)) return;
            Directory.CreateDirectory(_root);
            string staging = Path.Combine(_root, ".writing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                string video = Path.Combine(staging, "segment.mp4");
                File.Copy(source, video);
                using (FileStream stream = File.OpenRead(video))
                    File.WriteAllText(Path.Combine(staging, "sha256.txt"), Convert.ToHexString(SHA256.HashData(stream)));
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(staging, destination);
                Prune();
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
    }, cancellationToken);

    private string Entry(string key)
    {
        if (!Regex.IsMatch(key, "^[A-F0-9]{64}$")) throw new ArgumentException("Invalid checkpoint identity.", nameof(key));
        return Path.Combine(_root, key);
    }
    private static void DiscardEntry(string entry)
    {
        // The caller supplies only Entry(key), where key is a validated SHA-256 name.
        try { if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void Prune()
    {
        var entries = new DirectoryInfo(_root).EnumerateDirectories()
            .Where(directory => !directory.Name.StartsWith(".", StringComparison.Ordinal))
            .Select(directory => (Directory: directory, Bytes: directory.EnumerateFiles().Sum(file => file.Length)))
            .OrderBy(entry => entry.Directory.LastWriteTimeUtc).ToArray();
        long total = entries.Sum(entry => entry.Bytes);
        foreach (var entry in entries)
        {
            if (total <= _maximumBytes) break;
            entry.Directory.Delete(true); total -= entry.Bytes;
        }
    }
}
