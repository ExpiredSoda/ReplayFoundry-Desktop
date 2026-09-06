using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task SemanticNominationRepairsCoverageOfMalformedCut()
    {
        var intent = new GenerationDiscoveryIntent(naturalLanguageQuery: "Why the surroundings make the playable people look small.");
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("semantic-coverage.mkv", 1)],
            sourceDuration: TimeSpan.FromSeconds(120), desiredCount: 1, discoveryIntent: intent,
            maximumClipDuration: TimeSpan.FromSeconds(45));
        GenerationMomentFindingResult original = SemanticCoverageFixture(request);
        var segment = SentenceSegment("size-commentary", "source", "Everything else is always bigger than the characters you play.", .27, 26.97);
        var transcript = new GenerationSourceTranscript(request.ReferenceSource.FullPath, 1, [segment], []);
        var retrieval = new GenerationSemanticRetrievalResult(
            [new(new(request.ReferenceSource.FullPath, 1, segment.AbsoluteSourceStart, segment.AbsoluteSourceEnd,
                segment.Text, [segment.Id]), .5247386326555965)], "fixture", null, null, TimeSpan.Zero, 0, "Not model qualification.");
        var expanded = GenerationTranscriptCandidatePlanner.Expand(original, [transcript], CancellationToken.None, retrieval);
        MomentCandidate nominated = expanded.Sources.Single().Moments.Proposals.Single(candidate =>
            candidate.ConstructionReason == MomentCandidateConstructionReason.SemanticExploration);
        TestAssert.Equal(TimeSpan.Zero, nominated.Window.Start, "The nomination must include the .27-second passage beginning that the .804-second old cut lost.");
        TestAssert.True(nominated.Window.End >= segment.AbsoluteSourceEnd && nominated.Window.Duration < TimeSpan.FromSeconds(30),
            "The complete 26.7-second passage should keep bounded padding and reserve room for natural ending repair.");
        TestAssert.Equal(0d, nominated.Score.RawComponentTotal, "A matching transcript never creates event evidence.");
        var speech = CreateSpeech(request, new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
            [(TimeSpan.FromMilliseconds(322), TimeSpan.FromMilliseconds(1598)),
                (TimeSpan.FromMilliseconds(12578), TimeSpan.FromMilliseconds(16254)),
                (TimeSpan.FromMilliseconds(17666), TimeSpan.FromMilliseconds(20062)),
                (TimeSpan.FromMilliseconds(20866), TimeSpan.FromMilliseconds(22846)),
                (TimeSpan.FromMilliseconds(23298), TimeSpan.FromMilliseconds(24030)),
                (TimeSpan.FromMilliseconds(24354), TimeSpan.FromMilliseconds(26430)),
                (TimeSpan.FromMilliseconds(26978), TimeSpan.FromMilliseconds(28894)),
                (TimeSpan.FromMilliseconds(29154), TimeSpan.FromMilliseconds(30622)),
                (TimeSpan.FromMilliseconds(33282), TimeSpan.FromMilliseconds(34334)),
                (TimeSpan.FromMilliseconds(34722), TimeSpan.FromMilliseconds(37438)),
                (TimeSpan.FromMilliseconds(41058), TimeSpan.FromMilliseconds(43070)),
                (TimeSpan.FromMilliseconds(44322), TimeSpan.FromMilliseconds(45054))]);
        var service = new GenerationCandidateRefinementService();
        var intelligence = service.Refine(expanded, speech, new GenerationTranscriptAnalysisResult(expanded, [transcript], retrieval));
        var reviewedNomination = intelligence.Refinements.Single(value =>
            value.Candidate.ConstructionReason == MomentCandidateConstructionReason.SemanticExploration);
        TestAssert.False(reviewedNomination.HasIncompleteSpeechBeginning || reviewedNomination.HasIncompleteSpeechEnding,
            "The smaller nomination must leave enough duration for existing boundary repair to include the closing utterance.");
        TestAssert.Equal(TimeSpan.FromMilliseconds(31372), reviewedNomination.Candidate.Window.End,
            "The recorded speech timing must extend the nomination through 30.622 seconds and its natural tail, without swallowing the next separated utterance.");
        TestAssert.True(intelligence.Refinements.Single(value => value.Candidate.ConstructionReason != MomentCandidateConstructionReason.SemanticExploration)
            .HasIncompleteSpeechBeginning, "The original 0.804–45.804 cut must remain ineligible instead of borrowing the new passage's completeness.");
        TestAssert.True(reviewedNomination.RequiresSemanticReview && intelligence.RefinedMoments.SelectedCandidates.Count == 0,
            "Even complete retrieved speech must await a qualified visual Keep before automatic selection.");
        var shortlist = GenerationVisualSemanticAnalysisService.CreateShortlist(intelligence, 1);
        TestAssert.Equal(1, shortlist.Count, "Query exploration must stay inside the requested review budget.");
        TestAssert.Same(reviewedNomination.Candidate, shortlist.Single().Candidate, "The full relevant passage must reach review before the malformed old cut.");
        using var visual = new GenerationVisualSemanticAnalysisResult(intelligence, new InferenceProviderIdentity("fixture", "1", "1"),
            [Reviewed(intelligence, reviewedNomination.Candidate, CreateVisualObservation(VisualSemanticEditorialDisposition.Keep,
                VisualSemanticEditorialRejectReason.None, VisualSemanticTernary.Yes, "The complete bounded interval contains an event and payoff."))], TimeSpan.Zero, null);
        var grounded = service.ApplyVisualSemantic(intelligence, visual);
        TestAssert.Same(reviewedNomination.Candidate, grounded.RefinedMoments.SelectedCandidates.Single().Candidate,
            "Only the subsequent complete grounded review may make the alternative edit eligible.");
        return Task.CompletedTask;
    }

    private static Task SemanticNominationCoveragePreservesBoundsAndBudgets()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("semantic-bounds.mkv", 1)],
            sourceDuration: TimeSpan.FromSeconds(120), desiredCount: 1,
            discoveryIntent: new(naturalLanguageQuery: "A spoken passage"), maximumClipDuration: TimeSpan.FromSeconds(45));
        GenerationMomentFindingResult original = SemanticCoverageFixture(request);
        var source = original.Sources.Single().Moments;
        GenerationTimedExplorationSeed Seed(double start, double end) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), "Fixture retrieval relevance, not evidence.");
        var first = GenerationSemanticExplorationPlanner.Expand(source, 1, CancellationToken.None, semanticSeeds: [Seed(.27, 26.97)]);
        var repeated = GenerationSemanticExplorationPlanner.Expand(first, 1, CancellationToken.None, semanticSeeds: [Seed(.27, 26.97)]);
        TestAssert.Same(first, repeated, "An exact existing semantic edit must not consume another proposal.");
        var literal = GenerationSemanticExplorationPlanner.Expand(source, 1, CancellationToken.None,
            [SentenceSegment("literal", "source", "A complete spoken passage.", .27, 26.97)]);
        TestAssert.Same(source, literal, "The default/literal exploration overlap policy must remain unchanged.");
        var bounded = GenerationSemanticExplorationPlanner.Expand(source, 3, CancellationToken.None,
            semanticSeeds: [Seed(60, 62), Seed(65, 109.9), Seed(60, 106)]);
        MomentCandidate[] alternatives = bounded.Proposals.Where(candidate =>
            candidate.ConstructionReason == MomentCandidateConstructionReason.SemanticExploration).ToArray();
        TestAssert.Equal(2, alternatives.Length, "An over-maximum passage is rejected; available padding may shrink for a complete in-bounds passage.");
        TestAssert.True(alternatives.Any(candidate => candidate.Window.Duration == source.Request.Options.MinimumDuration), "Short reactions must retain the configured minimum duration.");
        TestAssert.True(alternatives.Any(candidate => candidate.Window.Start <= TimeSpan.FromSeconds(65) && candidate.Window.End >= TimeSpan.FromSeconds(109.9)),
            "Padding must never truncate a retained passage when approaching MaximumDuration.");
        TestAssert.True(alternatives.All(candidate => candidate.Window.Duration <= source.Request.Options.MaximumDuration && candidate.Score.RawComponentTotal == 0),
            "Duration and zero-event-score constraints survive semantic nomination.");
        var matches = Enumerable.Range(0, 30).Select(index => new GenerationSemanticRetrievalMatch(
            new(request.ReferenceSource.FullPath, 1, TimeSpan.FromSeconds(index * 3), TimeSpan.FromSeconds(index * 3 + 2),
                "A spoken passage.", [$"bounded-{index}"]), .9 - index * .01)).ToArray();
        var retrieval = new GenerationSemanticRetrievalResult(matches, "fixture", null, null, TimeSpan.Zero, 0, "fixture");
        var expanded = GenerationTranscriptCandidatePlanner.Expand(original, [], CancellationToken.None, retrieval);
        TestAssert.True(expanded.Sources.Single().Moments.Proposals.Count - source.Proposals.Count <= 8 &&
            expanded.Sources.Sum(value => value.Moments.Proposals.Count) - original.Sources.Sum(value => value.Moments.Proposals.Count) <= 32,
            "Alternative edits must remain inside the existing per-source and whole-request budgets.");
        return Task.CompletedTask;
    }

    private static GenerationMomentFindingResult SemanticCoverageFixture(GenerationRequest request)
    {
        var original = CreateMoments(request, [90]);
        var source = original.Sources.Single();
        var previous = source.Moments.Proposals.Single();
        var window = new MomentCandidateWindow(TimeSpan.FromMilliseconds(804), TimeSpan.FromMilliseconds(45804), source.Moments.Request.Media.Duration);
        var evidence = new MomentEvidenceReference(MomentEvidenceReferenceKind.SourceCoverage, window.Start, window.End, "Existing deterministic proposal fixture.");
        var anchor = new MomentAnchor("existing-anchor", MomentAnchorKind.SourceCoverage, TimeSpan.FromSeconds(20), 0, 0, [evidence]);
        var neighborhood = new MomentEventNeighborhood("existing-neighborhood", window.Start, anchor.Timestamp, window.End, [anchor], [MomentSignalFamily.SourceCoverage]);
        var candidate = new MomentCandidate(previous.Id, window, previous.ConstructionReason, neighborhood, [anchor], previous.Score,
            previous.Disposition, 0, 0, 0, 0);
        var media = new MediaMomentFindingResult(source.Moments.Request, [candidate], [candidate], source.Moments.Warnings, source.Moments.Manifest);
        var replacement = new GenerationSourceMomentResult(source.AnalyzedSource, media);
        return new(original.Request, [replacement], new GenerationMomentPortfolioSelector().Select(original.Request, [replacement]));
    }
}
