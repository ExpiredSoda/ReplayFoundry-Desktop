using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static IEnumerable<TestCase> PromotedReviewTests()
    {
        yield return new("Thorough reviews a promoted candidate with retained speech and without duplicate scoring", PromotedCandidateIsReviewedOnce);
        yield return new("Thorough closes count-fill to the reviewed pool within supplemental and configured budgets", PromotedReviewHonorsBudgets);
        yield return new("Supplemental review failure preserves initial evidence and releases only failed media", PromotedReviewFailurePreservesEvidence);
        yield return new("Cancellation after supplemental completion releases its media while retaining initial ownership", PromotedReviewCancellationCleansMedia);
    }

    private static GenerationCandidateIntelligenceResult PromotedIntelligence(int count = 9, int desired = 1)
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("promoted.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(count <= 9 ? 12 : 30), desiredCount: desired);
        var baseline = CreateCandidateIntelligence(request, Enumerable.Repeat(80d, count).ToArray());
        var transcript = new GenerationSourceTranscript(request.ReferenceSource.FullPath, 1,
            baseline.Refinements.Select(item => new AudioTranscriptionSegment("speech-" + item.Candidate.Id,
                "retained-source", "These retained words describe this moment.",
                item.Candidate.Window.Start + TimeSpan.FromSeconds(2), item.Candidate.Window.Start + TimeSpan.FromSeconds(5),
                item.Candidate.Window.Start + TimeSpan.FromSeconds(2), item.Candidate.Window.Start + TimeSpan.FromSeconds(5))).ToArray(), []);
        return baseline.WithTranscripts(new(baseline.BaseMoments, [transcript]));
    }

    private static FakeVisualEditorialProvider RejectFirstReviews(int count)
    {
        int reviewed = 0;
        return new FakeVisualEditorialProvider
        {
            ObservationFactory = (_, _) => reviewed++ < count
                ? CreateVisualObservation(VisualSemanticEditorialDisposition.Reject, VisualSemanticEditorialRejectReason.RoutineTraversal,
                    VisualSemanticTernary.No, "Only routine traversal is visible.")
                : CreateVisualObservation(VisualSemanticEditorialDisposition.Keep, VisualSemanticEditorialRejectReason.None, VisualSemanticTernary.Yes),
        };
    }

    private static async Task PromotedCandidateIsReviewedOnce()
    {
        var baseline = PromotedIntelligence();
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = RejectFirstReviews(8);
        var service = new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings());
        using var initial = await service.AnalyzeAsync(baseline, null, CancellationToken.None);
        var refinement = new GenerationCandidateRefinementService();
        var promoted = refinement.ApplyVisualSemantic(baseline, initial);
        var candidate = promoted.RefinedMoments.SelectedCandidates.Single().Candidate;
        TestAssert.True(initial.Observations.All(item => !ReferenceEquals(item.Candidate, candidate)),
            "The rejected initial pool must actually promote a previously unreviewed candidate in this regression.");
        using var combined = await service.ReviewPromotedAsync(baseline, promoted.RefinedMoments.SelectedCandidates,
            initial, null, CancellationToken.None);
        TestAssert.Equal(2, provider.Requests.Count, "A promoted candidate requires exactly one supplemental provider call.");
        TestAssert.Equal(1, provider.Requests[1].Requests.Count, "The supplemental batch must not rerun already reviewed candidates.");
        TestAssert.True(provider.Requests[1].Requests[0].Transcript.Spans.Single().Text.Contains("retained words", StringComparison.Ordinal),
            "The new review must reuse source-relative in-memory transcript context without another ASR pass.");
        var rescored = refinement.ApplyVisualSemantic(baseline, combined);
        var final = GenerationReviewedSelectionPolicy.Apply(rescored, CancellationToken.None);
        TestAssert.Same(candidate, final.RefinedMoments.SelectedCandidates.Single().Candidate, "The promoted cut must now be selected with its own review.");
        TestAssert.Same(baseline.Transcripts!, final.Transcripts!, "Rescoring must preserve the shared transcript object.");
        foreach (var firstScore in promoted.Refinements.Where(item => initial.Observations.Any(observation => ReferenceEquals(item.Candidate, observation.Candidate))))
        {
            var finalScore = final.Refinements.Single(item => ReferenceEquals(item.Candidate, firstScore.Candidate));
            TestAssert.Equal(firstScore.RankingScore, finalScore.RankingScore, "Initial visual contributions must be applied exactly once when merging later observations.");
            TestAssert.Equal(firstScore.Components.Count, finalScore.Components.Count, "Merged review cannot duplicate score components.");
        }
        TestAssert.True(combined.FindReviewVideo(candidate.Id) is not null &&
            combined.FindReviewVideo(initial.Observations[0].Candidate.Id) is not null,
            "Both review generations must stay leased through metadata preparation.");
        TestAssert.Equal(0, materializer.CleanupCount, "Successful review media must remain available until the combined owner is released.");
        TestAssert.Same(combined, await service.ReviewPromotedAsync(baseline, final.RefinedMoments.SelectedCandidates,
            combined, null, CancellationToken.None), "A combined review cannot start an unbounded chain of supplemental passes.");
        combined.Dispose();
        initial.Dispose();
        TestAssert.Equal(9, materializer.CleanupCount, "Combined and original disposal must release every artifact exactly once.");
    }

    private static async Task PromotedReviewHonorsBudgets()
    {
        var baseline = PromotedIntelligence(32, 9);
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = RejectFirstReviews(18);
        var service = new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings());
        using var initial = await service.AnalyzeAsync(baseline, null, CancellationToken.None);
        TestAssert.Equal(18, initial.Observations.Count, "The test uses the existing adaptive initial-review budget.");
        var refinement = new GenerationCandidateRefinementService();
        var promoted = refinement.ApplyVisualSemantic(baseline, initial);
        using var combined = await service.ReviewPromotedAsync(baseline, promoted.RefinedMoments.SelectedCandidates,
            initial, null, CancellationToken.None);
        TestAssert.Equal(8, provider.Requests[^1].Requests.Count, "One supplemental batch is capped at eight even when nine new moments are selected.");
        TestAssert.True(provider.Requests.All(request => request.Requests.Count <= 8) && combined.Observations.Count <= 32,
            "Both per-call and aggregate qualified-provider bounds must remain intact.");
        var final = GenerationReviewedSelectionPolicy.Apply(refinement.ApplyVisualSemantic(baseline, combined), CancellationToken.None);
        TestAssert.Equal(8, final.RefinedMoments.SelectedCount, "Count fill must not promote another unreviewed candidate after the supplemental pass.");
        TestAssert.True(final.RefinedMoments.SelectedCandidates.All(item => combined.Observations.Any(observation => ReferenceEquals(item.Candidate, observation.Candidate))),
            "Every final automatically selected candidate must belong to the successfully reviewed pool.");
        TestAssert.True(final.RefinedMoments.FulfillmentMessage.Contains("visually reviewed pool", StringComparison.Ordinal),
            "A bounded review shortfall must explain why fewer results are returned.");

        var limitedBaseline = PromotedIntelligence(9, 9);
        using var limitedMaterializer = new FakeVisualReviewMaterializer();
        var limitedProvider = new FakeVisualEditorialProvider();
        var settings = CreateVisualSettings();
        var limited = new GenerationVisualSemanticAnalysisService(limitedProvider, limitedMaterializer,
            new(settings.Prompt, settings.Model, settings.VideoPolicy, maximumCandidateCount: 8));
        using var limitedReview = await limited.AnalyzeAsync(limitedBaseline, null, CancellationToken.None);
        var limitedRefined = refinement.ApplyVisualSemantic(limitedBaseline, limitedReview);
        var unchanged = await limited.ReviewPromotedAsync(limitedBaseline, limitedRefined.RefinedMoments.SelectedCandidates,
            limitedReview, null, CancellationToken.None);
        TestAssert.Same(limitedReview, unchanged, "A configured total limit smaller than 32 must remain authoritative.");
        TestAssert.Equal(1, limitedProvider.Requests.Count, "Exhausted configured budget must prevent another provider invocation.");
        TestAssert.Equal(8, GenerationReviewedSelectionPolicy.Apply(limitedRefined, CancellationToken.None).RefinedMoments.SelectedCount,
            "Exhausted review budgets must still respect final pool eligibility.");
    }

    private static async Task PromotedReviewFailurePreservesEvidence()
    {
        var baseline = PromotedIntelligence();
        using var materializer = new FakeVisualReviewMaterializer();
        var initialService = new GenerationVisualSemanticAnalysisService(new FakeVisualEditorialProvider(), materializer, CreateVisualSettings());
        using var initial = await initialService.AnalyzeAsync(baseline, null, CancellationToken.None);
        var selected = UnreviewedSelection(baseline, initial);
        var failing = new GenerationVisualSemanticAnalysisService(new FakeVisualEditorialProvider
            { Failure = new Qwen3VlInferenceException("Failed supplemental review.", "private source text") }, materializer, CreateVisualSettings());
        using var combined = await failing.ReviewPromotedAsync(baseline, selected, initial, null, CancellationToken.None);
        TestAssert.Equal(initial.Observations.Count, combined.Observations.Count, "A supplemental failure must retain every valid initial observation.");
        TestAssert.True(combined.NeedsReview && combined.Outcome == GenerationVisualSemanticOutcome.Completed &&
            !combined.DiagnosticDetails!.Contains("private source text", StringComparison.Ordinal),
            "Partial review retains its successful outcome and exposes a safe, explicit review notice.");
        TestAssert.Equal(1, materializer.CleanupCount, "Only the failed supplemental artifact is released before metadata.");
        TestAssert.True(combined.FindReviewVideo(initial.Observations[0].Candidate.Id) is not null, "Previously reviewed media must remain usable.");
        var final = GenerationReviewedSelectionPolicy.Apply(new GenerationCandidateRefinementService().ApplyVisualSemantic(baseline, combined), CancellationToken.None);
        TestAssert.True(final.RefinedMoments.SelectedCandidates.All(item => initial.Observations.Any(observation => ReferenceEquals(item.Candidate, observation.Candidate))),
            "Supplemental failure must not let the failed candidate enter the automatic final selection.");
        combined.Dispose();
        TestAssert.Equal(9, materializer.CleanupCount, "The combined owner must eventually release successful and failed generations exactly once.");
        using var fallback = await failing.AnalyzeAsync(baseline, null, CancellationToken.None);
        var deterministic = new GenerationCandidateRefinementService().ApplyVisualSemantic(baseline, fallback);
        TestAssert.Same(deterministic, GenerationReviewedSelectionPolicy.Apply(deterministic, CancellationToken.None),
            "Initial provider unavailability must retain the established deterministic fallback.");
    }

    private static async Task PromotedReviewCancellationCleansMedia()
    {
        var baseline = PromotedIntelligence();
        using var materializer = new FakeVisualReviewMaterializer();
        var service = new GenerationVisualSemanticAnalysisService(new FakeVisualEditorialProvider(), materializer, CreateVisualSettings());
        using var initial = await service.AnalyzeAsync(baseline, null, CancellationToken.None);
        var foreignSource = PromotedIntelligence().BaseMoments.Sources[0].AnalyzedSource;
        var unreviewed = UnreviewedSelection(baseline, initial).Single();
        var mismatched = new GenerationMomentCandidate("mismatched-source", foreignSource, unreviewed.Candidate, 0, 1);
        await TestAssert.ThrowsAsync<ArgumentException>(() => service.ReviewPromotedAsync(baseline, [mismatched], initial,
            null, CancellationToken.None), "A selected wrapper must not attach another source's pixels to a retained candidate.");
        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress<GenerationVisualSemanticProgress>(update =>
        {
            if (update.Phase == GenerationVisualSemanticPhase.Completed) cancellation.Cancel();
        });
        await TestAssert.ThrowsAsync<OperationCanceledException>(() => service.ReviewPromotedAsync(baseline,
            UnreviewedSelection(baseline, initial), initial, progress, cancellation.Token),
            "Cancellation after successful supplemental construction must propagate instead of merging a cancelled run.");
        TestAssert.Equal(1, materializer.CleanupCount, "The newly returned supplemental lease must be released on cancellation.");
        TestAssert.True(initial.FindReviewVideo(initial.Observations[0].Candidate.Id) is not null,
            "The pipeline must retain ownership of its initial review until its own cancellation cleanup.");
        initial.Dispose();
        TestAssert.Equal(9, materializer.CleanupCount, "Pipeline cleanup must release all initial review media too.");
    }

    private static IReadOnlyList<GenerationMomentCandidate> UnreviewedSelection(GenerationCandidateIntelligenceResult baseline,
        GenerationVisualSemanticAnalysisResult initial)
    {
        var refinement = baseline.Refinements.First(item => initial.Observations.All(observation => !ReferenceEquals(item.Candidate, observation.Candidate)));
        return [new("promoted-test", baseline.BaseMoments.Sources[0].AnalyzedSource, refinement.Candidate, 0, 1, refinement: refinement)];
    }
}
