using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationEvidenceAnalysisTests
{
    private static CompositionPlan CreatePlan(
        PreparedGenerationSource source,
        NormalizedRectangle? gameplayGeometry,
        string gameplayId,
        CompositionRegionTraits gameplayTraits,
        bool includeSecondary,
        CompositionRegionRole secondaryRole,
        bool useTwoIntervals,
        DateTimeOffset createdAtUtc)
    {
        CompositionRegion[] regions =
            CreateRegions(
                gameplayGeometry,
                gameplayId,
                gameplayTraits,
                includeSecondary,
                secondaryRole);

        if (!useTwoIntervals)
        {
            return ManualCompositionPlanFactory
                .CreateUserConfirmedSingleInterval(
                    source.Source.FullPath,
                    source.Media.Duration,
                    regions,
                    createdAtUtc);
        }

        TimeSpan split =
            TimeSpan.FromTicks(
                source.Media.Duration.Ticks /
                3);

        return new CompositionPlan(
            source.Source.FullPath,
            source.Media.Duration,
            CompositionCoordinateSpace
                .EffectiveDisplayNormalizedBeforeCrop,
            [
                new CompositionLayoutInterval(
                    TimeSpan.Zero,
                    split,
                    regions),
                new CompositionLayoutInterval(
                    split,
                    source.Media.Duration,
                    CreateRegions(
                        gameplayGeometry,
                        gameplayId,
                        gameplayTraits,
                        includeSecondary,
                        secondaryRole)),
            ],
            CompositionCoverage.CreateManual(
                source.Media.Duration),
            new CompositionPlanManifest(
                CompositionPlan.CurrentSchemaVersion,
                CompositionPlan
                    .CurrentCoordinateSpaceVersion,
                "ReplayFoundry.ManualCompositionPlan",
                "1.0",
                CompositionPlanOrigin.Manual,
                createdAtUtc));
    }

    private static CompositionRegion[] CreateRegions(
        NormalizedRectangle? gameplayGeometry,
        string gameplayId,
        CompositionRegionTraits gameplayTraits,
        bool includeSecondary,
        CompositionRegionRole secondaryRole)
    {
        var regions =
            new List<CompositionRegion>
            {
                new(
                    gameplayId,
                    gameplayGeometry ??
                    NormalizedRectangle.FullFrame,
                    CompositionRegionRole.Gameplay,
                    gameplayTraits,
                    CompositionConfidence.Certain,
                    CompositionConfidence.Certain,
                    CompositionValueSource.UserConfirmed,
                    CompositionValueSource.UserConfirmed),
            };

        if (includeSecondary)
        {
            regions.Add(
                new CompositionRegion(
                    "secondary",
                    new NormalizedRectangle(
                        0.1,
                        0.7,
                        0.4,
                        0.25),
                    secondaryRole,
                    CompositionRegionRoleDefaults
                        .GetTraits(secondaryRole),
                    CompositionConfidence.Certain,
                    CompositionConfidence.Certain,
                    CompositionValueSource.UserConfirmed,
                    CompositionValueSource.UserConfirmed));
        }

        return regions.ToArray();
    }

    private static CompositionPlan CreatePlanForMedia(
        MediaProbeResult media)
    {
        return ManualCompositionPlanFactory
            .CreateUserConfirmedSingleInterval(
                media.FullPath,
                media.Duration,
                [
                    new CompositionRegion(
                        "gameplay",
                        NormalizedRectangle.FullFrame,
                        CompositionRegionRole.Gameplay,
                        CompositionRegionTraits.Dynamic,
                        CompositionConfidence.Certain,
                        CompositionConfidence.Certain,
                        CompositionValueSource.UserConfirmed,
                        CompositionValueSource.UserConfirmed),
                ],
                new DateTimeOffset(
                    2026,
                    7,
                    26,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));
    }

    private static ServiceContext CreateServiceContext(
        GenerationEvidenceAnalysisRequest request,
        Func<
            MediaEvidenceAnalysisRequest,
            IProgress<MediaEvidenceProgressUpdate>?,
            CancellationToken,
            int,
            Task<MediaEvidenceResult>>? handler = null)
    {
        var analyzer =
            new RecordingEvidenceAnalyzer(
                handler);

        var provider =
            CreateFreshSnapshotProvider(
                request.Preparation);

        var validator =
            new GenerationSourceFreshnessValidator(
                provider);

        var service =
            new GenerationEvidenceAnalysisService(
                analyzer,
                validator);

        return new ServiceContext(
            analyzer,
            provider,
            service);
    }

    private static CoordinatorContext
        CreateCoordinatorContext(
            GenerationEvidenceAnalysisRequest request,
            Func<
                MediaEvidenceAnalysisRequest,
                IProgress<MediaEvidenceProgressUpdate>?,
                CancellationToken,
                int,
                Task<MediaEvidenceResult>>? handler = null)
    {
        ServiceContext serviceContext =
            CreateServiceContext(
                request,
                handler);

        var validator =
            new GenerationSourceFreshnessValidator(
                serviceContext.SnapshotProvider);

        var coordinator =
            new GenerationEvidenceAnalysisCoordinator(
                serviceContext.Service,
                validator,
                request.Settings);

        return new CoordinatorContext(
            serviceContext.Analyzer,
            serviceContext.SnapshotProvider,
            coordinator);
    }

    private static FakeGenerationSourceFileSnapshotProvider
        CreateFreshSnapshotProvider(
            GenerationSourcePreparationResult preparation)
    {
        var provider =
            new FakeGenerationSourceFileSnapshotProvider();

        foreach (PreparedGenerationSource source in
                 preparation.Sources)
        {
            provider.SetDefault(
                source.FileSnapshot);
        }

        return provider;
    }

    private static Task<GenerationEvidenceAnalysisResult>
        Analyze(
            CoordinatorContext context,
            GenerationEvidenceAnalysisRequest request)
    {
        return context.Coordinator
            .GetOrAnalyzeAsync(
                request,
                progress: null,
                CancellationToken.None);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow +
            TimeSpan.FromSeconds(2);

        while (!condition())
        {
            if (DateTimeOffset.UtcNow >=
                deadline)
            {
                throw new TimeoutException(
                    "The asynchronous test condition did not complete.");
            }

            await Task.Delay(10);
        }
    }

    private sealed record ServiceContext(
        RecordingEvidenceAnalyzer Analyzer,
        FakeGenerationSourceFileSnapshotProvider SnapshotProvider,
        GenerationEvidenceAnalysisService Service);

    private sealed record CoordinatorContext(
        RecordingEvidenceAnalyzer Analyzer,
        FakeGenerationSourceFileSnapshotProvider SnapshotProvider,
        GenerationEvidenceAnalysisCoordinator Coordinator);

    private sealed class RecordingEvidenceAnalyzer :
        IMediaEvidenceAnalyzer
    {
        private readonly Func<
            MediaEvidenceAnalysisRequest,
            IProgress<MediaEvidenceProgressUpdate>?,
            CancellationToken,
            int,
            Task<MediaEvidenceResult>>? _handler;

        private MediaEvidenceAnalyzerIdentity _identity =
            new(
                "test-analyzer",
                "3.0.0");

        private int _activeCalls;
        private int _maximumConcurrentCalls;

        public RecordingEvidenceAnalyzer(
            Func<
                MediaEvidenceAnalysisRequest,
                IProgress<MediaEvidenceProgressUpdate>?,
                CancellationToken,
                int,
                Task<MediaEvidenceResult>>? handler = null)
        {
            _handler = handler;
        }

        public MediaEvidenceAnalyzerIdentity Identity =>
            _identity;

        public List<MediaEvidenceAnalysisRequest> Requests
        {
            get;
        } = [];

        public int MaximumConcurrentCalls =>
            _maximumConcurrentCalls;

        public void SetIdentity(
            string name,
            string version)
        {
            _identity =
                new MediaEvidenceAnalyzerIdentity(
                    name,
                    version);
        }

        public async Task<MediaEvidenceResult> AnalyzeAsync(
            MediaEvidenceAnalysisRequest request,
            IProgress<MediaEvidenceProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            int invocation =
                Requests.Count + 1;

            Requests.Add(request);

            int active =
                Interlocked.Increment(
                    ref _activeCalls);

            UpdateMaximum(active);

            try
            {
                if (_handler is not null)
                {
                    return await _handler(
                        request,
                        progress,
                        cancellationToken,
                        invocation);
                }

                ReportDefaultProgress(
                    request,
                    progress);

                await Task.Yield();
                cancellationToken
                    .ThrowIfCancellationRequested();

                return TestMediaFactory
                    .CreateMediaEvidenceResult(
                        request,
                        Identity.Name,
                        Identity.Version);
            }
            finally
            {
                Interlocked.Decrement(
                    ref _activeCalls);
            }
        }

        private void UpdateMaximum(
            int active)
        {
            int observed;

            do
            {
                observed =
                    _maximumConcurrentCalls;

                if (active <= observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                       ref _maximumConcurrentCalls,
                       active,
                       observed) !=
                   observed);
        }

        private static void ReportDefaultProgress(
            MediaEvidenceAnalysisRequest request,
            IProgress<MediaEvidenceProgressUpdate>? progress)
        {
            int totalPasses =
                2 +
                request.Media.AudioStreams.Count;

            progress?.Report(
                new MediaEvidenceProgressUpdate(
                    MediaEvidenceAnalysisPhase.Preparing,
                    "Preparing.",
                    0));

            progress?.Report(
                new MediaEvidenceProgressUpdate(
                    MediaEvidenceAnalysisPhase.ScenePassStarted,
                    "Scenes active.",
                    0));

            progress?.Report(
                new MediaEvidenceProgressUpdate(
                    MediaEvidenceAnalysisPhase.ScenePassCompleted,
                    "Scenes complete.",
                    100d / totalPasses));

            progress?.Report(
                new MediaEvidenceProgressUpdate(
                    MediaEvidenceAnalysisPhase.VisualIntervalPassStarted,
                    "Visual intervals active.",
                    100d / totalPasses));

            progress?.Report(
                new MediaEvidenceProgressUpdate(
                    MediaEvidenceAnalysisPhase.VisualIntervalPassCompleted,
                    "Visual intervals complete.",
                    200d / totalPasses));

            int completed =
                2;

            foreach (AudioStreamInfo stream in
                     request.Media.AudioStreams)
            {
                progress?.Report(
                    new MediaEvidenceProgressUpdate(
                        MediaEvidenceAnalysisPhase.AudioPassStarted,
                        "Audio active.",
                        completed /
                        (double)totalPasses *
                        100,
                        stream.Index));

                completed++;

                progress?.Report(
                    new MediaEvidenceProgressUpdate(
                        MediaEvidenceAnalysisPhase.AudioPassCompleted,
                        "Audio complete.",
                        completed /
                        (double)totalPasses *
                        100,
                        stream.Index));
            }

            progress?.Report(
                new MediaEvidenceProgressUpdate(
                    MediaEvidenceAnalysisPhase.Completed,
                    "Complete.",
                    100));
        }
    }
}
