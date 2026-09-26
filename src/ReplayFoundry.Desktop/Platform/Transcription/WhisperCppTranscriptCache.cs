using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Transcription;

internal sealed record WhisperCppCachedTranscript(string Key, string Json, string Output, string Error,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, TimeSpan Duration);

internal sealed class WhisperCppTranscriptCache
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    private readonly string _directory = ReplayFoundryLocalDataPaths.Resolve(null, "Cache/SourceSpeech");

    public static async Task<string> KeyAsync(AudioTranscriptionRequest request, string modelHash, string executableHash,
        IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken)
    {
        await using var audio = File.OpenRead(request.InputAudioPath);
        string audioHash = Convert.ToHexString(await SHA256.HashDataAsync(audio, cancellationToken));
        string identity = JsonSerializer.Serialize(new { version = 1, audioHash, modelHash, executableHash,
            request.InputDuration, request.AbsoluteSourceOffset, request.SourceDuration, request.AbsoluteAudioStreamIndex,
            options = options.OrderBy(value => value.Key, StringComparer.Ordinal) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public async Task<WhisperCppCachedTranscript?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(_directory, key + ".json");
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes) return null;
            var entry = JsonSerializer.Deserialize<WhisperCppCachedTranscript>(await File.ReadAllTextAsync(path, cancellationToken));
            return entry is { Json: not null, Output: not null, Error: not null } && entry.Key == key &&
                entry.Duration >= TimeSpan.Zero && entry.CompletedAtUtc >= entry.StartedAtUtc ? entry : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public async Task SaveAsync(WhisperCppCachedTranscript entry, CancellationToken cancellationToken)
    {
        string path = Path.Combine(_directory, entry.Key + ".json");
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry);
            if (bytes.Length > MaximumBytes) return;
            Directory.CreateDirectory(_directory);
            await File.WriteAllBytesAsync(pending, bytes, cancellationToken);
            File.Move(pending, path, true);
            long retained = 0;
            foreach (var file in new DirectoryInfo(_directory).EnumerateFiles("*.json")
                .Where(file => file.Name.Length == 69 && file.Name[..64].All(Uri.IsHexDigit))
                .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                retained += file.Length;
                if (retained > 256L * 1024 * 1024 && !file.FullName.Equals(path, StringComparison.OrdinalIgnoreCase)) file.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(pending)) File.Delete(pending); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
