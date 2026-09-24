using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task FollowupReviewPrefersUnseenFootage()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("review-regions.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(10), desiredCount: 1);
        var moments = CreateMoments(request, Enumerable.Range(0, 40).Select(index => 99d-index).ToArray(), windowSpacingSeconds: 10);
        var baseline = new GenerationCandidateRefinementService().Refine(moments,
            CreateSpeech(request, AudioContentRoleAssignment.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        var proposals = moments.Sources[0].Moments.Proposals;
        var fresh = GenerationVisualSemanticAnalysisService.CreateShortlist(baseline, 1,
            previouslyReviewed: [proposals[0]]);
        TestAssert.Same(proposals[2], fresh.Single().Candidate,
            "Review should reach unseen footage before another high-scoring trim of the same event.");
        TestAssert.True(MomentIntervalMath.PairOverlapRatio(proposals[0].Window, fresh[0].Candidate.Window) < .5,
            "A fresh review must not mostly repeat footage already inspected.");
        var exhaustedFront = proposals.Take(32).ToArray();
        var remaining = GenerationVisualSemanticAnalysisService.CreateShortlist(baseline, 8,
            previouslyReviewed: exhaustedFront);
        TestAssert.Equal(8, remaining.Count,
            "Attempted candidates must be removed before bounding the next shortlist, so its first page cannot hide later alternatives.");
        TestAssert.True(remaining.All(item => !exhaustedFront.Contains(item.Candidate)), "Attempted cuts cannot consume new review slots.");
        TestAssert.True(remaining.Any(item => ReferenceEquals(item.Candidate, proposals[32])),
            "Overlapping trims remain available after novel footage; novelty is review order, not a permanent rejection.");
        return Task.CompletedTask;
    }

    private static async Task ReviewShortfallUsesRemainingCapacity()
    {
        foreach (bool failedMiddle in new[] { false, true })
        {
            var original = PromotedIntelligence(32, 5);
            var mapped = original.Refinements.Select(item => new GenerationCandidateRefinement(item.Candidate,
                [new(GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, 1, 0, "Mapped"),
                 new(GenerationCandidateRefinementComponentCode.NeuralTimelineValue, .8, 0, "Timeline estimate")],
                "recovery-test")).ToArray();
            var baseline = new GenerationCandidateIntelligenceResult(original.BaseMoments, original.SpeechActivity,
                mapped, original.RefinedMoments, transcripts: original.Transcripts);
            var rejected = mapped.Take(8).Select(item => item.Candidate.Id).ToHashSet();
            var failed = mapped.Skip(8).Take(failedMiddle ? 16 : 0).Select(item => item.Candidate.Id).ToHashSet();
            using var materializer = new FakeVisualReviewMaterializer();
            var provider = new FakeVisualEditorialProvider
            {
                RetryFailures = false,
                FailCase = (request, _) => failed.Contains(request.CandidateId),
                ObservationFactory = (request, _) => rejected.Contains(request.CandidateId)
                    ? CreateVisualObservation(VisualSemanticEditorialDisposition.Reject,
                        VisualSemanticEditorialRejectReason.RoutineTraversal, VisualSemanticTernary.No,
                        "Only routine traversal is visible.")
                    : QueryKeep(),
            };
            var service = new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings());
            var refinement = new GenerationCandidateRefinementService();
            using var initial = await service.AnalyzeAsync(baseline, null, CancellationToken.None);
            TestAssert.Equal(8, initial.AttemptedCandidates.Count, "A mapped recording starts with the small review budget.");
            var current = GenerationReviewedSelectionPolicy.Apply(refinement.ApplyVisualSemantic(baseline, initial), CancellationToken.None);
            TestAssert.Equal(0, current.RefinedMoments.SelectedCount, "The initial weak cuts must remain rejected.");
            GenerationVisualSemanticAnalysisResult retained = initial;
            try
            {
                current = await GenerationReviewedPoolRecovery.FillAsync(baseline, current, service, refinement,
                    null, review => retained = review, CancellationToken.None);
                TestAssert.Equal(5, current.RefinedMoments.SelectedCount,
                    "A shortfall must examine remaining candidates until five independently reviewed cuts qualify.");
                TestAssert.Equal(failedMiddle ? 32 : 16, retained.AttemptedCandidates.Count,
                    "Failed checks consume the total budget, while successful recovery stops without examining every candidate.");
                TestAssert.True(current.RefinedMoments.SelectedCandidates.All(item =>
                    !rejected.Contains(item.Candidate.Id) && !failed.Contains(item.Candidate.Id)),
                    "Neither rejected nor failed cuts can fill the requested count.");
                if (!failedMiddle)
                {
                    var copyRejections = new HashSet<string>();
                    for (int index = 0; index < 5; index++)
                    {
                        string id = current.RefinedMoments.SelectedCandidates[0].Id;
                        copyRejections.Add(id);
                        current = GenerationEditorialReplacementPolicy.RejectAutomaticCut(current, id,
                            CancellationToken.None, allowEmptyPool: true)!;
                    }
                    current = await GenerationReviewedPoolRecovery.FillAsync(baseline, current, service, refinement,
                        null, review => retained = review, CancellationToken.None);
                    TestAssert.Equal(5, current.RefinedMoments.SelectedCount,
                        "Writing rejections can trigger another bounded review when the approved pool runs short.");
                    TestAssert.True(current.RefinedMoments.SelectedCandidates.All(item => !copyRejections.Contains(item.Id)),
                        "Rescoring the expanded review must not resurrect rejected wording candidates.");
                }
                var requests = provider.Requests.SelectMany(batch => batch.Requests).ToArray();
                TestAssert.True(requests.Length <= 32 && requests.Select(item => item.CandidateId).Distinct().Count() == requests.Length,
                    "Alternative rounds never repeat attempted cuts or exceed the global review limit.");
                TestAssert.True(provider.Requests.All(batch => batch.Requests.Count <= 2),
                    "Additional rounds preserve the provider's small batch limit.");
            }
            finally { retained.Dispose(); }
            TestAssert.Equal(materializer.Requests.Count, materializer.CleanupCount,
                "Successful and failed artifacts across every recovery round are released exactly once.");
        }
    }
}
