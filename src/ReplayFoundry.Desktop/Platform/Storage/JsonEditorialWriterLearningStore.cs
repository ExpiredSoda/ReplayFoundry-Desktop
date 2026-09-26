using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.Storage;

/// <summary>Local explicit wording pairs with immutable clip-fact bindings.</summary>
public sealed class JsonEditorialWriterLearningStore : IEditorialWriterLearningStore
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string Path, long Length, DateTime Modified), string> SourceGroups = new();
    private readonly string _root;
    private readonly Func<bool> _enabled;
    private static readonly JsonSerializerOptions Options = ReplayFoundryLocalJsonPolicy.IndentedCamelCase;
    public JsonEditorialWriterLearningStore(string? root = null, Func<bool>? enabled = null)
    {
        _root = ReplayFoundryLocalDataPaths.Resolve(root, Path.Combine("Personalization", "Writer"));
        _enabled = enabled ?? (() =>
        {
            var consent = new JsonEditorialMetadataPreferenceLearningConsentStore().Current;
            return consent.IsEnabled && consent.NoticeVersion == EditorialMetadataPreferenceLearningConsentSnapshot.CurrentNoticeVersion;
        });
    }
    public bool IsEnabled => _enabled();
    public string? LearningDirectory => IsEnabled ? _root : null;

    public void RetainValidatedBatch(string contextPath, string validatedOutput,
        IReadOnlyList<ClipEditorialMetadataRequest> requests)
    {
        if (!IsEnabled || !File.Exists(contextPath)) return;
        if (new FileInfo(contextPath).Length > 2_097_152) throw new InvalidDataException("Writer context exceeds its size limit.");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllText(contextPath));
        using JsonDocument result = JsonDocument.Parse(validatedOutput);
        if (capture.RootElement.GetProperty("schema").GetString() != "foundry-writer-contexts-1")
            throw new InvalidDataException("Writer context version is unsupported.");
        lock (Gate)
        {
            if (!IsEnabled) return;
            string directory = Path.Combine(_root, "contexts");
            Directory.CreateDirectory(directory);
            foreach (JsonElement row in capture.RootElement.GetProperty("contexts").EnumerateArray())
            {
                var request = requests.Single(item => item.Context.CandidateId == row.GetProperty("candidateId").GetString()
                    && item.Attempt == row.GetProperty("attempt").GetInt32());
                JsonElement output = result.RootElement.GetProperty("results").EnumerateArray().Single(item =>
                    item.GetProperty("candidateId").GetString() == request.Context.CandidateId &&
                    item.GetProperty("attempt").GetInt32() == request.Attempt);
                if (Qwen3VlCanonicalJson.ComputeObjectSha256(output, "__none") != row.GetProperty("outputCanonicalHash").GetString())
                    throw new InvalidDataException("Writer context does not match the parsed generation result.");
                JsonElement prompt = row.GetProperty("prompt");
                if (prompt.GetArrayLength() != 2 || prompt[0].GetProperty("role").GetString() != "system" ||
                    prompt[1].GetProperty("role").GetString() != "user" ||
                    Hash(prompt[1].GetProperty("content").GetString()!) != row.GetProperty("factSha256").GetString())
                    throw new InvalidDataException("Writer factual prompt identity changed.");
                JsonElement metadata = output.GetProperty("metadata");
                string title = metadata.GetProperty("title").GetString()!;
                string description = metadata.GetProperty("description").GetString()!;
                string[] tags = metadata.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()!).ToArray();
                var context = new JsonObject
                {
                    ["prompt"] = JsonNode.Parse(prompt.GetRawText()), ["factSha256"] = row.GetProperty("factSha256").GetString(),
                    ["sourceGroup"] = SourceGroup(request.Context),
                    ["generated"] = Copy(TitleBody(title, request.Context), description, tags, metadata.GetProperty("grounding")),
                    ["evidence"] = JsonSerializer.SerializeToNode(new
                    {
                        schema = "foundry-learning-source-1",
                        sourcePath = request.Context.SourceFullPath,
                        sourceLength = new FileInfo(request.Context.SourceFullPath).Length,
                        sourceModifiedUtcTicks = File.GetLastWriteTimeUtc(request.Context.SourceFullPath).Ticks,
                        startSeconds = request.Context.SourceStart.TotalSeconds,
                        endSeconds = request.Context.SourceEnd.TotalSeconds,
                        observations = request.Context.Evidence.Select(item => new { id = item.Id, text = item.Description }),
                        transcripts = request.Context.Transcripts.Select(track => new {
                            audioStreamIndex = track.AbsoluteAudioStreamIndex, authority = track.Authority.ToString(),
                            text = track.Text, spans = track.Spans.Select(span => new {
                                startSeconds = span.SourceStart.TotalSeconds, endSeconds = span.SourceEnd.TotalSeconds, text = span.Text }) }),
                        reviewVideoSha256 = request.ReviewVideo?.ReviewVideoSha256,
                        provenance = "ModelObservedNotHumanApproved"
                    }),
                };
                string key = ContextKey(request.Context, title, description);
                WriteAtomic(Path.Combine(directory, key + ".json"), context.ToJsonString(Options));
            }
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(512))
                file.Delete();
        }
    }

    public bool Record(ClipEditorialContext context, string beforeTitle, string beforeDescription,
        IReadOnlyList<string> beforeTags, string afterTitle, string afterDescription,
        IReadOnlyList<string> afterTags, bool explicitApproval = false)
        => Record(context, beforeTitle, beforeDescription, beforeTags, afterTitle, afterDescription,
            afterTags, new EditorialWordingFeedback(), explicitApproval);

    public bool Record(ClipEditorialContext context, string beforeTitle, string beforeDescription,
        IReadOnlyList<string> beforeTags, string afterTitle, string afterDescription,
        IReadOnlyList<string> afterTags, EditorialWordingFeedback feedback, bool explicitApproval = false)
    {
        if (!IsEnabled) return false;
        feedback.Validate();
        beforeTitle = TitleBody(beforeTitle, context);
        afterTitle = TitleBody(afterTitle, context);
        bool changed = beforeTitle != afterTitle || beforeDescription.Trim() != afterDescription.Trim() ||
            !beforeTags.SequenceEqual(afterTags, StringComparer.OrdinalIgnoreCase);
        if (!changed && !explicitApproval) return false;
        if (afterTitle.Length is < 1 or > 100 || afterDescription.Trim().Length is < 1 or > 420 ||
            afterTags.Count is < 1 or > 8 || afterTags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 60)) return false;
        lock (Gate)
        {
            if (!IsEnabled) return false;
            string source = Path.Combine(_root, "contexts", ContextKey(context, beforeTitle, beforeDescription) + ".json");
            if (!File.Exists(source) || new FileInfo(source).Length > 65_536) return false;
            JsonNode captured = JsonNode.Parse(File.ReadAllText(source))!;
            if (captured["sourceGroup"]!.GetValue<string>() != SourceGroup(context)) return false;
            if (feedback.Reason == "Unspecified" && captured["pendingFeedback"] is JsonObject pending)
                feedback = new EditorialWordingFeedback(pending["reason"]!.GetValue<string>(), pending["correctedEvent"]!.GetValue<string>());
            JsonNode original = captured["generated"]!;
            using JsonDocument grounding = JsonDocument.Parse(original["grounding"]!.ToJsonString());
            JsonObject chosen = Copy(afterTitle, afterDescription.Trim(), afterTags, grounding.RootElement);
            string[] fields = new[] { "titleBody", "description", "tags" }.Where(field => explicitApproval ||
                !JsonNode.DeepEquals(chosen[field], original[field])).ToArray();
            var example = new JsonObject
            {
                ["schema"] = "foundry-writer-example-2", ["sourceGroup"] = captured["sourceGroup"]!.DeepClone(),
                ["factSha256"] = captured["factSha256"]!.DeepClone(), ["prompt"] = captured["prompt"]!.DeepClone(),
                ["chosen"] = chosen, ["rejected"] = changed ? original.DeepClone() : null,
                ["kind"] = changed ? "HumanCorrection" : "ExplicitWordingApproval",
                ["feedback"] = JsonSerializer.SerializeToNode(new { reason = feedback.Reason,
                    correctedEvent = feedback.CorrectedEvent.Trim(), fields,
                    factsReviewed = explicitApproval }),
                ["evidence"] = captured["evidence"]?.DeepClone(),
            };
            using JsonDocument canonical = JsonDocument.Parse(example.ToJsonString());
            string id = Qwen3VlCanonicalJson.ComputeObjectSha256(canonical.RootElement, "id");
            example["id"] = id;
            string json = example.ToJsonString(Options);
            if (Encoding.UTF8.GetByteCount(json) > 65_536) return false;
            string directory = Path.Combine(_root, "examples");
            Directory.CreateDirectory(directory);
            if (Directory.EnumerateFiles(directory, "*.json").Take(10_000).Count() >= 10_000) return false;
            WriteAtomic(Path.Combine(directory, id + ".json"), json);
            // Later edits stay linked to the same facts and compare against the
            // immediately previous saved wording, not an unrelated model draft.
            captured["generated"] = chosen.DeepClone();
            captured["pendingFeedback"] = JsonSerializer.SerializeToNode(new { reason = feedback.Reason, correctedEvent = feedback.CorrectedEvent.Trim() });
            WriteAtomic(Path.Combine(_root, "contexts", ContextKey(context, afterTitle, afterDescription) + ".json"), captured.ToJsonString(Options));
            return true;
        }
    }

    private static JsonObject Copy(string title, string description, IEnumerable<string> tags, JsonElement grounding) => new()
    {
        ["titleBody"] = title, ["description"] = description,
        ["tags"] = JsonSerializer.SerializeToNode(tags.Take(8).ToArray()),
        ["grounding"] = JsonNode.Parse(grounding.GetRawText()), ["temporalVoice"] = "RetrospectivePast",
    };
    private static string TitleBody(string title, ClipEditorialContext context)
    {
        string suffix = " " + context.GameContext.AudienceGameHashtag;
        return (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? title[..^suffix.Length] : title).Trim();
    }
    private static string SourceGroup(ClipEditorialContext context)
    {
        // Group copied/renamed recordings together without rehashing hours of
        // video on the UI thread. This grouping identity is not a fact proof;
        // the independent generation receipt supplies the factual integrity.
        var before = new FileInfo(context.SourceFullPath);
        long length = before.Length;
        DateTime modified = before.LastWriteTimeUtc;
        var identity = (Path.GetFullPath(context.SourceFullPath).ToUpperInvariant(), length, modified);
        if (SourceGroups.TryGetValue(identity, out string? existing)) return existing;
        using FileStream stream = new(context.SourceFullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("foundry-recording-group-1"));
        hash.AppendData(BitConverter.GetBytes(length));
        byte[] buffer = new byte[(int)Math.Min(length, 131_072)];
        for (int index = 0; index < 17; index++)
        {
            stream.Position = (length - buffer.Length) / 16 * index;
            stream.ReadExactly(buffer);
            hash.AppendData(buffer);
        }
        before.Refresh();
        if (before.Length != length || before.LastWriteTimeUtc != modified)
            throw new IOException("Recording changed while retaining writer feedback.");
        string result = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (SourceGroups.Count >= 64) SourceGroups.Clear();
        SourceGroups[identity] = result;
        return result;
    }
    private static string ContextKey(ClipEditorialContext context, string title, string description) => Hash(string.Join('\n',
        SourceGroup(context), context.SourceStart.Ticks.ToString(CultureInfo.InvariantCulture),
        context.SourceEnd.Ticks.ToString(CultureInfo.InvariantCulture), context.EditorialBrief.Fingerprint,
        TitleBody(title, context), description.Trim()));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void WriteAtomic(string path, string text)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
