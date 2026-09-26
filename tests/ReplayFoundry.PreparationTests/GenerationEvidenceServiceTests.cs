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
    private static async Task
        ServiceAnalyzesSourcesAndBuildsSummaries()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest(
                sourceCount: 2,
                referenceIndex: 1);

        ServiceContext context =
            CreateServiceContext(
                request);

        GenerationEvidenceAnalysisResult result =
            await context.Service.AnalyzeAsync(
                request,
                progress: null,
                CancellationToken.None);

        TestAssert.Equal(
            2,
            context.Analyzer.Requests.Count,
            "The production analyzer should be invoked exactly once per source.");

        for (int index = 0;
             index < request.SourceCount;
             index++)
        {
            TestAssert.Same(
                request.SourcePlans[index].Plan,
                context.Analyzer
                    .Requests[index]
                    .Composition!,
                "Each analyzer call should use the exact matching confirmed plan.");

            TestAssert.True(
                context.Analyzer
                    .Requests[index]
                    .IncludedRegionRoles
                    .SequenceEqual(
                    [
                        CompositionRegionRole.Gameplay,
                        CompositionRegionRole.Presenter,
                    ]),
                "Desktop analysis should request Gameplay and Presenter by default.");

            TestAssert.Equal(
                request.PreparedSources[index]
                    .Media.Duration,
                result.Sources[index]
                    .Summary.SourceDuration,
                "Every source should receive a deterministic summary.");
        }
    }

    private static async Task ServiceIsSequential()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest(sourceCount: 3);

        ServiceContext context =
            CreateServiceContext(request);

        await context.Service.AnalyzeAsync(
            request,
            progress: null,
            CancellationToken.None);

        TestAssert.Equal(
            1,
            context.Analyzer.MaximumConcurrentCalls,
            "Source analysis should remain sequential.");

        TestAssert.True(
            request.PreparedSources
                .Select(
                    static source =>
                        source.Media.FullPath)
                .SequenceEqual(
                    context.Analyzer
                        .Requests
                        .Select(
                            static analyzerRequest =>
                                analyzerRequest
                                    .Media.FullPath),
                    StringComparer.OrdinalIgnoreCase),
            "Analyzer calls should preserve preparation order.");
    }

    private static async Task
        ServiceCancellationStopsLaterSources()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest(sourceCount: 2);

        using var cancellationSource =
            new CancellationTokenSource();

        ServiceContext context =
            CreateServiceContext(
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
                    context.Service.AnalyzeAsync(
                        request,
                        progress: null,
                        cancellationSource.Token),
                "Cancellation should propagate.");

        TestAssert.Equal(
            1,
            context.Analyzer.Requests.Count,
            "Cancellation should stop before the next source.");
    }

    private static async Task
        ServicePreservesFailureDetails()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        var inner =
            new InvalidOperationException(
                "synthetic inner failure");

        var lowLevel =
            new MediaEvidenceAnalysisException(
                "Synthetic scene failure.",
                "stderr diagnostics",
                inner);

        ServiceContext context =
            CreateServiceContext(
                request,
                (
                    analyzerRequest,
                    progress,
                    cancellationToken,
                    invocation) =>
                    Task.FromException<
                        MediaEvidenceResult>(
                        lowLevel));

        GenerationEvidenceAnalysisException exception =
            await TestAssert.ThrowsAsync<
                GenerationEvidenceAnalysisException>(
                () =>
                    context.Service.AnalyzeAsync(
                        request,
                        progress: null,
                        CancellationToken.None),
                "Low-level analysis failure should be translated.");

        TestAssert.Same(
            lowLevel,
            exception.InnerException!,
            "The low-level evidence exception should remain the direct inner failure.");

        TestAssert.Equal(
            "stderr diagnostics",
            exception.DiagnosticDetails,
            "Diagnostic details should be preserved.");

        TestAssert.True(
            exception.Message.Contains(
                request.PreparedSources[0]
                    .Source.FileName,
                StringComparison.Ordinal) &&
            exception.Message.Contains(
                "video 1 of 1",
                StringComparison.OrdinalIgnoreCase),
            "Friendly failures should identify the filename and batch position.");
    }

    private static async Task
        ServiceDistinguishesToolFailure()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        ServiceContext context =
            CreateServiceContext(
                request,
                (
                    analyzerRequest,
                    progress,
                    cancellationToken,
                    invocation) =>
                    Task.FromException<
                        MediaEvidenceResult>(
                        new MediaToolNotFoundException(
                            "ffmpeg unavailable")));

        GenerationEvidenceToolUnavailableException exception =
            await TestAssert.ThrowsAsync<
                GenerationEvidenceToolUnavailableException>(
                () =>
                    context.Service.AnalyzeAsync(
                        request,
                        progress: null,
                        CancellationToken.None),
                "Tool-unavailable failures should remain distinguishable.");

        TestAssert.True(
            exception.InnerException is
                MediaToolNotFoundException,
            "The original tool failure should remain available.");
    }

    private static async Task
        ServiceTranslatesTypedProgress()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        ServiceContext context =
            CreateServiceContext(request);

        var progress =
            new RecordingProgress<
                GenerationEvidenceAnalysisProgress>();

        await context.Service.AnalyzeAsync(
            request,
            progress,
            CancellationToken.None);

        TestAssert.True(
            progress.Values.Any(
                static update =>
                    update.Phase ==
                    GenerationEvidenceAnalysisPhase
                        .StudyingSceneChanges &&
                    update.IsIndeterminate),
            "An active scene pass should be indeterminate.");

        TestAssert.True(
            progress.Values.Any(
                static update =>
                    update.Phase ==
                    GenerationEvidenceAnalysisPhase
                        .CheckingDarkAndFrozenSections &&
                    update.IsIndeterminate),
            "An active dark/freeze pass should be indeterminate.");

        GenerationEvidenceAnalysisProgress audio =
            progress.Values.First(
                static update =>
                    update.Phase ==
                    GenerationEvidenceAnalysisPhase
                        .ListeningForQuietSections);

        TestAssert.Equal(
            1,
            audio.AudioStreamIndex,
            "Absolute audio stream index should be retained.");

        double[] boundaries =
            progress.Values
                .Where(
                    static update =>
                        !update.IsIndeterminate &&
                        update.OverallPercentage
                            is not null)
                .Select(
                    static update =>
                        update.OverallPercentage!.Value)
                .ToArray();

        TestAssert.True(
            boundaries.SequenceEqual(
                boundaries.OrderBy(
                    static value =>
                        value)) &&
            boundaries[^1] == 100,
            "Real pass and batch boundaries should be monotonic and finish at 100.");
    }

}
