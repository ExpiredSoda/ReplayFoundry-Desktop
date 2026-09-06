using ReplayFoundry.Desktop.Features.Studio.Editing;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.Transcription;

/// <summary>CPU-only English forced alignment. A phrase is bounded to 30 seconds and two inference threads.</summary>
public sealed class OnnxCorrectedCaptionAlignmentService : ICorrectedCaptionAlignmentService
{
    public const string ModelIdentity = "Xenova/wav2vec2-base-960h INT8 @ a19f851b3d42865797e410752b4c570c871e4825";
    public const string ModelSha256 = "CD5040C147381580ED73258143DD8E0C28E800A09E74EE42EE2B3E8CB4D760A3";
    private const long ModelBytes = 95_286_046;
    private const string ModelUrl = "https://huggingface.co/Xenova/wav2vec2-base-960h/resolve/a19f851b3d42865797e410752b4c570c871e4825/onnx/model_quantized.onnx";
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);
    private readonly IAudioSegmentExtractor _extractor;
    private readonly string _modelPath;

    public OnnxCorrectedCaptionAlignmentService(IAudioSegmentExtractor extractor, string? modelPath = null)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _modelPath = ReplayFoundryLocalDataPaths.Resolve(modelPath, Path.Combine("Models", "CaptionAlignment", "wav2vec2-base-960h-int8.onnx"));
    }

    public async Task<CorrectedCaptionAlignmentResult> AlignAsync(CorrectedCaptionAlignmentRequest request,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Corrected-text alignment currently supports English speech. Choose English for this phrase.");
        if (string.IsNullOrWhiteSpace(request.SourceFullPath) || !Path.IsPathFullyQualified(request.SourceFullPath) ||
            request.SourceStart < TimeSpan.Zero || request.SourceEnd <= request.SourceStart || request.SourceEnd > request.SourceDuration ||
            request.SourceEnd - request.SourceStart > TimeSpan.FromSeconds(30) || request.AbsoluteAudioStreamIndex < 0 ||
            string.IsNullOrWhiteSpace(request.CorrectedText) || request.CorrectedText.Length > 1000)
            throw new ArgumentException("Choose an English phrase of at most 30 seconds and 1,000 characters inside the recording.");
        cancellationToken.ThrowIfCancellationRequested();
        var sourceBefore = new FileInfo(request.SourceFullPath);
        if (!sourceBefore.Exists) throw new FileNotFoundException("The recording is unavailable.", request.SourceFullPath);
        long sourceLength = sourceBefore.Length;
        DateTime sourceWrite = sourceBefore.LastWriteTimeUtc;
        var timer = Stopwatch.StartNew();
        await EnsureModelAsync(progress, cancellationToken).ConfigureAwait(false);
        progress?.Report("Extracting the selected phrase and audio track for alignment.");
        using var audio = await _extractor.ExtractAsync(new AudioSegmentExtractionRequest("caption-realignment",
            request.SourceFullPath, request.SourceDuration, request.AbsoluteAudioStreamIndex, request.SourceStart, request.SourceEnd,
            TimeSpan.FromSeconds(60)), cancellationToken).ConfigureAwait(false);
        string pcmHash;
        await using (var input = File.OpenRead(audio.Path))
            pcmHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        progress?.Report("Aligning corrected English words on CPU. Your text is preserved.");
        using var admission = await MediaWorkBudget.AcquireAsync(cancellationToken, MediaWorkPriority.Foreground, MediaWorkKind.HeavyAi).ConfigureAwait(false);
        IReadOnlyList<CtcAlignedCaptionWord> aligned;
        try { aligned = await Task.Run(() => AlignWave(audio.Path, request.CorrectedText, cancellationToken), cancellationToken).ConfigureAwait(false); }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken); }
        var sourceAfter = new FileInfo(request.SourceFullPath);
        if (!sourceAfter.Exists || sourceAfter.Length != sourceLength || sourceAfter.LastWriteTimeUtc != sourceWrite)
            throw new IOException("The recording changed during alignment. Existing timings were kept.");
        int weak = aligned.Count(static word => word.AcousticScore < .15);
        return new(aligned.Select(static word => new CorrectedCaptionAlignedWord(word.Text,
            TimeSpan.FromSeconds(word.StartSeconds), TimeSpan.FromSeconds(word.EndSeconds), word.AcousticScore)).ToArray(),
            ModelIdentity, ModelSha256, timer.Elapsed,
            weak == 0 ? "Audio-aligned timing proposed. Listen and review before saving; alignment does not verify the words were spoken." :
                $"Audio-aligned timing proposed. {weak} of {aligned.Count} words have weak acoustic matches; review them before saving. Scores are not correctness probabilities.", pcmHash);
    }

    private IReadOnlyList<CtcAlignedCaptionWord> AlignWave(string wavePath, string text, CancellationToken cancellationToken)
    {
        using var reader = new PcmWaveFileReader(wavePath);
        if (reader.SampleRate != 16000 || reader.Pcm16SampleCount is < 400 or > 480000)
            throw new InvalidDataException("Alignment audio must be mono 16 kHz PCM and at most 30 seconds.");
        float[] samples = new float[checked((int)reader.Pcm16SampleCount)];
        if (reader.ReadNormalizedMonoSamples(samples) != samples.Length) throw new InvalidDataException("Alignment audio is incomplete.");
        double mean = samples.Average(static sample => (double)sample);
        double variance = samples.Average(sample => (sample - mean) * (sample - mean));
        if (variance < 1e-8) throw new InvalidOperationException("This phrase contains too little audible signal to align words. Existing timings were kept.");
        double scale = Math.Sqrt(variance + 1e-7);
        for (int index = 0; index < samples.Length; index++) samples[index] = (float)((samples[index] - mean) / scale);
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        using var session = new InferenceSession(_modelPath, options);
        using var run = new RunOptions();
        using var cancellation = cancellationToken.Register(() => run.Terminate = true);
        cancellationToken.ThrowIfCancellationRequested();
        using var outputs = session.Run([NamedOnnxValue.CreateFromTensor("input_values", new DenseTensor<float>(samples, [1, samples.Length]))],
            ["logits"], run);
        cancellationToken.ThrowIfCancellationRequested();
        Tensor<float> logits = outputs.Single().AsTensor<float>();
        if (logits.Dimensions.Length != 3 || logits.Dimensions[0] != 1 || logits.Dimensions[2] != 32)
            throw new InvalidDataException("Alignment model returned an unexpected emission shape.");
        IReadOnlyList<CtcAlignedCaptionWord> aligned = CtcCaptionAlignment.Align(logits.ToArray(), logits.Dimensions[1], samples.Length, text, cancellationToken);
        if (aligned.All(static word => word.AcousticScore < .02))
            throw new InvalidOperationException("The supplied words did not obtain a usable acoustic match. Existing timings were kept.");
        return aligned;
    }

    private async Task EnsureModelAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        cancellationToken = timeout.Token;
        await DownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (await MatchesModelAsync(_modelPath, cancellationToken).ConfigureAwait(false))
            { await WriteNoticesAsync(cancellationToken).ConfigureAwait(false); return; }
            if (File.Exists(_modelPath)) throw new InvalidDataException("The alignment model failed its integrity check. Remove the cached alignment model and try again.");
            progress?.Report("Downloading the English alignment model (91 MiB). Audio and captions stay on this computer.");
            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
            temporary = _modelPath + ".download-" + Guid.NewGuid().ToString("N");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long declared && declared != ModelBytes)
                throw new InvalidDataException("The alignment model download has an unexpected length.");
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long received = 0; int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > ModelBytes) throw new InvalidDataException("The alignment model download exceeds its expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
            }
            if (!await MatchesModelAsync(temporary, cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("The alignment model download did not match its pinned hash.");
            File.Move(temporary, _modelPath, overwrite: false); temporary = null;
            await WriteNoticesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch (IOException) { } }
            DownloadGate.Release();
        }
    }

    private static async Task<bool> MatchesModelAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != ModelBytes) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)) == ModelSha256;
    }

    private async Task WriteNoticesAsync(CancellationToken cancellationToken)
    {
        foreach (string name in new[] { "LICENSE", "NOTICE" })
        {
            string destination = Path.Combine(Path.GetDirectoryName(_modelPath)!, $"Wav2Vec2-{name}.txt");
            if (File.Exists(destination)) continue;
            using Stream source = typeof(OnnxCorrectedCaptionAlignmentService).Assembly.GetManifestResourceStream($"ReplayFoundry.CaptionAlignment.{name}.txt")
                ?? throw new InvalidOperationException("The alignment model notice is missing from this application build.");
            using var reader = new StreamReader(source);
            await File.WriteAllTextAsync(destination, await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
    }
}
