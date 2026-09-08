using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Features.Generate.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task ComparativeReviewCoversRegions()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("comparative-review.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(10), desiredCount: 1, qualityThreshold: 1);
        var original = CreateCandidateIntelligence(request, [99, 98, 80]);
        var updated = original.Refinements.Select((value, index) => new GenerationCandidateRefinement(value.Candidate,
            [new(GenerationCandidateRefinementComponentCode.NeuralTimelineValue, new[] { .99, .98, .8 }[index], 0, "Individual estimate"),
             new(GenerationCandidateRefinementComponentCode.NeuralReviewPriority, index < 2 ? 1 : .5, 0, "Comparative order", [index < 2 ? "region-a" : "region-b"]),
             new(GenerationCandidateRefinementComponentCode.NeuralRegionCoverage, new[] { .2, .8, .9 }[index], 0, "Region coverage")], "comparative-test")).ToArray();
        var mapped = new GenerationCandidateIntelligenceResult(original.BaseMoments, original.SpeechActivity, updated, original.RefinedMoments);
        var shortlist = GenerationVisualSemanticAnalysisService.CreateShortlist(mapped, 2);
        TestAssert.True(ReferenceEquals(updated[1].Candidate, shortlist[0].Candidate) && ReferenceEquals(updated[2].Candidate, shortlist[1].Candidate),
            "Close review must inspect one representative of each nominated region instead of using both slots on the same region.");
        TestAssert.Equal(98d, updated[1].RankingScore, "Comparative nominations allocate review work without adding a fixed score bonus.");
        var personal = updated.Select((value, index) => new GenerationCandidateRefinement(value.Candidate,
            value.Components.Append(new(GenerationCandidateRefinementComponentCode.NeuralPersonalValue, new[] { .99, .98, .1 }[index], 0, "Qualified personal prediction")), "personal-test")).ToArray();
        var personalized = new GenerationCandidateIntelligenceResult(original.BaseMoments, original.SpeechActivity, personal, original.RefinedMoments);
        TestAssert.True(ReferenceEquals(personal[0].Candidate, GenerationVisualSemanticAnalysisService.CreateShortlist(personalized, 2)[0].Candidate),
            "A qualified personal model must take precedence over generic comparative nominations.");
        var source = original.BaseMoments.Sources[0];
        foreach (var disposition in new[] { MomentCandidateDisposition.RejectedBlack, MomentCandidateDisposition.RejectedFreeze })
        {
            var proposals = source.Moments.Proposals.Select(candidate => candidate.WithDisposition(disposition)).ToArray();
            var priorManifest = source.Moments.Manifest;
            var manifest = new MediaMomentFindingManifest(priorManifest.FinderIdentity, priorManifest.FoundAtUtc,
                priorManifest.SourcePath, priorManifest.SourceDuration, priorManifest.Options, priorManifest.EvidenceAnalyzerName,
                priorManifest.EvidenceAnalyzerVersion, priorManifest.EvidenceSignalSchemaVersion, priorManifest.VisualSampleCadence,
                priorManifest.AudioWindowCadence, priorManifest.IncludedRoles, priorManifest.CompositionSchemaVersion,
                priorManifest.CompositionCoordinateSpaceVersion, priorManifest.CompositionPlanOrigin, priorManifest.AnchorCounts,
                proposals.Length, proposals.Length, 0, 0, 0, priorManifest.TotalElapsed, priorManifest.DeterministicCoverageStatement,
                priorManifest.NeighborhoodCount, 0, priorManifest.PolicyHash, priorManifest.EpisodeCount, 0);
            var media = new MediaMomentFindingResult(source.Moments.Request, proposals, [], source.Moments.Warnings,
                manifest, source.Moments.ActivationSeries, source.Moments.Episodes);
            var sources = new[] { new GenerationSourceMomentResult(source.AnalyzedSource, media) };
            var checkedValues = proposals.Select((candidate, index) => new GenerationCandidateRefinement(candidate,
                [new(GenerationCandidateRefinementComponentCode.NeuralSceneValue, .8, 0, "Contextual model review"),
                 new(GenerationCandidateRefinementComponentCode.NeuralReviewPriority, index*.5, 0, "Comparative model order")],
                "checked-test")).ToDictionary(value => value.Candidate);
            var selected = new GenerationMomentPortfolioSelector().Select(original.BaseMoments.Request, sources, checkedValues);
            TestAssert.Equal(1, selected.Count, "A completed neural scene judgment must not be silently removed by an old dark/still detector.");
            TestAssert.True(ReferenceEquals(proposals[2], selected[0].Candidate),
                "Equal neural values use the model's comparative order before older hand-set detector scores.");
            TestAssert.Equal(0, new GenerationMomentPortfolioSelector().Select(original.BaseMoments.Request, sources).Count,
                "Without a neural review, the existing detector fallback remains intact.");
        }
        return Task.CompletedTask;
    }

    private static async Task ExhaustedGroundingIsNotRetried()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("grounding-budget.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(2), desiredCount: 1, qualityThreshold: 1);
        var intelligence = CreateCandidateIntelligence(request, [80, 75]);
        using var materializer = new FakeVisualReviewMaterializer();
        var failedId = intelligence.Refinements[0].Candidate.Id;
        var provider = new FakeVisualEditorialProvider { RetryFailures = false, FailCase = (item, _) => item.CandidateId == failedId };
        using var result = await new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings())
            .AnalyzeAsync(intelligence, null, CancellationToken.None);
        TestAssert.Equal(1, provider.Requests.Count, "A model's exhausted correction must not trigger an identical second batch.");
        TestAssert.Equal(1, result.Observations.Count, "The other checked scene remains available.");
        TestAssert.True(result.Observations.All(value => value.Candidate.Id != failedId),
            "Unsupported facts must not be promoted into a successful scene observation.");
    }

    private static Task NeuralShortlistUsesCurrentScores()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("neural-shortlist.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(10), desiredCount: 1, qualityThreshold: 1);
        var original = CreateCandidateIntelligence(request, [99, 80, 50]);
        var updated = original.Refinements.Select((value, index) => new GenerationCandidateRefinement(
            value.Candidate,
            [new(GenerationCandidateRefinementComponentCode.NeuralTimelineValue, new[] { .1, .5, .95 }[index], 0, "Contextual neural value"),
             new(GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, 1, 0, "Full recording coverage")],
            "neural-shortlist-test")).ToArray();
        var mapped = new GenerationCandidateIntelligenceResult(original.BaseMoments, original.SpeechActivity,
            updated, original.RefinedMoments);
        var shortlist = GenerationVisualSemanticAnalysisService.CreateShortlist(mapped, 2);
        TestAssert.Equal(2, shortlist.Count, "Neural review remains within the requested budget.");
        TestAssert.True(ReferenceEquals(updated[2].Candidate, shortlist[0].Candidate),
            "A later moment with stronger neural value must displace the earlier selected candidate.");
        TestAssert.True(ReferenceEquals(updated[1].Candidate, shortlist[1].Candidate),
            "The next neural score determines the second review, without reserving a category slot.");
        TestAssert.Equal(95d, shortlist[0].Score, "Close review must receive the current neural score.");
        return Task.CompletedTask;
    }

    private static async Task GroundedSceneEvidence()
    {
        var neutralScore = JsonSerializer.SerializeToElement(new { version = "scene-value-1", calibrated = false,
            margins = new[] { 0d, 0d }, probabilities = new[] { .5, .5 }, value = .5 });
        Qwen3VlSceneReviewProvider.ValidateNeuralValue(neutralScore, 50);
        TestAssert.Throws<InvalidDataException>(() => Qwen3VlSceneReviewProvider.ValidateNeuralValue(neutralScore, 75),
            "A model-written number cannot replace the measured neural comparisons.");
        var setup = CreateRequest(GenerationAnalysisDepth.Thorough, [("scene-contract.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(2), desiredCount: 1, qualityThreshold: 70);
        var intelligence = CreateCandidateIntelligence(setup, [80]);
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = new FakeVisualEditorialProvider();
        using var review = await new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings())
            .AnalyzeAsync(intelligence, null, CancellationToken.None);
        var indexedDialogue = new GenerationCandidateRefinement(intelligence.Refinements[0].Candidate,
            [new(GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, 1, 0, "Classified recording"),
             new(GenerationCandidateRefinementComponentCode.NeuralGameplay, 0, 0, "Scripted scene")], "indexed-test");
        TestAssert.Equal(VisualSemanticObservableContentType.Action, review.Observations[0].Observation.ObservableContentType,
            "This fixture must exercise a broad physical-action scene label.");
        TestAssert.True(!GenerationMomentContentClassifier.Classify(indexedDialogue.Candidate, [], review.Observations[0], indexedDialogue).Gameplay,
            "A scripted character movement cannot override the model's explicit gameplay classification.");
        var request = provider.Requests[0].Requests[0];
        double start = request.CandidateStartRelative.TotalSeconds, end = request.CandidateEndRelative.TotalSeconds;
        var frames = JsonSerializer.SerializeToElement(Enumerable.Range(0, Qwen3VlSceneReviewProvider.FrameCount).Select(i => start+(end-start-.1)*i/(Qwen3VlSceneReviewProvider.FrameCount-1)));
        var menu = JsonSerializer.SerializeToElement(new { setup = "A settings menu covers the screen.", @event = "The user browses the settings panel.", outcome = "The same settings panel remains open.",
            firstFrame = 0, lastFrame = Qwen3VlSceneReviewProvider.FrameCount-1, kind = "MenuOrTraversal", hasDistinctEvent = "No", hasPayoff = "No", onlyRoutineMovementOrMenus = "Yes", needsEarlierContext = "No", onlyLightingOrCameraChanges = "No", transcriptSupport = "NotSupplied", editorialValue = 10, recommendation = "Reject" });
        var result = Qwen3VlSceneReviewProvider.ParseAssessment(request, menu, frames, TimeSpan.FromSeconds(2));
        TestAssert.Equal(VisualSemanticEditorialDisposition.Reject, result.Observation.EditorialDisposition, "A menu is a checked rejection, not an action payoff.");
        TestAssert.Equal(.1, result.NeuralEditorialValue!.Value, "The model's value estimate is retained without a category penalty.");
        TestAssert.Equal(Qwen3VlSceneReviewProvider.Version, result.CanonicalizationAudit.WireRepresentationVersion, "New evidence must not claim the old compact prompt identity.");
        var action = JsonSerializer.SerializeToElement(new { setup = "A car approaches an aircraft.", @event = "A car approaches the aircraft and flames appear.", outcome = "The aircraft bursts into flames.",
            firstFrame = 0, lastFrame = Qwen3VlSceneReviewProvider.FrameCount-1, kind = "Action", hasDistinctEvent = "Yes", hasPayoff = "Yes", onlyRoutineMovementOrMenus = "No", needsEarlierContext = "No", onlyLightingOrCameraChanges = "No", transcriptSupport = "NotSupplied", editorialValue = 85, recommendation = "Keep" });
        TestAssert.Equal(VisualSemanticEditorialDisposition.Keep, Qwen3VlSceneReviewProvider.ParseAssessment(request, action, frames, TimeSpan.Zero).Observation.EditorialDisposition,
            "Distinct, grounded action and payoff can qualify.");
        var quiet = JsonSerializer.SerializeToElement(new { setup = "A puzzle board presents several paths.", @event = "The final pieces connect to solve the puzzle.", outcome = "The final route connects the puzzle pieces.",
            firstFrame = 0, lastFrame = Qwen3VlSceneReviewProvider.FrameCount-1, kind = "MenuOrTraversal", hasDistinctEvent = "Yes", hasPayoff = "Yes", onlyRoutineMovementOrMenus = "Yes", needsEarlierContext = "No", onlyLightingOrCameraChanges = "No", transcriptSupport = "NotSupplied", editorialValue = 95, recommendation = "Keep" });
        var quietResult = Qwen3VlSceneReviewProvider.ParseAssessment(request, quiet, frames, TimeSpan.Zero);
        TestAssert.Equal(VisualSemanticEditorialDisposition.Keep, quietResult.Observation.EditorialDisposition,
            "An interface label cannot override the model's positive contextual judgment.");
        TestAssert.Equal(.95, quietResult.NeuralEditorialValue!.Value, "A quiet scene can score above an action scene.");
        var quietRefinement = new GenerationCandidateRefinement(intelligence.Refinements[0].Candidate,
            [new(GenerationCandidateRefinementComponentCode.NeuralSceneValue, .95, 0, "Model judgment"),
                new(GenerationCandidateRefinementComponentCode.NeuralMenu, 1, 0, "Observed interface"),
                new(GenerationCandidateRefinementComponentCode.NonGameplayCapture, 1, -40, "Legacy detector")], "neural-test");
        TestAssert.Equal(95d, quietRefinement.RankingScore,
            "A category observation or older detector penalty cannot override the trained model's value.");
        TestAssert.True(ReplayFoundry.Desktop.Features.Generate.Moments.GenerationAutomaticCandidateEligibility.IsEligible(quietRefinement.Candidate, quietRefinement),
            "A reviewed interface moment remains available for selection through its model score.");
        TestAssert.Equal("Checked 4 of 8 recording sections · 3 mapped · 1 reused from saved analysis.",
            GenerationRecordingIndexService.DescribeProgress("{\"stage\":\"recording-index-progress\",\"checked\":4,\"total\":8,\"mapped\":3,\"reused\":1}"),
            "Progress distinguishes inspected sections from successful model results.");
        TestAssert.Null(GenerationRecordingIndexService.DescribeProgress("{\"stage\":\"recording-index-progress\",\"checked\":9,\"total\":8,\"mapped\":3,\"reused\":1}"),
            "Impossible progress cannot reach the user interface.");
        TestAssert.Throws<InvalidDataException>(() => Qwen3VlSceneReviewProvider.ParseAssessment(request, action,
            JsonSerializer.SerializeToElement(Enumerable.Range(0, Qwen3VlSceneReviewProvider.FrameCount).Select(i => end+i)), TimeSpan.Zero), "Evidence from outside the actual cut is rejected.");
    }
}
