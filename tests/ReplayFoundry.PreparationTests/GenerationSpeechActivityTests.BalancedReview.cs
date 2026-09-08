using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static async Task BalancedAiReviewsBeforeWriting()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Balanced, [("balanced-ai-review.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(5), desiredCount: 1, qualityThreshold: 1);
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = new FakeVisualEditorialProvider();
        var writer = new RecordingEditorialMetadataGenerationService();
        var mappedReview = new RecordingMapReview(new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings()));
        GenerationPipelineRunner Pipeline(bool review) => new(new GenerationPreflightRunner(),
            new GenerationMomentFindingService(new GenerationMomentFindingTests.RecordingMomentFinder([[95, 80]])),
            new ReviewTestOutputPath(), new GenerationEditorialMetadataService(writer, new ClipEditorialProfileSession()),
            speechActivity: new ReviewTestSpeech(), candidateRefinement: new GenerationCandidateRefinementService(),
            visualSemantic: review ? mappedReview : null);
        await TestAssert.ThrowsAsync<GenerationEngineUnavailableException>(() => Pipeline(false).RunAsync(request,
            new RecordingProgress<GenerationProgressUpdate>(), CancellationToken.None), "Balanced AI must not silently skip visual selection.");
        TestAssert.Equal(0, writer.Requests.Count, "Missing picture review must stop before title writing.");
        var result = await Pipeline(true).RunAsync(request, new RecordingProgress<GenerationProgressUpdate>(), CancellationToken.None);
        TestAssert.True(provider.Requests.Count > 0 && writer.Requests.Count > 0, "Balanced AI must review and write.");
        TestAssert.Equal(1, mappedReview.IndexCalls, "Balanced AI must use the recording map to nominate useful regions before close review.");
        TestAssert.True(writer.Requests.Where(item => item.Preference == ReplayFoundry.Desktop.Media.Intelligence.Editorial.ClipEditorialGenerationPreference.AiRequired)
            .All(item => item.ReviewVideo is not null && provider.Requests
            .SelectMany(batch => batch.Requests).Any(review => review.Input.ReviewVideoSha256 == item.ReviewVideo.ReviewVideoSha256 &&
                review.CandidateEndRelative - review.CandidateStartRelative == item.Context.Duration)),
            "Each generated AI title must follow picture review of the selected cut; hidden moments keep simple labels.");
        TestAssert.True(result.Candidates.Count > 0 && materializer.CleanupCount > 0, "The pipeline releases review media after handing off its draft.");
    }

    private sealed class RecordingMapReview(IGenerationVisualSemanticAnalysisService inner) : IGenerationVisualSemanticAnalysisService
    {
        public int IndexCalls { get; private set; }
        public Task<GenerationCandidateIntelligenceResult> IndexRecordingAsync(GenerationCandidateIntelligenceResult intelligence,
            IProgress<string>? progress, CancellationToken cancellationToken)
        {
            IndexCalls++;
            return inner.IndexRecordingAsync(intelligence, progress, cancellationToken);
        }
        public Task<GenerationVisualSemanticAnalysisResult> AnalyzeAsync(GenerationCandidateIntelligenceResult intelligence,
            IProgress<GenerationVisualSemanticProgress>? progress, CancellationToken cancellationToken) =>
            inner.AnalyzeAsync(intelligence, progress, cancellationToken);
        public Task<GenerationVisualSemanticAnalysisResult> ReviewPromotedAsync(GenerationCandidateIntelligenceResult intelligence,
            IReadOnlyList<GenerationMomentCandidate> selected, GenerationVisualSemanticAnalysisResult previous,
            IProgress<GenerationVisualSemanticProgress>? progress, CancellationToken cancellationToken) =>
            inner.ReviewPromotedAsync(intelligence, selected, previous, progress, cancellationToken);
    }

    private sealed class ReviewTestSpeech : IGenerationSpeechActivityService
    {
        public Task<GenerationSpeechActivityResult> AnalyzeAsync(GenerationRequest request,
            IProgress<GenerationSpeechActivityProgress> progress, CancellationToken cancellationToken) =>
            Task.FromResult(CreateSpeech(request, AudioContentRoleAssignment.Unknown, []));
    }

    private sealed class ReviewTestOutputPath : IGenerationOutputPathProvider
    {
        public string CreateOutputDirectoryPath(GenerationMomentFindingResult moments) => Path.Combine(Path.GetTempPath(), "review-pipeline-output");
    }
}
