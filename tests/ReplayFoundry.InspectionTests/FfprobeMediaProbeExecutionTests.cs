using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.InspectionTests;

internal static class FfprobeMediaProbeExecutionTests
{
    private const string ProbeJson =
        """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "h264",
              "codec_long_name": "H.264",
              "codec_type": "video",
              "width": 1920,
              "height": 1080,
              "coded_width": 1920,
              "coded_height": 1080,
              "pix_fmt": "yuv420p",
              "r_frame_rate": "30/1",
              "avg_frame_rate": "30/1",
              "duration": "1.000000",
              "disposition": { "default": 1 }
            }
          ],
          "format": {
            "format_name": "matroska,webm",
            "format_long_name": "Matroska / WebM",
            "start_time": "0.000000",
            "duration": "1.000000",
            "size": "1024",
            "bit_rate": "8192",
            "probe_score": 100
          }
        }
        """;

    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "ffprobe retries one cold-start timeout and preserves request contracts",
            RetriesOneTimeoutAndPreservesRequestContracts),
        new(
            "ffprobe evicts a persistently timed-out manifest before a later call",
            EvictsPersistentTimeoutBeforeLaterCall),
        new(
            "ffprobe does not retry a non-timeout process failure",
            DoesNotRetryNonTimeoutFailure),
        new(
            "ffprobe evicts a canceled manifest before a later call",
            EvictsCanceledManifestBeforeLaterCall),
        new(
            "ffprobe shares one in-flight version verification across callers",
            SharesInflightVersionVerification),
        new(
            "ffprobe caller cancellation preserves shared manifest initialization",
            CallerCancellationPreservesSharedInitialization),
        new(
            "ffprobe callers share one replacement after a completed manifest fault",
            SharesReplacementAfterCompletedFault),
    ];

    private static void RetriesOneTimeoutAndPreservesRequestContracts() =>
        RetriesOneTimeoutAndPreservesRequestContractsAsync()
            .GetAwaiter()
            .GetResult();

    private static async Task RetriesOneTimeoutAndPreservesRequestContractsAsync()
    {
        using var fixture = new ProbeFixture();
        var runner = new ScriptedProcessRunner((invocation, request, _) =>
            invocation switch
            {
                1 => TimedOut(request),
                2 => VersionSucceeded(),
                3 => ProbeSucceeded(),
                _ => UnexpectedInvocation(invocation),
            });
        var probe = new FfprobeMediaProbe(
            runner,
            fixture.ToolLocator);

        MediaProbeResult result = await probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);

        ProcessRunRequest[] requests = runner.GetRequests();
        TestAssert.Equal(
            3,
            requests.Length,
            "One version timeout should produce exactly one version retry and one probe.");
        ValidateVersionRequest(requests[0], fixture.ToolPath);
        ValidateVersionRequest(requests[1], fixture.ToolPath);
        TestAssert.True(
            ReferenceEquals(requests[0], requests[1]),
            "The retry should reuse the same immutable bounded request.");
        ValidateProbeRequest(
            requests[2],
            fixture.ToolPath,
            fixture.MediaPath);
        TestAssert.Equal(
            "ffprobe version replay-foundry-test",
            result.Manifest.ToolVersion,
            "The successful retry should supply inspection provenance.");
    }

    private static void EvictsPersistentTimeoutBeforeLaterCall() =>
        EvictsPersistentTimeoutBeforeLaterCallAsync()
            .GetAwaiter()
            .GetResult();

    private static async Task EvictsPersistentTimeoutBeforeLaterCallAsync()
    {
        using var fixture = new ProbeFixture();
        var runner = new ScriptedProcessRunner((invocation, request, _) =>
            invocation switch
            {
                1 or 2 => TimedOut(request),
                3 => VersionSucceeded(),
                4 => ProbeSucceeded(),
                _ => UnexpectedInvocation(invocation),
            });
        var probe = new FfprobeMediaProbe(
            runner,
            fixture.ToolLocator);

        MediaProbeException exception =
            await CaptureAsync<MediaProbeException>(() =>
                probe.ProbeAsync(
                    fixture.MediaPath,
                    CancellationToken.None));

        TestAssert.True(
            exception.Message.Contains(
                "version verification timed out after two attempts",
                StringComparison.Ordinal),
            "A persistent timeout should report the bounded verification failure accurately.");
        TestAssert.True(
            exception.InnerException is ProcessTimeoutException,
            "The media failure should retain the typed process timeout.");
        TestAssert.Equal(
            2,
            runner.InvocationCount,
            "A single manifest load must stop after one internal retry.");

        MediaProbeResult result = await probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);

        TestAssert.Equal(
            4,
            runner.InvocationCount,
            "A later call should replace the faulted manifest task and then probe once.");
        TestAssert.Equal(
            fixture.ToolPath,
            result.Manifest.ToolPath,
            "The recovered manifest should retain the resolved tool path.");
    }

    private static void DoesNotRetryNonTimeoutFailure() =>
        DoesNotRetryNonTimeoutFailureAsync()
            .GetAwaiter()
            .GetResult();

    private static async Task DoesNotRetryNonTimeoutFailureAsync()
    {
        using var fixture = new ProbeFixture();
        var runner = new ScriptedProcessRunner((invocation, _, _) =>
            invocation switch
            {
                1 => Failed(
                    new ProcessOutputLimitException(
                        "standard output",
                        64 * 1024)),
                2 => VersionSucceeded(),
                3 => ProbeSucceeded(),
                _ => UnexpectedInvocation(invocation),
            });
        var probe = new FfprobeMediaProbe(
            runner,
            fixture.ToolLocator);

        MediaProbeException exception =
            await CaptureAsync<MediaProbeException>(() =>
                probe.ProbeAsync(
                    fixture.MediaPath,
                    CancellationToken.None));

        TestAssert.Equal(
            1,
            runner.InvocationCount,
            "A non-timeout process failure must not consume the timeout-only retry.");
        TestAssert.True(
            exception.Message.Contains(
                "version verification could not complete",
                StringComparison.Ordinal),
            "A non-timeout failure should not be described as a timeout.");
        TestAssert.True(
            exception.InnerException is ProcessExecutionException and
            not ProcessTimeoutException,
            "The original non-timeout process failure should remain available.");

        _ = await probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);
        TestAssert.Equal(
            3,
            runner.InvocationCount,
            "A later call should replace the non-timeout faulted manifest without an internal retry.");
    }

    private static void EvictsCanceledManifestBeforeLaterCall() =>
        EvictsCanceledManifestBeforeLaterCallAsync()
            .GetAwaiter()
            .GetResult();

    private static async Task EvictsCanceledManifestBeforeLaterCallAsync()
    {
        using var fixture = new ProbeFixture();
        var runner = new ScriptedProcessRunner((invocation, _, _) =>
            invocation switch
            {
                1 => Task.FromCanceled<ProcessRunResult>(
                    new CancellationToken(canceled: true)),
                2 => VersionSucceeded(),
                3 => ProbeSucceeded(),
                _ => UnexpectedInvocation(invocation),
            });
        var probe = new FfprobeMediaProbe(
            runner,
            fixture.ToolLocator);

        _ = await CaptureAsync<OperationCanceledException>(() =>
            probe.ProbeAsync(
                fixture.MediaPath,
                CancellationToken.None));

        _ = await probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);
        TestAssert.Equal(
            3,
            runner.InvocationCount,
            "A canceled manifest task should be replaced on the next call.");
    }

    private static void SharesInflightVersionVerification() =>
        SharesInflightVersionVerificationAsync()
            .GetAwaiter()
            .GetResult();

    private static async Task SharesInflightVersionVerificationAsync()
    {
        using var fixture = new ProbeFixture();
        var versionGate = new TaskCompletionSource<ProcessRunResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ScriptedProcessRunner((invocation, _, _) =>
            invocation switch
            {
                1 => versionGate.Task,
                2 or 3 or 4 => ProbeSucceeded(),
                _ => UnexpectedInvocation(invocation),
            });
        var probe = new FfprobeMediaProbe(
            runner,
            fixture.ToolLocator);

        Task<MediaProbeResult> first = probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);
        Task<MediaProbeResult> second = probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);

        TestAssert.Equal(
            1,
            runner.InvocationCount,
            "Concurrent callers should share the same in-flight manifest task.");

        versionGate.SetResult(CreateVersionResult());
        await Task.WhenAll(first, second);

        _ = await probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);

        ProcessRunRequest[] requests = runner.GetRequests();
        TestAssert.Equal(
            1,
            requests.Count(IsVersionRequest),
            "Concurrent first probes should execute one version verification.");
        TestAssert.Equal(
            3,
            requests.Count(request => !IsVersionRequest(request)),
            "Each caller should inspect its source while retaining the successful manifest.");
    }

    private static void CallerCancellationPreservesSharedInitialization() =>
        CallerCancellationPreservesSharedInitializationAsync()
            .GetAwaiter()
            .GetResult();

    private static async Task CallerCancellationPreservesSharedInitializationAsync()
    {
        using var fixture = new ProbeFixture();
        using var cancellationSource = new CancellationTokenSource();
        var versionGate = new TaskCompletionSource<ProcessRunResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ScriptedProcessRunner((invocation, _, _) =>
            invocation switch
            {
                1 => versionGate.Task,
                2 => ProbeSucceeded(),
                _ => UnexpectedInvocation(invocation),
            });
        var probe = new FfprobeMediaProbe(
            runner,
            fixture.ToolLocator);

        Task<MediaProbeResult> canceledCaller = probe.ProbeAsync(
            fixture.MediaPath,
            cancellationSource.Token);
        cancellationSource.Cancel();

        OperationCanceledException exception =
            await CaptureAsync<OperationCanceledException>(async () =>
                _ = await canceledCaller);
        TestAssert.Equal(
            cancellationSource.Token,
            exception.CancellationToken,
            "The canceled caller should retain its own cancellation token.");

        Task<MediaProbeResult> continuingCaller = probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);
        TestAssert.Equal(
            1,
            runner.InvocationCount,
            "Caller cancellation must not evict a live shared manifest task.");

        versionGate.SetResult(CreateVersionResult());
        _ = await continuingCaller;

        ProcessRunRequest[] requests = runner.GetRequests();
        TestAssert.Equal(
            1,
            requests.Count(IsVersionRequest),
            "The continuing caller should reuse the initialization begun by the canceled caller.");
        TestAssert.Equal(
            1,
            requests.Count(request => !IsVersionRequest(request)),
            "Only the continuing caller should reach media inspection.");
    }

    private static void SharesReplacementAfterCompletedFault() =>
        SharesReplacementAfterCompletedFaultAsync()
            .GetAwaiter()
            .GetResult();

    private static async Task SharesReplacementAfterCompletedFaultAsync()
    {
        using var fixture = new ProbeFixture();
        var replacementGate = new TaskCompletionSource<ProcessRunResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ScriptedProcessRunner((invocation, request, _) =>
            invocation switch
            {
                1 or 2 => TimedOut(request),
                3 => replacementGate.Task,
                4 or 5 => ProbeSucceeded(),
                _ => UnexpectedInvocation(invocation),
            });
        var probe = new FfprobeMediaProbe(
            runner,
            fixture.ToolLocator);

        _ = await CaptureAsync<MediaProbeException>(() =>
            probe.ProbeAsync(
                fixture.MediaPath,
                CancellationToken.None));

        Task<MediaProbeResult> firstReplacementCaller = probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);
        Task<MediaProbeResult> secondReplacementCaller = probe.ProbeAsync(
            fixture.MediaPath,
            CancellationToken.None);
        TestAssert.Equal(
            3,
            runner.InvocationCount,
            "Callers arriving after a fault should share one replacement manifest task.");

        replacementGate.SetResult(CreateVersionResult());
        await Task.WhenAll(
            firstReplacementCaller,
            secondReplacementCaller);

        ProcessRunRequest[] requests = runner.GetRequests();
        TestAssert.Equal(
            3,
            requests.Count(IsVersionRequest),
            "Two exhausted attempts should be followed by one shared replacement verification.");
        TestAssert.Equal(
            2,
            requests.Count(request => !IsVersionRequest(request)),
            "Both replacement callers should inspect their source after shared recovery.");
    }

    private static void ValidateVersionRequest(
        ProcessRunRequest request,
        string expectedToolPath)
    {
        TestAssert.Equal(
            expectedToolPath,
            request.ExecutablePath,
            "Version verification should use the resolved ffprobe path.");
        TestAssert.True(
            request.Arguments.SequenceEqual(
                ["-version"],
                StringComparer.Ordinal),
            "Version verification should pass only '-version'.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(30),
            request.Timeout,
            "Version verification should allow a bounded cold start.");
        TestAssert.Equal(
            64 * 1024,
            request.MaxStandardOutputCharacters,
            "Version output should remain bounded to 64 KiB.");
        TestAssert.Equal(
            64 * 1024,
            request.MaxStandardErrorCharacters,
            "Version diagnostics should remain bounded to 64 KiB.");
        TestAssert.Null(
            request.WorkingDirectory,
            "The runner should derive the version working directory from the tool path.");
    }

    private static void ValidateProbeRequest(
        ProcessRunRequest request,
        string expectedToolPath,
        string expectedMediaPath)
    {
        string[] expectedArguments =
        [
            "-hide_banner",
            "-v",
            "error",
            "-print_format",
            "json",
            "-show_error",
            "-show_format",
            "-show_streams",
            expectedMediaPath,
        ];

        TestAssert.Equal(
            expectedToolPath,
            request.ExecutablePath,
            "Media inspection should reuse the verified ffprobe path.");
        TestAssert.True(
            request.Arguments.SequenceEqual(
                expectedArguments,
                StringComparer.Ordinal),
            "Media inspection arguments should remain ordered and source-bound.");
        TestAssert.Equal(
            TimeSpan.FromMinutes(2),
            request.Timeout,
            "Media inspection should retain its independent two-minute deadline.");
        TestAssert.Equal(
            8 * 1024 * 1024,
            request.MaxStandardOutputCharacters,
            "Inspection JSON should retain its bounded output allowance.");
        TestAssert.Equal(
            1024 * 1024,
            request.MaxStandardErrorCharacters,
            "Inspection diagnostics should retain their bounded allowance.");
    }

    private static bool IsVersionRequest(
        ProcessRunRequest request) =>
        request.Arguments.Count == 1 &&
        string.Equals(
            request.Arguments[0],
            "-version",
            StringComparison.Ordinal);

    private static Task<ProcessRunResult> TimedOut(
        ProcessRunRequest request) =>
        Failed(
            new ProcessTimeoutException(
                request.ExecutablePath,
                request.Timeout));

    private static Task<ProcessRunResult> VersionSucceeded() =>
        Task.FromResult(CreateVersionResult());

    private static ProcessRunResult CreateVersionResult() =>
        new(
            0,
            "ffprobe version replay-foundry-test\r\nconfiguration: test",
            string.Empty,
            TimeSpan.FromMilliseconds(5));

    private static Task<ProcessRunResult> ProbeSucceeded() =>
        Task.FromResult(
            new ProcessRunResult(
                0,
                ProbeJson,
                string.Empty,
                TimeSpan.FromMilliseconds(10)));

    private static Task<ProcessRunResult> Failed(
        Exception exception) =>
        Task.FromException<ProcessRunResult>(exception);

    private static Task<ProcessRunResult> UnexpectedInvocation(
        int invocation) =>
        Failed(
            new InvalidOperationException(
                $"Unexpected process-runner invocation {invocation}."));

    private static async Task<TException> CaptureAsync<TException>(
        Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}, but received " +
                $"{exception.GetType().Name}.",
                exception);
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private sealed class ScriptedProcessRunner : IProcessRunner
    {
        private readonly Func<
            int,
            ProcessRunRequest,
            CancellationToken,
            Task<ProcessRunResult>> _run;
        private readonly object _sync = new();
        private readonly List<ProcessRunRequest> _requests = [];
        private int _invocationCount;

        public ScriptedProcessRunner(
            Func<
                int,
                ProcessRunRequest,
                CancellationToken,
                Task<ProcessRunResult>> run)
        {
            _run = run;
        }

        public int InvocationCount
        {
            get
            {
                lock (_sync)
                {
                    return _invocationCount;
                }
            }
        }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            int invocation;

            lock (_sync)
            {
                invocation = ++_invocationCount;
                _requests.Add(request);
            }

            return _run(
                invocation,
                request,
                cancellationToken);
        }

        public ProcessRunRequest[] GetRequests()
        {
            lock (_sync)
            {
                return [.. _requests];
            }
        }
    }

    private sealed class ProbeFixture : IDisposable
    {
        public ProbeFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundryInspectionTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ToolPath = Path.Combine(Root, "ffprobe.exe");
            string ffmpegPath = Path.Combine(Root, "ffmpeg.exe");
            MediaPath = Path.Combine(Root, "source.mkv");
            File.WriteAllText(ToolPath, "test tool placeholder");
            File.WriteAllText(ffmpegPath, "test tool placeholder");
            File.WriteAllText(MediaPath, "test media placeholder");
            ToolLocator = new FixedFfmpegToolLocator(
                ffmpegPath,
                ToolPath);
        }

        public string Root { get; }

        public string ToolPath { get; }

        public IFfmpegToolLocator ToolLocator { get; }

        public string MediaPath { get; }

        public void Dispose()
        {
            Directory.Delete(
                Root,
                recursive: true);
        }
    }

    private sealed class FixedFfmpegToolLocator(
        string ffmpegPath,
        string ffprobePath) : IFfmpegToolLocator
    {
        public string LocateFfmpeg() => ffmpegPath;

        public string LocateFfprobe() => ffprobePath;
    }
}
