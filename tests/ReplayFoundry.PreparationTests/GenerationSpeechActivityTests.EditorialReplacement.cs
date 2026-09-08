using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static async Task EditorialRejectionUsesReviewedReplacement()
    {
        foreach (var scenario in new[]
        {
            (Human: false, Kind: ClipEditorialAiFailureKind.CaseRejected, Failures: 1, ExpectedCalls: 2),
            (Human: true, Kind: ClipEditorialAiFailureKind.CaseRejected, Failures: 1, ExpectedCalls: 1),
            (Human: false, Kind: ClipEditorialAiFailureKind.ProviderFailed, Failures: 1, ExpectedCalls: 1),
            (Human: false, Kind: ClipEditorialAiFailureKind.CaseRejected, Failures: 10, ExpectedCalls: 3),
        })
        {
            string path = TestMediaFactory.CreateSourcePath("editorial-replacement.mkv");
            var guidance = scenario.Human ? new GenerationMomentGuidance([
                UserMomentGuidance.CreateRange(path, TimeSpan.FromMinutes(5), TimeSpan.Zero, TimeSpan.FromSeconds(299))]) : null;
            var request = CreateRequest(GenerationAnalysisDepth.Balanced, [("editorial-replacement.mkv", 1)],
                sourceDuration: TimeSpan.FromMinutes(5), desiredCount: 1, qualityThreshold: 1, momentGuidance: guidance);
            using var materializer = new FakeVisualReviewMaterializer();
            var provider = new FakeVisualEditorialProvider();
            var writer = new RejectingEditorialService(scenario.Kind, scenario.Failures);
            var pipeline = new GenerationPipelineRunner(new GenerationPreflightRunner(),
                new GenerationMomentFindingService(new GenerationMomentFindingTests.RecordingMomentFinder([[99, 98, 97, 96]])),
                new ReviewTestOutputPath(), writer, speechActivity: new ReviewTestSpeech(),
                candidateRefinement: new GenerationCandidateRefinementService(),
                visualSemantic: new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings()));
            if (!scenario.Human && scenario.Kind == ClipEditorialAiFailureKind.CaseRejected && scenario.Failures == 1)
            {
                var result = await pipeline.RunAsync(request, new RecordingProgress<GenerationProgressUpdate>(), CancellationToken.None);
                TestAssert.False(writer.RejectedIds.Contains(result.Candidates.Single().Id),
                    "An automatic cut with rejected wording must be replaced, not relabeled as successful.");
                TestAssert.True(result.CandidateIntelligence!.VisualSemantic!.Observations.Any(review =>
                    ReferenceEquals(review.Candidate, result.Moments.SelectedCandidates.Single().Candidate)),
                    "The replacement must already have passed picture review.");
                TestAssert.True(result.HiddenMoments.Moments.Any(item => writer.RejectedIds.Contains(item.Id)),
                    "The original cut remains available for manual review.");
            }
            else
                await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(() => pipeline.RunAsync(request,
                    new RecordingProgress<GenerationProgressUpdate>(), CancellationToken.None),
                    "Runtime failures, user choices and exhausted replacement budgets must not be silently bypassed.");
            TestAssert.Equal(scenario.ExpectedCalls, writer.Calls, "Editorial replacement work is bounded.");
        }
    }

    private sealed class RejectingEditorialService(ClipEditorialAiFailureKind kind, int failures) : IGenerationEditorialMetadataService
    {
        private readonly GenerationEditorialMetadataService _inner = new(new RecordingEditorialMetadataGenerationService(), new ClipEditorialProfileSession());
        public int Calls { get; private set; }
        public List<string> RejectedIds { get; } = [];
        public Task<GenerationEditorialMetadataResult> GenerateAsync(GenerationMomentFindingResult moments,
            GenerationCaptionPreparationResult? captions, CancellationToken cancellationToken,
            GenerationCandidateIntelligenceResult? candidateIntelligence = null)
        {
            if (++Calls <= failures)
            {
                string id = moments.SelectedCandidates[0].Id;
                RejectedIds.Add(id);
                throw new ClipEditorialAiGenerationException(kind, "Injected per-case editorial rejection.", id);
            }
            return _inner.GenerateAsync(moments, captions, cancellationToken, candidateIntelligence);
        }
        public Task<GenerationHiddenMomentDeck> GenerateHiddenAsync(GenerationHiddenMomentDeck hiddenMoments,
            GenerationCandidateIntelligenceResult? intelligence, CancellationToken cancellationToken) =>
            _inner.GenerateHiddenAsync(hiddenMoments, intelligence, cancellationToken);
        public Task<GenerationHiddenMoment> PrepareAcceptedHiddenAsync(GenerationHiddenMoment hiddenMoment,
            GenerationCandidateCaptionTrack? captions, CancellationToken cancellationToken, IReadOnlyList<string>? existingProjectTitles = null) =>
            _inner.PrepareAcceptedHiddenAsync(hiddenMoment, captions, cancellationToken, existingProjectTitles);
    }
}
