using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task TranscriptEndingRepairsRecordedSentencePause()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-recorded.mkv", 1)],
            sourceDuration: TimeSpan.FromSeconds(120), desiredCount: 1, maximumClipDuration: TimeSpan.FromSeconds(45));
        MomentCandidate candidate = EndingCandidate(request, 0, 28.2, 20);
        GenerationSourceTranscript transcript = RecordedEndingTranscript(request);
        var speech = SentenceSpeech(request, [(24.354, 26.43), (26.978, 28.894), (29.154, 30.622),
            (33.282, 34.334), (34.722, 37.438), (41.058, 43.07)]);
        var intervals = speech.Sources.Single().Streams.Single().Intervals;
        TestAssert.Equal(TimeSpan.FromMilliseconds(31372), GenerationCandidateNaturalEndingPolicy.Adjust(
            candidate, intervals, TimeSpan.FromSeconds(45)).Candidate.Window.End,
            "The recorded VAD-only shape must reproduce semantic24's incomplete 31.372-second ending.");
        var repaired = GenerationCandidateNaturalEndingPolicy.Adjust(candidate, intervals, TimeSpan.FromSeconds(45),
            transcript, TimeSpan.FromSeconds(12));
        TestAssert.Equal(TimeSpan.FromMilliseconds(39950), repaired.Candidate.Window.End,
            "The retained whole phrase must include the continuation through 'world.' and stop before the next phrase at 39.95.");
        TestAssert.Equal(TimeSpan.Zero, repaired.Candidate.Window.Start,
            "Ending repair must preserve the existing beginning, rather than shift it into another sentence.");
        TestAssert.Same(candidate.Score, repaired.Candidate.Score, "Sentence boundaries cannot invent quality or event scores.");
        TestAssert.True(repaired.EvidenceReferences.Any(reference => reference.Contains(":continuation:", StringComparison.Ordinal)),
            "The adjustment must cite the retained continuation, not claim that a VAD pause established completeness.");
        var moments = EndingMoments(request, candidate);
        var result = new GenerationCandidateRefinementService().Refine(moments, speech,
            new GenerationTranscriptAnalysisResult(moments, [transcript]));
        TestAssert.Equal(repaired.Candidate.Window.End, result.Refinements.Single().Candidate.Window.End,
            "The actual refinement path must repair endings before final selection and visual review.");
        TestAssert.False(result.Refinements.Single().HasIncompleteSpeechEnding, "A complete repaired sentence can pass the boundary gate.");
        return Task.CompletedTask;
    }

    private static Task TranscriptEndingUsesMeasuredBoundariesAndWholePhraseFallback()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-words.mkv", 1)]);
        MomentCandidate candidate = EndingCandidate(request, 0, 11, 5);
        (string Text, double Start, double End)[] words =
        [("We", 6, 7), ("wait", 8, 10), ("here.", 13, 14), ("Next", 14.1, 16), ("sentence.", 17, 40)];
        AudioTranscriptionSegment Segment(IEnumerable<AudioTranscriptionWarning>? warnings = null) =>
            new("measured", "chunk", string.Join(' ', words.Select(static word => word.Text)),
                TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(40),
                words.Select(static word => new AudioTranscriptionWord(word.Text, TimeSpan.FromSeconds(word.Start),
                    TimeSpan.FromSeconds(word.End), TimeSpan.FromSeconds(word.Start), TimeSpan.FromSeconds(word.End))), warnings: warnings);
        var transcript = SentenceTranscript(request, [Segment()], [("chunk", 0, 60)]);
        var measured = GenerationCandidateNaturalEndingPolicy.Adjust(candidate, [], TimeSpan.FromSeconds(45), transcript);
        TestAssert.Equal(TimeSpan.FromSeconds(14.1), measured.Candidate.Window.End,
            "Verified word timing can choose the first internal sentence end and cap its tail at the next word's onset.");
        var uncertainWords = transcript with { Segments = [Segment([new(AudioTranscriptionWarningCode.WordTimingCanonicalized,
            "The original word clocks overlapped and were repaired.")])] };
        TestAssert.Equal(TimeSpan.FromSeconds(40.75), GenerationCandidateNaturalEndingPolicy.Adjust(candidate, [],
            TimeSpan.FromSeconds(45), uncertainWords).Candidate.Window.End,
            "Repaired word times cannot locate an internal sentence boundary; the complete reliable timed phrase is retained instead.");
        foreach (string abbreviation in new[] { "U.S.", "St." })
        {
            (string Text, double Start, double End)[] abbreviatedWords =
                [("Visit", 10, 12), (abbreviation, 14, 14.5), ("again", 15.4, 16), ("tomorrow.", 17, 18)];
            var abbreviatedSegment = new AudioTranscriptionSegment("abbreviated", "chunk",
                string.Join(' ', abbreviatedWords.Select(static word => word.Text)), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(18),
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(18), abbreviatedWords.Select(static word =>
                    new AudioTranscriptionWord(word.Text, TimeSpan.FromSeconds(word.Start), TimeSpan.FromSeconds(word.End),
                        TimeSpan.FromSeconds(word.Start), TimeSpan.FromSeconds(word.End))));
            var abbreviated = SentenceTranscript(request, [abbreviatedSegment], [("chunk", 0, 60)]);
            TestAssert.Equal(TimeSpan.FromSeconds(18.75), GenerationCandidateNaturalEndingPolicy.Adjust(
                EndingCandidate(request, 0, 14.2, 10), [], TimeSpan.FromSeconds(30), abbreviated).Candidate.Window.End,
                "An internal dotted acronym or common abbreviation must not be mistaken for a complete sentence ending.");
        }
        return Task.CompletedTask;
    }

    private static Task TranscriptEndingCrossesContinuousChunks()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-chunks.mkv", 1)],
            sourceDuration: TimeSpan.FromSeconds(300));
        MomentCandidate candidate = EndingCandidate(request, 90, 118, 100);
        var transcript = SentenceTranscript(request,
            [SentenceSegment("opening", "first", "If we wait here.", 115, 119.8),
                SentenceSegment("continuation", "second", "the guard will move.", 120.1, 124, 120)],
            [("first", 0, 120), ("second", 120, 120)]);
        var repaired = GenerationCandidateNaturalEndingPolicy.Adjust(candidate, [], TimeSpan.FromSeconds(45), transcript);
        TestAssert.Equal(TimeSpan.FromSeconds(124.75), repaired.Candidate.Window.End,
            "A decoder-edge period must not truncate a sentence that continues in the next continuously covered chunk.");
        TestAssert.True(repaired.EvidenceReferences.Any(reference => reference.Contains(":first:", StringComparison.Ordinal)) &&
            repaired.EvidenceReferences.Any(reference => reference.Contains(":second:", StringComparison.Ordinal)),
            "Chunk-crossing repair must retain both source timing references.");
        TestAssert.Same(candidate, GenerationCandidateNaturalEndingPolicy.Adjust(candidate, [], TimeSpan.FromSeconds(45),
            transcript with { Manifests = [transcript.Manifests[0]] }).Candidate,
            "A missing continuation manifest leaves the existing fallback intact instead of asserting verified chunk coverage.");
        return Task.CompletedTask;
    }

    private static Task TranscriptEndingPreservesUncertainFallback()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-uncertain.mkv", 1)]);
        MomentCandidate candidate = EndingCandidate(request, 0, 28.2, 20);
        var valid = RecordedEndingTranscript(request);
        var timingWarning = new AudioTranscriptionWarning(AudioTranscriptionWarningCode.SegmentTimingCanonicalized,
            "The source phrase clocks were repaired.");
        GenerationSourceTranscript[] uncertain =
        [valid with { Manifests = [] },
            valid with { Segments = [valid.Segments[0], SentenceSegment("partial", "source", "Yet this is unfinished", 26.97, 33.61,
                warnings: [timingWarning]), valid.Segments[2]] },
            valid with { Segments = [valid.Segments[0], valid.Segments[1], SentenceSegment("overlap", "source", "It continues.", 32, 39.95)] },
            valid with { Segments = [SentenceSegment("partial", "source", "An unfinished thought...", 26, 30)] }];
        var intervals = SentenceSpeech(request, [(26.978, 28.894), (29.154, 30.622)]).Sources.Single().Streams.Single().Intervals;
        foreach (GenerationSourceTranscript transcript in uncertain)
        {
            var result = GenerationCandidateNaturalEndingPolicy.Adjust(candidate, intervals, TimeSpan.FromSeconds(45), transcript);
            TestAssert.Equal(TimeSpan.FromMilliseconds(31372), result.Candidate.Window.End,
                "Missing coverage, repaired phrase clocks, overlaps, or unconfirmed terminal punctuation retain VAD behavior.");
            TestAssert.False(result.EvidenceReferences.Any(reference => reference.StartsWith("transcript:", StringComparison.Ordinal)),
                "An uncertain ending cannot be presented as a measured complete sentence.");
        }
        return Task.CompletedTask;
    }

    private static Task TranscriptEndingPreservesDurationEventsAndBeginning()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-bounds.mkv", 1)]);
        var transcript = RecordedEndingTranscript(request);
        MomentCandidate candidate = EndingCandidate(request, 0, 28.2, 20);
        var shortened = GenerationCandidateNaturalEndingPolicy.Adjust(candidate, [], TimeSpan.FromSeconds(30),
            transcript, TimeSpan.FromSeconds(12));
        TestAssert.Equal(TimeSpan.FromSeconds(26.97), shortened.Candidate.Window.End,
            "When the complete next sentence cannot fit, the prior full sentence is safe only before the new utterance begins.");
        TestAssert.Equal(TimeSpan.Zero, shortened.Candidate.Window.Start, "Shortening cannot trade away the existing beginning.");
        TestAssert.True(GenerationCandidateNaturalEndingPolicy.Adjust(candidate, [], TimeSpan.FromSeconds(30),
            transcript, TimeSpan.FromSeconds(28)).RequiresAutomaticRejection,
            "A previous sentence shorter than the configured minimum is not an admissible edit.");
        MomentCandidate lateAnchor = EndingCandidate(request, 0, 31.372, 29);
        TestAssert.True(GenerationCandidateNaturalEndingPolicy.Adjust(lateAnchor, [], TimeSpan.FromSeconds(35),
            transcript, TimeSpan.FromSeconds(12)).RequiresAutomaticRejection,
            "The policy must not discard the retained 29-second event or move the beginning to squeeze in the next sentence.");
        var crossing = SentenceSpeech(request, [(26, 28)]).Sources.Single().Streams.Single().Intervals;
        TestAssert.True(GenerationCandidateNaturalEndingPolicy.Adjust(candidate, crossing, TimeSpan.FromSeconds(30),
            transcript, TimeSpan.FromSeconds(12)).RequiresAutomaticRejection,
            "A textual boundary crossed by measured speech cannot invent silence inside that utterance.");
        foreach (double episodeEnd in new[] { 27.5, 28.2, 35 })
        {
            var episode = new MomentEventEpisode("retained-payoff", TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(episodeEnd), .8, 1, .8, null, null,
                new MomentEpisodeEvidenceSummary([MomentSignalFamily.SourceCoverage], [], [candidate.Anchors.Single().Id], []),
                [new(MomentEventEpisodePhaseKind.Core, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(episodeEnd), true)],
                "A retained event continues after the candidate's only anchor.");
            var episodeCandidate = new MomentCandidate(candidate.Id, candidate.Window, candidate.ConstructionReason,
                candidate.EventNeighborhood, candidate.Anchors, candidate.Score, candidate.Disposition, 0, 0, 0, 0, episode: episode);
            TestAssert.True(GenerationCandidateNaturalEndingPolicy.Adjust(episodeCandidate, [], TimeSpan.FromSeconds(30),
                transcript, TimeSpan.FromSeconds(12)).RequiresAutomaticRejection,
                "A preceding sentence cannot discard an episode tail before, exactly at, or beyond the existing cut end.");
        }
        return Task.CompletedTask;
    }

    private static Task TranscriptEndingPreservesQualityRolesAndGuidance()
    {
        string path = TestMediaFactory.CreateSourcePath("sentence-ending-gates.mkv");
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-gates.mkv", 1)],
            desiredCount: 1, maximumClipDuration: TimeSpan.FromSeconds(30));
        MomentCandidate candidate = EndingCandidate(request, 0, 30, 29);
        var transcript = RecordedEndingTranscript(request);
        var moments = EndingMoments(request, candidate);
        var speech = SentenceSpeech(request, []);
        var service = new GenerationCandidateRefinementService();
        var unsafeResult = service.Refine(moments, speech, new GenerationTranscriptAnalysisResult(moments, [transcript]));
        TestAssert.True(unsafeResult.Refinements.Single().HasIncompleteSpeechEnding,
            "A sentence extension beyond the maximum that would lose a retained event must use the existing typed rejection.");
        TestAssert.Equal(0, unsafeResult.RefinedMoments.SelectedCandidates.Count,
            "A saturated score and requested count cannot waive the transcript ending gate.");
        MomentCandidate completeOpening = EndingCandidate(request, 0, 30, 10);
        var separatedText = SentenceTranscript(request,
            [SentenceSegment("opening", "source", "The door is open.", .27, 1.5),
                SentenceSegment("later", "source", "We can go through now.", 24, 29)], [("source", 0, 60)]);
        var separatedMoments = EndingMoments(request, completeOpening);
        var conflicted = service.Refine(separatedMoments, SentenceSpeech(request, [(24, 32)]),
            new GenerationTranscriptAnalysisResult(separatedMoments, [separatedText]));
        TestAssert.True(conflicted.Refinements.Single().HasIncompleteSpeechEnding &&
            conflicted.Refinements.Single().Candidate.Window.Start == TimeSpan.Zero,
            "A reliable sentence/VAD conflict followed by an earlier transcript gap cannot fall through to VAD and discard the complete opening.");
        TestAssert.Equal(0, conflicted.RefinedMoments.SelectedCandidates.Count,
            "A known boundary conflict remains ineligible rather than shifting to 2.75 seconds and losing the first sentence.");
        var unknown = service.Refine(moments, CreateSpeech(request, AudioContentRoleAssignment.Unknown, []),
            new GenerationTranscriptAnalysisResult(moments, [transcript]));
        TestAssert.Same(candidate, unknown.Refinements.Single().Candidate,
            "An unclassified audio stream must not be claimed as a creator sentence.");

        GenerationRequest guided = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-gates.mkv", 1)],
            desiredCount: 1, maximumClipDuration: TimeSpan.FromSeconds(30), momentGuidance: new([
                UserMomentGuidance.CreatePoint(path, request.ReferencePreparedSource.Media.Duration, TimeSpan.FromSeconds(29))]));
        var guidedMoments = EndingMoments(guided, candidate);
        var guidedResult = service.Refine(guidedMoments, SentenceSpeech(guided, []),
            new GenerationTranscriptAnalysisResult(guidedMoments, [transcript]));
        TestAssert.Same(candidate, guidedResult.Refinements.Single().Candidate,
            "Explicit user-prioritized windows retain their chosen boundaries.");

        GenerationRequest lowRequest = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-ending-gates.mkv", 1)],
            desiredCount: 1, maximumClipDuration: TimeSpan.FromSeconds(45));
        MomentCandidate low = EndingCandidate(lowRequest, 0, 28.2, 20, score: 0);
        var lowMoments = EndingMoments(lowRequest, low);
        var lowResult = service.Refine(lowMoments, SentenceSpeech(lowRequest, []),
            new GenerationTranscriptAnalysisResult(lowMoments, [transcript]));
        TestAssert.True(lowResult.Refinements.Single().FinalScore < lowRequest.SetupOptions.QualityThreshold,
            "Completing a sentence does not add quality or prove an event occurred.");
        TestAssert.Equal(GenerationCandidateSelectionReason.CountFillBelowQualityTarget,
            lowResult.RefinedMoments.SelectedCandidates.Single().SelectionReason,
            "The existing count-fill policy may retain a safe repaired sentence, but must not present it as quality-qualified.");
        return Task.CompletedTask;
    }

    private static GenerationSourceTranscript RecordedEndingTranscript(GenerationRequest request) => SentenceTranscript(request,
        [SentenceSegment("explanation", "source", "Everything else is always bigger than the characters you play.", .27, 26.97),
            SentenceSegment("partial", "source", "Yet, they're just big enough to make", 26.97, 33.61,
                warnings: [new(AudioTranscriptionWarningCode.WordTimingCanonicalized, "Recorded fixture includes repaired word times; use complete phrase clocks.")]),
            SentenceSegment("continuation", "source", "you feel like they're used to be normal people in this world.", 33.61, 39.95),
            SentenceSegment("next", "source", "So clearly, we're going to turn this off here and turn it off.", 39.95, 46.29)],
        [("source", 0, request.ReferencePreparedSource.Media.Duration.TotalSeconds)]);

    private static MomentCandidate EndingCandidate(GenerationRequest request, double start, double end, double anchorTime, double score = 100)
    {
        MomentCandidate original = CreateMoments(request, [score]).Sources.Single().Moments.Proposals.Single();
        var window = new MomentCandidateWindow(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), request.ReferencePreparedSource.Media.Duration);
        var evidence = new MomentEvidenceReference(MomentEvidenceReferenceKind.SourceCoverage, window.Start, window.End, "Retained event fixture.");
        var anchor = new MomentAnchor("ending-anchor", MomentAnchorKind.SourceCoverage, TimeSpan.FromSeconds(anchorTime), 0, 0, [evidence]);
        var neighborhood = new MomentEventNeighborhood("ending-neighborhood", window.Start, anchor.Timestamp, window.End, [anchor], [MomentSignalFamily.SourceCoverage]);
        return new(original.Id, window, original.ConstructionReason, neighborhood, [anchor], original.Score, original.Disposition, 0, 0, 0, 0);
    }

    private static GenerationMomentFindingResult EndingMoments(GenerationRequest request, MomentCandidate candidate)
    {
        var original = CreateMoments(request, [candidate.Score.RawComponentTotal]);
        var source = original.Sources.Single();
        var media = new MediaMomentFindingResult(source.Moments.Request, [candidate],
            candidate.Disposition == MomentCandidateDisposition.Selected ? [candidate] : [], source.Moments.Warnings, source.Moments.Manifest);
        var replacement = new GenerationSourceMomentResult(source.AnalyzedSource, media);
        return new(original.Request, [replacement], new GenerationMomentPortfolioSelector().Select(original.Request, [replacement]));
    }
}
