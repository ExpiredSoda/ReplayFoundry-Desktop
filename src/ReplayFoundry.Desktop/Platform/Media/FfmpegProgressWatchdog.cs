using System.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Media;

internal sealed class FfmpegProgressWatchdog : IAsyncDisposable
{
    private readonly CancellationTokenSource _operation;
    private readonly CancellationTokenSource _monitorStop = new();
    private readonly Task _monitor;
    private readonly TimeSpan _stallTimeout;
    private long _lastAdvance = Stopwatch.GetTimestamp();
    private long _lastMediaTime = -1;
    private int _stalled;
    private int _paused;
    internal FfmpegProgressWatchdog(CancellationToken cancellationToken, TimeSpan? stallTimeout = null)
    {
        _operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stallTimeout = stallTimeout ?? TimeSpan.FromMinutes(2);
        _monitor = MonitorAsync();
    }
    internal CancellationToken Token => _operation.Token;
    internal bool IsStalled => Volatile.Read(ref _stalled) != 0;
    internal void Pause() => Volatile.Write(ref _paused, 1);
    internal void Restart()
    {
        Interlocked.Exchange(ref _lastMediaTime, -1);
        Interlocked.Exchange(ref _lastAdvance, Stopwatch.GetTimestamp());
        Volatile.Write(ref _paused, 0);
    }
    internal void Advance(long mediaTime)
    {
        if (mediaTime <= Interlocked.Read(ref _lastMediaTime)) return;
        Interlocked.Exchange(ref _lastMediaTime, mediaTime);
        Interlocked.Exchange(ref _lastAdvance, Stopwatch.GetTimestamp());
    }
    private async Task MonitorAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _monitorStop.Token).ConfigureAwait(false);
                if (Volatile.Read(ref _paused) != 0 ||
                    Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastAdvance)) <= _stallTimeout) continue;
                Interlocked.Exchange(ref _stalled, 1);
                _operation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (_monitorStop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        _monitorStop.Cancel();
        await _monitor.ConfigureAwait(false);
        _monitorStop.Dispose(); _operation.Dispose();
    }
}
