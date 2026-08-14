using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
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
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationSpeechActivityTests
{
    public static IEnumerable<TestCase> GetTests()
    {
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
            "Candidate refinement obeys the user-selected content focus",
            RefinementUsesContentFocus);
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
            "Qualified visual observations rerank through transparent components",
            VisualObservationsRerankTransparently);
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
            qualityThreshold: 70);
        GenerationMomentFindingResult moments = CreateMoments(request, [80, 78]);
        GenerationSpeechActivityResult speech = CreateSpeech(
            request,
            AudioContentRoleAssignment.Unknown,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(90));

        GenerationCandidateIntelligenceResult result =
            new GenerationCandidateRefinementService().Refine(moments, speech);

        TestAssert.Equal("candidate-1-1", result.RefinedMoments.SelectedCandidates[0].Candidate.Id, "Speech timing may rerank through policy, not provider rank output.");
        GenerationCandidateRefinement refinement = result.RefinedMoments.SelectedCandidates[0].Refinement!;
        TestAssert.Equal(78d, refinement.BaseScore, "The v1.3 score remains visible.");
        TestAssert.Equal(refinement.BaseScore + refinement.Components.Sum(static item => item.SignedContribution), refinement.UnclampedScore, "Every contribution must reconcile exactly.");
        TestAssert.Equal(refinement.FinalScore, result.RefinedMoments.SelectedCandidates[0].FinalScore, "The selected score is the transparent final score.");
        return Task.CompletedTask;
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
            metadataRequest.ReviewVideo is null,
            "A long ranking review must not be reused as the metadata review; metadata needs one focused sampling window.");
        TestAssert.Equal(
            refined.RefinedMoments.SelectedCandidates[0]
                .Candidate.EventNeighborhood.PeakTimestamp,
            metadataRequest.ReviewFocusSourceTimestamp!.Value,
            "Metadata review must remain centered on the retained deterministic event when the qualified observation has no narrower evidence interval.");
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
            desiredCount: 9,
            qualityThreshold: 1);
        GenerationCandidateIntelligenceResult intelligence =
            CreateCandidateIntelligence(
                request,
                [99, 98, 97, 96, 95, 94, 93, 92, 91]);

        IReadOnlyList<GenerationVisualSemanticAnalysisService.CandidateSource>
            shortlist = GenerationVisualSemanticAnalysisService.CreateShortlist(
                intelligence,
                maximumCandidateCount: 8);

        TestAssert.Equal(8, shortlist.Count, "A production Qwen batch remains bounded to eight candidates.");
        TestAssert.True(
            shortlist.Select(static value => value.Candidate.Id)
                .SequenceEqual(
                    Enumerable.Range(0, 8).Select(static index => $"candidate-1-{index}"),
                    StringComparer.Ordinal),
            "The strongest shortlist order must be stable and deterministic.");
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
                        distinctEvent: VisualSemanticTernary.No)
                    : CreateVisualObservation(
                        VisualSemanticEditorialDisposition.Keep,
                        VisualSemanticEditorialRejectReason.None,
                        distinctEvent: VisualSemanticTernary.Yes),
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
        VisualSemanticTernary distinctEvent) =>
        new(
            VisualSemanticObservableContentType.Action,
            distinctEvent,
            distinctEvent,
            distinctEvent == VisualSemanticTernary.No
                ? VisualSemanticTernary.Yes
                : VisualSemanticTernary.No,
            VisualSemanticTernary.No,
            VisualSemanticTernary.No,
            VisualSemanticTranscriptContextSupport.NotSupplied,
            [],
            [],
            [],
            disposition,
            reason,
            disposition == VisualSemanticEditorialDisposition.Keep
                ? "A bounded observable event is present."
                : "The bounded interval contains routine movement only.");

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
        ContentEmphasis contentEmphasis = ContentEmphasis.Balanced)
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
            captionSettings: captions,
            analysisDepth: analysisDepth);
        return PreparedGenerationWorkflowTests.CreateGenerationRequest(
            preparation,
            options);
    }

    private static GenerationMomentFindingResult CreateMoments(
        GenerationRequest request,
        IReadOnlyList<double> scores) =>
        new GenerationMomentFindingService(
            new GenerationMomentFindingTests.RecordingMomentFinder([scores]))
            .Find(new GenerationMomentFindingRequest(
                request.EvidenceAnalysis,
                request.SetupOptions));

    private static GenerationSpeechActivityResult CreateSpeech(
        GenerationRequest request,
        AudioContentRoleAssignment role,
        TimeSpan start,
        TimeSpan end)
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
        var interval = new SpeechActivityInterval(
            start,
            end,
            start,
            end,
            0.95,
            0.8);
        var source = new GenerationSourceSpeechActivity(
            request.AnalyzedSources[0],
            [new GenerationSpeechStreamResult(
                request.AnalyzedSources[0],
                1,
                role,
                [interval],
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

        public List<VisualSemanticReviewVideoMaterializationRequest> Requests { get; } = [];

        public Task<MaterializedVisualSemanticReviewVideo> MaterializeAsync(
            VisualSemanticReviewVideoMaterializationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
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
            return await new HeuristicClipEditorialMetadataGenerator()
                .GenerateAsync(request, cancellationToken);
        }
    }

    private sealed class FixedPreferenceProfileProvider(
        ClipPreferenceProfile profile) : IClipPreferenceProfileProvider
    {
        public ClipPreferenceProfile Current { get; } = profile;
    }
}
