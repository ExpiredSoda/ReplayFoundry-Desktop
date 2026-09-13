using System.Text.Json;
using System.Text.Json.Nodes;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationSpeechActivityTests
{
    private static IEnumerable<TestCase> TimedMomentEvidenceTests()
    {
        yield return new("Moment audio retains separate tracks and confirms only the assigned speaker route", SeparateMomentTracks);
        yield return new("Timed moment review replaces coarse overlapping category labels and supports explicit subtypes", TimedCategoriesReplaceCoarseHints);
        yield return new("Moment review rejects changed speech, speaker, waveform identity and out-of-cut citations", MomentWireOwnership);
        yield return new("Moment context sends only cited speech and supported categories to title writing", MomentWriterEvidence);
        yield return new("Category review admission preserves human priority and its fixed review budget", CategoryAdmissionPreservesBudget);
        yield return new("Recording event nominations use owned source frames and never invent missing timestamps", IndexedEventOwnership);
    }

    private static Task SeparateMomentTracks()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("audio-evidence.mkv", 5)],
            ("audio-evidence.mkv", 2, CaptionAudioContentRole.GameDialogue));
        var source = CreateCandidateIntelligence(request, [80]).BaseMoments.Sources[0].AnalyzedSource.PreparedSource;
        var transcript = new GenerationSourceTranscript(source.Media.FullPath, 1,
            [SentenceSegment("mic", "source", "That explains why they betrayed the city.", 2, 4)], [],
            AdditionalTracks: [new(source.Media.FullPath, 2,
                [SentenceSegment("game", "source", "The commander opened the gates to the invaders.", 5, 8)], [])]);
        var context = GenerationSceneReviewContextBuilder.Build(request.SetupOptions, source.FileSnapshot,
            [1, 2, 3, 4, 5], TimeSpan.Zero, TimeSpan.FromSeconds(20), transcript, [TimeSpan.FromSeconds(8)]);
        TestAssert.Equal(4, context.AudioTracks.Count, "Review work is bounded to four separate tracks.");
        TestAssert.Equal(2, context.AudioTracks[0].StreamIndex, "The explicitly selected track must not fall outside the budget.");
        TestAssert.Equal(AudioContentRole.GameDialogue, context.AudioTracks[0].Role.Role, "Game dialogue retains its assigned role.");
        TestAssert.Equal(AudioContentRoleSource.UserConfirmed, context.AudioTracks[0].Role.Source, "The explicit assignment retains its provenance.");
        TestAssert.Equal(AudioContentRole.Unknown, context.AudioTracks.Single(track => track.StreamIndex == 1).Role.Role,
            "The other voice cannot become creator speech just because a game track exists.");
        TestAssert.Equal(2, transcript.AllSegments.Count(), "Both conversations remain available to discovery.");
        TestAssert.Equal(2, context.AudioTracks.Sum(track => track.Speech.Count), "Track-specific speech reaches close review.");
        var mutableTracks = context.AudioTracks.ToArray();
        var mutable = context with { AudioTracks = mutableTracks };
        var snapshot = mutable.Snapshot(TimeSpan.FromSeconds(20));
        mutableTracks[0] = mutableTracks[0] with { Role = AudioContentRoleAssignment.Unknown };
        TestAssert.Equal(AudioContentRole.GameDialogue, snapshot.AudioTracks[0].Role.Role, "Async review cannot inherit later UI changes to track roles.");
        TestAssert.Throws<ArgumentException>(() => (context with { SourceEnd = TimeSpan.FromSeconds(30) }).Snapshot(TimeSpan.FromSeconds(20)),
            "Audio from another duration cannot attach to this cut.");
        return Task.CompletedTask;
    }

    private static Task TimedCategoriesReplaceCoarseHints()
    {
        var request = CreateRequest(GenerationAnalysisDepth.Thorough, [("typed-evidence.mkv", 1)]);
        var intelligence = CreateCandidateIntelligence(request, [80]); var candidate = intelligence.Refinements[0].Candidate;
        var old = Reviewed(intelligence, candidate, TypedKeep(VisualSemanticObservableContentType.Action));
        var timed = new SceneMomentEvidence(Enum.GetValues<SceneMomentCategory>().Select(category => new SceneCategoryEvidence(category,
            category is SceneMomentCategory.Lore or SceneMomentCategory.Clutch ? SceneEvidenceVerdict.Supported : SceneEvidenceVerdict.Uncertain,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.Zero, TimeSpan.FromSeconds(6),
            "A bounded source observation.", ["frame-1"])), "NoAudio", "{\"tracks\":[]}", candidate.Window.Duration);
        var reviewed = new GenerationVisualSemanticCandidateObservation(candidate, old.Source, old.ReviewedSourceStart, old.ReviewedSourceEnd,
            old.ReviewVideoSha256, old.Observation, old.CanonicalizationAudit, old.Elapsed, old.NeuralEditorialValue, timed);
        var coarse = new GenerationCandidateRefinement(candidate,
            [new(GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, 1, 0, "Recording map"),
             new(GenerationCandidateRefinementComponentCode.NeuralHumor, 1, 0, "Nearby laughter"),
             new(GenerationCandidateRefinementComponentCode.NeuralGameplay, 1, 0, "Nearby combat")], "test");
        var profile = GenerationMomentContentClassifier.Classify(candidate, [], reviewed, coarse);
        TestAssert.True(profile.Lore && !profile.Funny && !profile.Gameplay,
            "An uncertain category in this cut cannot inherit a positive label from its wider recording window.");
        TestAssert.Equal(1d, GenerationDiscoveryIntentPolicy.SemanticMatch(new(GenerationMomentIntent.Clutch), reviewed).RawValue,
            "A grounded, timed clutch can satisfy the explicit request.");
        TestAssert.Equal(0d, GenerationDiscoveryIntentPolicy.SemanticMatch(new(GenerationMomentIntent.Humor), reviewed).RawValue,
            "The coarse joke hint remains insufficient.");
        return Task.CompletedTask;
    }

    private static async Task WithMomentRequest(Func<VisualSemanticRequest, Task> check)
    {
        var setup = CreateRequest(GenerationAnalysisDepth.Thorough, [("moment-wire.mkv", 2)],
            ("moment-wire.mkv", 1, CaptionAudioContentRole.CreatorCommentary));
        var intelligence = CreateCandidateIntelligence(setup, [80]);
        double offset = intelligence.Refinements[0].Candidate.Window.Start.TotalSeconds;
        var transcript = new GenerationSourceTranscript(setup.ReferenceSource.FullPath, 1,
            [SentenceSegment("mic", "source", "The betrayal explains the missing defenders.", offset+2, offset+4)], [],
            AdditionalTracks: [new(setup.ReferenceSource.FullPath, 2,
                [SentenceSegment("game", "source", "The commander opened the gates.", offset+5, offset+8)], [])]);
        intelligence = intelligence.WithTranscripts(new(intelligence.BaseMoments, [transcript]));
        using var materializer = new FakeVisualReviewMaterializer(); var provider = new FakeVisualEditorialProvider();
        using var result = await new GenerationVisualSemanticAnalysisService(provider, materializer, CreateVisualSettings())
            .AnalyzeAsync(intelligence, null, CancellationToken.None);
        await check(provider.Requests[0].Requests[0]);
    }

    private static JsonObject MomentWire(VisualSemanticRequest request)
    {
        double duration = request.CandidateEndRelative.TotalSeconds;
        return JsonSerializer.SerializeToNode(new
        {
            frameTimes = Enumerable.Range(0, 12).Select(index => (duration-.1)*index/11),
            audioEvidence = new { version = "audio-evidence-1", status = "AcousticOnly", similaritiesAreProbabilities = false,
                tracks = request.SceneContext!.AudioTracks.Select(track => new
                {
                    streamIndex = track.StreamIndex, role = track.Role.Role.ToString(), roleSource = track.Role.Source.ToString(),
                    audioSha256 = new string('a',64), windows = Array.Empty<object>(),
                    speech = track.Speech.Select(span => new { id = $"speech-{track.StreamIndex}-{span.Id}",
                        start = span.ReviewRelativeStart.TotalSeconds, end = span.ReviewRelativeEnd.TotalSeconds, text = span.Text }),
                }) },
            momentEvidence = new { version = SceneMomentEvidence.Version, policyHash = QwenSceneMomentEvidenceParser.PolicyHash, grounded = true,
                categories = Enum.GetValues<SceneMomentCategory>().Select(category => new
                {
                    category = category.ToString(), verdict = category == SceneMomentCategory.Commentary ? "Supported" : "Uncertain",
                    start = 2, end = 4, setupStart = 2, payoffEnd = 4, explanation = "The creator explains a supported implication.",
                    evidenceIds = category == SceneMomentCategory.Commentary ? new[] { "speech-1-mic" } : [],
                }) },
        })!.AsObject();
    }

    private static Task MomentWireOwnership() => WithMomentRequest(request =>
    {
        var original = MomentWire(request);
        TestAssert.True(QwenSceneMomentEvidenceParser.Parse(JsonSerializer.SerializeToElement(original), request)
            .Supports(SceneMomentCategory.Commentary), "Owned, explicitly routed creator speech can support commentary.");
        Action<JsonObject>[] changes =
        [
            row => row["audioEvidence"]!["tracks"]![0]!["role"] = "GameDialogue",
            row => row["audioEvidence"]!["tracks"]![0]!["speech"]![0]!["text"] = "Invented words",
            row => row["audioEvidence"]!["tracks"]![0]!["speech"]![0]!["end"] = 1000,
            row => row["audioEvidence"]!["tracks"]!.AsArray().RemoveAt(1),
            row => row["momentEvidence"]!["categories"]![2]!["evidenceIds"]![0] = "speech-2-game",
            row => row["momentEvidence"]!["categories"]![2]!["setupStart"] = 3,
        ];
        foreach (var change in changes)
        {
            var altered = original.DeepClone().AsObject(); change(altered);
            TestAssert.Throws<InvalidDataException>(() => QwenSceneMomentEvidenceParser.Parse(JsonSerializer.SerializeToElement(altered), request),
                "Changed or unattributable evidence must fail before selection or writing.");
        }
        var prepared = JsonSerializer.SerializeToElement(new { tracks = request.SceneContext!.AudioTracks.Select(track =>
            new { streamIndex = track.StreamIndex, sha256 = new string('b',64) }) });
        TestAssert.Throws<InvalidDataException>(() => QwenSceneMomentEvidenceParser.Parse(JsonSerializer.SerializeToElement(original), request, prepared),
            "Cached or returned audio must match the actual prepared waveform.");
        return Task.CompletedTask;
    });

    private static Task MomentWriterEvidence() => WithMomentRequest(request =>
    {
        var evidence = QwenSceneMomentEvidenceParser.Parse(JsonSerializer.SerializeToElement(MomentWire(request)), request);
        var context = GenerationSceneEditorialEvidence.Create(evidence)!;
        using var json = JsonDocument.Parse(context.Description);
        TestAssert.Equal(1, json.RootElement.GetProperty("categories").GetArrayLength(), "Uncertain categories cannot seed a title claim.");
        var speech = json.RootElement.GetProperty("speech");
        TestAssert.Equal(1, speech.GetArrayLength(), "Uncited dialogue cannot be borrowed to fill a title.");
        TestAssert.Equal("CreatorSpeech", speech[0].GetProperty("role").GetString(), "Writer input preserves speaker routing.");
        TestAssert.Equal("UserConfirmed", speech[0].GetProperty("roleSource").GetString(), "Writer input preserves the source of attribution.");
        return Task.CompletedTask;
    });

    private static Task CategoryAdmissionPreservesBudget()
    {
        string[] ordered = ["human", "action-a", "action-b", "action-c", "quiet-lore", "funny"];
        IReadOnlyList<GenerationCandidateRefinementComponent> Evidence(string id) =>
            [new(GenerationCandidateRefinementComponentCode.NeuralIndexCoverage, 1, 0, "Mapped"),
             new(id == "quiet-lore" ? GenerationCandidateRefinementComponentCode.NeuralLore :
                 id == "funny" ? GenerationCandidateRefinementComponentCode.NeuralHumor : GenerationCandidateRefinementComponentCode.NeuralGameplay,
                 .8, 0, "Review nomination")];
        var selected = GenerationCategoryReviewAdmission.Select(ordered, 4, GenerationMomentIntent.Story, id => id == "human", Evidence);
        TestAssert.Equal(4, selected.Count, "Category coverage cannot enlarge the configured review budget.");
        TestAssert.True(selected.Contains("human") && selected.Contains("quiet-lore"), "A quiet lore candidate gets reviewed without displacing a manual selection.");
        TestAssert.False(selected.Contains("funny"), "An unrelated category does not acquire an output quota.");
        return Task.CompletedTask;
    }

    private static Task IndexedEventOwnership()
    {
        JsonElement Window(int first, int last) => JsonSerializer.SerializeToElement(new
            { start = 90, end = 112, prediction = new { eventStartFrame = first, eventEndFrame = last, editorialValue = 80 } });
        var seed = GenerationIndexedEventNominations.Create([Window(2,4)]).Single();
        TestAssert.Equal(TimeSpan.FromSeconds(100), seed.Start, "The event owns its original recording clock.");
        TestAssert.Equal(TimeSpan.FromSeconds(112), seed.End, "A last partial sample cannot run beyond its source window.");
        TestAssert.Equal(0, GenerationIndexedEventNominations.Create([Window(-1,-1),Window(1,5),Window(3,1)]).Count,
            "Missing, foreign, or reversed event ownership cannot nominate an invented cut.");
        return Task.CompletedTask;
    }
}
