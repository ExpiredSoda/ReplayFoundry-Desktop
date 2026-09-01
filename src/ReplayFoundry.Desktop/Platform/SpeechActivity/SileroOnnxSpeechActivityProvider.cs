using System.Diagnostics;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;

namespace ReplayFoundry.Desktop.Platform.SpeechActivity;

public sealed class SileroOnnxSpeechActivityProvider :
    ISpeechActivityProvider,
    IDisposable
{
    private readonly object _sessionGate = new();
    private SileroOnnxModelSession? _modelSession;
    private readonly string _modelPath;
    private bool _disposed;

    public SileroOnnxSpeechActivityProvider(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) ||
            !Path.IsPathFullyQualified(modelPath) ||
            !File.Exists(modelPath))
        {
            throw new ArgumentException(
                "Silero VAD requires an explicit existing ONNX model path.",
                nameof(modelPath));
        }

        _modelPath = Path.GetFullPath(modelPath);
        Model = CreateModelManifest(_modelPath);
    }

    public InferenceProviderIdentity Identity { get; } =
        new(
            "Silero VAD",
            "6.2.1",
            "ReplayFoundry.SileroOnnx/1.0.0");

    public ModelArtifactManifest Model { get; }

    public Task<SpeechActivityResult> AnalyzeAsync(
        SpeechActivityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!request.Model.Path.Equals(
                _modelPath,
                StringComparison.OrdinalIgnoreCase) ||
            !request.Model.Sha256.Equals(
                Model.Sha256,
                StringComparison.Ordinal))
        {
            throw new SpeechActivityProviderException(
                "The speech-activity request does not use the configured Silero model.");
        }

        return Task.Run(
            () => AnalyzeCore(request, cancellationToken),
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sessionGate)
        {
            _modelSession?.Dispose();
            _modelSession = null;
        }
    }

    private SpeechActivityResult AnalyzeCore(
        SpeechActivityRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<SpeechActivityWarning>();

        try
        {
            using var wave = new Pcm16MonoWaveReader(request.AudioPath);
            SileroProbabilitySeries series = GetOrCreateSession().Analyze(
                wave,
                request.Options.ProcessTimeout,
                cancellationToken);
            if (series.PaddedTail)
            {
                warnings.Add(
                    new SpeechActivityWarning(
                        SpeechActivityWarningCode.TrailingAudioPadded,
                        "The final partial 32 ms audio window was padded with digital silence for deterministic inference."));
            }

            IReadOnlyList<SpeechSampleRange> ranges =
                SileroSpeechRangeDetector.Detect(
                    series.Probabilities,
                    series.TotalSamples,
                    request.Options,
                    warnings,
                    cancellationToken);
            SpeechActivityInterval[] intervals = ranges
                .Select(range => SileroSpeechRangeDetector.CreateInterval(
                    range,
                    series.Probabilities,
                    request,
                    series.TotalSamples))
                .ToArray();
            if (intervals.Length == 0)
            {
                warnings.Add(
                    new SpeechActivityWarning(
                        SpeechActivityWarningCode.NoSpeechDetected,
                        "Silero VAD did not detect speech above the configured hysteresis policy."));
            }

            stopwatch.Stop();
            var manifest = new SpeechActivityExecutionManifest(
                Identity,
                request.Model,
                "Microsoft.ML.OnnxRuntime",
                typeof(InferenceSession).Assembly.GetName().Version?.ToString() ?? "unknown",
                "CPUExecutionProvider",
                request.Options.ToNormalizedValues(),
                startedAtUtc,
                DateTimeOffset.UtcNow,
                stopwatch.Elapsed,
                warnings);

            return new SpeechActivityResult(
                request,
                intervals,
                manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SpeechActivityProviderException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  IOException or
                  InvalidDataException or
                  OnnxRuntimeException or
                  UnauthorizedAccessException)
        {
            throw new SpeechActivityProviderException(
                "Silero VAD could not analyze the extracted audio stream.",
                exception.Message,
                exception);
        }
    }

    private SileroOnnxModelSession GetOrCreateSession()
    {
        lock (_sessionGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _modelSession ??=
                new SileroOnnxModelSession(_modelPath);
        }
    }

    private static ModelArtifactManifest CreateModelManifest(string path)
    {
        var file = new FileInfo(path);
        return new ModelArtifactManifest(
            "Silero VAD v6.2.1",
            path,
            ModelArtifactManifest.ComputeSha256(path),
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            "ONNX opset 16",
            "MIT",
            "https://github.com/snakers4/silero-vad/releases/tag/v6.2.1",
            "Speech activity only; no language, speaker, emotion, or semantic role inference.");
    }
}
