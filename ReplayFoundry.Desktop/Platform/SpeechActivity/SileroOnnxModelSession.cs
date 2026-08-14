using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;

namespace ReplayFoundry.Desktop.Platform.SpeechActivity;

internal sealed record SileroProbabilitySeries(
    IReadOnlyList<float> Probabilities,
    long TotalSamples,
    bool PaddedTail);

internal sealed class SileroOnnxModelSession : IDisposable
{
    public const int SampleRate = 16000;
    public const int WindowSamples = 512;
    public const int ContextSamples = 64;

    private readonly object _sync = new();
    private readonly string _modelPath;
    private InferenceSession? _session;
    private bool _disposed;

    public SileroOnnxModelSession(string modelPath)
    {
        _modelPath = modelPath;
    }

    public SileroProbabilitySeries Analyze(
        Pcm16MonoWaveReader wave,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wave);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (wave.SampleRate != SampleRate)
        {
            throw new SpeechActivityProviderException(
                $"Silero VAD requires {SampleRate} Hz PCM but received {wave.SampleRate} Hz.");
        }

        InferenceSession session = GetSession();
        var probabilities = new List<float>(
            checked((int)Math.Min(
                int.MaxValue,
                (wave.TotalSamples + WindowSamples - 1) / WindowSamples)));
        float[] state = new float[2 * 1 * 128];
        float[] context = new float[ContextSamples];
        float[] window = new float[WindowSamples];
        float[] input = new float[ContextSamples + WindowSamples];
        var stopwatch = Stopwatch.StartNew();
        bool paddedTail = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed > timeout)
            {
                throw new SpeechActivityProviderException(
                    $"Silero VAD exceeded its {timeout:c} processing limit.");
            }

            int read = wave.ReadNormalizedSamples(window);
            if (read == 0)
            {
                break;
            }

            if (read < WindowSamples)
            {
                Array.Clear(window, read, WindowSamples - read);
                paddedTail = true;
            }

            context.CopyTo(input, 0);
            window.CopyTo(input, ContextSamples);
            (float probability, float[] nextState) =
                RunWindow(session, input, state);
            probabilities.Add(probability);
            state = nextState;
            Array.Copy(
                input,
                input.Length - ContextSamples,
                context,
                0,
                ContextSamples);
        }

        return new SileroProbabilitySeries(
            probabilities.AsReadOnly(),
            wave.TotalSamples,
            paddedTail);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
    }

    private static (float Probability, float[] State) RunWindow(
        InferenceSession session,
        float[] input,
        float[] state)
    {
        var inputTensor = new DenseTensor<float>(
            input,
            [1, input.Length]);
        var stateTensor = new DenseTensor<float>(
            state,
            [2, 1, 128]);
        var sampleRateTensor = new DenseTensor<long>(
            Array.Empty<int>());
        sampleRateTensor.Buffer.Span[0] = SampleRate;

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue>
            output = session.Run(
                [
                    NamedOnnxValue.CreateFromTensor("input", inputTensor),
                    NamedOnnxValue.CreateFromTensor("state", stateTensor),
                    NamedOnnxValue.CreateFromTensor("sr", sampleRateTensor),
                ]);
        DisposableNamedOnnxValue[] values = output.ToArray();
        float probability = values[0]
            .AsTensor<float>()
            .ToArray()[0];
        float[] nextState = values[1]
            .AsTensor<float>()
            .ToArray();
        if (!float.IsFinite(probability) ||
            probability is < 0 or > 1 ||
            nextState.Length != state.Length ||
            nextState.Any(static value => !float.IsFinite(value)))
        {
            throw new SpeechActivityProviderException(
                "Silero VAD returned non-finite or structurally invalid output.");
        }

        return (probability, nextState);
    }

    private InferenceSession GetSession()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null)
            {
                return _session;
            }

            using var options = new SessionOptions
            {
                InterOpNumThreads = 1,
                IntraOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            _session = new InferenceSession(_modelPath, options);
            return _session;
        }
    }
}
