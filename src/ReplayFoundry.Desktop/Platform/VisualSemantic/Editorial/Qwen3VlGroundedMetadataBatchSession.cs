using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

/// <summary>One sequential automatic retry operation, isolated from other operations.</summary>
internal sealed class Qwen3VlGroundedMetadataBatchSession(
    Qwen3VlGroundedMetadataExecutor executor,
    Qwen3VlQualifiedEditorialRuntime runtime,
    ClipEditorialMetadataGeneratorIdentity identity) : IClipEditorialMetadataBatchSession
{
    private readonly Qwen3VlGroundingPacketHandoff _handoff = new();
    private readonly object _sync = new();
    private bool _disposed;
    private bool _active;

    public ClipEditorialMetadataGeneratorIdentity Identity => identity;
    public bool IsAvailable => !_disposed;

    public async Task<ClipEditorialMetadataDraft> GenerateAsync(
        ClipEditorialMetadataRequest request, CancellationToken cancellationToken) =>
        (await GenerateBatchAsync([request], cancellationToken))[0];

    public Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateBatchAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken) =>
        GenerateAsync(requests, (json, batch, receipts) => Qwen3VlGroundedMetadataResultParser.Parse(
            json, batch, runtime, identity, receipts), cancellationToken);

    public Task<IReadOnlyList<ClipEditorialMetadataBatchOutcome>> GenerateBatchOutcomesAsync(
        IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken) =>
        GenerateAsync(requests, (json, batch, receipts) => Qwen3VlGroundedMetadataResultParser.ParseOutcomes(
            json, batch, runtime, identity, receipts), cancellationToken);

    private async Task<TResult> GenerateAsync<TResult>(
        IReadOnlyList<ClipEditorialMetadataRequest> requests,
        Func<string, IReadOnlyList<ClipEditorialMetadataRequest>,
            Dictionary<string, Qwen3VlGroundingPacketReceipt>, TResult> parse,
        CancellationToken cancellationToken)
    {
        Qwen3VlGroundingPacketHandoff pending;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active) throw new InvalidOperationException("An editorial retry session requires sequential batches.");
            pending = _handoff.Copy();
            _active = true;
        }
        using (pending)
        {
            try
            {
                TResult result = await executor.GenerateBatchAsync(requests,
                    (json, batch) => parse(json, batch, pending.Receipts), pending, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _handoff.ReplaceWith(pending);
                }
                return result;
            }
            finally
            {
                lock (_sync) _active = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _handoff.Dispose();
        }
    }
}
