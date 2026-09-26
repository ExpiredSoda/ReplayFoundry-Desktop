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
    private static async Task CoordinatorReusesAndRebinds()
    {
        GenerationEvidenceAnalysisRequest first =
            CreateRequest();

        CoordinatorContext context =
            CreateCoordinatorContext(first);

        GenerationEvidenceAnalysisResult initial =
            await context.Coordinator
                .GetOrAnalyzeAsync(
                    first,
                    progress: null,
                    CancellationToken.None);

        GenerationCompositionReviewResult recreated =
            CreateReview(
                first.Preparation,
                createdAtUtc:
                    new DateTimeOffset(
                        2026,
                        7,
                        27,
                        12,
                        0,
                        0,
                        TimeSpan.Zero));

        var currentRequest =
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                recreated,
                first.Settings);

        var progress =
            new RecordingProgress<
                GenerationEvidenceAnalysisProgress>();

        GenerationEvidenceAnalysisResult rebound =
            await context.Coordinator
                .GetOrAnalyzeAsync(
                    currentRequest,
                    progress,
                    CancellationToken.None);

        TestAssert.Equal(
            1,
            context.Analyzer.Requests.Count,
            "A compatible request should not invoke the analyzer again.");

        TestAssert.Same(
            currentRequest,
            rebound.Request,
            "A cache hit should bind the result to the current request.");

        TestAssert.Same(
            currentRequest.SourcePlans[0],
            rebound.Sources[0].CompositionPlan,
            "A cache hit should bind payloads to current composition plans.");

        TestAssert.Same(
            initial.Sources[0].Evidence,
            rebound.Sources[0].Evidence,
            "Immutable evidence payloads should be reused.");

        TestAssert.True(
            progress.Values.Any(
                static update =>
                    update.Phase ==
                    GenerationEvidenceAnalysisPhase
                        .UsingSavedEvidence),
            "A cache hit should report saved evidence.");
    }

    private static async Task
        CoordinatorIgnoresCompositionCreationTime()
    {
        GenerationEvidenceAnalysisRequest first =
            CreateRequest();

        CoordinatorContext context =
            CreateCoordinatorContext(first);

        await Analyze(
            context,
            first);

        GenerationCompositionReviewResult recreated =
            CreateReview(
                first.Preparation,
                createdAtUtc:
                    new DateTimeOffset(
                        2030,
                        1,
                        1,
                        0,
                        0,
                        0,
                        TimeSpan.Zero));

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                recreated,
                first.Settings));

        TestAssert.Equal(
            1,
            context.Analyzer.Requests.Count,
            "CreatedAtUtc alone must not invalidate semantic evidence.");
    }

    private static async Task GeometryChangeInvalidates()
    {
        await AssertCompositionMutationInvalidates(
            (preparation, createdAt) =>
                CreateReview(
                    preparation,
                    gameplayGeometry:
                        new NormalizedRectangle(
                            0.05,
                            0,
                            0.95,
                            1),
                    createdAtUtc:
                        createdAt),
            "Geometry changes should rerun evidence.");
    }

    private static async Task IntervalAndIdChangesInvalidate()
    {
        await AssertCompositionMutationInvalidates(
            (preparation, createdAt) =>
                CreateReview(
                    preparation,
                    gameplayId:
                        "renamed-gameplay",
                    createdAtUtc:
                        createdAt),
            "Region identifier changes should rerun evidence.");

        GenerationEvidenceAnalysisRequest first =
            CreateRequest();

        CoordinatorContext context =
            CreateCoordinatorContext(first);

        await Analyze(context, first);

        GenerationCompositionReviewResult intervals =
            CreateReview(
                first.Preparation,
                useTwoIntervals: true);

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                intervals,
                first.Settings));

        TestAssert.Equal(
            2,
            context.Analyzer.Requests.Count,
            "Interval timing changes should rerun evidence.");
    }

    private static async Task RoleAndTraitChangesInvalidate()
    {
        await AssertCompositionMutationInvalidates(
            (preparation, createdAt) =>
                CreateReview(
                    preparation,
                    includeSecondary: true,
                    secondaryRole:
                        CompositionRegionRole.Overlay,
                    createdAtUtc:
                        createdAt),
            "Role changes should rerun evidence.",
            initialReviewFactory:
                preparation =>
                    CreateReview(
                        preparation,
                        includeSecondary: true,
                        secondaryRole:
                            CompositionRegionRole.Presenter));

        await AssertCompositionMutationInvalidates(
            (preparation, createdAt) =>
                CreateReview(
                    preparation,
                    gameplayTraits:
                        CompositionRegionTraits.Static,
                    createdAtUtc:
                        createdAt),
            "Trait changes should rerun evidence.");
    }

    private static async Task
        SourceSnapshotChangeFailsFreshness()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        CoordinatorContext context =
            CreateCoordinatorContext(request);

        await Analyze(
            context,
            request);

        PreparedGenerationSource source =
            request.PreparedSources[0];

        context.SnapshotProvider.SetDefault(
            new GenerationSourceFileSnapshot(
                source.Source.FullPath,
                source.FileSnapshot.FileLength + 1,
                source.FileSnapshot
                    .LastWriteTimeUtc));

        await TestAssert.ThrowsAsync<
            GenerationSourcePreparationException>(
                () =>
                    Analyze(
                        context,
                        request),
                "A changed file snapshot should fail freshness before reuse.");

        TestAssert.Null(
            context.Coordinator.Current,
            "Stale cached evidence should not remain current.");
    }

    private static async Task SettingsChangesInvalidate()
    {
        GenerationEvidenceAnalysisRequest first =
            CreateRequest();

        CoordinatorContext context =
            CreateCoordinatorContext(first);

        await Analyze(context, first);

        var changedOptions =
            new GenerationEvidenceAnalysisSettings(
                first.Settings.Options
                    .WithSceneThresholdPercent(44),
                first.Settings
                    .IncludedRegionRoles,
                first.Settings.SummaryOptions);

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                first.CompositionReview,
                changedOptions));

        TestAssert.Equal(
            2,
            context.Analyzer.Requests.Count,
            "Analysis-option changes should rerun evidence.");

        var changedRoles =
            new GenerationEvidenceAnalysisSettings(
                first.Settings.Options,
                [
                    CompositionRegionRole.Gameplay,
                    CompositionRegionRole.Presenter,
                    CompositionRegionRole.Overlay,
                ],
                first.Settings.SummaryOptions);

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                first.CompositionReview,
                changedRoles));

        TestAssert.Equal(
            3,
            context.Analyzer.Requests.Count,
            "Included-role changes should rerun evidence.");

        var changedSummaryOptions =
            new GenerationEvidenceAnalysisSettings(
                changedRoles.Options,
                changedRoles.IncludedRegionRoles,
                new MediaEvidenceSummaryOptions(
                    sceneClusterMaximumGap:
                        TimeSpan.FromSeconds(11),
                    sceneDensityBucketDuration:
                        changedRoles.SummaryOptions
                            .SceneDensityBucketDuration,
                    silenceMergeTolerance:
                        changedRoles.SummaryOptions
                            .SilenceMergeTolerance,
                    shortSilenceMaximum:
                        changedRoles.SummaryOptions
                            .ShortSilenceMaximum,
                    longSilenceMinimum:
                        changedRoles.SummaryOptions
                            .LongSilenceMinimum,
                    darkLumaThreshold:
                        changedRoles.SummaryOptions
                            .DarkLumaThreshold,
                    brightLumaThreshold:
                        changedRoles.SummaryOptions
                            .BrightLumaThreshold,
                    signalSummaryPolicyVersion:
                        changedRoles.SummaryOptions
                            .SignalSummaryPolicyVersion));

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                first.CompositionReview,
                changedSummaryOptions));

        TestAssert.Equal(
            4,
            context.Analyzer.Requests.Count,
            "Summary-option changes should rerun evidence.");
    }

    private static async Task
        AnalyzerVersionChangeInvalidates()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        CoordinatorContext context =
            CreateCoordinatorContext(request);

        await Analyze(context, request);

        context.Analyzer.SetIdentity(
            "test-analyzer",
            "3.1.0");

        await Analyze(context, request);

        TestAssert.Equal(
            2,
            context.Analyzer.Requests.Count,
            "Analyzer implementation version should participate in reuse.");
    }

    private static async Task
        SignalCadenceChangesInvalidate()
    {
        GenerationEvidenceAnalysisRequest first =
            CreateRequest();
        CoordinatorContext context =
            CreateCoordinatorContext(first);

        await Analyze(context, first);

        var visualChanged =
            new GenerationEvidenceAnalysisSettings(
                first.Settings.Options
                    .WithSignalSampling(
                        TimeSpan.FromMilliseconds(250),
                        first.Settings.Options
                            .AudioSignalWindowDuration),
                first.Settings.IncludedRegionRoles,
                first.Settings.SummaryOptions);

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                first.CompositionReview,
                visualChanged));

        var audioChanged =
            new GenerationEvidenceAnalysisSettings(
                visualChanged.Options
                    .WithSignalSampling(
                        visualChanged.Options
                            .VisualSignalSampleInterval,
                        TimeSpan.FromMilliseconds(750)),
                visualChanged.IncludedRegionRoles,
                visualChanged.SummaryOptions);

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                first.Preparation,
                first.CompositionReview,
                audioChanged));

        TestAssert.Equal(
            3,
            context.Analyzer.Requests.Count,
            "Both visual cadence and audio window duration must participate in the structural fingerprint.");
    }

    private static async Task
        CancellationAndFailureDoNotCache()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        using var cancellationSource =
            new CancellationTokenSource();

        CoordinatorContext cancelled =
            CreateCoordinatorContext(
                request,
                async (
                    analyzerRequest,
                    progress,
                    cancellationToken,
                    invocation) =>
                {
                    cancellationSource.Cancel();
                    await Task.Yield();
                    cancellationToken
                        .ThrowIfCancellationRequested();

                    return TestMediaFactory
                        .CreateMediaEvidenceResult(
                            analyzerRequest);
                });

        await TestAssert.ThrowsAsync<
            OperationCanceledException>(
                () =>
                    cancelled.Coordinator
                        .GetOrAnalyzeAsync(
                            request,
                            progress: null,
                            cancellationSource.Token),
                "Cancellation should propagate from the coordinator.");

        TestAssert.Null(
            cancelled.Coordinator.Current,
            "Cancellation must not cache a partial batch.");

        CoordinatorContext failed =
            CreateCoordinatorContext(
                request,
                (
                    analyzerRequest,
                    progress,
                    cancellationToken,
                    invocation) =>
                    Task.FromException<
                        MediaEvidenceResult>(
                        new MediaEvidenceAnalysisException(
                            "synthetic failure")));

        await TestAssert.ThrowsAsync<
            GenerationEvidenceAnalysisException>(
                () =>
                    Analyze(
                        failed,
                        request),
                "Analysis failure should propagate.");

        TestAssert.Null(
            failed.Coordinator.Current,
            "Failure must not cache a partial batch.");
    }

    private static async Task
        ConcurrentRequestsDoNotDuplicateAnalysis()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        var gate =
            new TaskCompletionSource(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        CoordinatorContext context =
            CreateCoordinatorContext(
                request,
                async (
                    analyzerRequest,
                    progress,
                    cancellationToken,
                    invocation) =>
                {
                    await gate.Task.WaitAsync(
                        cancellationToken);

                    return TestMediaFactory
                        .CreateMediaEvidenceResult(
                            analyzerRequest);
                });

        Task<GenerationEvidenceAnalysisResult> first =
            Analyze(context, request);

        Task<GenerationEvidenceAnalysisResult> second =
            Analyze(context, request);

        await WaitUntilAsync(
            () =>
                context.Analyzer.Requests.Count == 1);

        gate.SetResult();

        GenerationEvidenceAnalysisResult[] results =
            await Task.WhenAll(
                first,
                second);

        TestAssert.Equal(
            1,
            context.Analyzer.Requests.Count,
            "Concurrent compatible callers should share one analysis.");

        TestAssert.Same(
            results[0].Sources[0].Evidence,
            results[1].Sources[0].Evidence,
            "The waiting caller should receive rebound immutable payloads.");
    }

    private static async Task
        AssertCompositionMutationInvalidates(
            Func<
                GenerationSourcePreparationResult,
                DateTimeOffset,
                GenerationCompositionReviewResult>
                mutationFactory,
            string message,
            Func<
                GenerationSourcePreparationResult,
                GenerationCompositionReviewResult>?
                initialReviewFactory = null)
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation();

        GenerationCompositionReviewResult initialReview =
            initialReviewFactory?.Invoke(preparation) ??
            CreateReview(preparation);

        var first =
            new GenerationEvidenceAnalysisRequest(
                preparation,
                initialReview,
                GenerationEvidenceAnalysisSettings
                    .CreateDefault());

        CoordinatorContext context =
            CreateCoordinatorContext(first);

        await Analyze(context, first);

        GenerationCompositionReviewResult changed =
            mutationFactory(
                preparation,
                new DateTimeOffset(
                    2026,
                    7,
                    28,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));

        await Analyze(
            context,
            new GenerationEvidenceAnalysisRequest(
                preparation,
                changed,
                first.Settings));

        TestAssert.Equal(
            2,
            context.Analyzer.Requests.Count,
            message);
    }

    internal static GenerationEvidenceAnalysisRequest
        CreateRequest(
            int sourceCount = 1,
            int referenceIndex = 0)
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
                sourceCount,
                referenceIndex);

        return new GenerationEvidenceAnalysisRequest(
            preparation,
            CreateReview(preparation),
            GenerationEvidenceAnalysisSettings
                .CreateDefault());
    }

    internal static GenerationSourcePreparationResult
        CreatePreparation(
            int sourceCount = 1,
            int referenceIndex = 0)
    {
        return PreparedGenerationWorkflowTests
            .CreatePreparation(
                Enumerable.Range(
                        0,
                        sourceCount)
                    .Select(
                        index =>
                            (
                                TestMediaFactory.CreateSourcePath(
                                    $"evidence-integration-{sourceCount}-{referenceIndex}-{index}.mkv"),
                                index == referenceIndex,
                                true)));
    }

    internal static GenerationCompositionReviewResult
        CreateReview(
            GenerationSourcePreparationResult preparation,
            NormalizedRectangle? gameplayGeometry = null,
            string gameplayId = "gameplay",
            CompositionRegionTraits gameplayTraits =
                CompositionRegionTraits.Dynamic,
            bool includeSecondary = false,
            CompositionRegionRole secondaryRole =
                CompositionRegionRole.Presenter,
            bool useTwoIntervals = false,
            DateTimeOffset? createdAtUtc = null)
    {
        var request =
            new GenerationCompositionReviewRequest(
                preparation);

        DateTimeOffset timestamp =
            createdAtUtc ??
            new DateTimeOffset(
                2026,
                7,
                26,
                12,
                0,
                0,
                TimeSpan.Zero);

        return new GenerationCompositionReviewResult(
            request,
            preparation.Sources.Select(
                source =>
                    new PreparedSourceCompositionPlan(
                        source,
                        CreatePlan(
                            source,
                            gameplayGeometry,
                            gameplayId,
                            gameplayTraits,
                            includeSecondary,
                            secondaryRole,
                            useTwoIntervals,
                            timestamp))));
    }

}
