using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Media;

// Only analysis passes use this cache. It cannot satisfy an export or a tool repair.
internal sealed class FfmpegEvidenceCache(string? directory = null)
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    private readonly string _directory = directory ?? ReplayFoundryLocalDataPaths.Resolve(null, "Cache/MediaEvidence");
    private sealed record Entry(string Key, string Output, string Error);

    public async Task<ProcessRunResult> RunAsync(string source, ProcessRunRequest request,
        Func<Task<ProcessRunResult>> run, CancellationToken cancellationToken)
    {
        string key;
        string stamp;
        try
        {
            stamp = Stamp(source);
            string sourceHash = await HashAsync(source, cancellationToken);
            string toolHash = await HashAsync(request.ExecutablePath, cancellationToken);
            string contract = JsonSerializer.Serialize(new { version = 1, sourceHash, toolHash, request.Arguments,
                environment = request.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.Ordinal) });
            key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract)));
        }
        catch (IOException) { return await run(); }
        catch (UnauthorizedAccessException) { return await run(); }

        string path = Path.Combine(_directory, key + ".json.gz");
        var timer = Stopwatch.StartNew();
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= MaximumBytes)
            {
                await using var file = File.OpenRead(path);
                await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[65536];
                int count;
                while ((count = await gzip.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("Analysis cache exceeds its size bound.");
                    buffer.Write(chunk, 0, count);
                }
                var entry = JsonSerializer.Deserialize<Entry>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
                if (entry is { Output: not null, Error: not null } && entry.Key == key && entry.Output.Length <= request.MaxStandardOutputCharacters &&
                    entry.Error.Length <= request.MaxStandardErrorCharacters && IsUnchanged(source, stamp))
                    return new(0, entry.Output, entry.Error, timer.Elapsed);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }

        var result = await run();
        if (!result.Succeeded || !IsUnchanged(source, stamp)) return result;
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Entry(key, result.StandardOutput, result.StandardError));
            if (bytes.Length > MaximumBytes) return result;
            Directory.CreateDirectory(_directory);
            await using (var file = File.Create(pending))
            await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                await gzip.WriteAsync(bytes, cancellationToken);
            File.Move(pending, path, true);
            long retainedBytes = 0;
            foreach (var file in new DirectoryInfo(_directory).EnumerateFiles("*.json.gz")
                .Where(file => file.Name.Length == 72 && file.Name[..64].All(Uri.IsHexDigit))
                .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                retainedBytes += file.Length;
                if (retainedBytes > 512L * 1024 * 1024 && !file.FullName.Equals(path, StringComparison.OrdinalIgnoreCase))
                    file.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(pending)) File.Delete(pending); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private static bool IsUnchanged(string path, string stamp)
    {
        try { return Stamp(path) == stamp; }
        catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
    }

    private static string Stamp(string path)
    {
        var file = new FileInfo(path);
        return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
    }
}
