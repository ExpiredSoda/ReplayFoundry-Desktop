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
        NamedOnnxValue[] inferenceInputs = CreateInferenceInputs(
            input,
            state);
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
            probabilities.Add(
                RunWindow(session, inferenceInputs, state));
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

    private static NamedOnnxValue[] CreateInferenceInputs(
        float[] input,
        float[] state)
    {
        var sampleRateTensor = new DenseTensor<long>(
            Array.Empty<int>());
        sampleRateTensor.Buffer.Span[0] = SampleRate;
        return
        [
            NamedOnnxValue.CreateFromTensor(
                "input",
                new DenseTensor<float>(input, [1, input.Length])),
            NamedOnnxValue.CreateFromTensor(
                "state",
                new DenseTensor<float>(state, [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", sampleRateTensor),
        ];
    }

    private static float RunWindow(
        InferenceSession session,
        IReadOnlyCollection<NamedOnnxValue> inputs,
        float[] state)
    {
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue>
            output = session.Run(inputs);
        using IEnumerator<DisposableNamedOnnxValue> values =
            output.GetEnumerator();
        if (!values.MoveNext())
        {
            throw InvalidOutput();
        }
        float probability = values.Current
            .AsTensor<float>()
            .GetValue(0);
        if (!values.MoveNext())
        {
            throw InvalidOutput();
        }
        Tensor<float> nextState = values.Current.AsTensor<float>();
        if (!float.IsFinite(probability) ||
            probability is < 0 or > 1 ||
            nextState.Length != state.Length ||
            !CopyFiniteState(nextState, state))
        {
            throw InvalidOutput();
        }

        return probability;
    }

    private static bool CopyFiniteState(
        Tensor<float> source,
        float[] destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            float value = source.GetValue(index);
            if (!float.IsFinite(value))
            {
                return false;
            }
            destination[index] = value;
        }
        return true;
    }

    private static SpeechActivityProviderException InvalidOutput() =>
        new(
            "Silero VAD returned non-finite or structurally invalid output.");

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
