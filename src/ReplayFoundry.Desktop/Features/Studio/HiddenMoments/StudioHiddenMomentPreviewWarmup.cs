using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Preview;

namespace ReplayFoundry.Desktop.Features.Studio.HiddenMoments;

internal sealed class StudioHiddenMomentPreviewWarmup : IDisposable
{
    private readonly IStudioPreviewMediaService? _mediaService;
    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;
    private CancellationTokenSource? _cancellation;
    private string? _activeIdentity;
    private StudioPreviewMediaRequest[] _pending = [];
    private bool _running;
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private bool _isStopping;
    private bool _isDisposed;

    public StudioHiddenMomentPreviewWarmup(
        IStudioPreviewMediaService? mediaService) =>
        _mediaService = mediaService;

    public void Restart(params GenerationOutputAsset?[] assets)
    {
        if (_mediaService is null) return;
        lock (_sync)
        {
            if (_isDisposed || _isStopping) return;
            // Keep the active encode when navigation selects it. Its foreground
            // request shares the media-service key lock and consumes that cache
            // entry, instead of killing FFmpeg and starting the same cut again.
            _pending = assets.OfType<GenerationOutputAsset>()
                .Select(asset => new StudioPreviewMediaRequest(asset, StudioPreviewRangeMode.ExactSelection))
                .DistinctBy(StudioPreviewCacheKey.CreateMediaIdentity)
                .Where(request => StudioPreviewCacheKey.CreateMediaIdentity(request) != _activeIdentity &&
                    !_completed.Contains(StudioPreviewCacheKey.CreateMediaIdentity(request)))
                .Take(3).ToArray();
            if (_running || _pending.Length == 0) return;
            _running = true;
            _tail = Task.Run(WarmAsync);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _pending = [];
            _completed.Clear();
            TryCancel(_cancellation);
            _cancellation = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _pending = [];
            TryCancel(_cancellation);
            _cancellation = null;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task completion;
        lock (_sync)
        {
            _isStopping = true;
            _pending = [];
            TryCancel(_cancellation);
            _cancellation = null;
            completion = _tail;
        }
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WarmAsync()
    {
        while (true)
        {
            StudioPreviewMediaRequest request;
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (_isDisposed || _isStopping || _pending.Length == 0)
                { _running = false; return; }
                request = _pending[0];
                _pending = _pending.Skip(1).ToArray();
                _activeIdentity = StudioPreviewCacheKey.CreateMediaIdentity(request);
                _cancellation = cancellation = new CancellationTokenSource();
            }
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                using StudioPreviewMediaLease lease =
                    await _mediaService!.MaterializeAsync(
                        request,
                        cancellation.Token).ConfigureAwait(false);
                lock (_sync)
                    if (!cancellation.IsCancellationRequested) _completed.Add(StudioPreviewCacheKey.CreateMediaIdentity(request));
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException or
                ObjectDisposedException)
            {
                // Warm-ahead is opportunistic. The foreground preview keeps its
                // normal error and retry path if this cache fill is unavailable.
            }
            finally
            {
                lock (_sync)
                {
                    _activeIdentity = null;
                    if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                }
                cancellation.Dispose();
            }
        }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion raced with replacement or shutdown.
        }
    }
}

internal static class StudioHiddenMomentPreviewAssetFactory
{
    public static GenerationOutputAsset Create(
        GenerationOutputProject project,
        GenerationHiddenMoment moment) =>
        new(
            moment.Id,
            project.Assets.Count + 1,
            moment.SourceMedia,
            outputFullPath: null,
            moment.SourceStart,
            moment.SourceEnd,
            moment.FinalScore,
            moment.QualityTarget,
            GenerationCandidateSelectionReason.HiddenMomentRecovery,
            moment.Explanation,
            preferenceFeatures: moment.PreferenceFeatures);
}
