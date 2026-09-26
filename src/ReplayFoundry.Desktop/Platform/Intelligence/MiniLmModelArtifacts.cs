using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Intelligence;

internal sealed class MiniLmModelArtifacts
{
    internal const string Revision = "751bff37182d3f1213fa05d7196b954e230abad9";
    internal const string Identity = "Xenova/all-MiniLM-L6-v2 INT8 @ " + Revision;
    internal const string ModelSha256 = "AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1";
    internal const string VocabularySha256 = "07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3";
    private const string BaseUrl = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/" + Revision + "/";
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);
    private readonly string _directory;
    internal string ModelPath => Path.Combine(_directory, "model_quantized.onnx");
    internal string VocabularyPath => Path.Combine(_directory, "vocab.txt");

    internal MiniLmModelArtifacts(string? directory = null) =>
        _directory = ReplayFoundryLocalDataPaths.Resolve(directory, Path.Combine("Models", "SemanticSearch", Revision));

    internal async Task EnsureAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await DownloadGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            await EnsureFileAsync(ModelPath, "onnx/model_quantized.onnx", 22_972_370, ModelSha256, progress, timeout.Token).ConfigureAwait(false);
            await EnsureFileAsync(VocabularyPath, "vocab.txt", 231_508, VocabularySha256, progress, timeout.Token).ConfigureAwait(false);
            foreach (string name in new[] { "LICENSE", "NOTICE" })
            {
                using Stream source = typeof(MiniLmModelArtifacts).Assembly.GetManifestResourceStream($"ReplayFoundry.SemanticSearch.{name}.txt")
                    ?? throw new InvalidOperationException("The semantic search model notice is missing from this application build.");
                using var reader = new StreamReader(source);
                await File.WriteAllTextAsync(Path.Combine(_directory, $"MiniLM-{name}.txt"),
                    await reader.ReadToEndAsync(timeout.Token).ConfigureAwait(false), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Semantic search model preparation timed out. Try again when the download is available."); }
        finally { DownloadGate.Release(); }
    }

    private static async Task EnsureFileAsync(string path, string relativeUrl, long bytes, string sha256,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            if (!await MatchesAsync(path, bytes, sha256, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("A cached semantic search model file failed its pinned integrity check. Remove that model cache and try again.");
            return;
        }
        progress?.Report("Downloading the English semantic search model (22 MiB, once). Transcript text stays on this computer.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await client.GetAsync(BaseUrl + relativeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long declared && declared != bytes)
                throw new InvalidDataException("A semantic search model download has an unexpected length.");
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long received = 0; int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > bytes) throw new InvalidDataException("A semantic search model download exceeds its expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
            }
            if (!await MatchesAsync(temporary, bytes, sha256, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("A semantic search model download did not match its pinned hash.");
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<bool> MatchesAsync(string path, long bytes, string sha256, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != bytes) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)) == sha256;
    }
}
