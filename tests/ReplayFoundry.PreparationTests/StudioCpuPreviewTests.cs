using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal static class StudioCpuPreviewTests
{
    internal static IReadOnlyList<TestCase> GetTests() =>
    [
        new("One CPU preview overlaps exclusive AI without releasing ordinary capacity", CpuLaneDoesNotChangeMainCapacity),
        new("CPU preview lane serializes requests and queued cancellation does not leak", CpuLaneCancellationAndSerialization),
        new("Foreground preview cancels queued same-key prewarming and bypasses AI safely", ForegroundPromotesQueuedPrewarm),
        new("Foreground preview cancels executing same-key prewarming and retains cache identity", ForegroundPromotesExecutingPrewarm),
        new("Cancelling an active CPU preview removes staging and releases its lane", ActivePreviewCancellationCleansUp),
        new("Long or background previews retain ordinary media admission", IneligibleRequestsKeepMainAdmission),
    ];

    private static async Task CpuLaneDoesNotChangeMainCapacity()
    {
        var scheduler = new MediaWorkScheduler();
        using var ai = await scheduler.AcquireAsync(MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi, CancellationToken.None);
        Task<IDisposable> final = scheduler.AcquireAsync(MediaWorkPriority.FinalOutput, MediaWorkKind.MediaProcess, CancellationToken.None);
        using var preview = await scheduler.AcquireAsync(MediaWorkPriority.Foreground, MediaWorkKind.CpuForegroundPreview, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        TestAssert.False(final.IsCompleted, "The CPU lane cannot release AI's reserved main capacity.");
        preview.Dispose(); preview.Dispose();
        TestAssert.False(final.IsCompleted, "Returning a CPU lease must not create a phantom ordinary slot.");
        ai.Dispose();
        using var resumed = await final.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task CpuLaneCancellationAndSerialization()
    {
        var scheduler = new MediaWorkScheduler();
        using var first = await scheduler.AcquireAsync(MediaWorkPriority.Foreground, MediaWorkKind.CpuForegroundPreview, CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var abandoned = scheduler.AcquireAsync(MediaWorkPriority.Foreground, MediaWorkKind.CpuForegroundPreview, stop.Token);
        var next = scheduler.AcquireAsync(MediaWorkPriority.Foreground, MediaWorkKind.CpuForegroundPreview, CancellationToken.None);
        TestAssert.False(next.IsCompleted, "Only one foreground CPU encode may run at once.");
        stop.Cancel();
        await TestAssert.ThrowsAsync<OperationCanceledException>(async () => await abandoned, "Queued cancellation must complete promptly.");
        first.Dispose();
        using var admitted = await next.WaitAsync(TimeSpan.FromSeconds(2));
        await TestAssert.ThrowsAsync<ArgumentException>(async () => await scheduler.AcquireAsync(
            MediaWorkPriority.Background, MediaWorkKind.CpuForegroundPreview, CancellationToken.None), "Prewarming cannot use the independent lane.");
    }

    private static async Task ForegroundPromotesQueuedPrewarm()
    {
        using var fixture = new PreviewFixture();
        var process = new ControlledPreviewRunner();
        var service = fixture.Service(process, hardware: true);
        var prewarmer = new StudioPreviewPrewarmer(service);
        using var ai = await MediaWorkBudget.AcquireAsync(CancellationToken.None, kind: MediaWorkKind.HeavyAi);
        Task background = prewarmer.PrewarmAsync(fixture.Project, null, CancellationToken.None);
        TestAssert.False(background.IsCompleted, "The optional prewarm should be queued behind AI.");
        using var foreground = await service.MaterializeAsync(new(fixture.Asset, StudioPreviewRangeMode.ExactSelection), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await background.WaitAsync(TimeSpan.FromSeconds(2));
        TestAssert.Equal(1, process.Requests.Count, "Only the foreground CPU encode may start; no background or hardware qualification process.");
        AssertCpuCommand(process.Requests[0]);
        TestAssert.True(File.Exists(foreground.MediaPath), "Foreground materialization must retain a usable completed entry.");
    }

    private static async Task ForegroundPromotesExecutingPrewarm()
    {
        using var fixture = new PreviewFixture();
        var process = new ControlledPreviewRunner(blockFirst: true);
        var service = fixture.Service(process);
        var backgroundRequest = new StudioPreviewMediaRequest(fixture.Asset, workIntent: StudioPreviewWorkIntent.BackgroundPrewarm);
        Task<StudioPreviewMediaLease> background = service.MaterializeAsync(backgroundRequest, CancellationToken.None);
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var foreground = await service.MaterializeAsync(new(fixture.Asset), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await TestAssert.ThrowsAsync<OperationCanceledException>(async () => await background, "Executing optional work must stop when the same foreground key is requested.");
        TestAssert.True(process.FirstCancelled, "Promotion must cancel the obsolete process rather than wait for its encode timeout.");
        TestAssert.Equal(2, process.Requests.Count, "One canceled background encode and one foreground encode are expected.");
        AssertCpuCommand(process.Requests[1]);
        TestAssert.Equal(StudioPreviewCacheKey.Create(backgroundRequest).Hash, StudioPreviewCacheKey.Create(new(fixture.Asset)).Hash,
            "Execution intent must not fork visually compatible cache identities.");
        using var cached = await service.MaterializeAsync(backgroundRequest, CancellationToken.None);
        TestAssert.Equal(foreground.MediaPath, cached.MediaPath, "Prewarming and foreground playback share the completed entry.");
        TestAssert.Equal(2, process.Requests.Count, "A complete foreground entry must satisfy later prewarming without another job.");
    }

    private static async Task ActivePreviewCancellationCleansUp()
    {
        using var fixture = new PreviewFixture();
        var process = new ControlledPreviewRunner(blockFirst: true);
        var service = fixture.Service(process);
        using var stop = new CancellationTokenSource();
        Task<StudioPreviewMediaLease> pending = service.MaterializeAsync(new(fixture.Asset), stop.Token);
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); stop.Cancel();
        await TestAssert.ThrowsAsync<OperationCanceledException>(async () => await pending, "Active cancellation must reach the child process.");
        TestAssert.False(Directory.EnumerateFiles(fixture.Root, "preview.mp4", SearchOption.AllDirectories).Any(),
            "Canceled output cannot be committed to the preview cache.");
        using var next = await service.MaterializeAsync(new(fixture.Asset), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        TestAssert.True(File.Exists(next.MediaPath), "A later preview must obtain the released CPU lane and cache-key lock.");
    }

    private static async Task IneligibleRequestsKeepMainAdmission()
    {
        using var fixture = new PreviewFixture(duration: TimeSpan.FromMinutes(10));
        GenerationOutputAsset longAsset = new("long", 1, fixture.Asset.SourceMedia, null, TimeSpan.Zero, TimeSpan.FromMinutes(4),
            0, 0, GenerationCandidateSelectionReason.ManualSourceCut, "Long manual cut.");
        TestAssert.False(StudioCpuPreviewPolicy.IsEligible(new(longAsset)), "An unbounded manual section must not enter the CPU preview lane.");
        TestAssert.False(StudioCpuPreviewPolicy.IsEligible(new(fixture.Asset, workIntent: StudioPreviewWorkIntent.BackgroundPrewarm)),
            "Background intent remains in the ordinary priority queue even for a short source.");
        var process = new ControlledPreviewRunner();
        var service = fixture.Service(process);
        using var ai = await MediaWorkBudget.AcquireAsync(CancellationToken.None, kind: MediaWorkKind.HeavyAi);
        using var stop = new CancellationTokenSource();
        Task<StudioPreviewMediaLease> pending = service.MaterializeAsync(new(longAsset), stop.Token);
        TestAssert.False(pending.IsCompleted, "Long previews must respect the exclusive ordinary reservation.");
        TestAssert.Equal(0, process.Requests.Count, "No long preview process may start alongside exclusive inference.");
        stop.Cancel();
        await TestAssert.ThrowsAsync<OperationCanceledException>(async () => await pending, "Ineligible queued preview cancellation must still work.");
    }

    private static void AssertCpuCommand(ProcessRunRequest request)
    {
        static bool Pair(IReadOnlyList<string> args, string flag, string value) => args.Zip(args.Skip(1)).Any(pair => pair.First == flag && pair.Second == value);
        TestAssert.True(Pair(request.Arguments, "-hwaccel", "none") && Pair(request.Arguments, "-c:v", "h264_mf") &&
            Pair(request.Arguments, "-hw_encoding", "0"), "The independent lane must explicitly avoid GPU decode and encode.");
        TestAssert.True(Pair(request.Arguments, "-filter_complex_threads", "1") && Pair(request.Arguments, "-threads:v", "2"),
            "Bounded CPU preview commands must request conservative filter and codec threading.");
        TestAssert.True(request.Timeout <= TimeSpan.FromSeconds(90), "Interactive encoding must have its own bounded wall timeout.");
    }

    private sealed class PreviewFixture : IDisposable
    {
        internal PreviewFixture(TimeSpan? duration = null)
        {
            Root = Path.Combine(Path.GetTempPath(), "ReplayFoundryCpuPreview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            string source = Path.Combine(Root, "source.mkv"); File.WriteAllBytes(source, [1, 2, 3]);
            MediaProbeResult media = TestMediaFactory.Create(source, duration ?? TimeSpan.FromSeconds(4));
            Asset = new("cpu-preview", 1, media, null, TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(2.5), 0, 0,
                GenerationCandidateSelectionReason.ManualSourceCut, "Controlled preview fixture.");
            Project = new("cpu-preview-project", GenerationMode.IndividualClips, Path.Combine(Root, "rendered"), 1,
                ClipFulfillmentPreference.FillRequestedCount, GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
                [Asset], DateTimeOffset.UtcNow);
        }
        internal string Root { get; }
        internal GenerationOutputAsset Asset { get; }
        internal GenerationOutputProject Project { get; }
        internal FfmpegStudioPreviewMediaService Service(IProcessRunner runner, bool hardware = false) =>
            new(runner, new Locator(Path.Combine(Root, "ffmpeg.exe")), Path.Combine(Root, "cache"), hardwareEncoding: hardware);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
    private sealed class Locator(string path) : IFfmpegToolLocator
    {
        public string LocateFfmpeg() => path;
        public string LocateFfprobe() => path;
    }
    private sealed class ControlledPreviewRunner(bool blockFirst = false) : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FirstCancelled { get; private set; }
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            int count;
            lock (Requests) { Requests.Add(request); count = Requests.Count; }
            if (blockFirst && count == 1)
            {
                Started.TrySetResult(true);
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { FirstCancelled = true; throw; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            string output = request.Arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllBytes(output, new byte[256]);
            return new(0, "", "", TimeSpan.Zero);
        }
    }
}
