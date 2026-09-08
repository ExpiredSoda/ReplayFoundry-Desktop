using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlSceneCopyGenerator(Qwen3VlQualifiedEditorialRuntime runtime, IEditorialWriterLearningStore? learning)
{
    internal const string Version = "scene-copy-1.6";
    internal const string PromptHash = "6c53b17a5ba00ed5d3585133de00649fb8faba2b0f167e50f8332821640de934";
    internal const string ReviewPromptHash = "9bd2bac0f667bfcdb7a1f9113aa9fe6e8770179f108fc6ae191e0958d5e6d9e3";
    internal static bool CanUse(ClipEditorialMetadataRequest request)
    {
        if (!request.Context.Evidence.Any(item => item.Kind == ClipEditorialEvidenceKind.VisualObservation && item.Id == "scene-review-1.4-setup") ||
            !request.Context.Evidence.Any(item => item.Kind == ClipEditorialEvidenceKind.VisualObservation && item.Id == "scene-review-1.4-event") ||
            !request.Context.Evidence.Any(item => item.Kind == ClipEditorialEvidenceKind.VisualObservation && item.Id == "scene-review-1.4-outcome")) return false;
        var binding = request.Context.Evidence.SingleOrDefault(item => item.Id == "scene-review-source-binding");
        if (binding is null) return false;
        try
        {
            using var document = JsonDocument.Parse(binding.Description);
            var row = document.RootElement; var source = new FileInfo(request.Context.SourceFullPath);
            return source.Exists && row.GetProperty("start").GetInt64() == request.Context.SourceStart.Ticks &&
                row.GetProperty("end").GetInt64() == request.Context.SourceEnd.Ticks && row.GetProperty("length").GetInt64() == source.Length &&
                row.GetProperty("modified").GetInt64() == source.LastWriteTimeUtc.Ticks &&
                source.FullName.Equals(row.GetProperty("source").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }

    internal async Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateAsync(IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count is < 1 or > 30 || requests.Any(request => !CanUse(request))) throw new ArgumentException("Reviewed scene facts are required.");
        string directory = ReplayFoundryLocalDataPaths.ResolveTemporary("scene-copy/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var request in requests) await request.ReviewVideo!.VerifyIntegrityAsync(cancellationToken);
            await Task.Run(() => runtime.ModelIntegrity.Verify(cancellationToken), cancellationToken);
            string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "output.json");
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new
            {
                schemaVersion = Version, modelHash = runtime.Model.ManifestSha256,
                cases = requests.Select(request => new
                {
                    candidateId = request.Context.CandidateId, attempt = request.Attempt,
                    reviewVideoHash = request.ReviewVideo!.ReviewVideoSha256,
                    context = new
                    {
                        candidateMode = CandidateMode(request),
                        centralEvent = request.Context.Evidence.Single(item => item.Id == "scene-review-1.4-event").Description,
                        tags = new[] { request.Context.GameContext.AudienceGameHashtag.TrimStart('#') }
                            .Concat(request.Profile.DefaultTags).Distinct(StringComparer.OrdinalIgnoreCase).Take(8),
                        game = request.Context.GameContext.AudienceGameName,
                        titleLimit = Math.Min(72, 99 - request.Context.GameContext.AudienceGameHashtag.Length),
                        preferences = new { request.Profile.AudienceAddress, request.Profile.NamingGuidance,
                            voice = request.Profile.VoicePerspective.ToString(), objective = request.Profile.CopyObjective.ToString(), intent = request.VariantIntent.ToString() },
                        priorTitles = request.PriorAcceptedTitleExclusions.Select(item => StripHashtag(item.Title, request.Context.GameContext.AudienceGameHashtag))
                    }
                })
            }), cancellationToken);
            var host = runtime.Host;
            List<string> arguments = ["-B", "-m", "replayfoundry_visual_semantic.scene_copy", "--input", input,
                "--output", output, "--model", host.ModelDirectoryPath,
                "--cache", ReplayFoundryLocalDataPaths.Resolve(null, "Cache/SceneCopy")];
            if (learning is { IsEnabled: true, LearningDirectory: { } writerRoot })
                arguments.AddRange(["--writer-root", writerRoot]);
            var process = await MediaWorkBudget.RunAsync(new WindowsProcessRunner(), new ProcessRunRequest(
                host.PythonExecutablePath, arguments,
                TimeSpan.FromMinutes(10), Path.GetDirectoryName(host.HostScriptPath), 524288, 524288,
                host.EnvironmentVariables, inheritParentEnvironment: false), MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi, cancellationToken);
            QwenModelLoadDiagnostics.Report(process.StandardError);
            if (!process.Succeeded) throw new InvalidOperationException("The scene writer did not finish.");
            await Task.Run(() => runtime.ModelIntegrity.Verify(cancellationToken), cancellationToken);
            foreach (var request in requests) await request.ReviewVideo!.VerifyIntegrityAsync(cancellationToken);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetString() != Version || root.GetProperty("promptHash").GetString() != PromptHash ||
                root.GetProperty("reviewPromptHash").GetString() != ReviewPromptHash ||
                !runtime.Model.ManifestSha256.Equals(root.GetProperty("modelHash").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Scene writer identity changed.");
            var rows = root.GetProperty("cases").EnumerateArray().ToArray();
            if (rows.Length != requests.Count) throw new InvalidDataException("Scene writer omitted a case.");
            List<ClipEditorialMetadataDraft> drafts = [];
            List<object> captures = [], validated = [];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var request = requests[i];
                if (row.GetProperty("candidateId").GetString() != request.Context.CandidateId || row.GetProperty("attempt").GetInt32() != request.Attempt)
                    throw new InvalidDataException("Scene writer case identity changed.");
                if (row.GetProperty("status").GetString() != "Succeeded" || !row.GetProperty("review").GetProperty("grounded").GetBoolean() ||
                    !row.GetProperty("review").GetProperty("useful").GetBoolean())
                {
                    // Keep bounded local evidence of a failed model judgment;
                    // the successful drafts must not be mistaken for a parser failure.
                    new SystemQwen3VlGroundedFailureArchive().Archive(output, 1_048_576);
                    throw new InvalidDataException($"The scene writer could not verify wording for {request.Context.CandidateId} after one correction.");
                }
                foreach (string key in new[] { "neuralGrounding", "neuralQuality" })
                {
                    JsonElement judgment = row.GetProperty(key);
                    double value = judgment.GetProperty("value").GetDouble();
                    Qwen3VlSceneReviewProvider.ValidateNeuralValue(judgment, value * 100, "copy-judgment-1");
                    if (value <= .5) throw new InvalidDataException("The neural writer check did not support this draft.");
                }
                string titleBody = row.GetProperty("copy").GetProperty("titleBody").GetString()!.Trim();
                string description = row.GetProperty("copy").GetProperty("description").GetString()!.Trim();
                string title = titleBody + " " + request.Context.GameContext.AudienceGameHashtag;
                if (title.Length > 100 || titleBody.Contains('#') || description.Length is < 1 or > 420 ||
                    titleBody.TrimEnd('.', '!', '?').Equals(description.TrimEnd('.', '!', '?'), StringComparison.OrdinalIgnoreCase) ||
                    request.PriorAcceptedTitleExclusions.Any(prior => prior.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Scene copy is repetitive or out of bounds.");
                string? signature = request.Profile.ReusableDescriptionSignature;
                if (!string.IsNullOrWhiteSpace(signature)) description += "\n\n" + signature;
                var tags = new[] { request.Context.GameContext.AudienceGameHashtag.TrimStart('#') }
                    .Concat(request.Profile.DefaultTags).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
                var model = runtime.Model;
                var provenance = new ClipEditorialAiProvenance("Qwen3-VL scene writer", Version, "1.6", model.RepositoryId,
                    model.Revision, model.ManifestSha256, "ReplayFoundry reviewed scene copy", Version, PromptHash, process.Duration, null)
                {
                    WritingAttempts = row.TryGetProperty("writerIdentity", out var writer) && writer.ValueKind == JsonValueKind.Object
                        ? [new(request.Context.CandidateId, request.Attempt, writer.GetProperty("modelRepository").GetString()!,
                            writer.GetProperty("modelRevision").GetString()!, writer.GetProperty("baseManifestSha256").GetString()!,
                            writer.GetProperty("adapterSha256").GetString()!, writer.GetProperty("qualificationSha256").GetString()!)] : [],
                    NeuralCopyReview = new(row.GetProperty("neuralGrounding").GetProperty("value").GetDouble(),
                        row.GetProperty("neuralQuality").GetProperty("value").GetDouble(), ReviewPromptHash)
                };
                var draft = new ClipEditorialMetadataDraft(title, description, tags, ClipEditorialMetadataOrigin.AiAssisted,
                    Qwen3VlGroundedMetadataGenerator.SharedIdentity, request.Attempt, request.Context.Evidence, aiProvenance: provenance,
                    priorAcceptedTitles: request.PriorAcceptedTitleExclusions.Select(item => item.Title));
                drafts.Add(draft);
                var result = new { candidateId = request.Context.CandidateId, attempt = request.Attempt,
                    // Grounding here is the writer's public-knowledge binding field.
                    // Local scene facts remain in the captured factual prompt.
                    metadata = new { title, description, tags, grounding = Array.Empty<object>() } };
                validated.Add(result);
                using var canonical = JsonDocument.Parse(JsonSerializer.Serialize(result));
                captures.Add(new { candidateId = request.Context.CandidateId, attempt = request.Attempt,
                    prompt = row.GetProperty("prompt").Clone(), factSha256 = row.GetProperty("factHash").GetString(),
                    outputCanonicalHash = Qwen3VlCanonicalJson.ComputeObjectSha256(canonical.RootElement, "__none") });
            }
            if (learning?.IsEnabled == true)
            {
                try
                {
                    string path = Path.Combine(directory, "writer-contexts.json");
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { schema = "foundry-writer-contexts-1", contexts = captures }), cancellationToken);
                    learning.RetainValidatedBatch(path, JsonSerializer.Serialize(new { results = validated }), requests);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                { SafeDiagnosticTrace.Write("Reviewed scene wording context could not be retained", exception); }
            }
            return drafts;
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static string CandidateMode(ClipEditorialMetadataRequest request)
    {
        using var binding = JsonDocument.Parse(request.Context.Evidence.Single(item => item.Id == "scene-review-source-binding").Description);
        if (!binding.RootElement.TryGetProperty("candidateMode", out var value)) return "StandaloneClip";
        string? mode = value.GetString();
        return mode is "StandaloneClip" or "MontageSegment" ? mode : throw new InvalidDataException("Unknown scene wording purpose.");
    }

    private static string StripHashtag(string title, string hashtag) => title.EndsWith(" " + hashtag, StringComparison.OrdinalIgnoreCase)
        ? title[..^(hashtag.Length + 1)] : title;
}
