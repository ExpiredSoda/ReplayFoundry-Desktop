using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.MomentGuidance;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.PreparationTests;

internal static class CompositionReviewPreviewTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Composition preview timestamp policy is deterministic and safe",
            InitialTimestampIsDeterministic),
        new(
            "Composition review initially loads only the reference preview",
            InitiallyLoadsOnlyReference),
        new(
            "Composition review lazily loads a newly selected source",
            SelectionLoadsPreviewLazily),
        new(
            "Composition preview reuses the same loaded timestamp",
            SameTimestampReusesFrame),
        new(
            "Composition preview reloads one changed timestamp",
            ChangedTimestampLoadsOnce),
        new(
            "Composition preview prevents duplicate simultaneous extraction",
            DuplicateExtractionIsPrevented),
        new(
            "Composition preview failure exposes retry state",
            PreviewFailureCanRetry),
        new(
            "Composition review disposal cancels active extraction",
            DisposalCancelsPreview),
        new(
            "Composition review initialization reports expected lifecycle cancellation",
            InitializationReportsLifecycleCancellation),
        new(
            "Unexpected preview cancellation is owned by observable preview state",
            UnexpectedCancellationIsObservable),
        new(
            "Composition preview observer records an escaped failure",
            ObserverFailureIsObservable),
        new(
            "Composition preview does not fabricate decoded timestamp",
            DecodedTimestampIsNotFabricated),
        new(
            "New source confirmation requires successful preview extraction",
            ConfirmationRequiresPreview),
        new(
            "Priority Moments preview follows the selected scrub position",
            MomentGuidancePreviewFollowsScrub),
        new(
            "Layout region types apply internal behavior defaults",
            RegionRoleAppliesBehaviorDefaults),
        new(
            "Layout resize deltas use the logical preview coordinate space",
            ResizeDeltaUsesLogicalPreviewSpace),
    ];

    private static Task ResizeDeltaUsesLogicalPreviewSpace()
    {
        const double previewWidth = 1080;
        const double previewHeight = 1920;

        TestAssert.Equal(
            12 / previewWidth,
            CompositionRegionGeometryEditor.NormalizeDragDelta(
                12,
                previewWidth),
            "A horizontal Thumb delta inside the Viewbox must be normalized against the unscaled preview width.");
        TestAssert.Equal(
            12 / previewHeight,
            CompositionRegionGeometryEditor.NormalizeDragDelta(
                12,
                previewHeight),
            "A vertical Thumb delta inside the Viewbox must be normalized against the unscaled preview height.");

        return Task.CompletedTask;
    }

    private static async Task MomentGuidancePreviewFollowsScrub()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();
        ScriptedPreviewProvider provider = CreateSuccessfulProvider();
        using var source = new MomentGuidanceSourceViewModel(
            preparation.ReferenceSource,
            [],
            static () => { },
            provider);

        source.CurrentPositionSeconds = 42.875;
        await source.RefreshPreviewAsync();

        TestAssert.Equal(
            TimeSpan.FromSeconds(42.875),
            provider.Requests[^1].Timestamp,
            "The visual frame must retain the exact Priority Moments scrub position.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(42.875),
            source.Preview!.RequestedTimestamp,
            "Formatting must not reduce the requested preview timestamp precision.");
        TestAssert.Equal(
            "0:42",
            source.Preview.RequestedTimestampText,
            "The requested preview timestamp should display whole seconds.");
        source.AddPointCommand.Execute(null);
        TestAssert.Equal(1, source.Items.Count, "A visible tick should be retained.");
        TestAssert.Equal(42.875, source.Items[0].StartSeconds, "Tick position.");
    }

    private static Task RegionRoleAppliesBehaviorDefaults()
    {
        var regions = new CompositionRegionCollectionViewModel(static () => { });
        CompositionRegionDraftViewModel region = regions.AddRegion(
            CompositionRegionRole.Presenter);
        region.Role = CompositionRegionRole.Overlay;

        TestAssert.Equal(
            CompositionRegionRoleDefaults.GetTraits(CompositionRegionRole.Overlay),
            region.Traits,
            "Hidden behavior traits should follow the user-facing region type.");
        return Task.CompletedTask;
    }

    private static Task InitialTimestampIsDeterministic()
    {
        TimeSpan timestamp =
            CompositionPreviewTimestampPolicy
                .GetInitialTimestamp(
                    TimeSpan.FromMinutes(10));

        TestAssert.Equal(
            TimeSpan.FromMinutes(1),
            timestamp,
            "The initial timestamp should use ten percent.");

        TimeSpan tinyDuration =
            TimeSpan.FromTicks(1);

        TimeSpan tinyTimestamp =
            CompositionPreviewTimestampPolicy
                .GetInitialTimestamp(
                    tinyDuration);

        TestAssert.True(
            tinyTimestamp >= TimeSpan.Zero &&
            tinyTimestamp < tinyDuration,
            "Extremely short sources need a valid in-range timestamp.");

        return Task.CompletedTask;
    }

    private static async Task InitiallyLoadsOnlyReference()
    {
        GenerationSourcePreparationResult preparation =
            CreateThreeSourcePreparation();

        var provider =
            new ScriptedPreviewProvider(
                static (
                    request,
                    _) =>
                    Task.FromResult(
                        CreateFrame(request)));

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                provider);

        await viewModel.InitializeAsync();

        TestAssert.Equal(
            1,
            provider.Requests.Count,
            "Only one preview should load on open.");

        TestAssert.Equal(
            preparation.ReferenceSource.Source.FullPath,
            provider.Requests[0].Media.FullPath,
            "The explicit reference should load first.");
    }

    private static async Task SelectionLoadsPreviewLazily()
    {
        GenerationSourcePreparationResult preparation =
            CreateThreeSourcePreparation();

        var provider =
            new ScriptedPreviewProvider(
                static (
                    request,
                    _) =>
                    Task.FromResult(
                        CreateFrame(request)));

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                provider);

        await viewModel.InitializeAsync();

        viewModel.SelectedSource =
            viewModel.Sources[0];

        await viewModel.LoadSelectedPreviewAsync();

        TestAssert.Equal(
            2,
            provider.Requests.Count,
            "Selecting another source should load exactly one additional preview.");

        TestAssert.Equal(
            preparation.Sources[0].Source.FullPath,
            provider.Requests[1].Media.FullPath,
            "The selected source should be loaded lazily.");
    }

    private static async Task SameTimestampReusesFrame()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var provider =
            CreateSuccessfulProvider();

        using var source =
            new CompositionReviewSourceViewModel(
                preparation.Sources[0],
                isReference: true,
                provider);

        await source.LoadPreviewAsync();

        VideoPreviewFrame retained =
            source.PreviewFrame!;

        await source.LoadPreviewAsync();

        TestAssert.Equal(
            1,
            provider.Requests.Count,
            "The same timestamp should reuse its loaded frame.");

        TestAssert.Same(
            retained,
            source.PreviewFrame!,
            "The cached frame object should be retained.");
    }

    private static async Task ChangedTimestampLoadsOnce()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var provider =
            CreateSuccessfulProvider();

        using var source =
            new CompositionReviewSourceViewModel(
                preparation.Sources[0],
                isReference: true,
                provider);

        await source.LoadPreviewAsync();

        source.RequestedTimestampSeconds +=
            5;

        await source.LoadPreviewAsync();

        TestAssert.Equal(
            2,
            provider.Requests.Count,
            "A changed timestamp should cause one new extraction.");

        TestAssert.Equal(
            source.RequestedTimestamp,
            provider.Requests[1].Timestamp,
            "The second request should use the changed timestamp.");
    }

    private static async Task DuplicateExtractionIsPrevented()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var gate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var started =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var provider =
            new ScriptedPreviewProvider(
                async (
                    request,
                    cancellationToken) =>
                {
                    started.TrySetResult();

                    await gate.Task.WaitAsync(
                        cancellationToken);

                    return CreateFrame(request);
                });

        using var source =
            new CompositionReviewSourceViewModel(
                preparation.Sources[0],
                isReference: true,
                provider);

        Task first =
            source.LoadPreviewAsync();

        await started.Task;

        Task second =
            source.LoadPreviewAsync();

        TestAssert.Same(
            first,
            second,
            "Duplicate calls should share the active extraction task.");

        TestAssert.Equal(
            1,
            provider.Requests.Count,
            "Only one provider call should be active.");

        gate.SetResult();

        await Task.WhenAll(
            first,
            second);

        TestAssert.Equal(
            1,
            provider.MaximumConcurrentCalls,
            "A source must never extract two frames simultaneously.");
    }

    private static async Task PreviewFailureCanRetry()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        int attempts = 0;

        var provider =
            new ScriptedPreviewProvider(
                (
                    request,
                    _) =>
                {
                    attempts++;

                    if (attempts == 1)
                    {
                        throw new VideoPreviewFrameException(
                            "Synthetic preview failure.");
                    }

                    return Task.FromResult(
                        CreateFrame(request));
                });

        using var source =
            new CompositionReviewSourceViewModel(
                preparation.Sources[0],
                isReference: true,
                provider);

        await source.LoadPreviewAsync();

        TestAssert.True(
            source.HasPreviewError,
            "A provider failure should be visible.");

        TestAssert.True(
            source.CanRetryPreview,
            "A failed source should expose retry state.");

        await source.LoadPreviewAsync();

        TestAssert.True(
            source.HasPreviewFrame,
            "Retry should retain a successful frame.");

        TestAssert.False(
            source.HasPreviewError,
            "A successful retry should clear the error.");
    }

    private static async Task DisposalCancelsPreview()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var started =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        bool cancellationObserved = false;

        var provider =
            new ScriptedPreviewProvider(
                async (
                    _,
                    cancellationToken) =>
                {
                    started.TrySetResult();

                    try
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved = true;
                        throw;
                    }

                    throw new InvalidOperationException();
                });

        var source =
            new CompositionReviewSourceViewModel(
                preparation.Sources[0],
                isReference: true,
                provider);

        Task load =
            source.LoadPreviewAsync();

        await started.Task;

        source.Dispose();

        await TestAssert.ThrowsAsync<
            OperationCanceledException>(
                () => load,
                "Disposal should cancel the in-flight preview.");

        TestAssert.True(
            cancellationObserved,
            "The provider should observe cancellation.");
    }

    private static async Task
        InitializationReportsLifecycleCancellation()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var started =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var provider =
            new ScriptedPreviewProvider(
                async (
                    _,
                    cancellationToken) =>
                {
                    started.TrySetResult();
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);

                    throw new InvalidOperationException(
                        "The cancelled preview should not complete.");
                });

        var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                provider);

        Task<CompositionReviewInitializationOutcome>
            initialization =
            viewModel.InitializeAsync();

        await started.Task;
        viewModel.Dispose();

        CompositionReviewInitializationOutcome outcome =
            await initialization;

        TestAssert.Equal(
            CompositionReviewInitializationOutcome
                .LifecycleCancelled,
            outcome,
            "Window-close disposal must produce the explicit lifecycle-cancelled outcome.");
    }

    private static async Task UnexpectedCancellationIsObservable()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var provider =
            new ScriptedPreviewProvider(
                static (
                    _,
                    _) =>
                    Task.FromException<VideoPreviewFrame>(
                        new OperationCanceledException(
                            "unexpected provider cancellation")));

        using var viewModel =
            new CompositionReviewViewModel(
                new GenerationCompositionReviewRequest(
                    preparation),
                provider);

        CompositionReviewInitializationOutcome outcome =
            await viewModel.InitializeAsync();

        TestAssert.Equal(
            CompositionReviewInitializationOutcome.Completed,
            outcome,
            "A provider cancellation unrelated to lifecycle disposal must not masquerade as lifecycle cancellation.");

        TestAssert.True(
            viewModel.SelectedSource.PreviewError?.Contains(
                "unexpected provider cancellation",
                StringComparison.Ordinal) == true,
            "Unexpected provider cancellation must be visible in preview error state.");
    }

    private static Task ObserverFailureIsObservable()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        using var source =
            new CompositionReviewSourceViewModel(
                preparation.ReferenceSource,
                isReference: true,
                CreateSuccessfulProvider());

        source.ReportUnexpectedPreviewFailure(
            new InvalidOperationException(
                "escaped observer failure"));

        TestAssert.True(
            source.PreviewError?.Contains(
                "escaped observer failure",
                StringComparison.Ordinal) == true,
            "An escaped observer failure must have an explicit observable owner.");

        return Task.CompletedTask;
    }

    private static async Task DecodedTimestampIsNotFabricated()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var provider =
            CreateSuccessfulProvider();

        using var source =
            new CompositionReviewSourceViewModel(
                preparation.Sources[0],
                isReference: true,
                provider);

        await source.LoadPreviewAsync();

        TestAssert.Null(
            source.PreviewFrame!.DecodedTimestamp,
            "The provider's unavailable decoded timestamp should remain null.");

        TestAssert.Null(
            source.ActualDecodedTimestampText,
            "Presentation state must not substitute the requested timestamp.");
    }

    private static async Task ConfirmationRequiresPreview()
    {
        GenerationSourcePreparationResult preparation =
            CreateSingleSourcePreparation();

        var provider =
            CreateSuccessfulProvider();

        using var source =
            new CompositionReviewSourceViewModel(
                preparation.Sources[0],
                isReference: true,
                provider);

        TestAssert.False(
            source.TryConfirm(
                DateTimeOffset.UtcNow),
            "A new draft should not confirm before preview succeeds.");

        await source.LoadPreviewAsync();

        TestAssert.True(
            source.TryConfirm(
                DateTimeOffset.UtcNow),
            "A valid draft should confirm after preview succeeds.");
    }

    private static ScriptedPreviewProvider
        CreateSuccessfulProvider()
    {
        return new ScriptedPreviewProvider(
            static (
                request,
                _) =>
                Task.FromResult(
                    CreateFrame(request)));
    }

    private static VideoPreviewFrame CreateFrame(
        VideoPreviewFrameRequest request)
    {
        return new VideoPreviewFrame(
            request.Media.FullPath,
            request.Media.Duration,
            request.Media.PrimaryVideoStream.Index,
            request.Timestamp,
            decodedTimestamp: null,
            width: 1280,
            height: 720,
            coordinateSpace:
                CompositionCoordinateSpace
                    .EffectiveDisplayNormalizedBeforeCrop,
            pngData:
                TestMediaFactory.CreatePngHeader(
                    1280,
                    720),
            manifest:
                new VideoPreviewFrameManifest(
                "CompositionReviewPreviewTests",
                "1.0",
                "ffmpeg",
                "ffmpeg test",
                Path.GetFullPath(
                    Path.Combine(
                        Path.GetTempPath(),
                        "ffmpeg.exe")),
                new DateTimeOffset(
                    2026,
                    7,
                    26,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                TimeSpan.FromMilliseconds(10)));
    }

    private static GenerationSourcePreparationResult
        CreateSingleSourcePreparation()
    {
        return PreparedGenerationWorkflowTests
            .CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "composition-preview-single.mkv"),
                    true,
                    true),
            ]);
    }

    private static GenerationSourcePreparationResult
        CreateThreeSourcePreparation()
    {
        return PreparedGenerationWorkflowTests
            .CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "composition-preview-first.mkv"),
                    false,
                    true),
                (
                    TestMediaFactory.CreateSourcePath(
                        "composition-preview-reference.mkv"),
                    true,
                    true),
                (
                    TestMediaFactory.CreateSourcePath(
                        "composition-preview-third.mkv"),
                    false,
                    true),
            ]);
    }

    private sealed class ScriptedPreviewProvider :
        IVideoPreviewFrameProvider
    {
        private readonly Func<
            VideoPreviewFrameRequest,
            CancellationToken,
            Task<VideoPreviewFrame>> _handler;

        private int _activeCalls;

        public ScriptedPreviewProvider(
            Func<
                VideoPreviewFrameRequest,
                CancellationToken,
                Task<VideoPreviewFrame>> handler)
        {
            _handler = handler;
        }

        public List<VideoPreviewFrameRequest> Requests
        {
            get;
        } = [];

        public int MaximumConcurrentCalls { get; private set; }

        public async Task<VideoPreviewFrame> GetFrameAsync(
            VideoPreviewFrameRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            _activeCalls++;
            MaximumConcurrentCalls =
                Math.Max(
                    MaximumConcurrentCalls,
                    _activeCalls);

            try
            {
                return await _handler(
                    request,
                    cancellationToken);
            }
            finally
            {
                _activeCalls--;
            }
        }
    }
}
