using System.Diagnostics;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.Media;

internal enum MediaWorkPriority { Foreground, FinalOutput, Background }
internal enum MediaWorkKind { MediaProcess, HeavyAi, CpuForegroundPreview }

internal sealed record MediaWorkAdmissionDiagnostic(
    string Event, MediaWorkPriority Priority, MediaWorkKind Kind,
    double WaitMilliseconds, double WorkMilliseconds);

/// <summary>
/// Shared admission at external-process boundaries. Two ordinary media jobs
/// may run together; model inference reserves both ordinary slots. One explicitly
/// bounded software foreground preview has its own lane. Running work is not
/// preempted. Bounded priority bypass prevents an endless stream of previews
/// from starving final output or background analysis.
/// </summary>
internal static class MediaWorkBudget
{
    private static readonly MediaWorkScheduler Scheduler = new();
    private static readonly AsyncLocal<MediaWorkPriority?> Priority = new();
    private static readonly AsyncLocal<Action<MediaWorkAdmissionDiagnostic>?> Observer = new();

    internal static IDisposable WithPriority(MediaWorkPriority priority)
    {
        MediaWorkPriority? previous = Priority.Value;
        Priority.Value = priority;
        return new Scope(() => Priority.Value = previous);
    }

    internal static IDisposable Capture(Action<MediaWorkAdmissionDiagnostic> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var previous = Observer.Value;
        Observer.Value = observer;
        return new Scope(() => Observer.Value = previous);
    }

    internal static Task<IDisposable> AcquireAsync(CancellationToken cancellationToken,
        MediaWorkPriority priority = MediaWorkPriority.FinalOutput,
        MediaWorkKind kind = MediaWorkKind.MediaProcess) =>
        Scheduler.AcquireAsync(Priority.Value ?? priority, kind, cancellationToken, Observer.Value);

    internal static Task<ProcessRunResult> RunAsync(IProcessRunner runner, ProcessRunRequest request,
        MediaWorkPriority priority, CancellationToken cancellationToken) =>
        RunAsync(runner, request, priority, MediaWorkKind.MediaProcess, cancellationToken);

    internal static async Task<ProcessRunResult> RunAsync(IProcessRunner runner, ProcessRunRequest request,
        MediaWorkPriority priority, MediaWorkKind kind, CancellationToken cancellationToken)
    {
        using IDisposable admission = await AcquireAsync(cancellationToken, priority, kind).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}

/// <summary>Instance form permits deterministic admission tests without global jobs or timers.</summary>
internal sealed class MediaWorkScheduler
{
    private const int Capacity = 2;
    private readonly object _sync = new();
    private readonly List<WaitingWork> _waiting = [];
    private readonly int _maximumBypasses;
    private int _used;
    private readonly SemaphoreSlim _cpuPreview = new(1, 1);

    internal MediaWorkScheduler(int maximumBypasses = 8)
    {
        if (maximumBypasses < 1) throw new ArgumentOutOfRangeException(nameof(maximumBypasses));
        _maximumBypasses = maximumBypasses;
    }

    internal async Task<IDisposable> AcquireAsync(MediaWorkPriority priority, MediaWorkKind kind,
        CancellationToken cancellationToken, Action<MediaWorkAdmissionDiagnostic>? observer = null)
    {
        if (!Enum.IsDefined(priority) || !Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(priority));
        cancellationToken.ThrowIfCancellationRequested();
        if (kind == MediaWorkKind.CpuForegroundPreview)
        {
            if (priority != MediaWorkPriority.Foreground)
                throw new ArgumentException("The independent CPU preview lane is reserved for foreground requests.", nameof(priority));
            return await AcquireCpuPreviewAsync(observer, cancellationToken).ConfigureAwait(false);
        }
        var item = new WaitingWork(priority, kind, observer, cancellationToken);
        lock (_sync)
        {
            _waiting.Add(item);
            AdmitWaiting();
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(() => Cancel(item));
        IDisposable lease = await item.Completion.Task.ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            lease.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }
        item.Report("admitted", 0);
        return lease;
    }

    private async Task<IDisposable> AcquireCpuPreviewAsync(Action<MediaWorkAdmissionDiagnostic>? observer,
        CancellationToken cancellationToken)
    {
        var item = new WaitingWork(MediaWorkPriority.Foreground, MediaWorkKind.CpuForegroundPreview, observer, cancellationToken);
        try { await _cpuPreview.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { item.Report("cancelled", 0); throw; }
        item.AdmittedAt = Stopwatch.GetTimestamp();
        if (cancellationToken.IsCancellationRequested)
        {
            _cpuPreview.Release(); item.Report("cancelled", 0); cancellationToken.ThrowIfCancellationRequested();
        }
        item.Report("admitted", 0);
        return new CpuPreviewLease(_cpuPreview, item);
    }

    private void Cancel(WaitingWork item)
    {
        lock (_sync)
        {
            if (!_waiting.Remove(item)) return;
            item.Completion.TrySetCanceled(item.CancellationToken);
            AdmitWaiting();
        }
        item.Report("cancelled", 0);
    }

    // Invoked under _sync. Never call application callbacks while holding it.
    private void AdmitWaiting()
    {
        while (_waiting.Count > 0 && _used < Capacity)
        {
            WaitingWork next = _waiting.FirstOrDefault(item => item.Bypasses >= _maximumBypasses)
                ?? _waiting.OrderBy(static item => item.Priority).First();
            // Reserve the next free slot for a selected exclusive job. Allowing
            // smaller jobs to bypass it here would starve AI indefinitely.
            if (next.Slots > Capacity - _used) return;
            int nextIndex = _waiting.IndexOf(next);
            for (int index = 0; index < nextIndex; index++) _waiting[index].Bypasses++;
            _waiting.RemoveAt(nextIndex);
            _used += next.Slots;
            next.AdmittedAt = Stopwatch.GetTimestamp();
            next.Completion.TrySetResult(new Lease(this, next));
        }
    }

    private void Release(WaitingWork item)
    {
        double duration = Stopwatch.GetElapsedTime(item.AdmittedAt).TotalMilliseconds;
        lock (_sync)
        {
            _used -= item.Slots;
            AdmitWaiting();
        }
        item.Report("completed", duration);
    }

    private sealed class WaitingWork(MediaWorkPriority priority, MediaWorkKind kind,
        Action<MediaWorkAdmissionDiagnostic>? observer, CancellationToken cancellationToken)
    {
        internal MediaWorkPriority Priority { get; } = priority;
        internal MediaWorkKind Kind { get; } = kind;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal int Slots => Kind == MediaWorkKind.HeavyAi ? Capacity : 1;
        internal int Bypasses { get; set; }
        internal long QueuedAt { get; } = Stopwatch.GetTimestamp();
        internal long AdmittedAt { get; set; }
        internal TaskCompletionSource<IDisposable> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Report(string eventName, double workMilliseconds)
        {
            try
            {
                observer?.Invoke(new(eventName, Priority, Kind,
                    Stopwatch.GetElapsedTime(QueuedAt, AdmittedAt == 0 ? Stopwatch.GetTimestamp() : AdmittedAt).TotalMilliseconds,
                    workMilliseconds));
            }
            catch (Exception) { /* Optional diagnostics cannot alter admission or release. */ }
        }
    }

    private sealed class Lease(MediaWorkScheduler owner, WaitingWork item) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release(item);
        }
    }

    private sealed class CpuPreviewLease(SemaphoreSlim gate, WaitingWork item) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            gate.Release();
            item.Report("completed", Stopwatch.GetElapsedTime(item.AdmittedAt).TotalMilliseconds);
        }
    }
}
