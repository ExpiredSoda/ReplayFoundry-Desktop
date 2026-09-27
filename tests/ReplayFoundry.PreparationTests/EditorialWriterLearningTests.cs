using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class EditorialWriterLearningTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Writer feedback retains explicit wording under matching facts", RetainsBoundFacts),
        new("Old structural consent cannot silently enable storing wording", OldConsentRequiresUpdatedNotice),
        new("Montage feedback preserves the rendered order without approving wording", RetainsMontageSequence),
        new("Wording feedback follows its cut and survives pending drafts", FeedbackTracksCut),
        new("Shared training requires separate consent and projects text only", SharedTrainingConsent),
    ];

    private static async Task SharedTrainingConsent()
    {
        string root = Path.Combine(Path.GetTempPath(), "ReplayFoundry-SharedTraining-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool deleted = false;
            var service = new ReplayFoundry.Desktop.Platform.Research.SharedTrainingContributionService(root, (token, json, delete, cancel) =>
            {
                TestAssert.Equal(64, token.Length, "Deletion capability is random and bounded.");
                if (delete) deleted = true; else sent.TrySetResult(json);
                return Task.CompletedTask;
            });
            var context = new ClipEditorialContext("candidate", Path.Combine(root, "private-recording.mp4"), "private name", TimeSpan.Zero,
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60), 50, "An event.");
            var captured = System.Text.Json.Nodes.JsonNode.Parse("""
                {"sourceGroup":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                 "prompt":[{"content":"writer prompt"},{"content":"{\"setupObservation\":\"A scene.\",\"sourcePath\":\"private.mp4\",\"reviewedContext\":{\"speech\":[{\"text\":\"My commentary\",\"role\":\"CreatorSpeech\",\"roleSource\":\"UserConfirmed\",\"start\":1,\"end\":2,\"filePath\":\"secret.wav\"}]}}"}],
                 "generated":{"titleBody":"A title","description":"A description","tags":["game"],"grounding":[],"temporalVoice":"RetrospectivePast"}}
                """)!.AsObject();
            captured["capturedAtUtc"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
            service.Capture(captured, context);
            TestAssert.False(sent.Task.IsCompleted, "No consent means no upload.");
            service.Enable();
            service.Capture(captured, context);
            TestAssert.False(sent.Task.IsCompleted, "Enabling sharing cannot upload historical data.");
            captured["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
            service.Capture(captured, context);
            string wire = await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            TestAssert.False(wire.Contains("private", StringComparison.Ordinal) || wire.Contains("secret.wav", StringComparison.Ordinal), "Projection excludes paths and filenames.");
            using var payload = JsonDocument.Parse(wire);
            TestAssert.False(payload.RootElement.GetProperty("humanWordingFeedback").GetBoolean(), "A generated draft is not human approval.");
            TestAssert.Equal("CreatorSpeech", payload.RootElement.GetProperty("evidence").GetProperty("speech")[0].GetProperty("role").GetString(), "Role provenance survives sharing.");
            await service.DeleteSharedAsync();
            TestAssert.True(deleted && !service.IsEnabled, "Deletion stops collection before remote deletion.");
            var reloaded = new ReplayFoundry.Desktop.Platform.Research.SharedTrainingContributionService(root, (_, _, _, _) => throw new InvalidOperationException("No send expected"));
            TestAssert.False(reloaded.IsEnabled, "Deletion cannot silently re-enable sharing after restart.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static Task FeedbackTracksCut()
    {
        bool enabled = true;
        var model = new ReplayFoundry.Desktop.Features.Studio.Editorial.StudioWordingLearningViewModel(() => enabled);
        model.Bind("clip", 0, 100);
        model.CorrectionChoice = model.CorrectionChoices.Single(choice => choice.Code == "WrongSpeaker");
        model.CorrectedEvent = "The game character spoke.";
        var draft = model.CaptureDraft("Title", "Description", "tag");
        model.Bind("clip", 100, 200);
        TestAssert.Equal("Unspecified", model.CorrectionChoice.Code, "A new cut cannot inherit an old factual correction.");
        model.Restore(draft);
        TestAssert.Equal("WrongSpeaker", model.CorrectionChoice.Code, "Restoring an unsaved draft must keep its correction reason.");
        TestAssert.Equal("The game character spoke.", model.Snapshot().CorrectedEvent, "Corrected facts survive a draft handoff.");
        var changed = new List<string>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        enabled = false;
        model.RefreshAvailability();
        TestAssert.True(changed.Contains(nameof(model.CanTeachWording)) && !model.CanTeachWording,
            "Returning from Settings refreshes learning availability without rebinding the cut.");
        TestAssert.Equal("The game character spoke.", model.CorrectedEvent, "Refreshing consent preserves the pending correction note.");
        return Task.CompletedTask;
    }

    private static Task RetainsMontageSequence()
    {
        string root = Path.Combine(Path.GetTempPath(), "ReplayFoundry-SequenceTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            bool enabled = false;
            var store = new JsonMontageLearningStore(root, () => enabled);
            string source = Path.Combine(root, "missing-recording.mp4");
            ReplayFoundry.Desktop.Features.Publish.YouTube.YouTubePublishProvenance Cut(int start, int end) => new(
                source, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), null,
                ReplayFoundry.Desktop.Features.Studio.Editing.StudioOutputCanvas.Portrait, 1080, 1920,
                ReplayFoundry.Desktop.Features.Studio.Editing.StudioCompositionLayout.Fit, false, null);
            var first = Cut(40, 50);
            var sequence = new ReplayFoundry.Desktop.Features.Publish.YouTube.YouTubePublishProvenance(
                source, first.SourceStart, first.SourceEnd, null, first.Canvas, 1080, 1920, first.CompositionLayout,
                false, null, contributingCuts: [first, Cut(10, 20)], outputDuration: TimeSpan.FromSeconds(20));
            TestAssert.False(store.Record("output", "Rendered", sequence), "Disabled consent must not retain sequence evidence.");
            enabled = true;
            TestAssert.True(store.Record("output", "Rendered", sequence), "A successful outcome can retain a sequence even if its source was removed.");
            TestAssert.False(store.Record("output", "Rendered", sequence), "Replaying the same event cannot multiply training evidence.");
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(root, "*.json").Single()));
            TestAssert.Equal(40d, saved.RootElement.GetProperty("cuts")[0].GetProperty("startSeconds").GetDouble(), "Actual rendered order must not be sorted by source time.");
            TestAssert.Equal(10d, saved.RootElement.GetProperty("cuts")[1].GetProperty("startSeconds").GetDouble(), "The second cut remains second.");
            TestAssert.False(saved.RootElement.GetProperty("wordingApproved").GetBoolean(), "Export is not title approval.");
            TestAssert.False(saved.RootElement.GetProperty("sequenceQualityReviewed").GetBoolean(), "Export is not a reviewed quality label.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }

    private static Task OldConsentRequiresUpdatedNotice()
    {
        var state = new EditorialMetadataPreferenceLearningConsentState(new InMemoryEditorialMetadataPreferenceLearningConsentStore(
            new(true, DateTimeOffset.UtcNow, "editorial-metadata-preference-learning-notice-1.0")));
        TestAssert.False(state.IsEnabled, "The old notice promised that words would not be saved.");
        state.Enable(DateTimeOffset.UtcNow);
        TestAssert.True(state.IsEnabled, "Explicit updated consent enables wording learning.");
        return Task.CompletedTask;
    }

    private static Task RetainsBoundFacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "ReplayFoundry-WriterTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string video = Path.Combine(root, "video.mp4");
            File.WriteAllBytes(video, [1, 2, 3, 4, 5, 6]);
            var context = new ClipEditorialContext("candidate", video, "Recording", TimeSpan.Zero,
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60), 50, "A door opened.");
            var request = new ClipEditorialMetadataRequest(context, ClipEditorialProfile.Default, 0);
            bool enabled = true;
            var store = new JsonEditorialWriterLearningStore(Path.Combine(root, "writer"), () => enabled);
            const string title = "I opened the door";
            const string description = "A passage became visible beyond the doorway.";
            string[] tags = ["door"];
            TestAssert.False(store.Record(context, title, description, tags, "I found a passage", description, tags),
                "Unbound edits must not become training labels.");
            const string facts = "{\"event\":\"A door opened\"}";
            var result = new { candidateId = "candidate", attempt = 0, metadata = new { title, description, tags, grounding = Array.Empty<object>() } };
            using JsonDocument resultDocument = JsonDocument.Parse(JsonSerializer.Serialize(result));
            string output = JsonSerializer.Serialize(new { results = new[] { result } });
            string capture = Path.Combine(root, "writer-contexts.json");
            File.WriteAllText(capture, JsonSerializer.Serialize(new
            {
                schema = "foundry-writer-contexts-1", contexts = new[] { new {
                    candidateId = "candidate", attempt = 0,
                    prompt = new[] { new { role = "system", content = "Use verified facts." }, new { role = "user", content = facts } },
                    factSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts))).ToLowerInvariant(),
                    outputCanonicalHash = Qwen3VlCanonicalJson.ComputeObjectSha256(resultDocument.RootElement, "__none"),
                } },
            }));
            store.RetainValidatedBatch(capture, output, [request]);
            TestAssert.False(Directory.Exists(Path.Combine(root, "writer", "examples")), "Generated wording alone is not approval.");
            TestAssert.True(store.Record(context, title, description, tags, "I found a passage", description, tags), "A saved correction retains its pair.");
            string[] examples = Directory.GetFiles(Path.Combine(root, "writer", "examples"), "*.json");
            TestAssert.Equal(1, examples.Length, "One correction produces one example.");
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(examples[0]));
            TestAssert.Equal("HumanCorrection", saved.RootElement.GetProperty("kind").GetString(), "The label is explicit.");
            TestAssert.Equal(title, saved.RootElement.GetProperty("rejected").GetProperty("titleBody").GetString(), "The rejected wording is preserved.");
            TestAssert.Equal(facts, saved.RootElement.GetProperty("prompt")[1].GetProperty("content").GetString(), "Factual context stays unchanged.");
            TestAssert.Equal("foundry-writer-example-3", saved.RootElement.GetProperty("schema").GetString(), "Lossless edits have their own contract.");
            var edit = saved.RootElement.GetProperty("edits").GetProperty("fields")[0];
            TestAssert.Equal(title, edit.GetProperty("before").GetString(), "Trace retains the previous wording.");
            TestAssert.Equal("I found a passage", edit.GetProperty("after").GetString(), "Trace retains the saved wording.");
            var operations = edit.GetProperty("operations").EnumerateArray().ToArray();
            TestAssert.Equal(title, string.Concat(operations.Where(op => op.GetProperty("op").GetString() != "add").Select(op => op.GetProperty("text").GetString())), "Remove and keep reconstruct the original.");
            TestAssert.Equal("I found a passage", string.Concat(operations.Where(op => op.GetProperty("op").GetString() != "remove").Select(op => op.GetProperty("text").GetString())), "Add and keep reconstruct the revision.");
            var fields = saved.RootElement.GetProperty("feedback").GetProperty("fields");
            TestAssert.Equal(1, fields.GetArrayLength(), "Changing a title cannot approve an unchanged description.");
            TestAssert.Equal("titleBody", fields[0].GetString(), "Only the changed field is supervised.");
            TestAssert.Equal(video, saved.RootElement.GetProperty("evidence").GetProperty("sourcePath").GetString(), "Review evidence stays linked to the source.");
            var wrongEvent = new EditorialWordingFeedback("WrongEvent", "A character closed the door.");
            TestAssert.True(store.Record(context, "I found a passage", description, tags, "The door closed", description, tags, wrongEvent), "A factual correction is retained for review.");
            TestAssert.True(store.Record(context, "The door closed", description, tags, "The door closed", description, tags, explicitApproval: true), "Explicit review preserves the pending factual correction.");
            using JsonDocument approved = Directory.GetFiles(Path.Combine(root, "writer", "examples"), "*.json")
                .Select(path => JsonDocument.Parse(File.ReadAllText(path)))
                .Single(doc => doc.RootElement.GetProperty("kind").GetString() == "ExplicitWordingApproval");
            TestAssert.Equal("WrongEvent", approved.RootElement.GetProperty("feedback").GetProperty("reason").GetString(), "Review must not discard the reason for a factual correction.");
            TestAssert.True(approved.RootElement.GetProperty("feedback").GetProperty("factsReviewed").GetBoolean(), "Only the explicit review supplies reviewed facts.");
            TestAssert.Equal(0, approved.RootElement.GetProperty("edits").GetProperty("fields").GetArrayLength(), "An approval cannot invent a text edit.");
            TestAssert.True(approved.RootElement.GetProperty("edits").GetProperty("parentExampleId").GetString() is { Length: 64 }, "Successive saves link to their preceding feedback.");
            enabled = false;
            TestAssert.False(store.Record(context, "I found a passage", description, tags, title, description, tags), "Turning learning off stops capture.");
        }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }
}
