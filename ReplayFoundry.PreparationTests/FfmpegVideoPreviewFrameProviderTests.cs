using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal static class FfmpegVideoPreviewFrameProviderTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Preview provider reads PNG records provenance and cleans workspace",
            ExtractsAndCleans),
        new(
            "Preview provider cleans workspace after FFmpeg failure",
            CleansAfterFailure),
        new(
            "Preview provider preserves cancellation and cleans workspace",
            CleansAfterCancellation),
        new(
            "Preview provider rejects missing output and cleans workspace",
            RejectsMissingOutput),
        new(
            "Preview provider rejects empty output and cleans workspace",
            RejectsEmptyOutput),
        new(
            "Preview provider rejects invalid PNG and cleans workspace",
            RejectsInvalidPng),
        new(
            "Preview provider rejects unexpected dimensions and cleans workspace",
            RejectsUnexpectedDimensions),
        new(
            "Preview provider retries faulted FFmpeg initialization",
            RetriesFaultedToolInitialization),
    ];

    private static async Task ExtractsAndCleans()
    {
        using TestProviderContext context =
            CreateContext();

        EnqueueVersionSuccess(context);

        context.Runner.Enqueue(
            (request, _) =>
            {
                string outputPath =
                    request.Arguments[^1];

                File.WriteAllBytes(
                    outputPath,
                    TestMediaFactory.CreatePngHeader(
                        1280,
                        720));

                return Task.FromResult(
                    new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        TimeSpan.FromMilliseconds(20)));
            });

        string sourcePath =
            TestMediaFactory.CreateSourcePath(
                "preview-success.mkv");

        var media =
            TestMediaFactory.Create(
                sourcePath,
                width: 1920,
                height: 1080,
                videoStreamIndex: 4);

        VideoPreviewFrame frame =
            await context.Provider.GetFrameAsync(
                new VideoPreviewFrameRequest(
                    media,
                    TimeSpan.FromSeconds(30)),
                CancellationToken.None);

        TestAssert.Equal(
            sourcePath,
            frame.SourcePath,
            "Source identity should be preserved.");

        TestAssert.Equal(
            4,
            frame.VideoStreamIndex,
            "The absolute primary stream index should be preserved.");

        TestAssert.Equal(
            1280,
            frame.Width,
            "Preview width should match the command.");

        TestAssert.Equal(
            720,
            frame.Height,
            "Preview height should match the command.");

        TestAssert.Equal(
            CompositionCoordinateSpace
                .EffectiveDisplayNormalizedBeforeCrop,
            frame.CoordinateSpace,
            "Coordinate space should match composition contracts.");

        TestAssert.Null(
            frame.DecodedTimestamp,
            "Missing decoder timestamp evidence must remain null.");

        TestAssert.Equal(
            "ffmpeg version test",
            frame.Manifest.ToolVersion,
            "Tool version provenance should be preserved.");

        TestAssert.Equal(
            context.FfmpegPath,
            frame.Manifest.ToolPath,
            "Tool path provenance should be preserved.");

        TestAssert.Equal(
            TimeSpan.FromMilliseconds(20),
            frame.Manifest.ProcessDuration,
            "Extraction process duration should be preserved.");

        TestAssert.Equal(
            33,
            frame.PngData.Length,
            "PNG bytes should survive workspace cleanup.");

        AssertWorkspaceRemoved(
            context,
            "Temporary workspace should be removed after success.");
    }

    private static async Task CleansAfterFailure()
    {
        using TestProviderContext context =
            CreateContext();

        EnqueueVersionSuccess(context);

        context.Runner.Enqueue(
            static (_, _) =>
                Task.FromResult(
                    new ProcessRunResult(
                        1,
                        string.Empty,
                        "synthetic failure",
                        TimeSpan.Zero)));

        VideoPreviewFrameException exception =
            await TestAssert.ThrowsAsync<
                VideoPreviewFrameException>(
                () => GetDefaultFrameAsync(
                    context,
                    "preview-failure.mkv"),
                "FFmpeg failure should be translated.");

        TestAssert.Equal(
            "synthetic failure",
            exception.DiagnosticDetails,
            "FFmpeg diagnostics should be retained.");

        AssertWorkspaceRemoved(
            context,
            "Temporary workspace should be removed after failure.");
    }

    private static async Task CleansAfterCancellation()
    {
        using TestProviderContext context =
            CreateContext();

        EnqueueVersionSuccess(context);

        using var cancellationSource =
            new CancellationTokenSource();

        context.Runner.Enqueue(
            (_, cancellationToken) =>
            {
                cancellationSource.Cancel();

                return Task.FromCanceled<ProcessRunResult>(
                    cancellationToken);
            });

        OperationCanceledException exception =
            await TestAssert.ThrowsAsync<
                OperationCanceledException>(
                () => GetDefaultFrameAsync(
                    context,
                    "preview-cancel.mkv",
                    cancellationSource.Token),
                "Cancellation should be preserved.");

        TestAssert.Equal(
            cancellationSource.Token,
            exception.CancellationToken,
            "Provider cancellation should retain the caller token.");

        AssertWorkspaceRemoved(
            context,
            "Cancellation should remove the workspace.");
    }

    private static async Task RejectsMissingOutput()
    {
        using TestProviderContext context =
            CreateContext();

        EnqueueVersionSuccess(context);

        context.Runner.Enqueue(
            static (_, _) =>
                Task.FromResult(
                    new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        TimeSpan.Zero)));

        await TestAssert.ThrowsAsync<
            VideoPreviewFrameException>(
            () => GetDefaultFrameAsync(
                context,
                "preview-missing.mkv"),
            "Missing output should fail.");

        AssertWorkspaceRemoved(
            context,
            "Missing output should remove the workspace.");
    }

    private static async Task RejectsEmptyOutput()
    {
        using TestProviderContext context =
            CreateContext();

        EnqueueVersionSuccess(context);

        context.Runner.Enqueue(
            (request, _) =>
            {
                File.WriteAllBytes(
                    request.Arguments[^1],
                    []);

                return Task.FromResult(
                    new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        TimeSpan.Zero));
            });

        await TestAssert.ThrowsAsync<
            VideoPreviewFrameException>(
            () => GetDefaultFrameAsync(
                context,
                "preview-empty.mkv"),
            "Empty output should fail.");

        AssertWorkspaceRemoved(
            context,
            "Empty output should remove the workspace.");
    }

    private static async Task RejectsInvalidPng()
    {
        using TestProviderContext context =
            CreateContext();

        EnqueueVersionSuccess(context);

        context.Runner.Enqueue(
            (request, _) =>
            {
                File.WriteAllBytes(
                    request.Arguments[^1],
                    new byte[33]);

                return Task.FromResult(
                    new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        TimeSpan.Zero));
            });

        await TestAssert.ThrowsAsync<
            VideoPreviewFrameException>(
            () => GetDefaultFrameAsync(
                context,
                "preview-invalid.mkv"),
            "Malformed PNG output should fail.");

        AssertWorkspaceRemoved(
            context,
            "Invalid PNG output should remove the workspace.");
    }

    private static async Task RejectsUnexpectedDimensions()
    {
        using TestProviderContext context =
            CreateContext();

        EnqueueVersionSuccess(context);

        context.Runner.Enqueue(
            (request, _) =>
            {
                File.WriteAllBytes(
                    request.Arguments[^1],
                    TestMediaFactory.CreatePngHeader(
                        640,
                        360));

                return Task.FromResult(
                    new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        TimeSpan.Zero));
            });

        VideoPreviewFrameException exception =
            await TestAssert.ThrowsAsync<
                VideoPreviewFrameException>(
                () => GetDefaultFrameAsync(
                    context,
                    "preview-wrong-size.mkv"),
                "Unexpected PNG dimensions should fail.");

        TestAssert.True(
            exception.DiagnosticDetails?.Contains(
                "Expected 1280x720",
                StringComparison.Ordinal) == true,
            "Expected and actual dimensions should be diagnosable.");

        AssertWorkspaceRemoved(
            context,
            "Unexpected PNG dimensions should remove the workspace.");
    }

    private static async Task RetriesFaultedToolInitialization()
    {
        using TestProviderContext context =
            CreateContext();

        context.Runner.Enqueue(
            static (_, _) =>
                Task.FromResult(
                    new ProcessRunResult(
                        1,
                        string.Empty,
                        "version failed",
                        TimeSpan.Zero)));

        await TestAssert.ThrowsAsync<
            VideoPreviewFrameException>(
            () => GetDefaultFrameAsync(
                context,
                "first-attempt.mkv"),
            "The first version lookup should fail.");

        EnqueueVersionSuccess(context);

        context.Runner.Enqueue(
            (request, _) =>
            {
                File.WriteAllBytes(
                    request.Arguments[^1],
                    TestMediaFactory.CreatePngHeader(
                        1280,
                        720));

                return Task.FromResult(
                    new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        TimeSpan.Zero));
            });

        VideoPreviewFrame frame =
            await GetDefaultFrameAsync(
                context,
                "second-attempt.mkv");

        TestAssert.Equal(
            1280,
            frame.Width,
            "A faulted initialization must not poison later requests.");

        TestAssert.Equal(
            3,
            context.Runner.Requests.Count,
            "The provider should retry version discovery before extraction.");
    }

    private static void EnqueueVersionSuccess(
        TestProviderContext context)
    {
        context.Runner.Enqueue(
            static (_, _) =>
                Task.FromResult(
                    new ProcessRunResult(
                        0,
                        "ffmpeg version test",
                        string.Empty,
                        TimeSpan.FromMilliseconds(1))));
    }

    private static Task<VideoPreviewFrame> GetDefaultFrameAsync(
        TestProviderContext context,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        string sourcePath =
            TestMediaFactory.CreateSourcePath(
                fileName);

        return context.Provider.GetFrameAsync(
            new VideoPreviewFrameRequest(
                TestMediaFactory.Create(sourcePath),
                TimeSpan.Zero),
            cancellationToken);
    }

    private static void AssertWorkspaceRemoved(
        TestProviderContext context,
        string message)
    {
        TestAssert.True(
            context.WorkspaceFactory.LastDirectory is not null &&
            !Directory.Exists(
                context.WorkspaceFactory.LastDirectory),
            message);
    }

    private static TestProviderContext CreateContext()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundryPreparationTests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        string ffmpegPath =
            Path.Combine(
                root,
                "ffmpeg.exe");

        File.WriteAllText(
            ffmpegPath,
            "test");

        var runner =
            new ScriptedProcessRunner();

        var workspaceFactory =
            new TestPreviewWorkspaceFactory(
                Path.Combine(
                    root,
                    "workspaces"));

        var provider =
            new FfmpegVideoPreviewFrameProvider(
                runner,
                new FixedFfmpegToolLocator(
                    ffmpegPath),
                workspaceFactory);

        return new TestProviderContext(
            root,
            ffmpegPath,
            runner,
            workspaceFactory,
            provider);
    }

    private sealed class TestProviderContext :
        IDisposable
    {
        private readonly string _root;
        public TestProviderContext(
            string root,
            string ffmpegPath,
            ScriptedProcessRunner runner,
            TestPreviewWorkspaceFactory workspaceFactory,
            FfmpegVideoPreviewFrameProvider provider)
        {
            _root = root;
            FfmpegPath = ffmpegPath;
            Runner = runner;
            WorkspaceFactory = workspaceFactory;
            Provider = provider;
        }

        public string FfmpegPath { get; }

        public ScriptedProcessRunner Runner { get; }

        public TestPreviewWorkspaceFactory WorkspaceFactory { get; }

        public FfmpegVideoPreviewFrameProvider Provider { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(
                    _root,
                    recursive: true);
            }
        }
    }

    private sealed class FixedFfmpegToolLocator(
        string ffmpegPath) :
        IFfmpegToolLocator
    {
        public string LocateFfprobe() =>
            throw new NotSupportedException();

        public string LocateFfmpeg() => ffmpegPath;
    }
}
