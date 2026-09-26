using ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.SpeechActivity;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static async Task CaptureScreeningPreservesPersonalRanking()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Balanced, [("ranked-gameplay.mkv", 1)], desiredCount: 1);
        var speech = CreateSpeech(request, AudioContentRoleAssignment.Unknown, []);
        var intelligence = new GenerationCandidateRefinementService().Refine(CreateMoments(request, [81, 80]), speech);
        var refinements = intelligence.Refinements.ToDictionary(static item => item.Candidate);
        var preferred = intelligence.BaseMoments.Sources.Single().Moments.Proposals.Last();
        var preferences = new Dictionary<MomentCandidate, double> { [preferred] = 4 };
        var pool = refinements.Keys.ToHashSet();
        var selected = new GenerationMomentPortfolioSelector().SelectEligible(intelligence.BaseMoments.Request,
            intelligence.BaseMoments.Sources, refinements, pool, preferences, CancellationToken.None);
        var personalized = new GenerationMomentFindingResult(intelligence.BaseMoments.Request,
            intelligence.BaseMoments.Sources, selected, refinements, selectionPreferences: preferences);
        intelligence = new(intelligence.BaseMoments, speech, refinements.Values, personalized);
        var screened = await new GenerationCaptureContextScreeningService(new StartupVisualText { GameplayOnly = true })
            .ScreenAsync(intelligence, null, CancellationToken.None);
        TestAssert.Same(preferred, screened.RefinedMoments.SelectedCandidates.Single().Candidate,
            "The picture check must preserve personal ranking while applying menu exclusions and selecting replacements.");
        preferences.Clear();
        TestAssert.Equal(1, personalized.SelectionPreferences.Count, "Retained preference values must be immutable snapshots.");
    }

    private static Task StartupLeadInRequiresCompleteLeadingEvidence()
    {
        GenerationRequest request = StartupRequest();
        MomentCandidate candidate = EndingCandidate(request, 19, 61.12, 33);
        var speech = StartupSpeech(request);
        var repeated = new GenerationCaptureContextAssessment(GenerationCaptureContextKind.Launcher, [0, 1]);
        TimeSpan[] times = [TimeSpan.FromSeconds(23.212), TimeSpan.FromSeconds(29.53)];
        var result = GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated, times, speech.Sources.Single(), candidate, request.SetupOptions);
        TestAssert.Equal(TimeSpan.FromSeconds(54.754), result!.FirstSpeechStart,
            "The actual two-stream recording has 35.754 seconds of leading silence before the greeting.");
        TestAssert.False(GenerationCaptureContextPolicy.ShouldExcludeAutomatically(repeated, speech.Sources.Single(), candidate, request.SetupOptions),
            "A late greeting must not be mislabeled as a wholly silent application capture.");
        GenerationSourceSpeechActivity[] unavailableOrSpoken =
        [new(speech.Sources.Single().Source, []),
            new(speech.Sources.Single().Source, [speech.Sources.Single().Streams[1]]),
            StartupSpeech(request, first: [(20, 21)]).Sources.Single(),
            StartupSpeech(request, warning: SpeechActivityWarningCode.RuntimeReportedWarning).Sources.Single()];
        foreach (var sourceSpeech in unavailableOrSpoken)
            TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated, times, sourceSpeech, candidate, request.SetupOptions) is null,
                "Missing tracks, warned timing, or speech near the opening on any track cannot establish a long silent lead-in.");
        foreach (SpeechActivityWarningCode benign in new[] { SpeechActivityWarningCode.TrailingAudioPadded, SpeechActivityWarningCode.MaximumSpeechDurationSplit })
            TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated, times,
                StartupSpeech(request, warning: benign).Sources.Single(), candidate, request.SetupOptions) is not null,
                "A deterministically padded remote source tail or split interval does not invalidate otherwise bounded successful VAD timing.");
        foreach (TimeSpan[] invalidTimes in new[]
        {
            new[] { times[0], TimeSpan.FromSeconds(56.908) },
            new[] { times[0], times[0] },
            new[] { TimeSpan.FromSeconds(18), times[0] },
        })
            TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated, invalidTimes, speech.Sources.Single(), candidate, request.SetupOptions) is null,
                "Both repeated app observations must have distinct timestamps inside this candidate's verified leading silence.");
        TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(
            new(GenerationCaptureContextKind.Loading, [0, 1]), times, speech.Sources.Single(), candidate, request.SetupOptions) is null,
            "Loading labels alone do not become the stronger application-startup gate.");
        return Task.CompletedTask;
    }

    private static Task StartupLeadInPreservesDurationIntentAndManualChoice()
    {
        var repeated = new GenerationCaptureContextAssessment(GenerationCaptureContextKind.Launcher, [0, 1]);
        TimeSpan[] times = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)];
        GenerationRequest request = StartupRequest();
        var half = GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated, times,
            StartupSpeech(request, second: [(12, 20)]).Sources.Single(), EndingCandidate(request, 0, 24, 10), request.SetupOptions);
        TestAssert.True(half is not null, "Twelve seconds and exactly half the candidate meet both explicit review thresholds.");
        TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated, times,
            StartupSpeech(request, second: [(11.999, 20)]).Sources.Single(), EndingCandidate(request, 0, 20, 10), request.SetupOptions) is null,
            "A lead-in just below twelve seconds cannot pass on ratio alone.");
        TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated, times,
            StartupSpeech(request, second: [(12, 25)]).Sources.Single(), EndingCandidate(request, 0, 30, 10), request.SetupOptions) is null,
            "Twelve seconds shorter than half the cut is not the long-leading-silence case.");

        MomentCandidate mixedAction = EndingCandidate(request, 0, 60, 40);
        var conservative = GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated,
            [TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(15)], StartupSpeech(request, second: [(55, 60)]).Sources.Single(), mixedAction, request.SetupOptions);
        TestAssert.True(conservative is not null,
            "A mixed clip with a later action can conservatively require review of its verified app opening; this is not a claim that later silent gameplay is absent.");
        foreach (GenerationDiscoveryIntent intent in new[]
        {
            new GenerationDiscoveryIntent(GenerationMomentIntent.Tutorial), new(GenerationMomentIntent.Dialogue),
            new(GenerationMomentIntent.Story), new(GenerationMomentIntent.Discovery),
            new(naturalLanguageQuery: "Show the application setup and explain the controls."), new(spokenTerms: "settings"),
        })
        {
            GenerationRequest explicitRequest = StartupRequest(intent: intent);
            TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated,
                [TimeSpan.FromSeconds(23.212), TimeSpan.FromSeconds(29.53)], StartupSpeech(explicitRequest).Sources.Single(),
                EndingCandidate(explicitRequest, 19, 61.12, 33), explicitRequest.SetupOptions) is null,
                "Explicit tutorial, story, discovery, spoken-term, and natural-language preferences preserve their requested context.");
        }
        foreach (UserMomentGuidance guide in new[]
        {
            UserMomentGuidance.CreatePoint(request.ReferenceSource.FullPath, request.ReferencePreparedSource.Media.Duration, TimeSpan.FromSeconds(33)),
            UserMomentGuidance.CreateRange(request.ReferenceSource.FullPath, request.ReferencePreparedSource.Media.Duration, TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(61.12)),
        })
        {
            GenerationRequest guided = StartupRequest(guidance: new([guide]));
            TestAssert.True(GenerationCaptureContextPolicy.FindApplicationStartupLeadIn(repeated,
                [TimeSpan.FromSeconds(23.212), TimeSpan.FromSeconds(29.53)], StartupSpeech(guided).Sources.Single(),
                EndingCandidate(guided, 19, 61.12, 33), guided.SetupOptions) is null,
                "A matching manual marker or range remains the creator's explicit selection.");
        }
        return Task.CompletedTask;
    }

    private static async Task StartupScreeningUsesFiveOwnedFramesAndRetainsReview()
    {
        GenerationRequest request = StartupRequest();
        var intelligence = StartupIntelligence(request);
        var reader = new StartupVisualText();
        var diagnostics = new List<GenerationCaptureContextDiagnostic>();
        var service = new GenerationCaptureContextScreeningService(reader, diagnostics.Add);
        var screened = await service.ScreenAsync(intelligence, null, CancellationToken.None);
        TestAssert.Equal(2, reader.Requests.Count, "One strong initial app hit permits only one adaptive OCR call.");
        TestAssert.Equal(3, reader.Requests[0].MaximumSampleCount, "Initial 10/50/90 percent sampling remains unchanged.");
        TestAssert.Equal(2, reader.Requests[1].MaximumSampleCount, "The follow-up is bounded to two additional frames.");
        TestAssert.True(reader.Requests[1].PriorityTimestamps.SequenceEqual(new[] { TimeSpan.FromSeconds(29.53), TimeSpan.FromSeconds(50.59) }),
            "The recorded 19–61.12 cut receives the exact additional 25/75 percent timestamps.");
        var diagnostic = diagnostics.Single();
        TestAssert.Equal(5, diagnostic.Frames.Count, "Only five uniquely timed observations can support this candidate.");
        TestAssert.True(diagnostic.ApplicationStartupLeadIn && diagnostic.FirstSpeechStartSeconds == 54.754,
            "Diagnostics must show the separate lead-in finding and actual all-stream first speech time.");
        var refined = screened.Refinements.Single();
        TestAssert.True(refined.HasApplicationStartupLeadIn && !refined.HasNonGameplayCapture,
            "A mixed greeting clip receives a leading-edit review marker, not an assertion of total silence.");
        TestAssert.Equal(0, screened.RefinedMoments.SelectedCandidates.Count, "Count filling cannot waive a verified bad leading edit.");
        TestAssert.Equal(GenerationHiddenMomentReason.CaptureContextReview,
            GenerationHiddenMomentPlanner.Create(screened.RefinedMoments, screened).Moments.Single().Reason,
            "The candidate remains available for manual inspection and trimming in Studio.");
        using var visual = new GenerationVisualSemanticAnalysisResult(screened, new InferenceProviderIdentity("fixture", "1", "1"),
            [Reviewed(screened, refined.Candidate, CreateVisualObservation(VisualSemanticEditorialDisposition.Keep,
                VisualSemanticEditorialRejectReason.None, VisualSemanticTernary.Yes, "A later event has a complete visual Keep."))], TimeSpan.Zero, null);
        var afterKeep = new GenerationCandidateRefinementService().ApplyVisualSemantic(screened, visual);
        TestAssert.True(afterKeep.Refinements.Single().HasApplicationStartupLeadIn,
            "A general Keep cannot prove that this independently observed application opening is a suitable edit.");
        TestAssert.False(afterKeep.Refinements.Single().Components.Any(static value =>
            value.Code == GenerationCandidateRefinementComponentCode.CaptureContextPenalty),
            "The existing soft capture-penalty override is unchanged; only the independently typed leading-edit marker survives.");
        TestAssert.Equal(0, afterKeep.RefinedMoments.SelectedCandidates.Count, "A high reviewed score still cannot erase the leading-edit guard.");
    }

    private static async Task StartupScreeningPreservesBypassesOwnershipAndBounds()
    {
        foreach (GenerationRequest request in new[]
        {
            StartupRequest(intent: new(naturalLanguageQuery: "Explain this application's setup.")),
            StartupRequest(intent: new(GenerationMomentIntent.Tutorial)),
            StartupRequest(emphasis: ContentEmphasis.CommentaryFocused),
        })
        {
            var reader = new StartupVisualText();
            var intelligence = StartupIntelligence(request);
            TestAssert.Same(intelligence, await new GenerationCaptureContextScreeningService(reader).ScreenAsync(intelligence, null, CancellationToken.None),
                "Explicit query, tutorial, and commentary requests bypass capture screening without changing generation.");
            TestAssert.Equal(0, reader.Requests.Count, "A bypass cannot start extra OCR work.");
        }
        GenerationRequest ordinary = StartupRequest();
        foreach (var reader in new[] { new StartupVisualText { LateOnly = true }, new StartupVisualText { WrongFollowupTimestamp = true } })
        {
            var result = await new GenerationCaptureContextScreeningService(reader).ScreenAsync(StartupIntelligence(ordinary), null, CancellationToken.None);
            TestAssert.False(result.Refinements.Single().HasApplicationStartupLeadIn,
                "A late incidental launcher or a repeated response with an unrequested timestamp cannot establish leading app evidence.");
            TestAssert.True(reader.Requests.Sum(static value => value.MaximumSampleCount) <= 5, "Adaptive work never exceeds five requested frames.");
        }
        var speaking = StartupIntelligence(ordinary, StartupSpeech(ordinary, second: [(20, 60.958)]));
        var spokenResult = await new GenerationCaptureContextScreeningService(new StartupVisualText()).ScreenAsync(speaking, null, CancellationToken.None);
        TestAssert.False(spokenResult.Refinements.Single().HasApplicationStartupLeadIn,
            "An ordinary speaking software walkthrough beginning near the cut start retains its context.");
        var repeatedReader = new StartupVisualText { AllFramesAreLauncher = true };
        await new GenerationCaptureContextScreeningService(repeatedReader).ScreenAsync(StartupIntelligence(ordinary), null, CancellationToken.None);
        TestAssert.Equal(1, repeatedReader.Requests.Count, "Already repeated app evidence does not trigger adaptive sampling.");
    }

    private static GenerationRequest StartupRequest(GenerationDiscoveryIntent? intent = null,
        ContentEmphasis emphasis = ContentEmphasis.Balanced, GenerationMomentGuidance? guidance = null) =>
        CreateRequest(GenerationAnalysisDepth.Thorough, [("startup-review.mkv", 2)], sourceDuration: TimeSpan.FromSeconds(120),
            desiredCount: 1, contentEmphasis: emphasis, momentGuidance: guidance, discoveryIntent: intent);

    private static GenerationCandidateIntelligenceResult StartupIntelligence(GenerationRequest request, GenerationSpeechActivityResult? speech = null)
    {
        var moments = EndingMoments(request, EndingCandidate(request, 19, 61.12, 33));
        return new GenerationCandidateRefinementService().Refine(moments, speech ?? StartupSpeech(request));
    }

    private static GenerationSpeechActivityResult StartupSpeech(GenerationRequest request, (double Start, double End)[]? first = null,
        (double Start, double End)[]? second = null, SpeechActivityWarningCode? warning = null)
    {
        var original = CreateSpeech(request, AudioContentRoleAssignment.Unknown,
            (first ?? []).Select(static interval => (TimeSpan.FromSeconds(interval.Start), TimeSpan.FromSeconds(interval.End))).ToArray());
        var source = original.Sources.Single();
        var manifest = source.Streams.Single().ExecutionManifests.Single();
        SpeechActivityWarning[] warnings = warning is { } code ? [new(code, "Fixture VAD execution report.")] :
            source.Streams.Single().Intervals.Count == 0 ? [new(SpeechActivityWarningCode.NoSpeechDetected, "Fixture VAD detected no speech on this stream.")] : [];
        var firstManifest = new SpeechActivityExecutionManifest(manifest.Provider, manifest.Model, manifest.RuntimeName,
            manifest.RuntimeVersion, manifest.ExecutionBackend, manifest.Options, manifest.StartedAtUtc, manifest.CompletedAtUtc, manifest.Elapsed,
            warnings);
        var firstStream = new GenerationSpeechStreamResult(source.Source, 1, AudioContentRoleAssignment.Unknown,
            source.Streams.Single().Intervals, [firstManifest]);
        var secondStream = new GenerationSpeechStreamResult(source.Source, 2,
            new(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
            (second ?? [(54.754, 59.87), (60.162, 60.958)]).Select(static interval => new SpeechActivityInterval(
                TimeSpan.FromSeconds(interval.Start), TimeSpan.FromSeconds(interval.End), TimeSpan.FromSeconds(interval.Start), TimeSpan.FromSeconds(interval.End), .95, .8)),
            [manifest]);
        return new(request, original.Settings, original.ProviderIdentity, [new(source.Source, [firstStream, secondStream])], original.Elapsed);
    }

    private sealed class StartupVisualText : IGenerationVisualTextAnalysisService
    {
        public bool IsAvailable => true;
        public bool LateOnly { get; init; }
        public bool AllFramesAreLauncher { get; init; }
        public bool GameplayOnly { get; init; }
        public bool WrongFollowupTimestamp { get; init; }
        public List<GenerationVisualTextAnalysisRequest> Requests { get; } = [];

        public Task<ClipEditorialContext> EnrichAsync(GenerationVisualTextAnalysisRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var frames = request.PriorityTimestamps.Select(timestamp =>
            {
                TimeSpan reported = WrongFollowupTimestamp && Requests.Count > 1 ? TimeSpan.FromSeconds(23.212) : timestamp;
                bool launcher = !GameplayOnly && (AllFramesAreLauncher || (LateOnly ? reported.TotalSeconds > 55 : reported.TotalSeconds < 35));
                var frame = new VideoPreviewFrame(request.Media.FullPath, request.Media.Duration, request.Media.PrimaryVideoStream.Index,
                    reported, null, 1280, 720, CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop, [1],
                    new("fixture", "1", "ffmpeg", "fixture", TestMediaFactory.CreateSourcePath("ffmpeg.exe"), DateTimeOffset.UnixEpoch, TimeSpan.Zero));
                return new VisualTextFrameObservation(new(frame), new("fixture", "1", "CPU", "fixture", "en-US"),
                    (launcher ? new[] { "CLOUD STATUS", "LAST PLAYED", "PLAYTIME" } : new[] { "NORDIC" })
                        .Select(static text => new VisualTextLine(text, [new(text, new(0, 0, .9, .1))])), TimeSpan.Zero);
            }).ToArray();
            NormalizedRectangle region = request.Context.GameplayRegion!;
            // Reconstructed but equal geometry is valid ownership, not a reference-identity requirement.
            return Task.FromResult(request.Context.WithVisualText(new(request.Context.CandidateId, request.Media.FullPath,
                new(region.X, region.Y, region.Width, region.Height), frames, [])));
        }
    }
}
