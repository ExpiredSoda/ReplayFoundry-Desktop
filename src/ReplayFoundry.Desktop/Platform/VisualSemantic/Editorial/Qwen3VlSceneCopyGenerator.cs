using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlSceneCopyGenerator(Qwen3VlQualifiedEditorialRuntime runtime, IEditorialWriterLearningStore? learning)
{
    internal const string Version = "scene-copy-1.8";
    internal const string PromptHash = "5efe99894d22aa30afb3330a82ca3140a3f3df6c88170e96d7175d5efc577f3f";
    internal const string ReviewPromptHash = "037c23e37e37ea3a854dbda5368bc51e6995e86152f0e9a15f9cd79a0857bf0a";
    internal static bool CanUse(ClipEditorialMetadataRequest request)
    {
        if (!request.Context.Evidence.Any(item => item.Kind == ClipEditorialEvidenceKind.VisualObservation && item.Id == "scene-review-1.4-setup") ||
            !request.Context.Evidence.Any(item => item.Kind == ClipEditorialEvidenceKind.VisualObservation && item.Id == "scene-review-1.4-event") ||
            !request.Context.Evidence.Any(item => item.Kind == ClipEditorialEvidenceKind.VisualObservation && item.Id == "scene-review-1.4-outcome")) return false;
        var binding = request.Context.Evidence.SingleOrDefault(item => item.Id == "scene-review-source-binding");
        if (binding is null) return false;
        try
        {
            // Old projects can cheaply replay their cached scene review to restore
            // independent source text before writing with the current evidence contract.
            if (ReviewedContext(request) is not { } context || !context.TryGetProperty("sourceText", out var text) ||
                text.ValueKind != JsonValueKind.Array) return false;
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

    internal Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateAsync(IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken) =>
        GenerateCoreAsync(requests, false, cancellationToken);

    internal async Task<ClipEditorialMetadataDraft> GenerateSequenceAsync(IReadOnlyList<ClipEditorialMetadataRequest> requests, CancellationToken cancellationToken) =>
        (await GenerateCoreAsync(requests, true, cancellationToken))[0];

    private async Task<IReadOnlyList<ClipEditorialMetadataDraft>> GenerateCoreAsync(IReadOnlyList<ClipEditorialMetadataRequest> requests, bool sequence, CancellationToken cancellationToken)
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
                cases = BuildCases(requests, sequence)
            }), cancellationToken);
            var host = runtime.Host;
            List<string> arguments = ["-B", "-m", "replayfoundry_visual_semantic.scene_copy", "--input", input,
                "--output", output, "--model", host.ModelDirectoryPath,
                "--cache", ReplayFoundryLocalDataPaths.Resolve(null, "Cache/SceneCopy")];
            if (!sequence && learning is { IsEnabled: true, LearningDirectory: { } writerRoot })
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
            if (rows.Length != (sequence ? 1 : requests.Count)) throw new InvalidDataException("Scene writer omitted a case.");
            List<ClipEditorialMetadataDraft> drafts = [];
            List<object> captures = [], validated = [];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var request = requests[i];
                if (row.GetProperty("candidateId").GetString() != request.Context.CandidateId || row.GetProperty("attempt").GetInt32() != request.Attempt)
                    throw new InvalidDataException("Scene writer case identity changed.");
                if (row.GetProperty("status").GetString() is not ("Succeeded" or "Failed"))
                    throw new InvalidDataException("Unknown scene writing status.");
                if (row.GetProperty("status").GetString() != "Succeeded" || !row.GetProperty("review").GetProperty("grounded").GetBoolean() ||
                    !row.GetProperty("review").GetProperty("useful").GetBoolean())
                {
                    // Keep bounded local evidence of a failed model judgment;
                    // the successful drafts must not be mistaken for a parser failure.
                    new SystemQwen3VlGroundedFailureArchive().Archive(output, 1_048_576);
                    throw new ClipEditorialAiGenerationException(ClipEditorialAiFailureKind.CaseRejected,
                        request.PriorAcceptedTitleExclusions.Count > 0
                            ? "No reliable new angle was found. Your saved wording is unchanged. Try another angle or keep the current copy."
                            : "AI could not produce supported, useful wording for this clip after a correction. Try another cut or writing angle.",
                        request.Context.CandidateId);
                }
                foreach (string key in request.PriorAcceptedTitleExclusions.Count > 0
                    ? new[] { "neuralGrounding", "neuralQuality", "neuralNovelty" }
                    : new[] { "neuralGrounding", "neuralQuality" })
                {
                    JsonElement judgment = row.GetProperty(key);
                    double value = judgment.GetProperty("value").GetDouble();
                    Qwen3VlSceneReviewProvider.ValidateNeuralValue(judgment, value * 100, "copy-judgment-2");
                    if (value <= .5) throw new InvalidDataException("The neural writer check did not support this draft.");
                    if (key is "neuralGrounding" or "neuralNovelty" &&
                        judgment.GetProperty("margins").EnumerateArray().Any(margin => margin.GetDouble() <= 0))
                        throw new InvalidDataException("The factual or variation checks disagreed; the saved wording was kept.");
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
                var provenance = new ClipEditorialAiProvenance("Qwen3-VL scene writer", Version, "1.8", model.RepositoryId,
                    model.Revision, model.ManifestSha256, "ReplayFoundry reviewed scene copy", Version, PromptHash, process.Duration, null)
                {
                    WritingAttempts = row.TryGetProperty("writerIdentity", out var writer) && writer.ValueKind == JsonValueKind.Object
                        ? [new(request.Context.CandidateId, request.Attempt, writer.GetProperty("modelRepository").GetString()!,
                            writer.GetProperty("modelRevision").GetString()!, writer.GetProperty("baseManifestSha256").GetString()!,
                            writer.GetProperty("adapterSha256").GetString()!, writer.GetProperty("qualificationSha256").GetString()!)] : [],
                    NeuralCopyReview = new(row.GetProperty("neuralGrounding").GetProperty("value").GetDouble(),
                        row.GetProperty("neuralQuality").GetProperty("value").GetDouble(), ReviewPromptHash)
                };
                var evidence = sequence
                    ? requests.SelectMany((part, index) => part.Context.Evidence.Select(item =>
                        new ClipEditorialEvidenceReference($"sequence-{index + 1}-{item.Id}", item.Kind, item.Description))).ToArray()
                    : request.Context.Evidence.ToArray();
                var draft = new ClipEditorialMetadataDraft(title, description, tags, ClipEditorialMetadataOrigin.AiAssisted,
                    Qwen3VlGroundedMetadataGenerator.SharedIdentity, request.Attempt, evidence, aiProvenance: provenance,
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
            if (!sequence && learning?.IsEnabled == true)
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

    private static object[] BuildCases(IReadOnlyList<ClipEditorialMetadataRequest> requests, bool sequence)
    {
        object Facts(ClipEditorialMetadataRequest request) => new
        {
            recording = Path.GetFileName(request.Context.SourceFullPath),
            start = request.Context.SourceStart.TotalSeconds, end = request.Context.SourceEnd.TotalSeconds,
            centralEvent = request.Context.Evidence.Single(item => item.Id == "scene-review-1.4-event").Description,
            setupObservation = request.Context.Evidence.Single(item => item.Id == "scene-review-1.4-setup").Description,
            outcomeObservation = request.Context.Evidence.Single(item => item.Id == "scene-review-1.4-outcome").Description,
            reviewedContext = ReviewedContext(request),
        };
        return (sequence ? requests.Take(1) : requests).Select(request => (object)new
        {
            candidateId = request.Context.CandidateId, attempt = request.Attempt,
            reviewVideoHash = sequence ? string.Join("|", requests.Select(value => value.ReviewVideo!.ReviewVideoSha256)) : request.ReviewVideo!.ReviewVideoSha256,
            context = new
            {
                candidateMode = sequence ? "WholeMontage" : CandidateMode(request),
                centralEvent = sequence ? "Separate reviewed cuts presented in the listed order. No continuous encounter is established between cuts." : request.Context.Evidence.Single(item => item.Id == "scene-review-1.4-event").Description,
                setupObservation = sequence ? "Use the independent setup observation for each cut." : request.Context.Evidence.Single(item => item.Id == "scene-review-1.4-setup").Description,
                outcomeObservation = sequence ? "Use the independent outcome observation for each cut." : request.Context.Evidence.Single(item => item.Id == "scene-review-1.4-outcome").Description,
                reviewedContext = sequence ? (object)new { sequence = requests.Select(Facts).ToArray() } : ReviewedContext(request),
                tags = new[] { request.Context.GameContext.AudienceGameHashtag.TrimStart('#') }.Concat(request.Profile.DefaultTags).Distinct(StringComparer.OrdinalIgnoreCase).Take(8),
                game = request.Context.GameContext.AudienceGameName,
                titleLimit = Math.Min(72, 99 - request.Context.GameContext.AudienceGameHashtag.Length),
                preferences = new { request.Profile.AudienceAddress, request.Profile.NamingGuidance,
                    voice = request.Profile.VoicePerspective.ToString(), objective = request.Profile.CopyObjective.ToString(), intent = request.VariantIntent.ToString(), tone = request.Tone },
                priorTitles = request.PriorAcceptedTitleExclusions.Select(item => StripHashtag(item.Title, request.Context.GameContext.AudienceGameHashtag))
            }
        }).ToArray();
    }

    internal static string CandidateMode(ClipEditorialMetadataRequest request)
    {
        using var binding = JsonDocument.Parse(request.Context.Evidence.Single(item => item.Id == "scene-review-source-binding").Description);
        if (!binding.RootElement.TryGetProperty("candidateMode", out var value)) return "StandaloneClip";
        string? mode = value.GetString();
        return mode is "StandaloneClip" or "MontageSegment" ? mode : throw new InvalidDataException("Unknown scene wording purpose.");
    }

    internal static JsonElement? ReviewedContext(ClipEditorialMetadataRequest request)
    {
        var context = request.Context.Evidence.SingleOrDefault(item => item.Id == GenerationSceneEditorialEvidence.EvidenceId &&
            item.Kind == ClipEditorialEvidenceKind.ReviewedMomentContext);
        if (context is null) return null;
        using var document = JsonDocument.Parse(context.Description);
        if (document.RootElement.GetProperty("schema").GetString() != "moment-evidence-1")
            throw new InvalidDataException("Unknown reviewed audio context.");
        return document.RootElement.Clone();
    }

    private static string StripHashtag(string title, string hashtag) => title.EndsWith(" " + hashtag, StringComparison.OrdinalIgnoreCase)
        ? title[..^(hashtag.Length + 1)] : title;
}
