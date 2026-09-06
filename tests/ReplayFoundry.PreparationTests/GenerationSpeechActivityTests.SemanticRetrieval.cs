using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Intelligence;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static Task SemanticQueryIsSeparateAndOptIn()
    {
        TestAssert.False(GenerationDiscoveryIntent.Default.UsesSemanticRetrieval, "Default generation must never request the optional model.");
        TestAssert.False(new GenerationDiscoveryIntent(GenerationMomentIntent.Action, "final boss").UsesSemanticRetrieval,
            "Existing objective and literal phrase behavior must not start semantic inference.");
        var intent = new GenerationDiscoveryIntent(GenerationMomentIntent.Clutch, "final boss", "recover against the odds");
        TestAssert.Equal("recover against the odds", intent.SemanticQuery, "An explicit query must remain independent from the objective's default retrieval description.");
        TestAssert.Equal(0, intent.CountMatches("We recover against the odds"), "A semantic query must never become an asserted exact spoken phrase.");
        TestAssert.Equal(1, intent.CountMatches("The final boss arrived"), "Literal words retain their independent matching contract.");
        TestAssert.Equal(6, (int)GenerationMomentIntent.Dialogue, "Existing persisted objective ordinals must remain stable.");
        TestAssert.Throws<ArgumentException>(() => new GenerationDiscoveryIntent(naturalLanguageQuery: new string('a', 241)),
            "Semantic requests must stay bounded before any model download.");
        TestAssert.False(GenerationCaptureContextPolicy.MayScreen(new(naturalLanguageQuery: "explain these graphics settings"), ContentEmphasis.Balanced),
            "Explicit creator queries preserve the established manual/commentary protection.");
        foreach (var objective in new[] { GenerationMomentIntent.Clutch, GenerationMomentIntent.Tutorial, GenerationMomentIntent.Reaction })
            TestAssert.True(new GenerationDiscoveryIntent(objective).UsesSemanticRetrieval,
                "New objectives must have an actual independent retrieval path, not an intensity label.");
        return Task.CompletedTask;
    }

    private static Task MiniLmTokenizationUsesBoundedWordPieces()
    {
        string[] vocabulary = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "hello", "world", ",", "!", "play", "##ing", "cafe", "中"];
        var tokenizer = new BertWordPieceTokenizer(vocabulary);
        TestAssert.True(tokenizer.Encode("Héllo, WORLD! playing café 中").SequenceEqual(new long[] { 2, 4, 6, 5, 7, 8, 9, 10, 11, 3 }),
            "BERT basic tokenization must lowercase, strip accents, isolate punctuation/Chinese, and use greedy continuation pieces.");
        TestAssert.True(tokenizer.Encode("unknown").SequenceEqual(new long[] { 2, 1, 3 }),
            "Unsegmentable words must produce exactly one unknown token.");
        long[] longInput = tokenizer.Encode(string.Join(' ', Enumerable.Repeat("hello", 500)));
        TestAssert.Equal(256, longInput.Length, "Long input must respect the model's 256-token admission policy.");
        TestAssert.Equal(3L, longInput[^1], "Truncation must preserve the terminal SEP token.");
        return Task.CompletedTask;
    }

    private static Task SemanticWindowsPreserveLanguageClocksAndCoverage()
    {
        var first = new GenerationSourceTranscript("first.mkv", 1,
            Enumerable.Range(0, 400).Select(index => SentenceSegment("a" + index, "first",
                "The creator explains this game mechanic.", index * 40, index * 40 + 3)).ToArray(), [],
            [new(TimeSpan.Zero, TimeSpan.FromHours(5), "en", false, null)]);
        var second = first with { SourceFullPath = "second.mkv",
            Segments = first.Segments.Select(segment => SentenceSegment("b" + segment.Id, "second",
                segment.Text, segment.AbsoluteSourceStart.TotalSeconds, segment.AbsoluteSourceEnd.TotalSeconds)).ToArray() };
        var unknown = first with { SourceFullPath = "unknown.mkv", LanguageSpans = [] };
        var nonEnglish = first with { SourceFullPath = "other.mkv",
            LanguageSpans = [new(TimeSpan.Zero, TimeSpan.FromHours(5), "es", false, "en")] };
        var windows = GenerationSemanticRetrieval.CreateWindows([first, second, unknown, nonEnglish], CancellationToken.None);
        TestAssert.Equal(256, windows.Count, "Window admission must be capped request-wide, not per recording.");
        TestAssert.Equal(128, windows.Count(static window => window.SourceFullPath == "first.mkv"), "Dense sources must share the budget.");
        TestAssert.True(windows.All(static window => window.SourceFullPath is "first.mkv" or "second.mkv"),
            "Detected non-English and unknown language must not silently become English.");
        TestAssert.True(windows.Max(static window => window.Start) > TimeSpan.FromHours(4),
            "The bounded scan must sample later source passages, not only the opening.");
        TestAssert.True(windows.All(static window => window.End - window.Start == TimeSpan.FromSeconds(3) &&
            window.Text.Length <= 240 && window.SegmentIds.Count == 1), "Retrieval must retain actual phrase clocks and complete bounded text.");
        TestAssert.True(new GenerationTranscriptLanguageSpan(TimeSpan.Zero, TimeSpan.FromSeconds(10), "es", true, "es").IsEnglish,
            "Only explicitly requested English translation can qualify translated text.");
        TestAssert.Throws<OperationCanceledException>(() =>
            GenerationSemanticRetrieval.CreateWindows([first], new CancellationToken(true)),
            "Source window planning must observe cancellation.");
        return Task.CompletedTask;
    }

    private static async Task SemanticNominationsNeedGroundedReview()
    {
        var intent = new GenerationDiscoveryIntent(naturalLanguageQuery: "explain how to unlock the entrance");
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("semantic-source.mkv", 1)],
            sourceDuration: TimeSpan.FromMinutes(20), desiredCount: 2, discoveryIntent: intent);
        GenerationMomentFindingResult original = CreateMoments(request, [90]);
        var source = new GenerationSourceTranscript(request.ReferenceSource.FullPath, 1,
            [SentenceSegment("instruction", "source", "Use the brass key to open this locked doorway.", 1020, 1025),
                SentenceSegment("unrelated", "source", "I adjusted my microphone volume today.", 600, 604)], [],
            [new(TimeSpan.Zero, TimeSpan.FromMinutes(20), "en", false, null)]);
        var embeddings = new SemanticFixtureEmbeddingService();
        var retrieval = await GenerationSemanticRetrieval.SearchAsync(original, [source], embeddings, null, CancellationToken.None);
        TestAssert.Equal(intent.NaturalLanguageQuery, embeddings.Inputs![0],
            "Only the separate query input may contain the preference; retained spoken text must stay unchanged.");
        TestAssert.Equal("instruction", retrieval.Matches[0].Window.SegmentIds[0],
            "Injected semantic relevance must order the actual timed passages; this unit fixture is not model qualification.");
        var expanded = GenerationTranscriptCandidatePlanner.Expand(original, [source], CancellationToken.None, retrieval);
        var intelligence = new GenerationCandidateRefinementService().Refine(expanded,
            CreateSpeech(request, AudioContentRoleAssignment.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(1)))
            .WithTranscripts(new(expanded, [source], retrieval));
        var alternatives = intelligence.Refinements.Where(value =>
            value.Candidate.Window.Start <= TimeSpan.FromSeconds(1020) &&
            value.Candidate.Window.End >= TimeSpan.FromSeconds(1025)).ToArray();
        var nomination = alternatives.Single(value =>
            value.Candidate.Window.Duration == original.Sources.Single().Moments.Request.Options.MinimumDuration);
        TestAssert.True(alternatives.Length > 1,
            "The minimal semantic edit and the longer spoken-passage edit may both cover this passage without being identical cuts.");
        foreach (var alternative in alternatives)
        {
            TestAssert.Equal(0d, alternative.Candidate.Score.RawComponentTotal, "Retrieval must add no heuristic event score.");
            TestAssert.Equal(0d, alternative.Components.Single(static component =>
                component.Code == GenerationCandidateRefinementComponentCode.SemanticRetrievalRelevance).SignedContribution,
                "Text similarity only controls review admission, never automatic selection score.");
            TestAssert.True(alternative.RequiresSemanticReview, "A semantically relevant phrase alone cannot satisfy grounded Keep.");
            TestAssert.False(intelligence.RefinedMoments.SelectedCandidates.Any(value => ReferenceEquals(value.Candidate, alternative.Candidate)),
                "Count filling must not promote either unreviewed alternative.");
        }
        var shortlist = GenerationVisualSemanticAnalysisService.CreateShortlist(intelligence, 3);
        TestAssert.True(shortlist.Count <= 3 && shortlist.Any(value => ReferenceEquals(value.Candidate, nomination.Candidate)),
            "Query relevance must reserve admission within the existing visual budget.");
        TestAssert.Equal("Use the brass key to open this locked doorway.", source.Segments[0].Text,
            "The query must never be injected into observed speech or the qualified Qwen protocol.");
        var reaction = source with { Segments = [SentenceSegment("reaction", "source", "Unbelievable!", 1100, 1102)] };
        var shortRetrieval = new GenerationSemanticRetrievalResult(
            [new(new(request.ReferenceSource.FullPath, 1, TimeSpan.FromSeconds(1100), TimeSpan.FromSeconds(1102),
                "Unbelievable!", ["reaction"]), .7)], "fixture", null, null, TimeSpan.Zero, 0, "fixture");
        var shortExpanded = GenerationTranscriptCandidatePlanner.Expand(original, [reaction], CancellationToken.None, shortRetrieval);
        TestAssert.True(shortExpanded.Sources.Single().Moments.Proposals.Any(candidate =>
            candidate.ConstructionReason == MomentCandidateConstructionReason.SemanticExploration &&
            candidate.Window.Contains(TimeSpan.FromSeconds(1101))),
            "Semantic reaction nominations must not be blocked by the legacy twenty-letter transcript proposal heuristic.");
    }

    private static async Task SemanticSourceAnalysisIsInactiveByDefault()
    {
        GenerationRequest request = CreateRequest(GenerationAnalysisDepth.Thorough, [("semantic-default.mkv", 1)]);
        var original = CreateMoments(request, [90]);
        var options = SentenceManifest(request, "source", 0, 60).Options;
        var embeddings = new SemanticFixtureEmbeddingService();
        var service = new GenerationTranscriptAnalysisService(new SemanticFixtureTranscriptionService(request),
            options, _ => options, embeddings);
        var result = await service.AnalyzeAsync(original,
            CreateSpeech(request, AudioContentRoleAssignment.Unknown, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)),
            null, CancellationToken.None);
        TestAssert.True(embeddings.Inputs is null && result.SemanticRetrieval is null,
            "Ordinary Thorough discovery must not download or execute the optional semantic model.");
        TestAssert.True(result.Sources.Single().LanguageSpans!.Single().IsEnglish,
            "Root ASR language detection must survive source transcript retention, even without per-segment language tags.");
    }

    private sealed class SemanticFixtureEmbeddingService : ISemanticTextEmbeddingService
    {
        public IReadOnlyList<string>? Inputs { get; private set; }
        public Task<SemanticTextEmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs = texts.ToArray();
            return Task.FromResult(new SemanticTextEmbeddingResult(texts.Select((text, index) =>
                index == 0 || text.Contains("brass key", StringComparison.Ordinal) ? new float[] { 1, 0 } : [0, 1]).ToArray(),
                "fixture-only", new string('A', 64), new string('B', 64), TimeSpan.Zero, 0));
        }
    }

    private sealed class SemanticFixtureTranscriptionService(GenerationRequest request) : ISourceAudioTranscriptionService
    {
        public Task<AudioTranscriptionResult> TranscribeWindowAsync(string neighborhoodId, MediaProbeResult source, int audioStreamIndex,
            TimeSpan start, TimeSpan end, AudioTranscriptionOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AudioTranscriptionResult(neighborhoodId, audioStreamIndex,
                [SentenceSegment("default-speech", neighborhoodId, "Here is a complete spoken passage.", start.TotalSeconds + 1, start.TotalSeconds + 3, start.TotalSeconds)],
                SentenceManifest(request, neighborhoodId, start.TotalSeconds, (end - start).TotalSeconds),
                new AudioTranscriptionLanguage("en")));
        }
    }
}
