using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task TranscriptBeginningBridgesSentencePause()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("sentence-pause.mkv", 1)], desiredCount: 1);
        GenerationMomentFindingResult moments = CreateMoments(request, [90]);
        GenerationSourceTranscript transcript = SentenceTranscript(request,
            [SentenceSegment("prior", "chunk", "That failed.", 2, 3),
                SentenceSegment("opening", "chunk", "If we wait here", 5, 8),
                SentenceSegment("ending", "chunk", "the guard will move.", 10.5, 14)],
            [("chunk", 0, request.ReferencePreparedSource.Media.Duration.TotalSeconds)]);
        GenerationSpeechActivityResult speech = SentenceSpeech(request);
        IGenerationCandidateRefinementService service = new GenerationCandidateRefinementService();
        GenerationCandidateIntelligenceResult vadOnly = service.Refine(moments, speech);
        TestAssert.Equal(TimeSpan.FromSeconds(10), vadOnly.Refinements.Single().Candidate.Window.Start,
            "The fixture's pause must fall outside VAD's one-second continuation rule.");
        GenerationCandidateIntelligenceResult result = service.Refine(moments, speech,
            new GenerationTranscriptAnalysisResult(moments, [transcript]));
        GenerationCandidateRefinement refined = result.Refinements.Single();
        TestAssert.Equal(TimeSpan.FromSeconds(4.25), refined.Candidate.Window.Start,
            "Timed sentence context must retain the opening before the within-sentence pause.");
        TestAssert.Equal(TimeSpan.FromSeconds(40), refined.Candidate.Window.End,
            "Sentence repair must preserve a safe existing ending when duration permits.");
        TestAssert.True(result.Transcripts is not null && refined.Components.Any(component =>
                component.EvidenceReferences.Any(reference => reference.StartsWith("transcript:sentence-start:", StringComparison.Ordinal))),
            "The real interface path must retain transcript provenance and explain its boundary adjustment.");
        TestAssert.False(refined.HasIncompleteSpeechBeginning, "A successfully repaired beginning remains automatically eligible.");
        return Task.CompletedTask;
    }

    private static Task TranscriptBeginningUsesMeasuredWords()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-words.mkv", 1)]);
        MomentCandidate candidate = CreateMoments(request, [90]).Sources.Single().Moments.Proposals.Single();
        (string Text, double Start, double End)[] words =
        [ ("That", 2, 2.4), ("failed.", 2.5, 3), ("If", 5, 5.3), ("we", 5.4, 5.7),
            ("wait", 5.8, 6.3), ("here", 7, 8), ("the", 10.5, 10.8), ("guard", 11, 11.5),
            ("will", 12, 12.5), ("move.", 13, 14) ];
        var segment = new AudioTranscriptionSegment("measured", "chunk", string.Join(' ', words.Select(static word => word.Text)),
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(14),
            words.Select(static word => new AudioTranscriptionWord(word.Text, TimeSpan.FromSeconds(word.Start),
                TimeSpan.FromSeconds(word.End), TimeSpan.FromSeconds(word.Start), TimeSpan.FromSeconds(word.End))));
        GenerationSourceTranscript transcript = SentenceTranscript(request, [segment], [("chunk", 0, 60)]);
        GenerationCandidateNaturalEndingAdjustment result = GenerationCandidateNaturalBeginningPolicy.Adjust(
            candidate, [], TimeSpan.FromSeconds(60), transcript);
        TestAssert.Equal(TimeSpan.FromSeconds(4.25), result.Candidate.Window.Start,
            "Measured word boundaries must start at If, after the previous sentence, instead of at the phrase's first word.");
        TestAssert.True(result.EvidenceReferences.Any(reference => reference.Contains("00:00:05", StringComparison.Ordinal)),
            "The repaired sentence must cite an actual word timestamp, not an interpolated position.");
        return Task.CompletedTask;
    }

    private static Task TranscriptBeginningCrossesContinuousChunks()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough,
            [("sentence-chunks.mkv", 1)], sourceDuration: TimeSpan.FromSeconds(300));
        MomentCandidate candidate = CreateMoments(request, [90, 80], 110).Sources.Single().Moments.Proposals[1];
        GenerationSourceTranscript transcript = SentenceTranscript(request,
            [SentenceSegment("prior", "first", "We missed.", 110, 112),
                SentenceSegment("opening", "first", "If we wait here.", 115, 119.8),
                SentenceSegment("ending", "second", "the guard will move.", 120.1, 124, 120)],
            [("raw-first", 0, 120), ("raw-second", 120, 120)]);
        GenerationCandidateNaturalEndingAdjustment result = GenerationCandidateNaturalBeginningPolicy.Adjust(
            candidate, [], TimeSpan.FromSeconds(60), transcript);
        TestAssert.Equal(TimeSpan.FromSeconds(114.25), result.Candidate.Window.Start,
            "A decoder's artificial chunk-edge period cannot discard a sentence opening from the preceding contiguous chunk.");
        TestAssert.True(result.EvidenceReferences.Any(reference => reference.Contains(":first:", StringComparison.Ordinal)) &&
            result.EvidenceReferences.Any(reference => reference.Contains(":second:", StringComparison.Ordinal)),
            "Chunk-crossing repair must retain both chunks' actual timed-text identities.");
        GenerationSourceTranscript missingChunk = transcript with { Manifests = [transcript.Manifests[1]] };
        TestAssert.Equal(GenerationCandidateNaturalEndingStatus.Unchanged,
            GenerationCandidateNaturalBeginningPolicy.Adjust(candidate, [], TimeSpan.FromSeconds(60), missingChunk).Status,
            "Unverified coverage across the source chunk edge must not invent a sentence boundary.");
        return Task.CompletedTask;
    }

    private static Task TranscriptBeginningFallsBackWhenUncertain()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-uncertain.mkv", 1)]);
        MomentCandidate candidate = CreateMoments(request, [90]).Sources.Single().Moments.Proposals.Single();
        AudioTranscriptionSegment[] segments =
        [ SentenceSegment("prior", "chunk", "That failed.", 2, 3),
            SentenceSegment("opening", "chunk", "If we wait here", 5, 8),
            SentenceSegment("ending", "chunk", "the guard will move.", 10.5, 14) ];
        GenerationSourceTranscript valid = SentenceTranscript(request, segments, [("chunk", 0, 60)]);
        var timingWarning = new AudioTranscriptionWarning(AudioTranscriptionWarningCode.SegmentTimingCanonicalized,
            "The decoder supplied overlapping phrase times.");
        GenerationSourceTranscript[] uncertain =
        [ valid with { Manifests = [] },
            valid with { Segments = [segments[1], segments[2]], Manifests = [SentenceManifest(request, "chunk", 4, 56)] },
            valid with { Segments = [SentenceSegment("prior", "chunk", "That failed...", 2, 3), segments[1], segments[2]],
                Manifests = [SentenceManifest(request, "chunk", 1, 59)] },
            valid with { Segments = [segments[0], SentenceSegment("opening", "chunk", "If we wait here", 5, 8,
                warnings: [timingWarning]), segments[2]] },
            valid with { Segments = [segments[0], SentenceSegment("opening", "chunk", "If we wait here", 3, 4), segments[2]] },
            valid with { Segments = [segments[0], segments[1], SentenceSegment("ending", "chunk", "the guard will move.", 7, 14)] } ];
        var speech = SentenceSpeech(request, [(9.4, 14)]).Sources.Single().Streams.Single().Intervals;
        foreach (GenerationSourceTranscript transcript in uncertain)
        {
            GenerationCandidateNaturalEndingAdjustment result = GenerationCandidateNaturalBeginningPolicy.Adjust(
                candidate, speech, TimeSpan.FromSeconds(60), transcript);
            TestAssert.Equal(TimeSpan.FromSeconds(8.65), result.Candidate.Window.Start,
                "Unknown coverage, ellipsis, repaired timestamps, or overlapping phrases must preserve the established VAD fallback.");
            TestAssert.False(result.EvidenceReferences.Any(reference => reference.StartsWith("transcript:", StringComparison.Ordinal)),
                "Uncertain punctuation or timing cannot be presented as a known sentence opening.");
        }
        return Task.CompletedTask;
    }

    private static Task TranscriptBeginningPreservesEventEndAndIntent()
    {
        string path = TestMediaFactory.CreateSourcePath("sentence-constraints.mkv");
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-constraints.mkv", 1)]);
        GenerationMomentFindingResult moments = CreateMoments(request, [90, 80], 25);
        MomentCandidate candidate = moments.Sources.Single().Moments.Proposals[0];
        GenerationSourceTranscript transcript = SentenceTranscript(request,
            [SentenceSegment("prior", "chunk", "That failed.", 2, 3),
                SentenceSegment("opening", "chunk", "If we wait here", 5, 8),
                SentenceSegment("ending", "chunk", "the guard will move.", 10.5, 14)], [("chunk", 0, 60)]);
        GenerationCandidateNaturalEndingAdjustment reclaimed = GenerationCandidateNaturalBeginningPolicy.Adjust(
            candidate, [], TimeSpan.FromSeconds(30), transcript);
        TestAssert.Equal(TimeSpan.FromSeconds(30), reclaimed.Candidate.Window.Duration,
            "Only unused post-roll may be reclaimed to honor the configured maximum duration.");
        MomentCandidate lateEvent = moments.Sources.Single().Moments.Proposals[1].WithWindow(candidate.Window);
        TestAssert.True(GenerationCandidateNaturalBeginningPolicy.Adjust(lateEvent, [], TimeSpan.FromSeconds(30), transcript)
            .RequiresAutomaticRejection, "A known opening cannot replace an event at 35 seconds with an ending at 34.25 seconds.");
        GenerationSourceTranscript spokenEnding = transcript with { Segments = [.. transcript.Segments,
            SentenceSegment("payoff", "chunk", "There it goes!", 36, 39)] };
        TestAssert.True(GenerationCandidateNaturalBeginningPolicy.Adjust(candidate, [], TimeSpan.FromSeconds(30), spokenEnding)
            .RequiresAutomaticRejection, "An existing timed ending must survive even when VAD missed that phrase.");

        GenerationRequest guided = CreateRequest(GenerationAnalysisDepth.Thorough, [("sentence-constraints.mkv", 1)],
            momentGuidance: new GenerationMomentGuidance([UserMomentGuidance.CreatePoint(path,
                request.ReferencePreparedSource.Media.Duration, TimeSpan.FromSeconds(15))]));
        GenerationMomentFindingResult guidedMoments = CreateMoments(guided, [90]);
        GenerationSourceTranscript guidedTranscript = transcript with { SourceFullPath = guided.ReferenceSource.FullPath };
        var service = new GenerationCandidateRefinementService();
        TestAssert.Equal(TimeSpan.FromSeconds(10), service.Refine(guidedMoments, SentenceSpeech(guided),
                new GenerationTranscriptAnalysisResult(guidedMoments, [guidedTranscript])).Refinements.Single().Candidate.Window.Start,
            "A user-prioritized source window must retain its explicit boundaries.");
        TestAssert.Equal(TimeSpan.FromSeconds(10), service.Refine(moments,
                CreateSpeech(request, AudioContentRoleAssignment.Unknown, [(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)),
                    (TimeSpan.FromSeconds(10.5), TimeSpan.FromSeconds(14))]), new GenerationTranscriptAnalysisResult(moments, [transcript]))
                .Refinements.First().Candidate.Window.Start,
            "Unclassified game or mixed audio cannot be claimed as a creator sentence.");
        return Task.CompletedTask;
    }

    private static GenerationSpeechActivityResult SentenceSpeech(GenerationRequest request,
        (double Start, double End)[]? intervals = null) =>
        CreateSpeech(request, new AudioContentRoleAssignment(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
            (intervals ?? [(5, 8), (10.5, 14)]).Select(static value =>
                (TimeSpan.FromSeconds(value.Start), TimeSpan.FromSeconds(value.End))).ToArray());

    private static AudioTranscriptionSegment SentenceSegment(string id, string chunk, string text,
        double start, double end, double offset = 0, IEnumerable<AudioTranscriptionWarning>? warnings = null)
    {
        TimeSpan absoluteStart = TimeSpan.FromSeconds(start);
        TimeSpan absoluteEnd = TimeSpan.FromSeconds(end);
        TimeSpan sourceOffset = TimeSpan.FromSeconds(offset);
        return new(id, chunk, text, absoluteStart - sourceOffset, absoluteEnd - sourceOffset,
            absoluteStart, absoluteEnd, warnings: warnings);
    }

    private static GenerationSourceTranscript SentenceTranscript(GenerationRequest request,
        AudioTranscriptionSegment[] segments, (string Chunk, double Start, double Duration)[] chunks) =>
        new(request.ReferenceSource.FullPath, 1, segments,
            chunks.Select(chunk => SentenceManifest(request, chunk.Chunk, chunk.Start, chunk.Duration)).ToArray());

    private static AudioTranscriptionManifest SentenceManifest(GenerationRequest request, string chunk, double start, double duration)
    {
        var execution = new InferenceExecutionManifest(new("transcript-boundary-test", "1", "1"),
            Path.Combine(Path.GetTempPath(), "sentence-test.exe"), new string('B', 64), "test",
            CreateModel(), new Dictionary<string, string>(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            TimeSpan.Zero, false);
        return new(chunk, TimeSpan.FromSeconds(duration), TimeSpan.FromSeconds(start), request.ReferencePreparedSource.Media.Duration,
            1, new AudioTranscriptionOptions(AudioTranscriptionLanguageMode.Auto, null, false, true, true,
                0, null, AudioTranscriptionProcessorHint.Cpu, TimeSpan.FromMinutes(1), AudioTranscriptionOutputFormatPolicy.StructuredJson), execution);
    }
}
