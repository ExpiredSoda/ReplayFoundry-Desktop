using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class EditorialWriterLearningTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Writer feedback retains explicit wording under matching facts", RetainsBoundFacts),
        new("Old structural consent cannot silently enable storing wording", OldConsentRequiresUpdatedNotice),
    ];

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
            enabled = false;
            TestAssert.False(store.Record(context, "I found a passage", description, tags, title, description, tags), "Turning learning off stops capture.");
        }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }
}
