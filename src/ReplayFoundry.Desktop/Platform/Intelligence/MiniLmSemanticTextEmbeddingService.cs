using System.Diagnostics;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.Desktop.Platform.Intelligence;

/// <summary>Pinned English text retrieval, CPU only. No transcript is sent to a server.</summary>
public sealed class MiniLmSemanticTextEmbeddingService : ISemanticTextEmbeddingService
{
    private readonly MiniLmModelArtifacts _artifacts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, float[]> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    public MiniLmSemanticTextEmbeddingService(string? modelDirectory = null) => _artifacts = new(modelDirectory);

    public async Task<SemanticTextEmbeddingResult> EmbedAsync(IReadOnlyList<string> texts,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count is < 1 or > 257 || texts.Any(static text => string.IsNullOrWhiteSpace(text) || text.Length > 8192))
            throw new ArgumentException("Semantic retrieval accepts a query and at most 256 bounded text windows.", nameof(texts));
        string[] snapshot = texts.ToArray();
        var timer = Stopwatch.StartNew();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _artifacts.EnsureAsync(progress, cancellationToken).ConfigureAwait(false);
            string[] missing = snapshot.Where(text => !_cache.ContainsKey(text)).Distinct(StringComparer.Ordinal).ToArray();
            int hits = snapshot.Count(text => _cache.ContainsKey(text));
            if (missing.Length > 0)
            {
                var tokenizer = new BertWordPieceTokenizer(await File.ReadAllLinesAsync(_artifacts.VocabularyPath, cancellationToken).ConfigureAwait(false));
                progress?.Report($"Comparing {snapshot.Length - 1} English transcript passages locally. Similarity is not proof of an event.");
                using var admission = await MediaWorkBudget.AcquireAsync(cancellationToken,
                    MediaWorkPriority.Background, MediaWorkKind.HeavyAi).ConfigureAwait(false);
                float[][] computed;
                try { computed = await Task.Run(() => EmbedMissing(missing, tokenizer, cancellationToken), cancellationToken).ConfigureAwait(false); }
                catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
                { throw new OperationCanceledException(cancellationToken); }
                cancellationToken.ThrowIfCancellationRequested();
                for (int index = 0; index < missing.Length; index++)
                {
                    _cache.Add(missing[index], computed[index]);
                    _cacheOrder.Enqueue(missing[index]);
                }
            }
            // Snapshot first: eviction must never remove an entry needed by this request.
            float[][] vectors = snapshot.Select(text => (float[])_cache[text].Clone()).ToArray();
            while (_cache.Count > 512) _cache.Remove(_cacheOrder.Dequeue());
            return new(vectors, MiniLmModelArtifacts.Identity, MiniLmModelArtifacts.ModelSha256,
                MiniLmModelArtifacts.VocabularySha256, timer.Elapsed, hits);
        }
        finally { _gate.Release(); }
    }

    private float[][] EmbedMissing(string[] texts, BertWordPieceTokenizer tokenizer, CancellationToken cancellationToken)
    {
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 2, InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
        using var session = new InferenceSession(_artifacts.ModelPath, options);
        if (session.InputMetadata.Count != 3 ||
            !new[] { "input_ids", "attention_mask", "token_type_ids" }.All(session.InputMetadata.ContainsKey) ||
            !session.OutputMetadata.ContainsKey("last_hidden_state"))
            throw new InvalidDataException("The pinned MiniLM model has an unexpected input/output contract.");
        using var run = new RunOptions();
        using var cancellation = cancellationToken.Register(() => run.Terminate = true);
        var result = new List<float[]>(texts.Length);
        for (int offset = 0; offset < texts.Length; offset += 8)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long[][] tokens = texts.Skip(offset).Take(8).Select(tokenizer.Encode).ToArray();
            int length = tokens.Max(static item => item.Length);
            var ids = new DenseTensor<long>([tokens.Length, length]);
            var mask = new DenseTensor<long>([tokens.Length, length]);
            var types = new DenseTensor<long>([tokens.Length, length]);
            for (int row = 0; row < tokens.Length; row++)
                for (int column = 0; column < tokens[row].Length; column++)
                { ids[row, column] = tokens[row][column]; mask[row, column] = 1; }
            using var output = session.Run([
                NamedOnnxValue.CreateFromTensor("input_ids", ids),
                NamedOnnxValue.CreateFromTensor("attention_mask", mask),
                NamedOnnxValue.CreateFromTensor("token_type_ids", types)], ["last_hidden_state"], run);
            cancellationToken.ThrowIfCancellationRequested();
            Tensor<float> hidden = output.Single().AsTensor<float>();
            if (hidden.Dimensions.Length != 3 || hidden.Dimensions[0] != tokens.Length ||
                hidden.Dimensions[1] != length || hidden.Dimensions[2] != 384)
                throw new InvalidDataException("MiniLM returned an unexpected embedding shape.");
            for (int row = 0; row < tokens.Length; row++)
            {
                var vector = new float[384];
                for (int column = 0; column < tokens[row].Length; column++)
                    for (int dimension = 0; dimension < vector.Length; dimension++)
                        vector[dimension] += hidden[row, column, dimension] / tokens[row].Length;
                double norm = Math.Sqrt(vector.Sum(static value => (double)value * value));
                if (!double.IsFinite(norm) || norm < 1e-10) throw new InvalidDataException("MiniLM returned an invalid text embedding.");
                for (int dimension = 0; dimension < vector.Length; dimension++) vector[dimension] /= (float)norm;
                result.Add(vector);
            }
        }
        return result.ToArray();
    }
}
