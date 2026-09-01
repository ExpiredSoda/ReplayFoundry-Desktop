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
    private string? _targetIdentity;
    private bool _isStopping;
    private bool _isDisposed;

    public StudioHiddenMomentPreviewWarmup(
        IStudioPreviewMediaService? mediaService) =>
        _mediaService = mediaService;

    public void Restart(GenerationOutputAsset? asset)
    {
        if (asset is null || _mediaService is null)
        {
            Cancel();
            return;
        }

        var request = new StudioPreviewMediaRequest(
            asset,
            StudioPreviewRangeMode.ExactSelection);
        string identity = StudioPreviewCacheKey.CreateMediaIdentity(request);
        lock (_sync)
        {
            if (_isDisposed || _isStopping || identity == _targetIdentity)
            {
                return;
            }

            TryCancel(_cancellation);
            var cancellation = new CancellationTokenSource();
            Task predecessor = _tail;
            _cancellation = cancellation;
            _targetIdentity = identity;
            _tail = Task.Run(() => WarmAfterAsync(
                predecessor,
                request,
                cancellation));
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _targetIdentity = null;
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
            _targetIdentity = null;
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
            _targetIdentity = null;
            TryCancel(_cancellation);
            _cancellation = null;
            completion = _tail;
        }
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WarmAfterAsync(
        Task predecessor,
        StudioPreviewMediaRequest request,
        CancellationTokenSource cancellation)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            using StudioPreviewMediaLease lease =
                await _mediaService!.MaterializeAsync(
                    request,
                    cancellation.Token).ConfigureAwait(false);
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
            cancellation.Dispose();
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
