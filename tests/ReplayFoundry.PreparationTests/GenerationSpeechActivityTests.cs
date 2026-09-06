using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.AudioExtraction;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    public static IEnumerable<TestCase> GetTests()
    {
        foreach (TestCase test in GenerationMaturityTests())
        {
            yield return test;
        }
        foreach (TestCase test in PromotedReviewTests()) yield return test;
        yield return new TestCase(
            "Generation speech activity skips Fast before extraction",
            FastSkipsBeforeExtraction);
        yield return new TestCase(
            "Generation speech activity analyzes every absolute stream in order",
            EveryStreamInPreparationOrder);
        yield return new TestCase(
            "Generation speech activity uses only an explicit user-confirmed role",
            ExplicitRoleOnly);
        yield return new TestCase(
            "Generation speech activity reports typed stream progress",
            TypedStreamProgress);
        yield return new TestCase(
            "Generation speech activity cancellation cleans audio and stops later streams",
            CancellationCleansAndStops);
        yield return new TestCase(
            "Generation speech activity translates source-specific provider failures",
            FailurePreservesContext);
        yield return new TestCase(
            "Generation speech activity result collections are immutable",
            ResultCollectionsAreImmutable);
        yield return new TestCase(
            "Generation speech activity keeps long sources in bounded chunks",
            LongSourcesUseBoundedChunks);
        yield return new TestCase(
            "Candidate refinement reranks through transparent VAD components",
            RefinementReranksTransparently);
        yield return new TestCase(
            "Balanced unknown speech stays timing-only",
            BalancedUnknownSpeechStaysTimingOnly);
        yield return new TestCase(
            "Candidate refinement keeps meaningful order above the display ceiling",
            RefinementRanksByUnclampedEvidence);
        yield return new TestCase(
            "Candidate refinement rejects automatic clips that cut through speech",
            RefinementRejectsIncompleteSpeechEnding);
        yield return new TestCase(
            "Candidate refinement extends confirmed creator speech with a natural tail",
            RefinementExtendsCreatorSpeechEnding);
        yield return new TestCase(
            "Candidate refinement bridges one immediate creator-speech pause",
            RefinementBridgesImmediateCreatorPause);
        yield return new TestCase(
            "Natural ending reclaims only safe pre-roll at the duration limit",
            NaturalEndingReclaimsSafePreRoll);
        yield return new TestCase(
            "Unsafe creator continuation chooses an alternate candidate",
            UnsafeCreatorContinuationChoosesAlternate);
        yield return new TestCase(
            "Natural ending preserves user ranges and recalculates diversity",
            NaturalEndingPreservesGuidanceAndDiversity);
        yield return new TestCase(
            "Candidate refinement obeys the user-selected content focus",
            RefinementUsesContentFocus);
        yield return new TestCase(
            "Balanced portfolios retain one near-peer gameplay event",
            BalancedPortfolioRetainsGameplayEvent);
        yield return new TestCase(
            "Gameplay coverage stays quality-gated and user-directed",
            GameplayCoverageStaysBounded);
        yield return new TestCase(
            "Candidate refinement applies only bounded game-agnostic preference history",
            RefinementUsesPreferenceProfile);
        yield return new TestCase(
            "Visual review command trims one exact video-only source interval",
            VisualReviewCommandIsBounded);
        yield return new TestCase(
            "Editorial visual review crops the confirmed Gameplay region",
            VisualReviewCommandCropsGameplay);
        yield return new TestCase(
            "Materialized visual review samples its local timeline from zero",
            MaterializedVisualReviewUsesLocalTimeline);
        yield return new TestCase(
            "Visual review shortlist stays bounded and deterministic",
            VisualReviewShortlistIsBounded);
        yield return new TestCase(
            "Thorough review reserves one selected gameplay event",
            ThoroughShortlistReservesSelectedGameplayEvent);
        yield return new TestCase(
            "Thorough dialogue review does not erase an unreviewed gameplay event",
            ThoroughDialogueReviewKeepsUnreviewedGameplayEvent);
        yield return new TestCase(
            "Qualified visual observations rerank through transparent components",
            VisualObservationsRerankTransparently);
        yield return new TestCase(
            "Unavailable Thorough visual review retains deterministic candidates",
            VisualReviewFailureKeepsDeterministicCandidates);
        yield return new TestCase(
            "Qualified visual model verification never blocks the UI caller",
            QualifiedVisualModelVerificationLeavesCallerFree);
    }

    private static async Task QualifiedVisualModelVerificationLeavesCallerFree()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int callerThreadId = Environment.CurrentManagedThreadId;
        int verificationThreadId = callerThreadId;
        Task verification =
            Qwen3VlQualifiedEditorialProvider
                .RunModelIntegrityVerificationAsync(
                    cancellationToken =>
                    {
                        verificationThreadId =
                            Environment.CurrentManagedThreadId;
                        entered.Set();
                        release.Wait(cancellationToken);
                    },
                    CancellationToken.None);

        try
        {
            TestAssert.True(
                entered.Wait(TimeSpan.FromSeconds(5)),
                "The background model-integrity verification should start promptly.");
            TestAssert.True(
                verificationThreadId != callerThreadId,
                "Multi-gigabyte model hashing must not run on the UI caller thread.");
            TestAssert.True(
                !verification.IsCompleted,
                "The caller must remain free while model hashing is still in progress.");
        }
        finally
        {
            release.Set();
        }

        await verification;
    }

    private static async Task FastSkipsBeforeExtraction()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Fast,
            [("fast.mkv", 1)]);
        var extractor = new FakeAudioExtractor();
        var service = CreateService(extractor, new FakeSpeechProvider());

        await TestAssert.ThrowsAsync<ArgumentException>(
            () => service.AnalyzeAsync(
                request,
                new RecordingProgress<GenerationSpeechActivityProgress>(),
                CancellationToken.None),
            "Fast analysis must bypass VAD entirely.");
        TestAssert.Equal(0, extractor.Requests.Count, "Fast must perform no extraction.");
    }

    private static async Task EveryStreamInPreparationOrder()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("first.mkv", 2), ("second.mkv", 1)]);
        var extractor = new FakeAudioExtractor();
        var provider = new FakeSpeechProvider();
        GenerationSpeechActivityResult result = await CreateService(extractor, provider)
            .AnalyzeAsync(
                request,
                new RecordingProgress<GenerationSpeechActivityProgress>(),
                CancellationToken.None);

        TestAssert.Equal(3, extractor.Requests.Count, "Each audio stream should be extracted once.");
        TestAssert.Equal(3, provider.Requests.Count, "Each audio stream should be analyzed once.");
        TestAssert.Equal(1, provider.Requests[0].AbsoluteAudioStreamIndex, "The first absolute stream is one.");
        TestAssert.Equal(2, provider.Requests[1].AbsoluteAudioStreamIndex, "The second absolute stream is two.");
        TestAssert.Equal(1, provider.Requests[2].AbsoluteAudioStreamIndex, "The next source restarts its inspected indices.");
        TestAssert.Same(request.AnalyzedSources[1], result.Sources[1].Source, "Preparation order and identity must survive.");
        TestAssert.True(extractor.Requests.All(static request => request.Start == TimeSpan.Zero), "Full-source analysis starts at zero.");
        TestAssert.True(extractor.Requests.All(request => request.End == request.SourceDuration), "Full-source analysis covers each source once per stream.");
    }

    private static async Task ExplicitRoleOnly()
    {
        string fileName = "roles.mkv";
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [(fileName, 2)],
            captionSelection: (fileName, 2, CaptionAudioContentRole.CreatorCommentary));
        var provider = new FakeSpeechProvider();

        await CreateService(new FakeAudioExtractor(), provider).AnalyzeAsync(
            request,
            new RecordingProgress<GenerationSpeechActivityProgress>(),
            CancellationToken.None);

        TestAssert.Equal(AudioContentRole.Unknown, provider.Requests[0].Role.Role, "An unselected stream remains unknown even with a title.");
        TestAssert.Equal(AudioContentRole.CreatorSpeech, provider.Requests[1].Role.Role, "The selected stream uses the user's role.");
        TestAssert.Equal(AudioContentRoleSource.UserConfirmed, provider.Requests[1].Role.Source, "Known role provenance must be user-confirmed.");
    }

    private static async Task TypedStreamProgress()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("progress.mkv", 1)]);
        var progress = new RecordingProgress<GenerationSpeechActivityProgress>();

        await CreateService(new FakeAudioExtractor(), new FakeSpeechProvider())
            .AnalyzeAsync(request, progress, CancellationToken.None);

        TestAssert.True(progress.Values.Any(update =>
            update.Phase == GenerationSpeechActivityPhase.DetectingSpeech &&
            update.AbsoluteAudioStreamIndex == 1 &&
            update.IsIndeterminate), "The active pass should be typed and indeterminate.");
        TestAssert.Equal(GenerationSpeechActivityPhase.BatchComplete, progress.Values[^1].Phase, "The final boundary should be real and typed.");
        TestAssert.Equal(100d, progress.Values[^1].OverallPercentage, "The final boundary should be complete.");
    }

    private static async Task CancellationCleansAndStops()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("cancel.mkv", 2)]);
        var extractor = new FakeAudioExtractor();
        var provider = new FakeSpeechProvider
        {
            Handler = (_, token) => Task.FromCanceled<SpeechActivityResult>(
                token.IsCancellationRequested ? token : new CancellationToken(canceled: true)),
        };

        await TestAssert.ThrowsAsync<OperationCanceledException>(
            () => CreateService(extractor, provider).AnalyzeAsync(
                request,
                new RecordingProgress<GenerationSpeechActivityProgress>(),
                CancellationToken.None),
            "Cancellation should propagate.");
        TestAssert.Equal(1, extractor.Requests.Count, "Cancellation must stop before later streams.");
        TestAssert.Equal(1, extractor.CleanupCount, "The active extraction should still be cleaned.");
    }

    private static async Task FailurePreservesContext()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Thorough,
            [("failure.mkv", 1)]);
        var provider = new FakeSpeechProvider
        {
            Handler = (_, _) => throw new SpeechActivityProviderException(
                "provider failed",
                "real provider diagnostic"),
        };

        GenerationSpeechActivityException exception =
            await TestAssert.ThrowsAsync<GenerationSpeechActivityException>(
                () => CreateService(new FakeAudioExtractor(), provider).AnalyzeAsync(
                    request,
                    new RecordingProgress<GenerationSpeechActivityProgress>(),
                    CancellationToken.None),
                "Provider failures need source and stream context.");
        TestAssert.Equal(1, exception.AbsoluteAudioStreamIndex, "The absolute stream should survive translation.");
        TestAssert.Equal("real provider diagnostic", exception.DiagnosticDetails, "Diagnostics should survive translation.");
        TestAssert.True(exception.InnerException is SpeechActivityProviderException, "The provider exception should remain the cause.");
    }

    private static async Task ResultCollectionsAreImmutable()
    {
        GenerationSpeechActivityResult result = await CreateService(
                new FakeAudioExtractor(),
                new FakeSpeechProvider())
            .AnalyzeAsync(
                CreateRequest(GenerationAnalysisDepth.Balanced, [("immutable.mkv", 1)]),
                new RecordingProgress<GenerationSpeechActivityProgress>(),
                CancellationToken.None);

        TestAssert.Throws<NotSupportedException>(
            () => ((IList<GenerationSourceSpeechActivity>)result.Sources).Clear(),
            "Batch sources must be immutable.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<GenerationSpeechStreamResult>)result.Sources[0].Streams).Clear(),
            "Source streams must be immutable.");
    }

    private static async Task LongSourcesUseBoundedChunks()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("long.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(21));
        var extractor = new FakeAudioExtractor();
        GenerationSpeechActivityResult result = await CreateService(
                extractor,
                new FakeSpeechProvider())
            .AnalyzeAsync(
                request,
                new RecordingProgress<GenerationSpeechActivityProgress>(),
                CancellationToken.None);

        TestAssert.Equal(3, extractor.Requests.Count, "Twenty-one minutes should use three bounded WAVs.");
        TestAssert.True(extractor.Requests.All(static item => item.Duration <= TimeSpan.FromMinutes(10)), "No extracted WAV may exceed the configured bound.");
        TestAssert.Equal(3, result.Sources[0].Streams[0].ExecutionManifests.Count, "Every chunk execution keeps provenance.");
    }

    private static Task RefinementReranksTransparently()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("rerank.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70,
            contentEmphasis: ContentEmphasis.CommentaryFocused);
        GenerationMomentFindingResult moments = CreateMoments(request, [80, 78]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(90));

        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(moments, speech);

        TestAssert.Equal("candidate-1-1", result.RefinedMoments.SelectedCandidates[0].Candidate.Id, "Speech timing may rerank through policy, not provider rank output.");
        GenerationCandidateRefinement refinement = result.RefinedMoments.SelectedCandidates[0].Refinement!;
        TestAssert.Equal(78d, refinement.BaseScore, "The v1.3 score remains visible.");
        TestAssert.Equal(refinement.BaseScore + refinement.Components.Sum(static item => item.SignedContribution), refinement.UnclampedScore, "Every contribution must reconcile exactly.");
        TestAssert.Equal(refinement.Candidate.Score.RawComponentTotal + refinement.Components.Sum(static item => item.SignedContribution), refinement.RankingScore, "Ranking must retain the complete deterministic evidence total.");
        TestAssert.Equal(refinement.FinalScore, result.RefinedMoments.SelectedCandidates[0].FinalScore, "The selected score is the transparent final score.");
        return Task.CompletedTask;
    }

    private static Task BalancedUnknownSpeechStaysTimingOnly()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("balanced-unknown-speech.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(request, [80, 78]);
        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(
                moments,
                CreateSpeech(
                    request,
                    AudioContentRoleAssignment.Unknown,
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(90)));

        TestAssert.Equal(
            "candidate-1-0",
            result.RefinedMoments.SelectedCandidates.Single().Candidate.Id,
            "Unclassified speech must not displace stronger gameplay evidence in Balanced mode.");
        GenerationCandidateRefinement spoken = result.Refinements.Single(
            static value => value.Candidate.Id == "candidate-1-1");
        GenerationCandidateRefinementComponent speechCoverage = spoken.Components
            .Single(static value =>
                value.Code == GenerationCandidateRefinementComponentCode.SpeechCoverage);
        GenerationCandidateRefinementComponent unknownSpeech = spoken.Components
            .Single(static value =>
                value.Code == GenerationCandidateRefinementComponentCode.UnknownSpeechActivity);
        TestAssert.True(
            speechCoverage.RawValue > 0 &&
            speechCoverage.SignedContribution == 0 &&
            unknownSpeech.RawValue > 0 &&
            unknownSpeech.SignedContribution == 0,
            "Balanced VAD must retain speech timing for safe cuts without adding a dialogue selection bonus.");
        return Task.CompletedTask;
    }

    private static Task RefinementRejectsIncompleteSpeechEnding()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("complete-ending.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(
            request,
            [92, 78]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            AudioContentRoleAssignment.Unknown,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(45));

        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(
                moments,
                speech);

        GenerationCandidateRefinement incomplete = result.Refinements.Single(
            static refinement =>
                refinement.Candidate.Id == "candidate-1-0");
        TestAssert.True(
            incomplete.HasIncompleteSpeechEnding,
            "A VAD interval crossing the candidate end must be a typed hard boundary failure.");
        TestAssert.Equal(
            "candidate-1-1",
            result.RefinedMoments.SelectedCandidates.Single().Candidate.Id,
            "Automatic count filling must choose a lower-ranked complete cut instead of truncating active speech.");
        return Task.CompletedTask;
    }

    private static Task RefinementExtendsCreatorSpeechEnding()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("creator-natural-end.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(
            request,
            [92, 78]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(45));

        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(
                moments,
                speech);
        GenerationMomentCandidate selected =
            result.RefinedMoments.SelectedCandidates.Single();

        TestAssert.Equal(
            "candidate-1-0",
            selected.Candidate.Id,
            "A safe natural-ending repair must keep the stronger automatic candidate.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(45.75),
            selected.Candidate.Window.End,
            "The adjusted cut must include the complete creator-speech interval and a bounded 750 ms tail.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(10),
            selected.Candidate.Window.Start,
            "A repair below the duration limit must preserve all existing lead-in.");
        TestAssert.False(
            selected.Refinement!.HasIncompleteSpeechEnding,
            "A successfully repaired cut must clear the automatic hard-boundary rejection.");
        TestAssert.True(
            selected.Refinement.Components.Single(component =>
                    component.Code ==
                        GenerationCandidateRefinementComponentCode
                            .IncompleteSpeechEnding)
                .Explanation.Contains(
                    "short natural tail",
                    StringComparison.Ordinal),
            "The refinement must disclose that the automatic window was repaired.");
        TestAssert.Same(
            selected.Candidate,
            result.BaseMoments.Sources[0].Moments.Proposals.Single(candidate =>
                candidate.Id == selected.Candidate.Id),
            "Downstream captions and metadata must receive the repaired proposal identity, not the stale window.");
        return Task.CompletedTask;
    }

    private static Task RefinementBridgesImmediateCreatorPause()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("creator-pause.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(
            request,
            [92]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            [
                (TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(39.4)),
                (TimeSpan.FromSeconds(40.6), TimeSpan.FromSeconds(44)),
            ]);

        GenerationMomentCandidate selected =
            new GenerationCandidateRefinementService()
                .Refine(moments, speech)
                .RefinedMoments.SelectedCandidates.Single();

        TestAssert.Equal(
            TimeSpan.FromSeconds(44.75),
            selected.Candidate.Window.End,
            "A creator phrase resuming within one second of the proposed end must be kept with the natural tail.");
        return Task.CompletedTask;
    }

    private static Task NaturalEndingReclaimsSafePreRoll()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("reclaim-pre-roll.mkv", 1)],
            desiredCount: 1);
        MomentCandidate source = CreateMoments(request, [92])
            .Sources[0].Moments.Proposals.Single();
        var candidate = new MomentCandidate(
            source.Id,
            new MomentCandidateWindow(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(40),
                source.Window.SourceDuration),
            source.ConstructionReason,
            source.EventNeighborhood,
            source.Anchors,
            source.Score,
            source.Disposition,
            source.FullFrameBlackOverlapRatio,
            source.FullFrameFreezeOverlapRatio,
            source.GameplayBlackOverlapRatio,
            source.GameplayFreezeOverlapRatio,
            source.IntegrityEvidenceReferences,
            source.Episode,
            source.ContextAllocation,
            source.MontageObjective,
            source.MontageSelectionReason,
            source.EpisodeFeatures,
            source.StandaloneFeatures,
            source.MontageFeatures);
        var speech = new SpeechActivityInterval(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(65),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(65),
            0.95,
            0.8);

        GenerationCandidateNaturalEndingAdjustment adjustment =
            GenerationCandidateNaturalEndingPolicy.Adjust(
                candidate,
                [speech],
                TimeSpan.FromSeconds(60));

        TestAssert.True(
            adjustment.WasAdjusted &&
            !adjustment.RequiresAutomaticRejection,
            "The policy should reclaim expendable lead-in when the event remains intact.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(5.75),
            adjustment.Candidate.Window.Start,
            "Only the lead-in needed to fit the natural tail should be reclaimed.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(65.75),
            adjustment.Candidate.Window.End,
            "The complete speech interval and 750 ms tail must fit inside the maximum duration.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(60),
            adjustment.Candidate.Window.Duration,
            "Natural-ending repair must never exceed the configured maximum duration.");
        TestAssert.True(
            adjustment.Candidate.Window.Contains(
                source.Anchors.Single().Timestamp),
            "Reclaiming pre-roll must retain the event anchor.");
        return Task.CompletedTask;
    }

    private static Task UnsafeCreatorContinuationChoosesAlternate()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("unsafe-natural-end.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(
            request,
            [92, 78]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(75));

        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(
                moments,
                speech);
        GenerationCandidateRefinement unsafeRefinement = result.Refinements
            .Single(static refinement =>
                refinement.Candidate.Id == "candidate-1-0");

        TestAssert.True(
            unsafeRefinement.HasIncompleteSpeechEnding,
            "A repair that would discard the event anchor must remain a hard automatic rejection.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(40),
            unsafeRefinement.Candidate.Window.End,
            "An unsafe repair must not publish a partially shifted window.");
        TestAssert.Equal(
            "candidate-1-1",
            result.RefinedMoments.SelectedCandidates.Single().Candidate.Id,
            "Portfolio selection must choose the complete alternate when the stronger cut cannot be repaired safely.");
        return Task.CompletedTask;
    }

    private static Task NaturalEndingPreservesGuidanceAndDiversity()
    {
        TimeSpan sourceDuration = TimeSpan.FromMinutes(2);
        string guidedName = "guided-natural-end.mkv";
        string guidedPath = TestMediaFactory.CreateSourcePath(guidedName);
        var guidance = new GenerationMomentGuidance(
            [UserMomentGuidance.CreateRange(
                guidedPath,
                sourceDuration,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(45))]);
        GenerationRequest guidedRequest = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [(guidedName, 1)],
            sourceDuration: sourceDuration,
            desiredCount: 1,
            momentGuidance: guidance);
        GenerationMomentFindingResult guidedMoments = CreateMoments(
            guidedRequest,
            [92]);
        GenerationSpeechActivityResult guidedSpeech = CreateSpeech(
            guidedRequest,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(45));

        GenerationMomentCandidate guided =
            new GenerationCandidateRefinementService()
                .Refine(guidedMoments, guidedSpeech)
                .RefinedMoments.SelectedCandidates.Single();
        TestAssert.Equal(
            TimeSpan.FromSeconds(40),
            guided.Candidate.Window.End,
            "An explicit user range must keep its exact boundary even when creator speech crosses it.");
        TestAssert.Equal(
            GenerationCandidateSelectionReason.UserReservedRange,
            guided.SelectionReason,
            "The preserved range must retain its human-priority selection provenance.");

        GenerationRequest diversityRequest = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("natural-end-diversity.mkv", 1)],
            desiredCount: 2,
            qualityThreshold: 70);
        GenerationMomentFindingResult diversityMoments = CreateMoments(
            diversityRequest,
            [92, 91],
            windowSpacingSeconds: 25);
        GenerationSpeechActivityResult diversitySpeech = CreateSpeech(
            diversityRequest,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            TimeSpan.FromSeconds(35),
            TimeSpan.FromSeconds(50));
        GenerationMomentFindingResult diverse =
            new GenerationCandidateRefinementService()
                .Refine(diversityMoments, diversitySpeech)
                .RefinedMoments;

        TestAssert.Equal(
            TimeSpan.FromSeconds(50.75),
            diverse.SelectedCandidates[0].Candidate.Window.End,
            "The first candidate must carry its repaired boundary into portfolio selection.");
        TestAssert.True(
            diverse.SelectedCandidates[1].RequiredDiversityRelaxation,
            "Overlap and diversity must be recalculated from repaired windows rather than stale proposal ranges.");
        return Task.CompletedTask;
    }

    private static Task RefinementRanksByUnclampedEvidence()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("saturated-score-order.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(
            request,
            [99, 98]);
        moments = ReplaceMomentScores(
            moments,
            [
                [
                    ScoreComponent(MomentScoreComponentCode.AudioNovelty, 1, 100),
                    ScoreComponent(MomentScoreComponentCode.DurationFit, 0.30, 100),
                ],
                [
                    ScoreComponent(MomentScoreComponentCode.AudioNovelty, 1, 100),
                    ScoreComponent(MomentScoreComponentCode.DurationFit, 0.20, 100),
                ],
            ]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            AudioContentRoleAssignment.Unknown,
            [
                (TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)),
                (TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90)),
            ]);

        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(
                moments,
                speech);

        TestAssert.True(
            result.Refinements.All(static refinement =>
                refinement.FinalScore == 100),
            "Both candidates should reach the bounded score shown in the interface.");
        TestAssert.Equal(
            "candidate-1-0",
            result.RefinedMoments.SelectedCandidates.Single().Candidate.Id,
            "Selection must preserve stronger raw evidence after displayed scores reach 100 instead of letting speech break the tie.");
        return Task.CompletedTask;
    }

    private static Task BalancedPortfolioRetainsGameplayEvent()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("gameplay-coverage.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(5),
            desiredCount: 3,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = ReplaceMomentScores(
            CreateMoments(request, [100, 99, 98, 95]),
            [
                [ScoreComponent(MomentScoreComponentCode.AudioNovelty, 1, 100)],
                [ScoreComponent(MomentScoreComponentCode.PresenterGatedSupport, 0.99, 100)],
                [ScoreComponent(MomentScoreComponentCode.AudioNovelty, 0.98, 100)],
                [
                    ScoreComponent(MomentScoreComponentCode.GameplayProminence, 1, 30),
                    ScoreComponent(MomentScoreComponentCode.GameplayBurstIntegration, 1, 25),
                    ScoreComponent(MomentScoreComponentCode.GameplayOnset, 1, 20),
                    ScoreComponent(MomentScoreComponentCode.GameplaySceneChange, 1, 10),
                    ScoreComponent(MomentScoreComponentCode.GameplaySceneDensity, 1, 5),
                    ScoreComponent(MomentScoreComponentCode.IndependentFamilyAgreement, 1, 5),
                ],
            ]);
        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(
                moments,
                CreateSpeech(
                    request,
                    AudioContentRoleAssignment.Unknown,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1)));

        GenerationMomentCandidate coverage = result.RefinedMoments.SelectedCandidates
            .Single(static value =>
                value.SelectionReason ==
                    GenerationCandidateSelectionReason
                        .QualityQualifiedGameplayEventCoverage);
        TestAssert.Equal(
            "candidate-1-3",
            coverage.Candidate.Id,
            "One quality-qualified near-peer gameplay event must survive an otherwise non-gameplay top set.");
        TestAssert.Equal(
            3,
            result.RefinedMoments.SelectedCandidates.Count,
            "Gameplay coverage must replace at most one pick without changing the requested count.");
        return Task.CompletedTask;
    }

    private static Task GameplayCoverageStaysBounded()
    {
        IReadOnlyList<MomentScoreComponent> strongGameplay =
        [
            ScoreComponent(MomentScoreComponentCode.GameplayProminence, 1, 30),
            ScoreComponent(MomentScoreComponentCode.GameplayBurstIntegration, 1, 25),
            ScoreComponent(MomentScoreComponentCode.GameplayOnset, 1, 20),
            ScoreComponent(MomentScoreComponentCode.GameplaySceneChange, 1, 10),
            ScoreComponent(MomentScoreComponentCode.GameplaySceneDensity, 1, 5),
            ScoreComponent(MomentScoreComponentCode.IndependentFamilyAgreement, 1, 5),
        ];
        IReadOnlyList<MomentScoreComponent> distantGameplay =
        [
            ScoreComponent(MomentScoreComponentCode.GameplayProminence, 1, 30),
            ScoreComponent(MomentScoreComponentCode.GameplayBurstIntegration, 1, 25),
            ScoreComponent(MomentScoreComponentCode.GameplayOnset, 1, 20),
            ScoreComponent(MomentScoreComponentCode.GameplaySceneChange, 1, 10),
            ScoreComponent(MomentScoreComponentCode.GameplaySceneDensity, 1, 5),
        ];

        GenerationCandidateIntelligenceResult distant = Refine(
            ContentEmphasis.Balanced,
            desiredCount: 3,
            qualityThreshold: 70,
            distantGameplay);
        TestAssert.False(
            HasCoverageSelection(distant),
            "Gameplay coverage must not trade away more than four ranking points.");

        GenerationCandidateIntelligenceResult commentary = Refine(
            ContentEmphasis.CommentaryFocused,
            desiredCount: 3,
            qualityThreshold: 70,
            strongGameplay);
        TestAssert.False(
            HasCoverageSelection(commentary),
            "Commentary focus must remain user-directed without an action coverage swap.");

        GenerationCandidateIntelligenceResult shortSet = Refine(
            ContentEmphasis.Balanced,
            desiredCount: 2,
            qualityThreshold: 70,
            strongGameplay);
        TestAssert.False(
            HasCoverageSelection(shortSet),
            "A set below three clips must not impose gameplay coverage.");
        return Task.CompletedTask;

        static bool HasCoverageSelection(
            GenerationCandidateIntelligenceResult result) =>
            result.RefinedMoments.SelectedCandidates.Any(static value =>
                value.SelectionReason ==
                    GenerationCandidateSelectionReason
                        .QualityQualifiedGameplayEventCoverage);

        static GenerationCandidateIntelligenceResult Refine(
            ContentEmphasis emphasis,
            int desiredCount,
            double qualityThreshold,
            IReadOnlyList<MomentScoreComponent> gameplayScore)
        {
            GenerationRequest request = CreateRequest(
                GenerationAnalysisDepth.Balanced,
                [($"bounded-{emphasis}-{desiredCount}.mkv", 1)],
                sourceDuration: TimeSpan.FromMinutes(5),
                desiredCount: desiredCount,
                qualityThreshold: qualityThreshold,
                contentEmphasis: emphasis);
            GenerationMomentFindingResult moments = ReplaceMomentScores(
                CreateMoments(request, [100, 99, 98, 95]),
                [
                    [ScoreComponent(MomentScoreComponentCode.AudioNovelty, 1, 100)],
                    [ScoreComponent(MomentScoreComponentCode.AudioNovelty, 0.99, 100)],
                    [ScoreComponent(MomentScoreComponentCode.AudioNovelty, 0.98, 100)],
                    gameplayScore,
                ]);
            return new GenerationCandidateRefinementService().Refine(
                moments,
                CreateSpeech(
                    request,
                    AudioContentRoleAssignment.Unknown,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1)));
        }
    }

    private static Task RefinementUsesContentFocus()
    {
        GenerationRequest commentaryRequest = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("commentary.mkv", 1)],
            captionSelection: ("commentary.mkv", 1, CaptionAudioContentRole.CreatorCommentary),
            desiredCount: 1,
            contentEmphasis: ContentEmphasis.CommentaryFocused);
        GenerationMomentFindingResult commentaryMoments = CreateMoments(commentaryRequest, [80, 78]);
        GenerationCandidateIntelligenceResult commentary =
            new GenerationCandidateRefinementService().Refine(
                commentaryMoments,
                CreateSpeech(
                    commentaryRequest,
                    new AudioContentRoleAssignment(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(90)));

        GenerationRequest gameplayRequest = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("gameplay.mkv", 1)],
            captionSelection: ("gameplay.mkv", 1, CaptionAudioContentRole.CreatorCommentary),
            desiredCount: 1,
            contentEmphasis: ContentEmphasis.GameplayFocused);
        GenerationMomentFindingResult gameplayMoments = CreateMoments(gameplayRequest, [80, 78]);
        GenerationCandidateIntelligenceResult gameplay =
            new GenerationCandidateRefinementService().Refine(
                gameplayMoments,
                CreateSpeech(
                    gameplayRequest,
                    new AudioContentRoleAssignment(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(90)));

        TestAssert.Equal("candidate-1-1", commentary.RefinedMoments.SelectedCandidates[0].Candidate.Id, "Presenter Commentary should favor confirmed creator speech.");
        TestAssert.Equal("candidate-1-0", gameplay.RefinedMoments.SelectedCandidates[0].Candidate.Id, "Gameplay & Story should not let creator speech displace stronger gameplay evidence.");
        return Task.CompletedTask;
    }

    private static Task RefinementUsesPreferenceProfile()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Balanced,
            [("preference.mkv", 1)],
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(
            request,
            [80, 78]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            AudioContentRoleAssignment.Unknown,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(90));
        GenerationCandidateIntelligenceResult baseline =
            new GenerationCandidateRefinementService().Refine(
                moments,
                speech);
        GenerationCandidateRefinement target = baseline.Refinements[0];
        ClipPreferenceFeatureVector vector =
            GenerationClipPreferenceFeatureExtractor.Create(
                target.Candidate,
                target);
        var profile = new ClipPreferenceProfile(
            4,
            0,
            4,
            vector.Features.Select(feature =>
                new ClipPreferenceFeatureStatistics(
                    feature.Code,
                    4,
                    feature.NormalizedValue * 4,
                    4,
                    (1 - feature.NormalizedValue) * 4)));
        GenerationCandidateIntelligenceResult personalized =
            new GenerationCandidateRefinementService(
                preferenceProfiles:
                    new FixedPreferenceProfileProvider(profile))
                .Refine(moments, speech);
        GenerationCandidateRefinement updated = personalized.Refinements
            .Single(value => ReferenceEquals(
                value.Candidate,
                target.Candidate));
        GenerationCandidateRefinementComponent component =
            updated.Components.Single(value =>
                value.Code ==
                    GenerationCandidateRefinementComponentCode
                        .PersonalPreference);

        TestAssert.True(
            component.SignedContribution > 0 &&
            component.SignedContribution <=
                ClipPreferenceProfile.MaximumAbsoluteContribution,
            "Preference support must be positive and bounded.");
        TestAssert.Equal(
            updated.BaseScore + updated.Components.Sum(
                static item => item.SignedContribution),
            updated.UnclampedScore,
            "Personalization remains exactly reconcilable.");
        return Task.CompletedTask;
    }

    private static Task VisualReviewCommandIsBounded()
    {
        MediaProbeResult media = TestMediaFactory.Create(
            TestMediaFactory.CreateSourcePath("visual review with spaces.mkv"),
            duration: TimeSpan.FromMinutes(2),
            hasAudio: true,
            audioStreamCount: 2);
        var request = new VisualSemanticReviewVideoMaterializationRequest(
            "candidate-review-command",
            media,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40));
        string output = Path.Combine(
            Path.GetTempPath(),
            "Replay Foundry review output.mp4");

        FfmpegVisualSemanticReviewVideoCommand command =
            FfmpegVisualSemanticReviewVideoCommandBuilder.Build(request, output);

        TestAssert.Equal(
            "10",
            ArgumentAfter(command.Arguments, "-ss"),
            "The source seek must preserve the absolute candidate interval.");
        TestAssert.Equal(
            "30",
            ArgumentAfter(command.Arguments, "-t"),
            "Only the requested bounded duration may be encoded.");
        TestAssert.Equal(
            $"0:{media.PrimaryVideoStream.Index}",
            ArgumentAfter(command.Arguments, "-map"),
            "The exact inspected primary video stream must be mapped.");
        TestAssert.True(
            command.Arguments.Contains("-an", StringComparer.Ordinal) &&
            command.Arguments.Contains("-sn", StringComparer.Ordinal) &&
            command.Arguments.Contains("-dn", StringComparer.Ordinal),
            "Qwen review material is video-only.");
        TestAssert.Equal(
            "h264_mf",
            ArgumentAfter(command.Arguments, "-c:v"),
            "The bounded review must use Windows Media Foundation software H.264 encoding.");
        TestAssert.Equal(
            "0",
            ArgumentAfter(command.Arguments, "-hw_encoding"),
            "The bounded review must not consume a hardware encoder session.");
        TestAssert.Equal(
            "77",
            ArgumentAfter(command.Arguments, "-profile:v"),
            "The bounded review must request the numeric H.264 main profile accepted by Media Foundation.");
        TestAssert.Equal(
            "2000000",
            ArgumentAfter(command.Arguments, "-b:v"),
            "The compact review encode must have a deterministic bounded bitrate.");
        TestAssert.False(
            command.Arguments.Contains("libx264", StringComparer.Ordinal) ||
            command.Arguments.Contains("libopenh264", StringComparer.Ordinal) ||
            command.Arguments.Contains("-crf", StringComparer.Ordinal) ||
            command.Arguments.Contains("-preset", StringComparer.Ordinal),
            "The review command cannot require optional H.264 libraries or their private encoder controls.");
        TestAssert.False(
            ArgumentAfter(command.Arguments, "-vf").Contains(
                "tpad",
                StringComparison.Ordinal),
            "The materialized file must not invent frames outside the declared review interval.");
        TestAssert.Equal(
            media.FullPath,
            command.Arguments[command.Arguments.ToList().IndexOf("-i") + 1],
            "A source path with spaces remains one ArgumentList value.");
        TestAssert.Equal(output, command.Arguments[^1], "Output path.");
        return Task.CompletedTask;
    }

    private static Task VisualReviewCommandCropsGameplay()
    {
        MediaProbeResult media = TestMediaFactory.Create(
            TestMediaFactory.CreateSourcePath("visual crop.mkv"),
            duration: TimeSpan.FromMinutes(2),
            hasAudio: true);
        var gameplay = new NormalizedRectangle(
            0.075,
            0.125,
            0.85,
            0.425);
        var request = new VisualSemanticReviewVideoMaterializationRequest(
            "candidate-gameplay-crop",
            media,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            gameplay);

        FfmpegVisualSemanticReviewVideoCommand command =
            FfmpegVisualSemanticReviewVideoCommandBuilder.Build(
                request,
                Path.Combine(Path.GetTempPath(), "gameplay crop.mp4"));
        string filter = ArgumentAfter(command.Arguments, "-vf");

        TestAssert.True(
            filter.Contains("scale=1920:1080:flags=lanczos,setsar=1", StringComparison.Ordinal),
            "Effective-display normalization precedes the crop.");
        TestAssert.True(
            filter.Contains("crop=1632:460:144:134", StringComparison.Ordinal),
            "The confirmed normalized Gameplay rectangle uses deterministic even crop geometry.");
        TestAssert.Same(
            gameplay,
            request.ContentRegion!,
            "Immutable crop identity.");
        return Task.CompletedTask;
    }

    private static async Task MaterializedVisualReviewUsesLocalTimeline()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Thorough,
            [("visual-local-timeline.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(2),
            desiredCount: 1);
        var gameplay = new NormalizedRectangle(
            74d / 1080,
            210d / 1920,
            938d / 1080,
            870d / 1920);
        var compositionRequest = new GenerationCompositionReviewRequest(
            request.Preparation);
        var gameplayRegion = new CompositionRegion(
            "confirmed-gameplay",
            gameplay,
            CompositionRegionRole.Gameplay,
            CompositionRegionTraits.Dynamic,
            CompositionConfidence.Certain,
            CompositionConfidence.Certain,
            CompositionValueSource.UserConfirmed,
            CompositionValueSource.UserConfirmed);
        var composition = new GenerationCompositionReviewResult(
            compositionRequest,
            [new PreparedSourceCompositionPlan(
                request.ReferencePreparedSource,
                ManualCompositionPlanFactory.CreateUserConfirmedSingleInterval(
                    request.ReferenceSource.FullPath,
                    request.ReferencePreparedSource.Media.Duration,
                    [gameplayRegion],
                    new DateTimeOffset(
                        2026,
                        8,
                        11,
                        12,
                        0,
                        0,
                        TimeSpan.Zero)))]);
        request = PreparedGenerationWorkflowTests.CreateGenerationRequest(
            request.Preparation,
            request.SetupOptions,
            composition);
        GenerationCandidateIntelligenceResult intelligence =
            CreateCandidateIntelligence(request, [80]);
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = new FakeVisualEditorialProvider();
        var progress = new RecordingProgress<GenerationVisualSemanticProgress>();

        using GenerationVisualSemanticAnalysisResult result =
            await new GenerationVisualSemanticAnalysisService(
                provider,
                materializer,
                CreateVisualSettings())
                .AnalyzeAsync(intelligence, progress, CancellationToken.None);

        VisualSemanticRequest observed = provider.Requests.Single().Requests.Single();
        TestAssert.Equal(
            TimeSpan.Zero,
            observed.SourceAbsoluteOffset,
            "The provider reads a newly trimmed local artifact whose timeline begins at zero.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(10),
            result.Observations.Single().ReviewedSourceStart,
            "The application result separately retains the original absolute source offset.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(40),
            result.Observations.Single().ReviewedSourceEnd,
            "The application result separately retains the original absolute source end.");
        TestAssert.Same(
            gameplay,
            materializer.Requests.Single().ContentRegion!,
            "The focused review artifact must crop to the exact confirmed Gameplay region used by composition metadata.");
        TestAssert.Equal(
            0,
            materializer.CleanupCount,
            "Review media must remain leased for the following grounded metadata stage.");

        GenerationCandidateIntelligenceResult refined =
            new GenerationCandidateRefinementService().ApplyVisualSemantic(
                intelligence,
                result);
        var metadataGenerator =
            new RecordingEditorialMetadataGenerationService();
        await new GenerationEditorialMetadataService(
                metadataGenerator,
                new ClipEditorialProfileSession())
            .GenerateAsync(
                refined.RefinedMoments,
                captions: null,
                cancellationToken: CancellationToken.None,
                candidateIntelligence: refined);
        ClipEditorialMetadataRequest metadataRequest =
            metadataGenerator.Requests.Single();
        TestAssert.True(
            metadataRequest.ReviewVideo is not null,
            "An exact complete selected-cut review must be reused for adaptive grounded metadata sampling.");
        TestAssert.Equal(
            refined.RefinedMoments.SelectedCandidates[0].Candidate.Window.Duration,
            metadataRequest.ReviewVideo!.ReviewVideoDuration,
            "The reused review must cover the complete selected cut rather than a focused slice.");
        TestAssert.Equal(
            ClipEditorialGenerationPreference.AiRequired,
            metadataRequest.Preference,
            "Thorough generation must never silently replace exhausted Qwen metadata with heuristics.");
        result.Dispose();
        TestAssert.Equal(
            1,
            materializer.CleanupCount,
            "Disposing the analysis result must clean its leased review media.");
        TestAssert.Equal(
            GenerationVisualSemanticPhase.Completed,
            progress.Values[^1].Phase,
            "The final progress boundary must be typed.");
    }

    private static Task VisualReviewShortlistIsBounded()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Thorough,
            [("visual-shortlist.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(10),
            desiredCount: 5,
            qualityThreshold: 1);
        GenerationMomentFindingResult moments = ReplaceMomentScores(
            CreateMoments(request, [99, 98, 97, 96, 95, 94, 93, 92, 91]),
            new[] { 110d, 109d, 108d, 107d, 106d, 105d, 104d, 103d, 120d }
                .Select(total => (IReadOnlyList<MomentScoreComponent>)
                [
                    ScoreComponent(MomentScoreComponentCode.AudioNovelty, 1, 100),
                    ScoreComponent(
                        MomentScoreComponentCode.DurationFit,
                        (total - 100) / 100,
                        100),
                ])
                .ToArray());
        GenerationCandidateIntelligenceResult intelligence =
            new GenerationCandidateRefinementService().Refine(
                moments,
                CreateSpeech(
                    request,
                    AudioContentRoleAssignment.Unknown,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1)));

        IReadOnlyList<GenerationVisualSemanticAnalysisService.CandidateSource>
            shortlist = GenerationVisualSemanticAnalysisService.CreateShortlist(
                intelligence,
                maximumCandidateCount: 8);

        TestAssert.Equal(8, shortlist.Count, "A production Qwen batch remains bounded to eight candidates.");
        TestAssert.True(
            intelligence.Refinements.All(static value => value.FinalScore == 100),
            "The regression fixture must reproduce the displayed-score saturation from the real run.");
        TestAssert.True(
            shortlist.Select(static value => value.Candidate.Id)
                .SequenceEqual(
                    new[]
                    {
                        "candidate-1-8",
                        "candidate-1-0",
                        "candidate-1-1",
                        "candidate-1-2",
                        "candidate-1-3",
                        "candidate-1-4",
                        "candidate-1-5",
                        "candidate-1-6",
                    },
                    StringComparer.Ordinal),
            "The shortlist must preserve raw evidence above 100 instead of reviewing the earliest capped candidates.");
        return Task.CompletedTask;
    }

    private static Task ThoroughShortlistReservesSelectedGameplayEvent()
    {
        GenerationCandidateIntelligenceResult intelligence =
            CreateThoroughGameplayCoverageIntelligence();
        GenerationMomentCandidate selectedGameplay = intelligence
            .RefinedMoments
            .SelectedCandidates
            .Single(static value =>
                value.SelectionReason ==
                    GenerationCandidateSelectionReason
                        .QualityQualifiedGameplayEventCoverage);

        IReadOnlyList<GenerationVisualSemanticAnalysisService.CandidateSource>
            shortlist = GenerationVisualSemanticAnalysisService.CreateShortlist(
                intelligence,
                maximumCandidateCount: 8);

        TestAssert.Equal(
            8,
            shortlist.Count,
            "Thorough picture review must remain bounded to eight candidates.");
        TestAssert.True(
            shortlist.Any(value => ReferenceEquals(
                value.Candidate,
                selectedGameplay.Candidate)),
            "A selected gameplay-event proxy below the ordinary eight-item cutoff must receive the reserved automatic review slot.");
        return Task.CompletedTask;
    }

    private static Task ThoroughDialogueReviewKeepsUnreviewedGameplayEvent()
    {
        GenerationCandidateIntelligenceResult intelligence =
            CreateThoroughGameplayCoverageIntelligence();
        GenerationCandidateRefinement reviewed = intelligence.Refinements
            .Single(static value =>
                value.Candidate.Id == "candidate-1-0");
        AnalyzedGenerationSource source = intelligence.BaseMoments.Sources
            .Single()
            .AnalyzedSource;
        VisualSemanticEditorialCollectionAudit empty = new(0, 0, 0, false);
        var audit = new VisualSemanticEditorialCanonicalizationAudit(
            VisualSemanticEditorialCanonicalizer.PolicyVersion,
            empty,
            empty,
            empty,
            OuterWhitespaceTrimmed: false,
            SyntacticCanonicalizationCount: 0,
            SchemaShapeCanonicalizationCount: 0,
            SemanticRepairCount: 0,
            WireRepresentationVersion:
                "visual-semantic-editorial-wire-1.1");
        var candidateObservation =
            new GenerationVisualSemanticCandidateObservation(
                reviewed.Candidate,
                source,
                reviewed.Candidate.Window.Start,
                reviewed.Candidate.Window.End,
                new string('A', 64),
                CreateVisualObservation(
                    VisualSemanticEditorialDisposition.Keep,
                    VisualSemanticEditorialRejectReason.None,
                    VisualSemanticTernary.Yes,
                    qualificationEvidence:
                        "A bounded conversation is visible.",
                    contentType:
                        VisualSemanticObservableContentType.Dialogue),
                audit,
                TimeSpan.FromMilliseconds(1));
        using var visual = new GenerationVisualSemanticAnalysisResult(
            intelligence,
            new InferenceProviderIdentity(
                "fake-qualified-qwen",
                "2.7",
                "test"),
            [candidateObservation],
            TimeSpan.FromMilliseconds(1),
            peakAllocatedGpuBytes: 1024);

        GenerationCandidateIntelligenceResult refined =
            new GenerationCandidateRefinementService().ApplyVisualSemantic(
                intelligence,
                visual);

        TestAssert.True(
            refined.RefinedMoments.SelectedCandidates.Any(static value =>
                value.Candidate.Id == "candidate-1-9" &&
                value.SelectionReason ==
                    GenerationCandidateSelectionReason
                        .QualityQualifiedGameplayEventCoverage),
            "Dialogue evidence for one reviewed candidate must not erase the deterministic gameplay evidence of a different unreviewed candidate.");
        return Task.CompletedTask;
    }

    private static async Task VisualObservationsRerankTransparently()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Thorough,
            [("visual-rerank.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(2),
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationCandidateIntelligenceResult intelligence =
            CreateCandidateIntelligence(request, [80, 78]);
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = new FakeVisualEditorialProvider
        {
            ObservationFactory = (_, index) =>
                index == 0
                    ? CreateVisualObservation(
                        VisualSemanticEditorialDisposition.Reject,
                        VisualSemanticEditorialRejectReason.RoutineTraversal,
                        distinctEvent: VisualSemanticTernary.No,
                        contentType: VisualSemanticObservableContentType.Dialogue)
                    : CreateVisualObservation(
                        VisualSemanticEditorialDisposition.Keep,
                        VisualSemanticEditorialRejectReason.None,
                        distinctEvent: VisualSemanticTernary.Yes,
                        qualificationEvidence:
                            "A visual evidence point supports the observation."),
        };
        using GenerationVisualSemanticAnalysisResult visual =
            await new GenerationVisualSemanticAnalysisService(
                provider,
                materializer,
                CreateVisualSettings())
                .AnalyzeAsync(intelligence, null, CancellationToken.None);

        GenerationCandidateIntelligenceResult refined =
            new GenerationCandidateRefinementService().ApplyVisualSemantic(
                intelligence,
                visual);

        TestAssert.Equal(
            "candidate-1-1",
            refined.RefinedMoments.SelectedCandidates[0].Candidate.Id,
            "A bounded qualified observation may influence the deterministic rank without selecting it directly.");
        GenerationCandidateRefinement selected =
            refined.RefinedMoments.SelectedCandidates[0].Refinement!;
        TestAssert.Equal(
            selected.BaseScore + selected.Components.Sum(static value => value.SignedContribution),
            selected.UnclampedScore,
            "Every VAD and visual contribution must reconcile exactly.");
        TestAssert.True(
            selected.Components.Any(static value =>
                value.Code == GenerationCandidateRefinementComponentCode.VisualSemanticSupport),
            "The selected result retains its typed visual support component.");
        TestAssert.True(
            selected.Components.Any(static value =>
                value.Code ==
                    GenerationCandidateRefinementComponentCode
                        .VisualSemanticActionEvidence &&
                value.RawValue == 1 &&
                value.SignedContribution == 0),
            "A qualified Action observation must be retained as a typed coverage marker without inflating its score.");

        var metadataGenerator =
            new RecordingEditorialMetadataGenerationService();
        await new GenerationEditorialMetadataService(
                metadataGenerator,
                new ClipEditorialProfileSession())
            .GenerateAsync(
                refined.RefinedMoments,
                captions: null,
                cancellationToken: CancellationToken.None,
                candidateIntelligence: refined);
        ClipEditorialContext editorialContext =
            metadataGenerator.Requests.Single().Context;
        ClipEditorialEvidenceReference[] qualificationEvidence =
            editorialContext.Evidence.Where(static item =>
                    item.Id.StartsWith("visual-", StringComparison.Ordinal))
                .ToArray();
        TestAssert.Equal(
            2,
            qualificationEvidence.Length,
            "Both qualified observation records remain available for audit.");
        TestAssert.True(
            qualificationEvidence.All(static item =>
                item.Kind ==
                    ClipEditorialEvidenceKind.CandidateQualification),
            "Candidate-ranking evidence must retain a non-copy authority kind.");
        TestAssert.True(
            editorialContext.EditorialBrief.PrimaryGameplayBeat is null,
            "Candidate-ranking boilerplate must not become the audience gameplay beat.");
    }

    private static async Task VisualReviewFailureKeepsDeterministicCandidates()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Thorough,
            [("visual-fallback.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(2),
            desiredCount: 1,
            qualityThreshold: 70);
        GenerationCandidateIntelligenceResult intelligence =
            CreateCandidateIntelligence(request, [80, 78]);
        string[] selectedBefore = intelligence.RefinedMoments
            .SelectedCandidates.Select(static item => item.Candidate.Id)
            .ToArray();
        double[] scoresBefore = intelligence.Refinements
            .Select(static item => item.FinalScore)
            .ToArray();
        using var materializer = new FakeVisualReviewMaterializer();
        var provider = new FakeVisualEditorialProvider
        {
            Failure = new Qwen3VlInferenceException(
                "The qualified review failed.",
                diagnosticDetails:
                    @"C:\Users\FictionalCreator\private\clip.mp4; transcript='I found it'; OCR='SECRET NOTICE'; title='Leaked title'; Bearer secret-token"),
        };

        using GenerationVisualSemanticAnalysisResult visual =
            await new GenerationVisualSemanticAnalysisService(
                provider,
                materializer,
                CreateVisualSettings())
                .AnalyzeAsync(intelligence, null, CancellationToken.None);

        TestAssert.Equal(
            GenerationVisualSemanticOutcome.RetainedDeterministicCandidates,
            visual.Outcome,
            "A provider or structured-output failure must become an explicit reviewable fallback.");
        TestAssert.True(visual.NeedsReview,
            "A retained deterministic shortlist must be visibly reviewable.");
        TestAssert.Equal(0, visual.Observations.Count,
            "Failed visual observations must not be invented.");
        TestAssert.False(
            visual.DiagnosticDetails!.Contains("FictionalCreator", StringComparison.OrdinalIgnoreCase) ||
            visual.DiagnosticDetails.Contains("secret-token", StringComparison.Ordinal) ||
            visual.DiagnosticDetails.Contains("I found it", StringComparison.Ordinal) ||
            visual.DiagnosticDetails.Contains("SECRET NOTICE", StringComparison.Ordinal) ||
            visual.DiagnosticDetails.Contains("Leaked title", StringComparison.Ordinal),
            "Fallback diagnostics must project only typed failure state, never provider bodies, transcript, OCR, audience copy, paths, or credentials.");
        GenerationCandidateIntelligenceResult retained =
            new GenerationCandidateRefinementService().ApplyVisualSemantic(
                intelligence,
                visual);
        TestAssert.True(selectedBefore.SequenceEqual(
                retained.RefinedMoments.SelectedCandidates.Select(
                    static item => item.Candidate.Id),
                StringComparer.Ordinal),
            "Visual fallback must preserve deterministic selection order.");
        TestAssert.True(scoresBefore.SequenceEqual(
                retained.Refinements.Select(static item => item.FinalScore)),
            "Visual fallback must preserve every deterministic score.");
        TestAssert.Equal(provider.Requests.Single().Requests.Count,
            materializer.CleanupCount,
            "Failed review artifacts must be released before generation continues.");

        using var failingMaterializer = new FakeVisualReviewMaterializer
        {
            Failure = new IOException("The bounded review could not be written."),
        };
        using GenerationVisualSemanticAnalysisResult materializationFallback =
            await new GenerationVisualSemanticAnalysisService(
                new FakeVisualEditorialProvider(),
                failingMaterializer,
                CreateVisualSettings())
                .AnalyzeAsync(intelligence, null, CancellationToken.None);
        TestAssert.Equal(
            GenerationVisualSemanticOutcome.RetainedDeterministicCandidates,
            materializationFallback.Outcome,
            "Materialization failures must retain deterministic candidates too.");
    }

    private static GenerationCandidateIntelligenceResult CreateCandidateIntelligence(
        GenerationRequest request,
        IReadOnlyList<double> scores)
    {
        GenerationMomentFindingResult moments = CreateMoments(request, scores);
        return new GenerationCandidateRefinementService().Refine(
            moments,
            CreateSpeech(
                request,
                AudioContentRoleAssignment.Unknown,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1)));
    }

    private static GenerationVisualSemanticSettings CreateVisualSettings()
    {
        const string promptText = "Frozen qualified test prompt.";
        string promptSha = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(promptText)));
        var prompt = new VisualSemanticPromptManifest(
            VisualSemanticPromptManifest.QualifiedEditorialSchemaVersion,
            VisualSemanticPromptManifest.QualifiedEditorialName,
            VisualSemanticPromptManifest.QualifiedEditorialVersion,
            promptText,
            promptSha,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var file = new VisualSemanticModelFile(
            "model.safetensors",
            new string('B', 64),
            1);
        string manifestSha = VisualSemanticModelManifest.ComputeManifestSha256(
            VisualSemanticModelManifest.SupportedSchemaVersion,
            "test/qwen",
            "test-revision",
            "Apache-2.0",
            "https://example.invalid/test",
            [file]);
        var model = new VisualSemanticModelManifest(
            VisualSemanticModelManifest.SupportedSchemaVersion,
            "test/qwen",
            "test-revision",
            Path.GetTempPath(),
            "Apache-2.0",
            "https://example.invalid/test",
            [file],
            manifestSha);
        return new GenerationVisualSemanticSettings(
            prompt,
            model,
            VisualSemanticVideoInputPolicy.CreateV05A1());
    }

    private static VisualSemanticEditorialObservation CreateVisualObservation(
        VisualSemanticEditorialDisposition disposition,
        VisualSemanticEditorialRejectReason reason,
        VisualSemanticTernary distinctEvent,
        string? qualificationEvidence = null,
        VisualSemanticObservableContentType contentType =
            VisualSemanticObservableContentType.Action)
    {
        const string intervalId = "qualification-1";
        VisualSemanticEditorialEvidenceInterval[] intervals =
            qualificationEvidence is null
                ? []
                :
                [
                    new VisualSemanticEditorialEvidenceInterval(
                        intervalId,
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(1),
                        qualificationEvidence,
                        VisualSemanticEvidenceBasis.Visual),
                ];
        VisualSemanticEditorialObservedChange[] changes =
            qualificationEvidence is null
                ? []
                :
                [
                    new VisualSemanticEditorialObservedChange(
                        qualificationEvidence,
                        VisualSemanticEvidenceBasis.Visual,
                        [intervalId]),
                ];
        return new VisualSemanticEditorialObservation(
            contentType,
            distinctEvent,
            distinctEvent,
            distinctEvent == VisualSemanticTernary.No
                ? VisualSemanticTernary.Yes
                : VisualSemanticTernary.No,
            VisualSemanticTernary.No,
            VisualSemanticTernary.No,
            VisualSemanticTranscriptContextSupport.NotSupplied,
            changes,
            intervals,
            [],
            disposition,
            reason,
            disposition == VisualSemanticEditorialDisposition.Keep
                 ? "A bounded observable event is present."
                 : "The bounded interval contains routine movement only.");
    }

    private static GenerationCandidateIntelligenceResult
        CreateThoroughGameplayCoverageIntelligence()
    {
        GenerationRequest request = CreateRequest(
            GenerationAnalysisDepth.Thorough,
            [("thorough-gameplay-coverage.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(12),
            desiredCount: 9,
            qualityThreshold: 70);
        IReadOnlyList<IReadOnlyList<MomentScoreComponent>> scores =
            Enumerable.Range(0, 9)
                .Select(index =>
                    (IReadOnlyList<MomentScoreComponent>)
                    [
                        ScoreComponent(
                            MomentScoreComponentCode.AudioNovelty,
                            1,
                            100),
                        ScoreComponent(
                            MomentScoreComponentCode.DurationFit,
                            (20 - index) / 100d,
                            100),
                    ])
                .Append(
                [
                    ScoreComponent(
                        MomentScoreComponentCode.GameplayProminence,
                        1,
                        30),
                    ScoreComponent(
                        MomentScoreComponentCode.GameplayBurstIntegration,
                        1,
                        25),
                    ScoreComponent(
                        MomentScoreComponentCode.GameplayOnset,
                        1,
                        20),
                    ScoreComponent(
                        MomentScoreComponentCode.GameplaySceneChange,
                        1,
                        10),
                    ScoreComponent(
                        MomentScoreComponentCode.GameplaySceneDensity,
                        1,
                        5),
                    ScoreComponent(
                        MomentScoreComponentCode.IndependentFamilyAgreement,
                        1,
                        10),
                    ScoreComponent(
                        MomentScoreComponentCode.DurationFit,
                        1,
                        11),
                ])
                .ToArray();
        GenerationMomentFindingResult moments = ReplaceMomentScores(
            CreateMoments(
                request,
                [100, 99, 98, 97, 96, 95, 94, 93, 92, 91]),
            scores);
        return new GenerationCandidateRefinementService().Refine(
            moments,
            CreateSpeech(
                request,
                AudioContentRoleAssignment.Unknown,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1)));
    }

    private static string ArgumentAfter(
        IReadOnlyList<string> arguments,
        string name)
    {
        int index = arguments.ToList().IndexOf(name);
        TestAssert.True(index >= 0 && index + 1 < arguments.Count, $"Missing argument '{name}'.");
        return arguments[index + 1];
    }

    private static GenerationSpeechActivityService CreateService(
        FakeAudioExtractor extractor,
        FakeSpeechProvider provider) =>
        new(extractor, provider, new GenerationSpeechActivitySettings(
            SpeechActivityOptions.CreateBalancedDefaults(),
            CreateModel()));

    private static GenerationRequest CreateRequest(
        GenerationAnalysisDepth analysisDepth,
        (string FileName, int AudioStreamCount)[] sources,
        (string FileName, int StreamIndex, CaptionAudioContentRole Role)? captionSelection = null,
        TimeSpan? sourceDuration = null,
        int desiredCount = 10,
        double qualityThreshold = 70,
        ContentEmphasis contentEmphasis = ContentEmphasis.Balanced,
        GenerationMomentGuidance? momentGuidance = null,
        GenerationDiscoveryIntent? discoveryIntent = null,
        TimeSpan? maximumClipDuration = null)
    {
        var selected = sources.Select((source, index) =>
            new SelectedVideoSource(
                TestMediaFactory.CreateSourcePath(source.FileName),
                isReference: index == 0)).ToArray();
        var preparationRequest = new GenerationSourcePreparationRequest(selected);
        var preparation = new GenerationSourcePreparationResult(
            preparationRequest,
            selected.Select((source, index) =>
                new PreparedGenerationSource(
                    source,
                    TestMediaFactory.Create(
                        source.FullPath,
                        duration: sourceDuration,
                        hasAudio: true,
                        audioStreamCount: sources[index].AudioStreamCount),
                    TestMediaFactory.CreateSnapshot(source.FullPath))));
        GenerationCaptionSettings captions = captionSelection is null
            ? GenerationCaptionSettings.Disabled
            : new GenerationCaptionSettings(
                true,
                GenerationCaptionStylePreset.Clean,
                [new GenerationCaptionSourceSelection(
                    selected.Single(source => Path.GetFileName(source.FullPath).Equals(
                        captionSelection.Value.FileName,
                        StringComparison.OrdinalIgnoreCase)).FullPath,
                    captionSelection.Value.StreamIndex,
                    captionSelection.Value.Role)]);
        var options = new GenerationSetupOptions(
            GenerationMode.IndividualClips,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            desiredCount,
            qualityThreshold,
            contentEmphasis,
            momentGuidance: momentGuidance,
            captionSettings: captions,
            analysisDepth: analysisDepth,
            discoveryIntent: discoveryIntent,
            maximumClipDuration: maximumClipDuration);
        return PreparedGenerationWorkflowTests.CreateGenerationRequest(
            preparation,
            options);
    }

    private static GenerationMomentFindingResult CreateMoments(
        GenerationRequest request,
        IReadOnlyList<double> scores,
        double windowSpacingSeconds = 50) =>
        new GenerationMomentFindingService(
            new GenerationMomentFindingTests.RecordingMomentFinder(
                [scores],
                windowSpacingSeconds: windowSpacingSeconds))
            .Find(new GenerationMomentFindingRequest(
                request.EvidenceAnalysis,
                request.SetupOptions));

    private static GenerationMomentFindingResult ReplaceMomentScores(
        GenerationMomentFindingResult original,
        IReadOnlyList<IReadOnlyList<MomentScoreComponent>> componentsByCandidate)
    {
        TestAssert.Equal(
            1,
            original.Sources.Count,
            "The score-replacement fixture expects one source.");
        GenerationSourceMomentResult originalSource = original.Sources.Single();
        TestAssert.Equal(
            originalSource.Moments.Proposals.Count,
            componentsByCandidate.Count,
            "Every fixture candidate needs one replacement score.");
        var replacements = new Dictionary<MomentCandidate, MomentCandidate>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0;
             index < originalSource.Moments.Proposals.Count;
             index++)
        {
            MomentCandidate candidate = originalSource.Moments.Proposals[index];
            replacements.Add(
                candidate,
                new MomentCandidate(
                    candidate.Id,
                    candidate.Window,
                    candidate.ConstructionReason,
                    candidate.EventNeighborhood,
                    candidate.Anchors,
                    new MomentScore(componentsByCandidate[index]),
                    candidate.Disposition,
                    candidate.FullFrameBlackOverlapRatio,
                    candidate.FullFrameFreezeOverlapRatio,
                    candidate.GameplayBlackOverlapRatio,
                    candidate.GameplayFreezeOverlapRatio,
                    candidate.IntegrityEvidenceReferences,
                    candidate.Episode,
                    candidate.ContextAllocation,
                    candidate.MontageObjective,
                    candidate.MontageSelectionReason,
                    candidate.EpisodeFeatures,
                    candidate.StandaloneFeatures,
                    candidate.MontageFeatures));
        }

        var mediaMoments = new MediaMomentFindingResult(
            originalSource.Moments.Request,
            originalSource.Moments.Proposals.Select(candidate =>
                replacements[candidate]),
            originalSource.Moments.SelectedCandidates.Select(candidate =>
                replacements[candidate]),
            originalSource.Moments.Warnings,
            originalSource.Moments.Manifest,
            originalSource.Moments.ActivationSeries,
            originalSource.Moments.Episodes);
        var source = new GenerationSourceMomentResult(
            originalSource.AnalyzedSource,
            mediaMoments);
        IReadOnlyList<GenerationMomentCandidate> selected =
            new GenerationMomentPortfolioSelector().Select(
                original.Request,
                [source]);
        return new GenerationMomentFindingResult(
            original.Request,
            [source],
            selected);
    }

    private static MomentScoreComponent ScoreComponent(
        MomentScoreComponentCode code,
        double normalizedValue,
        double weight) =>
        new(
            code,
            normalizedValue,
            normalizedValue,
            weight,
            normalizedValue * weight,
            "test component");

    private static GenerationSpeechActivityResult CreateSpeech(
        GenerationRequest request,
        AudioContentRoleAssignment role,
        TimeSpan start,
        TimeSpan end) =>
        CreateSpeech(request, role, [(start, end)]);

    private static GenerationSpeechActivityResult CreateSpeech(
        GenerationRequest request,
        AudioContentRoleAssignment role,
        IReadOnlyList<(TimeSpan Start, TimeSpan End)> intervals)
    {
        ModelArtifactManifest model = CreateModel();
        InferenceProviderIdentity provider = new("fake-vad", "1.0", "1.0");
        DateTimeOffset timestamp = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var manifest = new SpeechActivityExecutionManifest(
            provider,
            model,
            "fake-runtime",
            "1.0",
            "CPU",
            SpeechActivityOptions.CreateBalancedDefaults().ToNormalizedValues(),
            timestamp,
            timestamp.AddMilliseconds(1),
            TimeSpan.FromMilliseconds(1));
        SpeechActivityInterval[] detected = intervals
            .Select(static interval => new SpeechActivityInterval(
                interval.Start,
                interval.End,
                interval.Start,
                interval.End,
                0.95,
                0.8))
            .ToArray();
        var source = new GenerationSourceSpeechActivity(
            request.AnalyzedSources[0],
            [new GenerationSpeechStreamResult(
                request.AnalyzedSources[0],
                1,
                role,
                detected,
                [manifest])]);
        return new GenerationSpeechActivityResult(
            request,
            new GenerationSpeechActivitySettings(
                SpeechActivityOptions.CreateBalancedDefaults(),
                model),
            provider,
            [source],
            TimeSpan.FromMilliseconds(1));
    }

    private static ModelArtifactManifest CreateModel() =>
        new(
            "silero-vad-test",
            Path.Combine(Path.GetTempPath(), "silero-vad-test.onnx"),
            new string('A', 64),
            1024,
            new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
            "ONNX");

    private sealed class FakeAudioExtractor : IAudioSegmentExtractor
    {
        public List<AudioSegmentExtractionRequest> Requests { get; } = [];
        public int CleanupCount { get; private set; }

        public Task<ExtractedAudioSegment> ExtractAsync(
            AudioSegmentExtractionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var manifest = new AudioSegmentExtractionManifest(
                "fake", "1.0", Path.Combine(Path.GetTempPath(), "ffmpeg.exe"),
                new string('B', 64), "fake", [], request.SourcePath,
                request.Start, request.End, request.AbsoluteAudioStreamIndex,
                16000, 1, 16,
                new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 31, 12, 0, 1, TimeSpan.Zero),
                TimeSpan.FromSeconds(1));
            return Task.FromResult(new ExtractedAudioSegment(
                request.NeighborhoodId,
                Path.Combine(Path.GetTempPath(), $"{request.NeighborhoodId}.wav"),
                request.Duration,
                44 + (long)(request.Duration.TotalSeconds * 32000),
                manifest,
                () => CleanupCount++));
        }
    }

    private sealed class FakeSpeechProvider : ISpeechActivityProvider
    {
        public InferenceProviderIdentity Identity { get; } = new(
            "fake-vad", "1.0", "1.0");
        public List<SpeechActivityRequest> Requests { get; } = [];
        public Func<SpeechActivityRequest, CancellationToken, Task<SpeechActivityResult>>?
            Handler
        { get; init; }

        public Task<SpeechActivityResult> AnalyzeAsync(
            SpeechActivityRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Handler is not null)
            {
                return Handler(request, cancellationToken);
            }

            DateTimeOffset started = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
            var manifest = new SpeechActivityExecutionManifest(
                Identity,
                request.Model,
                "fake-runtime",
                "1.0",
                "CPU",
                request.Options.ToNormalizedValues(),
                started,
                started.AddMilliseconds(1),
                TimeSpan.FromMilliseconds(1));
            TimeSpan end = request.InputDuration < TimeSpan.FromSeconds(1)
                ? request.InputDuration
                : TimeSpan.FromSeconds(1);
            return Task.FromResult(new SpeechActivityResult(
                request,
                [new SpeechActivityInterval(
                    TimeSpan.Zero,
                    end,
                    request.AbsoluteSourceOffset,
                    request.AbsoluteSourceOffset + end,
                    0.9,
                    0.8)],
                manifest));
        }
    }

    private sealed class FakeVisualReviewMaterializer :
        IVisualSemanticReviewVideoMaterializer,
        IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry",
            "VisualSemanticTests",
            Guid.NewGuid().ToString("N"));

        public FakeVisualReviewMaterializer() => Directory.CreateDirectory(_directory);

        public int CleanupCount { get; private set; }

        public Exception? Failure { get; init; }

        public List<VisualSemanticReviewVideoMaterializationRequest> Requests { get; } = [];

        public Task<MaterializedVisualSemanticReviewVideo> MaterializeAsync(
            VisualSemanticReviewVideoMaterializationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (Failure is not null)
            {
                return Task.FromException<MaterializedVisualSemanticReviewVideo>(
                    Failure);
            }
            string path = Path.Combine(_directory, $"review-{Requests.Count}.mp4");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var info = new FileInfo(path);
            var input = new VisualSemanticInputManifest(
                path,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                info.Length,
                request.Duration,
                new DateTimeOffset(
                    DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc)));
            return Task.FromResult(new MaterializedVisualSemanticReviewVideo(
                request,
                input,
                () =>
                {
                    CleanupCount++;
                    File.Delete(path);
                }));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class FakeVisualEditorialProvider :
        IVisualSemanticEditorialProvider
    {
        public InferenceProviderIdentity Identity { get; } = new(
            "fake-qualified-qwen",
            "2.7",
            "test");

        public List<VisualSemanticBatchRequest> Requests { get; } = [];

        public Exception? Failure { get; init; }

        public Func<VisualSemanticRequest, int, VisualSemanticEditorialObservation>
            ObservationFactory
        { get; init; } =
                static (_, _) => CreateVisualObservation(
                    VisualSemanticEditorialDisposition.Keep,
                    VisualSemanticEditorialRejectReason.None,
                    VisualSemanticTernary.Yes);

        public Task<VisualSemanticEditorialBatchResult> ObserveAsync(
            VisualSemanticBatchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (Failure is not null)
            {
                return Task.FromException<VisualSemanticEditorialBatchResult>(
                    Failure);
            }
            VisualSemanticEditorialCollectionAudit empty = new(0, 0, 0, false);
            var audit = new VisualSemanticEditorialCanonicalizationAudit(
                VisualSemanticEditorialCanonicalizer.PolicyVersion,
                empty,
                empty,
                empty,
                OuterWhitespaceTrimmed: false,
                SyntacticCanonicalizationCount: 0,
                SchemaShapeCanonicalizationCount: 0,
                SemanticRepairCount: 0,
                WireRepresentationVersion: "visual-semantic-editorial-wire-1.1");
            VisualSemanticEditorialResult[] results = request.Requests
                .Select((item, index) => new VisualSemanticEditorialResult(
                    item,
                    ObservationFactory(item, index),
                    audit,
                    TimeSpan.FromMilliseconds(1)))
                .ToArray();
            return Task.FromResult(new VisualSemanticEditorialBatchResult(
                request,
                results,
                TimeSpan.FromMilliseconds(results.Length),
                1024));
        }
    }

    private sealed class RecordingEditorialMetadataGenerationService :
        IClipEditorialMetadataGenerationService
    {
        private static readonly ClipEditorialMetadataGeneratorIdentity
            AiIdentity = new(
                "recording-grounded-editorial-ai",
                "1.0");
        private static readonly ClipEditorialAiProvenance AiProvenance = new(
            AiIdentity.Name,
            AiIdentity.Version,
            runtimeVersion: "test-runtime-1.0",
            modelRepositoryId: "test/recording-grounded-editorial-model",
            modelRevision: "test-revision",
            modelManifestSha256: new string('a', 64),
            promptName: "recording-grounded-editorial-prompt",
            promptVersion: "1.0",
            promptSha256: new string('b', 64),
            batchElapsed: TimeSpan.FromMilliseconds(1),
            peakAllocatedGpuBytes: null);
        private readonly List<ClipEditorialMetadataRequest> _requests = [];

        public bool IsAiAvailable => true;

        public IReadOnlyList<ClipEditorialMetadataRequest> Requests =>
            _requests;

        public async Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests.Add(request);
            ClipEditorialMetadataDraft naturalDraft =
                await new HeuristicClipEditorialMetadataGenerator()
                .GenerateAsync(request, cancellationToken);
            if (request.Preference ==
                ClipEditorialGenerationPreference.HeuristicOnly)
            {
                return naturalDraft;
            }

            return new ClipEditorialMetadataDraft(
                naturalDraft.Title,
                naturalDraft.Description,
                naturalDraft.Tags,
                ClipEditorialMetadataOrigin.AiAssisted,
                AiIdentity,
                request.Attempt,
                naturalDraft.Evidence,
                naturalDraft.Warnings,
                AiProvenance,
                ClipEditorialMetadataReadiness.GroundedDraft,
                naturalDraft.QualityIssues,
                naturalDraft.PriorAcceptedTitles,
                groundingAudit: new GameKnowledgeInfluenceAudit(
                    request.Context.EditorialBrief.Fingerprint,
                    request.RevisionKind,
                    usedClaimIds: [],
                    usedEvidenceIds:
                        naturalDraft.GroundingAudit?.UsedEvidenceIds ?? [],
                    needsReview: false,
                    fallbackReason: null,
                    resolvedBrief: request.Context.EditorialBrief,
                    sourceBindings: []));
        }
    }

    private sealed class FixedPreferenceProfileProvider(
        ClipPreferenceProfile profile) : IClipPreferenceProfileProvider
    {
        public ClipPreferenceProfile Current { get; } = profile;
    }
}
