using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.PreparationTests;

internal static class MediaWorkBudgetTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        .. StudioCpuPreviewTests.GetTests(),
        .. StudioRegionTranslationTrackerTests.GetTests(),
        new("Media admission prioritizes foreground then final output among queued work", PriorityOrdersQueuedWork),
        new("Exclusive AI admission drains ordinary work and prevents overlap", ExclusiveAiDrainsOrdinaryWork),
        new("Bounded bypass admits background work despite continuous foreground demand", PriorityCannotStarveBackground),
        new("Cancellation removes queued media work without leaking admission", CancellationDoesNotLeakSlots),
    ];

    private static Task<IDisposable> Acquire(MediaWorkScheduler scheduler, MediaWorkPriority priority,
        MediaWorkKind kind = MediaWorkKind.MediaProcess, CancellationToken token = default) =>
        scheduler.AcquireAsync(priority, kind, token);

    private static async Task PriorityOrdersQueuedWork()
    {
        var scheduler = new MediaWorkScheduler();
        using var occupied = await Acquire(scheduler, MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi);
        Task<IDisposable> background = Acquire(scheduler, MediaWorkPriority.Background);
        Task<IDisposable> final = Acquire(scheduler, MediaWorkPriority.FinalOutput);
        Task<IDisposable> foreground = Acquire(scheduler, MediaWorkPriority.Foreground);
        occupied.Dispose();
        using var first = await foreground.WaitAsync(TimeSpan.FromSeconds(2));
        using var second = await final.WaitAsync(TimeSpan.FromSeconds(2));
        TestAssert.False(background.IsCompleted, "Background work must wait behind both queued user-visible requests.");
        first.Dispose();
        using var third = await background.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task ExclusiveAiDrainsOrdinaryWork()
    {
        var scheduler = new MediaWorkScheduler();
        using var first = await Acquire(scheduler, MediaWorkPriority.FinalOutput);
        using var second = await Acquire(scheduler, MediaWorkPriority.FinalOutput);
        Task<IDisposable> ai = Acquire(scheduler, MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi);
        Task<IDisposable> ordinary = Acquire(scheduler, MediaWorkPriority.Background);
        first.Dispose();
        TestAssert.False(ai.IsCompleted, "AI cannot overlap a remaining media process.");
        TestAssert.False(ordinary.IsCompleted, "A queued exclusive request must reserve the draining slot.");
        second.Dispose();
        using var model = await ai.WaitAsync(TimeSpan.FromSeconds(2));
        TestAssert.False(ordinary.IsCompleted, "Media processes cannot overlap admitted model inference.");
        model.Dispose();
        using var resumed = await ordinary.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task PriorityCannotStarveBackground()
    {
        var scheduler = new MediaWorkScheduler(maximumBypasses: 2);
        using var occupied = await Acquire(scheduler, MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi);
        Task<IDisposable> background = Acquire(scheduler, MediaWorkPriority.Background, MediaWorkKind.HeavyAi);
        Task<IDisposable>[] foreground = Enumerable.Range(0, 4).Select(_ => Acquire(scheduler, MediaWorkPriority.Foreground)).ToArray();
        occupied.Dispose();
        using var first = await foreground[0].WaitAsync(TimeSpan.FromSeconds(2));
        using var second = await foreground[1].WaitAsync(TimeSpan.FromSeconds(2));
        first.Dispose();
        TestAssert.False(foreground[2].IsCompleted, "Aged AI must reserve the next free slot after its bypass budget is spent.");
        second.Dispose();
        using var aged = await background.WaitAsync(TimeSpan.FromSeconds(2));
        TestAssert.False(foreground[2].IsCompleted, "Continuous foreground demand cannot starve older work.");
        aged.Dispose();
        using var third = await foreground[2].WaitAsync(TimeSpan.FromSeconds(2));
        using var fourth = await foreground[3].WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task CancellationDoesNotLeakSlots()
    {
        var scheduler = new MediaWorkScheduler();
        var diagnostics = new List<MediaWorkAdmissionDiagnostic>();
        using var occupied = await scheduler.AcquireAsync(MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi,
            CancellationToken.None, value => { lock (diagnostics) diagnostics.Add(value); });
        using var cancelled = new CancellationTokenSource();
        Task<IDisposable> abandoned = Acquire(scheduler, MediaWorkPriority.Foreground, token: cancelled.Token);
        Task<IDisposable> next = Acquire(scheduler, MediaWorkPriority.Background, MediaWorkKind.HeavyAi);
        cancelled.Cancel();
        await TestAssert.ThrowsAsync<OperationCanceledException>(async () => await abandoned,
            "Cancelling queued work must complete its caller promptly.");
        occupied.Dispose();
        occupied.Dispose();
        using var resumed = await next.WaitAsync(TimeSpan.FromSeconds(2));
        lock (diagnostics)
        {
            TestAssert.Equal(1, diagnostics.Count(value => value.Event == "completed"), "A repeated release must not return capacity twice.");
            TestAssert.True(diagnostics.All(value => value.WaitMilliseconds >= 0 && value.WorkMilliseconds >= 0),
                "Optional admission telemetry must report measured nonnegative wait and work durations.");
        }
    }
}
