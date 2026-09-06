using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Features.Generate.Guidance;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static IEnumerable<TestCase> GenerationMaturityTests()
    {
        yield return new("Generation includes the beginning and ending of creator speech", SpeechRepairsBothBoundaries);
        yield return new("Transcript beginning repair includes sentence context across a VAD pause", TranscriptBeginningBridgesSentencePause);
        yield return new("Transcript beginning repair uses measured sentence word boundaries", TranscriptBeginningUsesMeasuredWords);
        yield return new("Transcript beginning repair crosses continuously transcribed source chunks", TranscriptBeginningCrossesContinuousChunks);
        yield return new("Uncertain sentence boundaries preserve VAD beginning repair", TranscriptBeginningFallsBackWhenUncertain);
        yield return new("Transcript beginning repair preserves duration, event, ending, and user intent", TranscriptBeginningPreservesEventEndAndIntent);
        yield return new("Transcript ending repair includes the recorded continuation across a VAD pause", TranscriptEndingRepairsRecordedSentencePause);
        yield return new("Transcript endings use measured words or complete reliable phrases", TranscriptEndingUsesMeasuredBoundariesAndWholePhraseFallback);
        yield return new("Transcript ending repair crosses continuously covered decoder chunks", TranscriptEndingCrossesContinuousChunks);
        yield return new("Uncertain transcript endings preserve the VAD fallback", TranscriptEndingPreservesUncertainFallback);
        yield return new("Transcript endings preserve duration, retained events, and complete beginnings", TranscriptEndingPreservesDurationEventsAndBeginning);
        yield return new("Transcript ending gates preserve quality, source roles, and user guidance", TranscriptEndingPreservesQualityRolesAndGuidance);
        yield return new("Caption model languages come from GGML metadata instead of file names", CaptionModelCapabilitiesUseMetadata);
        yield return new("Missing and unknown caption models expose unavailable language choices", CaptionModelUnknownIsExplicit);
        yield return new("Caption setup preserves unsupported saved languages until explicitly corrected", CaptionLanguageSelectionPreservesUnsupportedChoice);
        yield return new("Caption language catalog is complete and never leaks translation mode", CaptionLanguageCatalogAndOptionsAreComplete);
        yield return new("Caption provider rejects unsupported language and replaced-model requests before starting a process", CaptionProviderRejectsUnsupportedAndChangedModels);
        yield return new("Semantic creator queries remain separate from literal phrases and opt in explicitly", SemanticQueryIsSeparateAndOptIn);
        yield return new("MiniLM tokenization uses bounded uncased BERT WordPiece input", MiniLmTokenizationUsesBoundedWordPieces);
        yield return new("Semantic transcript windows preserve language provenance, timing, source coverage and cancellation", SemanticWindowsPreserveLanguageClocksAndCoverage);
        yield return new("Semantic text nominations add no event score and require grounded review within the budget", SemanticNominationsNeedGroundedReview);
        yield return new("Semantic passages survive overlapping mid-speech heuristic cuts and await complete review", SemanticNominationRepairsCoverageOfMalformedCut);
        yield return new("Semantic passage edits preserve exact coverage, duration bounds, duplicates and proposal budgets", SemanticNominationCoveragePreservesBoundsAndBudgets);
        yield return new("Final explicit-query preference resolves reviewed near-ties without changing evidence scores or defaults", FinalQueryPreferenceResolvesOnlyNearTies);
        yield return new("Final query preference requires a complete unambiguous Keep from the owning source", FinalQueryPreferenceRequiresOwnedCompleteKeep);
        yield return new("Final query preference cannot cross speech capture rejection or quality gates", FinalQueryPreferencePreservesEligibility);
        yield return new("Final query preference preserves manual guidance and footage novelty", FinalQueryPreferencePreservesGuidanceAndNovelty);
        yield return new("Final query preference preserves transcript source stream and complete passage ownership", FinalQueryPreferencePreservesTranscriptOwnership);
        yield return new("Default source transcription never runs semantic inference and retains detected language", SemanticSourceAnalysisIsInactiveByDefault);
        yield return new("Generation never fills counts with a mid-speech beginning", SpeechBeginningRejectsCountFill);
        yield return new("Grounded semantic rejection excludes even saturated high scores", GroundedRejectionExcludesCountFill);
        yield return new("Visual rejection cannot override an unreviewed spoken story", VisualRejectionRespectsSpeechAndPartialReview);
        yield return new("Semantic exploration finds unproposed source windows without inventing evidence", ExplorationFindsUnproposedWindows);
        yield return new("Semantic exploration requires a grounded Keep before selection", ExplorationRequiresGroundedKeep);
        yield return new("Semantic review budgets scale with duration and preserve bounded provider batches", AdaptiveReviewUsesBoundedBatches);
        yield return new("Semantic review represents different source recordings", SemanticReviewRepresentsSources);
        yield return new("Portfolio rewards new footage before nearly overlapping repeats", PortfolioRewardsNewFootage);
        yield return new("Spoken intent discovers late-source matches without claiming unseen events", SpokenIntentDiscoversLateSource);
        yield return new("Preference learning persists independent game and intent contexts", ContextualPreferencesPersistSeparately);
        yield return new("Capture screening requires repeated multi-label software chrome and preserves gameplay HUD", CaptureScreeningIsConservative);
        yield return new("Startup lead-in review requires repeated timed app evidence and complete all-stream speech analysis", StartupLeadInRequiresCompleteLeadingEvidence);
        yield return new("Startup lead-in review preserves duration thresholds, explicit intent, and manual guidance", StartupLeadInPreservesDurationIntentAndManualChoice);
        yield return new("Startup screening uses at most five owned OCR frames and retains its distinct review marker", StartupScreeningUsesFiveOwnedFramesAndRetainsReview);
        yield return new("Startup screening preserves query and speech bypasses, timestamp ownership, and adaptive bounds", StartupScreeningPreservesBypassesOwnershipAndBounds);
        yield return new("Silent launcher exclusion requires successful speech evidence and preserves user guidance", SilentLauncherGateRequiresEvidence);
        yield return new("Grounded semantic Keep supersedes capture-screen ranking penalties", VisualKeepSupersedesScreenPenalty);
        yield return new("Corroborated unavailable gameplay stays reviewable but cannot fill automatic clip counts", UnavailableGameplayExcludesCountFill);
        yield return new("Unavailable gameplay rejection requires both integrity signals and a complete unambiguous no-payoff review", UnavailableGameplayRequiresJointEvidence);
        yield return new("Unavailable gameplay rejection preserves explicit commentary, story, discovery, query, and marker intent", UnavailableGameplayPreservesExplicitIntent);
    }

    private static Task SpeechRepairsBothBoundaries()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Balanced,
            [("whole-utterance.mkv", 1)], desiredCount: 1);
        GenerationMomentFindingResult moments = CreateMoments(request, [90, 78]);
        GenerationCandidateIntelligenceResult result = new GenerationCandidateRefinementService().Refine(
            moments, CreateSpeech(request, new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(45)));
        GenerationMomentCandidate selected = result.RefinedMoments.SelectedCandidates.Single();
        TestAssert.Equal(TimeSpan.FromSeconds(4.25), selected.Candidate.Window.Start,
            "The start must include the opening of the creator utterance and natural lead-in.");
        TestAssert.Equal(TimeSpan.FromSeconds(45.75), selected.Candidate.Window.End,
            "Beginning repair must not undo a repaired natural ending.");
        TestAssert.False(selected.Refinement!.HasIncompleteSpeechBeginning ||
            selected.Refinement.HasIncompleteSpeechEnding,
            "A complete repaired utterance must be eligible automatically.");
        return Task.CompletedTask;
    }

    private static Task PortfolioRewardsNewFootage()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Fast,
            [("novel-footage.mkv", 1)], desiredCount: 2);
        GenerationMomentFindingResult result = CreateMoments(request, [100, 99, 94], windowSpacingSeconds: 21);
        TestAssert.Equal(TimeSpan.FromSeconds(52), result.SelectedCandidates[1].Candidate.Window.Start,
            "A strong independent interval should beat a nearly equal cut that repeats already-selected footage.");
        return Task.CompletedTask;
    }

    private static Task SpokenIntentDiscoversLateSource()
    {
        var intent = new GenerationDiscoveryIntent(GenerationMomentIntent.Action, "final boss, win");
        TestAssert.Equal(0, intent.CountMatches("The window is open"), "Phrase matching must respect word boundaries.");
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("spoken-source.mkv", 1)], sourceDuration: TimeSpan.FromMinutes(20), desiredCount: 2,
            discoveryIntent: intent);
        GenerationMomentFindingResult original = CreateMoments(request, [80]);
        var source = new GenerationSourceTranscript(request.ReferenceSource.FullPath, 1,
            [new AudioTranscriptionSegment("late-boss", "source", "Now we have finally reached the final boss",
                TimeSpan.FromMinutes(17), TimeSpan.FromMinutes(17) + TimeSpan.FromSeconds(5),
                TimeSpan.FromMinutes(17), TimeSpan.FromMinutes(17) + TimeSpan.FromSeconds(5))], []);
        GenerationMomentFindingResult expanded = GenerationTranscriptCandidatePlanner.Expand(original, [source], CancellationToken.None);
        var intelligence = new GenerationCandidateRefinementService().Refine(expanded,
            CreateSpeech(request, AudioContentRoleAssignment.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(1)))
            .WithTranscripts(new(expanded, [source]));
        var discovery = intelligence.Refinements.Single(value => value.Candidate.ConstructionReason ==
            MomentCandidateConstructionReason.SemanticExploration);
        TestAssert.True(discovery.Candidate.Window.Start >= TimeSpan.FromMinutes(16),
            "Explicit spoken objectives must discover windows beyond the heuristic proposals.");
        TestAssert.True(discovery.Components.Any(value => value.Code == GenerationCandidateRefinementComponentCode.SpokenIntentMatch && value.RawValue > 0),
            "The exact timed phrase match must be retained as transparent ranking context.");
        TestAssert.True(discovery.RequiresSemanticReview, "Saying final boss cannot prove a boss encounter occurred.");
        return Task.CompletedTask;
    }

    private static Task ContextualPreferencesPersistSeparately()
    {
        string path = Path.Combine(Path.GetTempPath(), "preference-context-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var first = new ClipPreferenceContext("First game", "IndividualClips", "Balanced", "Action");
            var second = new ClipPreferenceContext("Second game", "IndividualClips", "Balanced", "Story");
            var features = new ClipPreferenceFeatureVector([new(ClipPreferenceFeatureCode.Duration, .5)], first);
            var store = new JsonClipPreferenceFeedbackStore(path);
            store.Update(features, null, ClipPreferenceRating.Like);
            var reopened = new JsonClipPreferenceFeedbackStore(path);
            TestAssert.Equal(1, reopened.ForContext(first).LikeCount, "Contextual feedback must survive reopening.");
            TestAssert.Equal(0, reopened.ForContext(second).RatedCount, "Action feedback in another game must not become story preference evidence.");
            reopened.Update(features, ClipPreferenceRating.Like, ClipPreferenceRating.Dislike);
            TestAssert.Equal(0, reopened.ForContext(first).LikeCount, "Changing a rating removes its old contextual contribution.");
            TestAssert.Equal(1, reopened.ForContext(first).DislikeCount, "Changing a rating records its replacement exactly once.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        return Task.CompletedTask;
    }

    private static Task CaptureScreeningIsConservative()
    {
        IReadOnlyList<string> steam = new[] { "CLOUD STATUS", "LAST PLAYED", "PLAY TIME", "ACHIEVEMENTS" };
        var launcher = GenerationCaptureContextPolicy.Assess([steam, steam, new[] { "Play" }]);
        TestAssert.Equal(GenerationCaptureContextKind.Launcher, launcher!.Kind,
            "Repeated cloud-status, last-played and play-time labels identify launcher chrome.");
        IReadOnlyList<string> wrapped = new[] { "Cloud\r\n STATUS", "Last", "PLAYED", "Play\t  time" };
        TestAssert.Equal(GenerationCaptureContextKind.Launcher,
            GenerationCaptureContextPolicy.Assess([wrapped, wrapped])!.Kind,
            "Case, repeated whitespace, and OCR line breaks must not break exact multi-label matching.");
        IReadOnlyList<string> recordedSteam = new[] { "STORE LIBRARY COMMUNITY SODA", "STOPPING", "CLOUD STATUS", "Up to date", "LAST PLAYED", "PLAYTIME", "ACHIEVEMENTS" };
        TestAssert.Equal(GenerationCaptureContextKind.Launcher,
            GenerationCaptureContextPolicy.Assess([steam, recordedSteam, new[] { "Pres", "NORDIC" }])!.Kind,
            "Recorded 22.5-second OCR joins PLAY TIME to PLAYTIME; its two other exact Steam labels must preserve repeated launcher evidence.");
        TestAssert.True(GenerationCaptureContextPolicy.Assess([new[] { "PLAYTIME", "Inventory" }, new[] { "PLAYTIME", "Inventory" }]) is null,
            "The known joined label alone cannot classify gameplay as a launcher.");
        TestAssert.True(GenerationCaptureContextPolicy.Assess([steam, new[] { "health 100", "ammo 30" }]) is null,
            "A single uncertain launcher frame cannot penalize a complete gameplay event.");
        IReadOnlyList<string> hud = new[] { "Health 100", "Ammo 30", "Inventory", "Achievement unlocked", "Loading bay" };
        TestAssert.True(GenerationCaptureContextPolicy.Assess([hud, hud, hud]) is null,
            "Readable HUD and world-location text must not be mistaken for loading screens or software chrome.");
        TestAssert.Equal(GenerationCaptureContextKind.Loading,
            GenerationCaptureContextPolicy.Assess([new[] { "Loading..." }, new[] { "LOADING 50%" }])!.Kind,
            "Repeated exact loading-state labels are bounded evidence of a waiting screen.");
        TestAssert.False(GenerationCaptureContextPolicy.MayScreen(new(GenerationMomentIntent.Dialogue), ContentEmphasis.Balanced),
            "Dialogue and tutorial/story intent must not be demoted by software-surface screening.");
        TestAssert.False(GenerationCaptureContextPolicy.MayScreen(GenerationDiscoveryIntent.Default, ContentEmphasis.CommentaryFocused),
            "Commentary focus does not require gameplay to be visible.");
        return Task.CompletedTask;
    }

    private static Task VisualKeepSupersedesScreenPenalty()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("screen-authority.mkv", 1)], desiredCount: 1);
        var original = CreateCandidateIntelligence(request, [90]);
        var refinement = new GenerationCandidateRefinement(original.Refinements.Single().Candidate,
            [.. original.Refinements.Single().Components,
                new(GenerationCandidateRefinementComponentCode.CaptureContextPenalty, 1, -20, "Repeated launcher labels."),
                new(GenerationCandidateRefinementComponentCode.NonGameplayCapture, 1, 0, "Repeated launcher labels and no speech.")], "test");
        var screened = new GenerationCandidateIntelligenceResult(original.BaseMoments, original.SpeechActivity,
            [refinement], original.RefinedMoments);
        using var visual = new GenerationVisualSemanticAnalysisResult(screened,
            new InferenceProviderIdentity("fake-review", "1", "1"),
            [Reviewed(screened, refinement.Candidate, CreateVisualObservation(VisualSemanticEditorialDisposition.Keep,
                VisualSemanticEditorialRejectReason.None, VisualSemanticTernary.Yes, "The full reviewed cut contains a meaningful event."))], TimeSpan.Zero, null);
        var result = new GenerationCandidateRefinementService().ApplyVisualSemantic(screened, visual);
        TestAssert.False(result.Refinements.Single().Components.Any(static component =>
            component.Code is GenerationCandidateRefinementComponentCode.CaptureContextPenalty or GenerationCandidateRefinementComponentCode.NonGameplayCapture),
            "A full grounded Keep has more editorial authority than conservative OCR surface context.");
        TestAssert.Equal(1, result.RefinedMoments.SelectedCandidates.Count, "Clearing both capture markers must restore automatic eligibility.");
        return Task.CompletedTask;
    }

    private static Task SilentLauncherGateRequiresEvidence()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("silent-launcher.mkv", 1)], desiredCount: 2);
        var speech = CreateSpeech(request, AudioContentRoleAssignment.Unknown, []);
        var intelligence = new GenerationCandidateRefinementService().Refine(CreateMoments(request, [100, 78]), speech);
        var first = intelligence.Refinements.First();
        var sourceSpeech = speech.Sources.Single();
        var launcher = GenerationCaptureContextPolicy.Assess([
            new[] { "CLOUD STATUS", "LAST PLAYED", "PLAY TIME" },
            new[] { "CLOUD STATUS", "LAST PLAYED", "PLAYTIME" }, new[] { "NORDIC" }])!;
        bool excluded = GenerationCaptureContextPolicy.ShouldExcludeAutomatically(launcher, sourceSpeech, first.Candidate, request.SetupOptions);
        TestAssert.True(excluded, "Repeated launcher chrome and complete successful VAD with zero speech may exclude automatic selection.");
        var gated = new GenerationCandidateRefinement(first.Candidate,
            [.. first.Components, new(GenerationCandidateRefinementComponentCode.CaptureContextPenalty, 1, -20, "Repeated launcher labels."),
                new(GenerationCandidateRefinementComponentCode.NonGameplayCapture, excluded ? 1 : 0, 0, "Repeated software interface; measured no speech. Manual review remains available.")], "test");
        TestAssert.True(gated.FinalScore >= request.SetupOptions.QualityThreshold,
            "The regression must demonstrate why a soft penalty alone leaves this launcher qualified.");
        var refinements = intelligence.Refinements.ToDictionary(static value => value.Candidate);
        refinements[first.Candidate] = gated;
        var selected = new GenerationMomentPortfolioSelector().Select(intelligence.BaseMoments.Request, intelligence.BaseMoments.Sources, refinements);
        TestAssert.Equal(1, selected.Count, "Automatic count filling must not return a positively identified silent application capture.");
        var result = new GenerationMomentFindingResult(intelligence.BaseMoments.Request, intelligence.BaseMoments.Sources, selected, refinements);
        var screened = new GenerationCandidateIntelligenceResult(intelligence.BaseMoments, speech, refinements.Values, result);
        TestAssert.Equal(GenerationHiddenMomentReason.CaptureContextReview,
            GenerationHiddenMomentPlanner.Create(result, screened).Moments.Single().Reason, "The gated clip must remain discoverable for manual review.");

        TestAssert.False(GenerationCaptureContextPolicy.ShouldExcludeAutomatically(launcher,
            new GenerationSourceSpeechActivity(sourceSpeech.Source, []), first.Candidate, request.SetupOptions),
            "Unavailable VAD is never evidence of silence.");
        var spoken = CreateSpeech(request, AudioContentRoleAssignment.Unknown,
            first.Candidate.Window.Start + TimeSpan.FromSeconds(1), first.Candidate.Window.End - TimeSpan.FromSeconds(1));
        TestAssert.False(GenerationCaptureContextPolicy.ShouldExcludeAutomatically(launcher, spoken.Sources.Single(), first.Candidate, request.SetupOptions),
            "Even speech with an unknown stream role preserves a potentially meaningful spoken story.");
        TestAssert.False(GenerationCaptureContextPolicy.ShouldExcludeAutomatically(new(GenerationCaptureContextKind.Loading, [0, 1]),
            sourceSpeech, first.Candidate, request.SetupOptions), "Loading text alone remains a soft ranking signal.");
        foreach (var guidance in new[]
        {
            UserMomentGuidance.CreatePoint(sourceSpeech.Source.PreparedSource.Media.FullPath, sourceSpeech.Source.PreparedSource.Media.Duration,
                first.Candidate.Window.Start + TimeSpan.FromSeconds(1)),
            UserMomentGuidance.CreateRange(sourceSpeech.Source.PreparedSource.Media.FullPath, sourceSpeech.Source.PreparedSource.Media.Duration,
                first.Candidate.Window.Start, first.Candidate.Window.End),
        })
        {
            var guided = CreateRequest(GenerationAnalysisDepth.Thorough, [("silent-launcher.mkv", 1)],
                momentGuidance: new GenerationMomentGuidance([guidance]));
            TestAssert.False(GenerationCaptureContextPolicy.ShouldExcludeAutomatically(launcher, sourceSpeech, first.Candidate, guided.SetupOptions),
                "An explicit matching creator marker or range must preserve the requested moment.");
        }
        var twoStreams = CreateRequest(GenerationAnalysisDepth.Thorough, [("partial-vad-launcher.mkv", 2)]);
        var partial = CreateSpeech(twoStreams, AudioContentRoleAssignment.Unknown, []);
        TestAssert.False(GenerationCaptureContextPolicy.ShouldExcludeAutomatically(launcher, partial.Sources.Single(), first.Candidate, twoStreams.SetupOptions),
            "A successful analysis of one track cannot establish silence on an unexamined second track.");
        return Task.CompletedTask;
    }

    private static Task SpeechBeginningRejectsCountFill()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Balanced,
            [("unknown-utterance.mkv", 1)], desiredCount: 2);
        GenerationMomentFindingResult moments = CreateMoments(request, [99, 78]);
        GenerationCandidateIntelligenceResult result = new GenerationCandidateRefinementService().Refine(
            moments, CreateSpeech(request, AudioContentRoleAssignment.Unknown,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)));
        TestAssert.True(result.Refinements.First().HasIncompleteSpeechBeginning,
            "Unclassified audio crossing the start must still have a typed boundary failure.");
        TestAssert.Equal(1, result.RefinedMoments.SelectedCandidates.Count,
            "Count filling must not resurrect an incomplete beginning.");
        TestAssert.Equal(GenerationHiddenMomentReason.IncompleteSpeechBoundary,
            GenerationHiddenMomentPlanner.Create(result.RefinedMoments, result).Moments.Single().Reason,
            "The creator must be able to find and repair the suppressed window.");
        return Task.CompletedTask;
    }

    private static Task GroundedRejectionExcludesCountFill()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("grounded-menu-reject.mkv", 1)], desiredCount: 2);
        GenerationCandidateIntelligenceResult intelligence = CreateCandidateIntelligence(request, [100, 78]);
        GenerationCandidateRefinement rejected = intelligence.Refinements.First();
        GenerationVisualSemanticCandidateObservation reviewed = Reviewed(intelligence,
            rejected.Candidate, CreateVisualObservation(VisualSemanticEditorialDisposition.Reject,
                VisualSemanticEditorialRejectReason.RoutineTraversal, VisualSemanticTernary.No,
                "The sampled frames consistently show routine traversal with no distinct event."));
        using var visual = new GenerationVisualSemanticAnalysisResult(intelligence,
            new InferenceProviderIdentity("fake-review", "1", "1"), [reviewed], TimeSpan.Zero, null);
        GenerationCandidateIntelligenceResult result = new GenerationCandidateRefinementService()
            .ApplyVisualSemantic(intelligence, visual);
        TestAssert.True(result.Refinements.First().HasGroundedVisualRejection,
            "A complete grounded Reject must have an explicit eligibility marker.");
        TestAssert.True(result.Refinements.First().FinalScore >= 70,
            "The fixture must demonstrate rejection even when a bounded penalty leaves a high score.");
        TestAssert.Equal(1, result.RefinedMoments.SelectedCandidates.Count,
            "Neither quality selection nor relaxed count filling may return the rejected candidate.");
        TestAssert.Equal(GenerationHiddenMomentReason.GroundedVisualRejection,
            GenerationHiddenMomentPlanner.Create(result.RefinedMoments, result).Moments.Single().Reason,
            "Rejected candidates remain reviewable with the actual reason.");
        return Task.CompletedTask;
    }

    private static Task VisualRejectionRespectsSpeechAndPartialReview()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("creator-narrative.mkv", 1)], desiredCount: 1);
        GenerationCandidateIntelligenceResult intelligence = new GenerationCandidateRefinementService().Refine(
            CreateMoments(request, [90]), CreateSpeech(request, new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(25)));
        GenerationCandidateRefinement refinement = intelligence.Refinements.Single();
        VisualSemanticEditorialObservation observation = CreateVisualObservation(
            VisualSemanticEditorialDisposition.Reject, VisualSemanticEditorialRejectReason.RoutineTraversal,
            VisualSemanticTernary.No, "Only routine traversal is visible.");
        TestAssert.False(GenerationGroundedVisualRejectionPolicy.ShouldExcludeAutomatically(refinement,
            Reviewed(intelligence, refinement.Candidate, observation)),
            "Visual-only evidence cannot disqualify a spoken story whose meaning was not supplied.");
        GenerationCandidateIntelligenceResult quiet = CreateCandidateIntelligence(request, [90]);
        TestAssert.False(GenerationGroundedVisualRejectionPolicy.ShouldExcludeAutomatically(quiet.Refinements.Single(),
            Reviewed(quiet, quiet.Refinements.Single().Candidate, observation, partial: true)),
            "A focused slice cannot prove the whole candidate should be rejected.");
        TestAssert.False(GenerationGroundedVisualRejectionPolicy.ShouldExcludeAutomatically(quiet.Refinements.Single(),
            Reviewed(quiet, quiet.Refinements.Single().Candidate,
                CreateVisualObservation(VisualSemanticEditorialDisposition.Reject,
                    VisualSemanticEditorialRejectReason.RoutineTraversal, VisualSemanticTernary.No))),
            "An unsupported Reject remains a bounded ranking signal, not an automatic exclusion.");
        return Task.CompletedTask;
    }

    private static Task ExplorationFindsUnproposedWindows()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("quiet-long-source.mkv", 1)], sourceDuration: TimeSpan.FromMinutes(20), desiredCount: 2);
        GenerationMomentFindingResult original = CreateMoments(request, [90]);
        GenerationMomentFindingResult expanded = GenerationSemanticExplorationPlanner.Expand(original, CancellationToken.None);
        MomentCandidate[] additional = expanded.Sources.Single().Moments.Proposals.Where(static candidate =>
            candidate.ConstructionReason == MomentCandidateConstructionReason.SemanticExploration).ToArray();
        TestAssert.True(additional.Length >= 2 && additional.Any(candidate =>
            candidate.Window.Start > TimeSpan.FromMinutes(10)),
            "A long recording must offer review windows well outside the heuristic proposal.");
        TestAssert.True(additional.All(static candidate => candidate.HeuristicScore == 0 &&
            candidate.Anchors.All(static anchor => anchor.Kind == MomentAnchorKind.SourceCoverage)),
            "Coverage alone cannot masquerade as an observed event or heuristic confidence.");
        TestAssert.True(expanded.SelectedCandidates.All(static candidate =>
            candidate.Candidate.ConstructionReason != MomentCandidateConstructionReason.SemanticExploration),
            "Unreviewed source windows cannot enter the selected portfolio.");
        return Task.CompletedTask;
    }

    private static Task ExplorationRequiresGroundedKeep()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("semantic-recall.mkv", 1)], sourceDuration: TimeSpan.FromMinutes(20), desiredCount: 2);
        GenerationMomentFindingResult expanded = GenerationSemanticExplorationPlanner.Expand(
            CreateMoments(request, [40]), CancellationToken.None);
        GenerationCandidateIntelligenceResult intelligence = new GenerationCandidateRefinementService().Refine(
            expanded, CreateSpeech(request, AudioContentRoleAssignment.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        MomentCandidate candidate = intelligence.Refinements.First(static value => value.RequiresSemanticReview).Candidate;
        IReadOnlyList<GenerationVisualSemanticAnalysisService.CandidateSource> shortlist =
            GenerationVisualSemanticAnalysisService.CreateShortlist(intelligence, 8);
        TestAssert.True(shortlist.Any(static value => value.Candidate.ConstructionReason ==
            MomentCandidateConstructionReason.SemanticExploration),
            "The visual budget must reserve a real exploration slot despite a zero heuristic score.");
        using var visual = new GenerationVisualSemanticAnalysisResult(intelligence,
            new InferenceProviderIdentity("fake-review", "1", "1"),
            [Reviewed(intelligence, candidate, CreateVisualObservation(
                VisualSemanticEditorialDisposition.Keep, VisualSemanticEditorialRejectReason.None,
                VisualSemanticTernary.Yes, "The player completes a distinct action and the resulting payoff is visible."))],
            TimeSpan.Zero, null);
        GenerationCandidateIntelligenceResult refined = new GenerationCandidateRefinementService()
            .ApplyVisualSemantic(intelligence, visual);
        TestAssert.True(refined.RefinedMoments.SelectedCandidates.Any(value => ReferenceEquals(value.Candidate, candidate)),
            "A grounded discovery must be able to enter output without a pre-existing heuristic event.");
        TestAssert.True(refined.RefinedMoments.SelectedCandidates.All(value => value.Refinement?.RequiresSemanticReview != true),
            "Count filling must continue excluding all other unreviewed windows.");
        return Task.CompletedTask;
    }

    private static async Task AdaptiveReviewUsesBoundedBatches()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("long-review-budget.mkv", 1)], sourceDuration: TimeSpan.FromHours(2), desiredCount: 12);
        GenerationCandidateIntelligenceResult intelligence = CreateCandidateIntelligence(request,
            Enumerable.Range(0, 12).Select(static index => 90d - index).ToArray());
        TestAssert.Equal(32, GenerationSemanticReviewBudgetPolicy.Resolve(intelligence.BaseMoments, 32),
            "Long sources receive a larger bounded review budget.");
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = new FakeVisualEditorialProvider();
        using GenerationVisualSemanticAnalysisResult result = await new GenerationVisualSemanticAnalysisService(
            provider, materializer, CreateVisualSettings()).AnalyzeAsync(intelligence, null, CancellationToken.None);
        TestAssert.Equal(12, result.Observations.Count, "The analysis result must retain all reviewed batches.");
        TestAssert.Equal(2, provider.Requests.Count, "Twelve reviews must execute as two bounded batches.");
        TestAssert.True(provider.Requests.All(static batch => batch.Requests.Count <= 8),
            "No expanded review budget may grow one provider batch past eight.");
    }

    private static async Task SemanticReviewRepresentsSources()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("source-dominant.mkv", 1), ("source-quiet.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(10), desiredCount: 2);
        var finder = new GenerationMomentFindingService(new GenerationMomentFindingTests.RecordingMomentFinder(
            [new double[] { 99, 98, 97 }, new double[] { 40, 39, 38 }]));
        GenerationMomentFindingResult moments = finder.Find(new GenerationMomentFindingRequest(
            request.EvidenceAnalysis, request.SetupOptions));
        GenerationSpeechActivityResult speech = await CreateService(new FakeAudioExtractor(), new FakeSpeechProvider())
            .AnalyzeAsync(request, new RecordingProgress<GenerationSpeechActivityProgress>(), CancellationToken.None);
        GenerationCandidateIntelligenceResult intelligence = new GenerationCandidateRefinementService().Refine(moments, speech);
        IReadOnlyList<GenerationVisualSemanticAnalysisService.CandidateSource> shortlist =
            GenerationVisualSemanticAnalysisService.CreateShortlist(intelligence, 3);
        TestAssert.Equal(2, shortlist.Select(static item => item.Source).Distinct().Count(),
            "One source's higher heuristic scores cannot consume every review slot.");
    }

    private static GenerationVisualSemanticCandidateObservation Reviewed(
        GenerationCandidateIntelligenceResult intelligence, MomentCandidate candidate,
        VisualSemanticEditorialObservation observation, bool partial = false)
    {
        var canonical = VisualSemanticEditorialCanonicalizer.Canonicalize(
            observation.ObservedChanges, observation.EvidenceIntervals, observation.UncertaintyReasons);
        return new(candidate, intelligence.BaseMoments.Sources.Single(source =>
                source.Moments.Proposals.Contains(candidate)).AnalyzedSource,
            candidate.Window.Start + (partial ? TimeSpan.FromSeconds(1) : TimeSpan.Zero), candidate.Window.End,
            new string('A', 64), observation, canonical.Audit, TimeSpan.Zero);
    }
}
