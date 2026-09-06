using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task FinalQueryPreferenceResolvesOnlyNearTies()
    {
        foreach (var intent in new[] { new GenerationDiscoveryIntent(naturalLanguageQuery: "Opening the locked doorway"),
                     GenerationDiscoveryIntent.Default, new GenerationDiscoveryIntent(GenerationMomentIntent.Clutch) })
        {
            var fixture = FinalQueryFixture(intent: intent);
            using var visual = fixture.Visual;
            var before = fixture.Intelligence;
            TestAssert.Same(before.Refinements[1].Candidate, before.RefinedMoments.SelectedCandidates.Single().Candidate,
                "Before final preference, the unrelated candidate must genuinely lead on evidence score.");
            var result = GenerationReviewedSelectionPolicy.Apply(before, CancellationToken.None);
            int expected = string.IsNullOrEmpty(intent.NaturalLanguageQuery) ? 1 : 0;
            TestAssert.Same(before.Refinements[expected].Candidate, result.RefinedMoments.SelectedCandidates.Single().Candidate,
                "Only an explicit query may prefer the slightly lower-scored relevant candidate; defaults and typed objectives retain their ordering.");
            foreach (var original in before.Refinements)
            {
                var retained = result.Refinements.Single(value => ReferenceEquals(value.Candidate, original.Candidate));
                TestAssert.Same(original, retained, "Final preference must preserve the complete refinement and every evidence/final/ranking score.");
                TestAssert.Equal(0d, retained.Components.Single(value => value.Code == GenerationCandidateRefinementComponentCode.SemanticRetrievalRelevance).SignedContribution,
                    "Retrieval remains zero event evidence even when it affects final preference.");
            }
        }
        var largerGap = FinalQueryFixture(scores: [74, 76.5]);
        using var largerReview = largerGap.Visual;
        var unchanged = GenerationReviewedSelectionPolicy.Apply(largerGap.Intelligence, CancellationToken.None);
        TestAssert.Same(largerGap.Intelligence.Refinements[1].Candidate, unchanged.RefinedMoments.SelectedCandidates.Single().Candidate,
            "A query preference capped at one point must not overturn a larger evidence-quality gap.");
        return Task.CompletedTask;
    }

    private static Task FinalQueryPreferenceRequiresOwnedCompleteKeep()
    {
        foreach (string condition in new[] { "missing", "partial", "unsure", "reject", "foreign", "unreliable", "uncertain" })
        {
            var fixture = FinalQueryFixture(reviewFactory: (baseline, candidate, index) =>
            {
                if (index != 0) return Reviewed(baseline, candidate, QueryKeep());
                if (condition == "missing") return null;
                var observation = condition switch
                {
                    "unsure" => CreateVisualObservation(VisualSemanticEditorialDisposition.Unsure, VisualSemanticEditorialRejectReason.InsufficientEvidence,
                        VisualSemanticTernary.Unsure, "The sampled event is uncertain."),
                    "reject" => CreateVisualObservation(VisualSemanticEditorialDisposition.Reject, VisualSemanticEditorialRejectReason.RoutineTraversal,
                        VisualSemanticTernary.No, "Only routine traversal is observed."),
                    "unreliable" => QueryKeep(VisualSemanticTranscriptContextSupport.UnreliableOrAmbiguous),
                    "uncertain" => QueryKeep(uncertain: true),
                    _ => QueryKeep(),
                };
                var reviewed = Reviewed(baseline, candidate, observation, partial: condition == "partial");
                if (condition != "foreign") return reviewed;
                var foreign = CreateCandidateIntelligence(CreateRequest(GenerationAnalysisDepth.Thorough, [("final-query.mkv", 1)],
                    sourceDuration: TimeSpan.FromMinutes(5), desiredCount: 1), [80]);
                return new GenerationVisualSemanticCandidateObservation(candidate, foreign.BaseMoments.Sources.Single().AnalyzedSource,
                    reviewed.ReviewedSourceStart, reviewed.ReviewedSourceEnd, reviewed.ReviewVideoSha256,
                    reviewed.Observation, reviewed.CanonicalizationAudit, reviewed.Elapsed);
            });
            using var visual = fixture.Visual;
            TestAssert.False(GenerationSemanticFinalSelectionPreference.Create(fixture.Intelligence).ContainsKey(fixture.Intelligence.Refinements[0].Candidate),
                $"The {condition} review must not authorize a final query preference.");
        }
        return Task.CompletedTask;
    }

    private static Task FinalQueryPreferencePreservesEligibility()
    {
        var fixture = FinalQueryFixture();
        using var visual = fixture.Visual;
        foreach (var code in new[] { GenerationCandidateRefinementComponentCode.IncompleteSpeechBeginning,
                     GenerationCandidateRefinementComponentCode.IncompleteSpeechEnding,
                     GenerationCandidateRefinementComponentCode.GroundedVisualRejection,
                     GenerationCandidateRefinementComponentCode.NonGameplayCapture })
        {
            var before = fixture.Intelligence;
            var first = before.Refinements[0];
            var blocked = new GenerationCandidateRefinement(first.Candidate,
                [.. first.Components.Where(value => value.Code != code), new(code, 1, 0, "Retained eligibility gate fixture.", ["fixture:gate"])], first.PolicyVersion);
            var refinements = before.Refinements.Select(value => ReferenceEquals(value, first) ? blocked : value).ToDictionary(value => value.Candidate);
            var moments = new GenerationMomentFindingResult(before.BaseMoments.Request, before.BaseMoments.Sources,
                new GenerationMomentPortfolioSelector().Select(before.BaseMoments.Request, before.BaseMoments.Sources, refinements), refinements);
            var gated = new GenerationCandidateIntelligenceResult(before.BaseMoments, before.SpeechActivity, refinements.Values, moments, visual, before.Transcripts);
            TestAssert.False(GenerationSemanticFinalSelectionPreference.Create(gated).ContainsKey(first.Candidate), $"{code} remains authoritative despite a high text similarity.");
            TestAssert.Same(before.Refinements[1].Candidate, GenerationReviewedSelectionPolicy.Apply(gated, CancellationToken.None).RefinedMoments.SelectedCandidates.Single().Candidate,
                "Final preference cannot reintroduce an automatically ineligible candidate.");
        }
        var lowQuality = FinalQueryFixture(scores: [61.9, 62.1], similarities: [1, .01]);
        using var lowQualityReview = lowQuality.Visual;
        TestAssert.True(lowQuality.Intelligence.Refinements[0].FinalScore < 70 && lowQuality.Intelligence.Refinements[1].FinalScore > 70,
            "The fixture must straddle the unchanged quality threshold before preference is applied.");
        TestAssert.False(GenerationSemanticFinalSelectionPreference.Create(lowQuality.Intelligence).ContainsKey(lowQuality.Intelligence.Refinements[0].Candidate),
            "Even maximal similarity cannot turn a below-threshold clip into a quality-qualified clip.");
        TestAssert.Same(lowQuality.Intelligence.Refinements[1].Candidate,
            GenerationReviewedSelectionPolicy.Apply(lowQuality.Intelligence, CancellationToken.None).RefinedMoments.SelectedCandidates.Single().Candidate,
            "The existing quality-qualified candidate must win before any count-fill pass.");
        return Task.CompletedTask;
    }

    private static Task FinalQueryPreferencePreservesGuidanceAndNovelty()
    {
        string path = TestMediaFactory.CreateSourcePath("final-query.mkv");
        foreach (var guidance in new[] { UserMomentGuidance.CreatePoint(path, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(65)),
                     UserMomentGuidance.CreateRange(path, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90)) })
        {
            var fixture = FinalQueryFixture(guidance: new([guidance]));
            using var visual = fixture.Visual;
            var selected = GenerationReviewedSelectionPolicy.Apply(fixture.Intelligence, CancellationToken.None).RefinedMoments.SelectedCandidates.Single();
            TestAssert.Same(fixture.Intelligence.Refinements[1].Candidate, selected.Candidate, "An explicit point or reserved range takes priority over query similarity.");
            TestAssert.True(selected.IsHumanPriority, "The selected candidate must retain its manual selection reason.");
        }
        var diverse = FinalQueryFixture(scores: [90, 80, 79.9], similarities: [.1, 1, .2], desired: 2, spacing: 21);
        using var diverseReview = diverse.Visual;
        var portfolio = GenerationReviewedSelectionPolicy.Apply(diverse.Intelligence, CancellationToken.None).RefinedMoments.SelectedCandidates;
        TestAssert.Same(diverse.Intelligence.Refinements[0].Candidate, portfolio[0].Candidate, "The stronger first moment still wins.");
        TestAssert.Same(diverse.Intelligence.Refinements[2].Candidate, portfolio[1].Candidate,
            "A one-point preference must be scaled by existing footage novelty; the independent cut must beat the highly similar overlapping repeat.");
        return Task.CompletedTask;
    }

    private static Task FinalQueryPreferencePreservesTranscriptOwnership()
    {
        var fixture = FinalQueryFixture();
        using var visual = fixture.Visual;
        var before = fixture.Intelligence;
        var transcripts = before.Transcripts!;
        var originalRetrieval = transcripts.SemanticRetrieval!;
        var malformedRetrieval = originalRetrieval with
        {
            Matches = originalRetrieval.Matches.Select((match, index) => index == 0 ? match with { Similarity = 2 } : match).ToArray(),
        };
        // Exercise the final preference's defensive cap independently; the
        // normal refinement component correctly rejects out-of-range inputs.
        var capFixture = new GenerationCandidateIntelligenceResult(before.BaseMoments, before.SpeechActivity,
            before.Refinements, before.RefinedMoments, visual,
            new GenerationTranscriptAnalysisResult(transcripts.ExpandedMoments, transcripts.Sources, malformedRetrieval));
        TestAssert.Same(before.Refinements[0], capFixture.Refinements[0], "The cap fixture must retain valid independently constructed evidence scores.");
        TestAssert.Equal(1d, GenerationSemanticFinalSelectionPreference.Create(capFixture)[before.Refinements[0].Candidate],
            "The preference must be capped even when an injected retrieval result exceeds the cosine contract.");
        foreach (string condition in new[] { "path", "stream", "outside" })
        {
            var matches = originalRetrieval.Matches.Select((match, index) => index == 0 ? match with
            {
                Window = match.Window with
                {
                    SourceFullPath = condition == "path" ? TestMediaFactory.CreateSourcePath("foreign-query.mkv") : match.Window.SourceFullPath,
                    AudioStreamIndex = condition == "stream" ? 99 : match.Window.AudioStreamIndex,
                    Start = condition == "outside" ? before.Refinements[0].Candidate.Window.Start - TimeSpan.FromSeconds(1) : match.Window.Start,
                },
            } : match).ToArray();
            var retrieval = originalRetrieval with { Matches = matches };
            var rebound = before.WithTranscripts(new(transcripts.ExpandedMoments, transcripts.Sources, retrieval));
            TestAssert.False(GenerationSemanticFinalSelectionPreference.Create(rebound).ContainsKey(before.Refinements[0].Candidate),
                $"A {condition} mismatch cannot borrow relevance from outside the retained source, stream, or complete passage.");
        }
        return Task.CompletedTask;
    }

    private static (GenerationCandidateIntelligenceResult Intelligence, GenerationVisualSemanticAnalysisResult Visual) FinalQueryFixture(
        GenerationDiscoveryIntent? intent = null, IReadOnlyList<double>? scores = null, IReadOnlyList<double>? similarities = null,
        int desired = 1, double spacing = 50, GenerationMomentGuidance? guidance = null,
        Func<GenerationCandidateIntelligenceResult, MomentCandidate, int, GenerationVisualSemanticCandidateObservation?>? reviewFactory = null)
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("final-query.mkv", 1)], sourceDuration: TimeSpan.FromMinutes(5),
            desiredCount: desired, discoveryIntent: intent ?? new(naturalLanguageQuery: "Opening the locked doorway"), momentGuidance: guidance);
        var moments = CreateMoments(request, scores ?? [76.2, 76.5], windowSpacingSeconds: spacing);
        var baseline = new GenerationCandidateRefinementService().Refine(moments, CreateSpeech(request, AudioContentRoleAssignment.Unknown, []));
        var segments = baseline.Refinements.Select((item, index) => SentenceSegment("query-passage-" + index, "source",
            "This complete sentence describes a moment.", item.Candidate.Window.Start.TotalSeconds + 2, item.Candidate.Window.Start.TotalSeconds + 5)).ToArray();
        var transcript = new GenerationSourceTranscript(request.ReferenceSource.FullPath, 1, segments, []);
        var retrieval = new GenerationSemanticRetrievalResult(segments.Select((segment, index) => new GenerationSemanticRetrievalMatch(
            new(request.ReferenceSource.FullPath, 1, segment.AbsoluteSourceStart, segment.AbsoluteSourceEnd, segment.Text, [segment.Id]),
            (similarities ?? [.75, .05])[index])).ToArray(), "fixture-only", null, null, TimeSpan.Zero, 0, "Injected relevance, not model qualification.");
        baseline = baseline.WithTranscripts(new(moments, [transcript], retrieval));
        var observations = baseline.Refinements.Select((item, index) => reviewFactory is null
                ? Reviewed(baseline, item.Candidate, QueryKeep()) : reviewFactory(baseline, item.Candidate, index))
            .OfType<GenerationVisualSemanticCandidateObservation>().ToArray();
        var visual = new GenerationVisualSemanticAnalysisResult(baseline, new InferenceProviderIdentity("fixture", "1", "1"), observations, TimeSpan.Zero, null);
        return (new GenerationCandidateRefinementService().ApplyVisualSemantic(baseline, visual), visual);
    }

    private static VisualSemanticEditorialObservation QueryKeep(VisualSemanticTranscriptContextSupport support = VisualSemanticTranscriptContextSupport.NotSupplied,
        bool uncertain = false)
    {
        var original = CreateVisualObservation(VisualSemanticEditorialDisposition.Keep, VisualSemanticEditorialRejectReason.None,
            VisualSemanticTernary.Yes, "A distinct visual event and its payoff are present.");
        return new(original.ObservableContentType, original.HasDistinctEvent, original.HasObservablePayoff, original.RoutineTraversalOrMenuOnly,
            original.CandidateRequiresMissingContext, original.CandidateContainsOnlyAmbientChange, support, original.ObservedChanges,
            original.EvidenceIntervals, uncertain ? [new VisualSemanticEditorialUncertainty(VisualSemanticEditorialUncertaintyCode.TranscriptMayBeInaccurate,
                "Retained transcript uncertainty fixture.")] : original.UncertaintyReasons,
            original.EditorialDisposition, original.RejectReason, original.DispositionRationale);
    }
}
