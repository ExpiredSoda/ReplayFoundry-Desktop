using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed class Qwen3VlSceneReviewProvider(Qwen3VlQualifiedEditorialRuntime runtime) : IVisualSemanticEditorialProvider
{
    internal const string Version = "scene-review-1.4";
    internal const int FrameCount = 12;
    internal const string PromptHash = "2c4ce23d223f72814bcf6f8c78f8aeeef56ea403995ab0261724922b443d9e25";
    internal const string FactPromptHash = "b7a07e2c31b6c7a1d9983405f99f202a60ea9068bee721abfb6ead56dccdbd22";
    internal const string StatesPromptHash = "13e5ea14912c03940ac42cc79998afefdcf12c2942eeab0bb2a54de22a5d38a1";
    internal const string ScorePromptHash = "0a32dc19d9980217710c6ac16340800f08ea2833bf893e69ac197aa06193de9b";
    public InferenceProviderIdentity Identity { get; } = new("Qwen3-VL grounded scene review", "1.4", "1.4.0");

    internal static VisualSemanticPromptManifest LoadPrompt(string hostPath)
    {
        string text = File.ReadAllText(Path.Combine(Path.GetDirectoryName(hostPath)!, "replayfoundry-scene-review-prompt-1.4.txt"))
            .Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (!Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Equals(PromptHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The grounded scene review prompt changed.");
        string factText = File.ReadAllText(Path.Combine(Path.GetDirectoryName(hostPath)!, "replayfoundry-scene-fact-check-prompt-1.2.txt"))
            .Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (!Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(factText))).Equals(FactPromptHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The visual fact check prompt changed.");
        string statesText = File.ReadAllText(Path.Combine(Path.GetDirectoryName(hostPath)!, "replayfoundry-scene-states-prompt-1.0.txt"))
            .Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (!Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(statesText))).Equals(StatesPromptHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The frame state description prompt changed.");
        return new(VisualSemanticPromptManifest.QualifiedEditorialSchemaVersion,
            VisualSemanticPromptManifest.GroundedSceneName, Version, text, PromptHash,
            new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero));
    }

    public async Task<VisualSemanticEditorialBatchResult> ObserveAsync(VisualSemanticBatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Requests.Count is < 1 or > 8 || request.Prompt.Version != Version ||
            !request.Prompt.Sha256.Equals(PromptHash, StringComparison.OrdinalIgnoreCase) ||
            !request.Model.ManifestSha256.Equals(runtime.Model.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Grounded review requires its frozen prompt, verified model and at most eight clips.", nameof(request));
        string directory = ReplayFoundryLocalDataPaths.ResolveTemporary("scene-review/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var item in request.Requests) await item.Input.VerifyIntegrityAsync(cancellationToken);
            await Task.Run(() => runtime.ModelIntegrity.Verify(cancellationToken), cancellationToken);
            string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "output.json");
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new
            {
                schemaVersion = Version, modelHash = runtime.Model.ManifestSha256,
                promptHash = PromptHash, factPromptHash = FactPromptHash, statesPromptHash = StatesPromptHash, scorePromptHash = ScorePromptHash,
                cases = request.Requests.Select(item => new
                {
                    caseId = item.CaseId, path = item.Input.ReviewVideoPath, inputHash = item.Input.ReviewVideoSha256,
                    candidateMode = item.CandidateMode.ToString(),
                    start = item.CandidateStartRelative.TotalSeconds, end = item.CandidateEndRelative.TotalSeconds,
                    transcript = item.Transcript.Spans.Where(span => !span.IsNonSpeech).Select(span => new
                    { id = span.Id, start = span.ReviewRelativeStart.TotalSeconds, end = span.ReviewRelativeEnd.TotalSeconds, text = span.Text })
                })
            }), cancellationToken);
            var host = runtime.Host;
            var process = await MediaWorkBudget.RunAsync(new WindowsProcessRunner(), new ProcessRunRequest(
                host.PythonExecutablePath, ["-B", "-m", "replayfoundry_visual_semantic.scene_review", "--input", input,
                    "--output", output, "--model", host.ModelDirectoryPath, "--ffmpeg", new FfmpegToolLocator().LocateFfmpeg(),
                    "--cache", ReplayFoundryLocalDataPaths.Resolve(null, "Cache/SceneReview")],
                host.ProcessTimeout, Path.GetDirectoryName(host.HostScriptPath), 524288, 524288,
                host.EnvironmentVariables, inheritParentEnvironment: false),
                MediaWorkPriority.FinalOutput, MediaWorkKind.HeavyAi, cancellationToken);
            QwenModelLoadDiagnostics.Report(process.StandardError);
            if (!process.Succeeded) throw new InvalidOperationException("Grounded scene review did not finish. " +
                Qwen3VlProcessOutputReader.FailureSummary(process));
            await Task.Run(() => runtime.ModelIntegrity.Verify(cancellationToken), cancellationToken);
            foreach (var item in request.Requests) await item.Input.VerifyIntegrityAsync(cancellationToken);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetString() != Version ||
                !PromptHash.Equals(root.GetProperty("promptHash").GetString(), StringComparison.OrdinalIgnoreCase) ||
                !FactPromptHash.Equals(root.GetProperty("factPromptHash").GetString(), StringComparison.OrdinalIgnoreCase) ||
                !StatesPromptHash.Equals(root.GetProperty("statesPromptHash").GetString(), StringComparison.OrdinalIgnoreCase) ||
                !ScorePromptHash.Equals(root.GetProperty("scorePromptHash").GetString(), StringComparison.OrdinalIgnoreCase) ||
                !runtime.Model.ManifestSha256.Equals(root.GetProperty("modelHash").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Scene review output identity changed.");
            var rows = root.GetProperty("cases").EnumerateArray().ToArray();
            if (rows.Length != request.Requests.Count) throw new InvalidDataException("Scene review omitted a case.");
            List<VisualSemanticEditorialResult> results = [];
            List<VisualSemanticEditorialFailure> failures = [];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var item = request.Requests[i];
                if (row.GetProperty("caseId").GetString() != item.CaseId ||
                    !item.Input.ReviewVideoSha256.Equals(row.GetProperty("inputHash").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Scene review case identity changed.");
                var elapsed = TimeSpan.FromSeconds(row.GetProperty("elapsedSeconds").GetDouble());
                if (row.GetProperty("status").GetString() == "Failed")
                {
                    string errorCode = row.GetProperty("errorCode").GetString()!;
                    bool exhaustedGrounding = errorCode == "SceneClaimsUnverifiedError";
                    failures.Add(new(item, exhaustedGrounding ? "SceneGrounding" : "SceneReview", errorCode, elapsed,
                        CanRetry: !exhaustedGrounding));
                    continue;
                }
                if (row.GetProperty("status").GetString() != "Succeeded") throw new InvalidDataException("Unknown scene status.");
                try
                {
                    if (!row.GetProperty("factReview").GetProperty("grounded").GetBoolean())
                        throw new InvalidDataException("Scene claims did not pass the separate frame check.");
                    ValidateNeuralValue(row.GetProperty("neuralValue"), row.GetProperty("assessment").GetProperty("editorialValue").GetDouble());
                    results.Add(ParseAssessment(item, row.GetProperty("assessment"), row.GetProperty("frameTimes"), elapsed));
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
                { failures.Add(new(item, "SceneValidation", exception.GetType().Name, elapsed)); }
            }
            if (failures.Count > 0)
                new SystemQwen3VlGroundedFailureArchive().Archive(output, 2_097_152);
            return new(request, results, process.Duration, root.GetProperty("peakAllocatedGpuBytes").GetInt64(), failures);
        }
        finally
        {
            // Only this newly created private workspace is owned by this operation.
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static VisualSemanticEditorialResult ParseAssessment(VisualSemanticRequest request, JsonElement value, JsonElement sampledTimes, TimeSpan elapsed)
    {
        var times = sampledTimes.EnumerateArray().Select(item => item.GetDouble()).ToArray();
        if (times.Length != FrameCount || times.Any(time => !double.IsFinite(time) || time < request.CandidateStartRelative.TotalSeconds ||
                time >= request.CandidateEndRelative.TotalSeconds) || !times.SequenceEqual(times.Order()) || times.Distinct().Count() != times.Length)
            throw new InvalidDataException("Scene evidence times are not bounded chronological samples.");
        int first = value.GetProperty("firstFrame").GetInt32(), last = value.GetProperty("lastFrame").GetInt32();
        if (first is < 0 or > 1 || last < FrameCount-2 || last >= FrameCount)
            throw new InvalidDataException("Scene evidence does not cite the independently described opening and closing frames.");
        T EnumValue<T>(string key) where T : struct, Enum => Enum.TryParse<T>(value.GetProperty(key).GetString(), out var result) && Enum.IsDefined(result)
            ? result : throw new InvalidDataException("Unknown scene label.");
        var distinct = EnumValue<VisualSemanticTernary>("hasDistinctEvent");
        var payoff = EnumValue<VisualSemanticTernary>("hasPayoff");
        var support = EnumValue<VisualSemanticTranscriptContextSupport>("transcriptSupport");
        if (!request.Transcript.Spans.Any(span => !span.IsNonSpeech) && support != VisualSemanticTranscriptContextSupport.NotSupplied)
            throw new InvalidDataException("Scene review invented speech support.");
        string setup = value.GetProperty("setup").GetString()!.Trim(), outcome = value.GetProperty("outcome").GetString()!.Trim();
        string centralEvent = value.GetProperty("event").GetString()!.Trim();
        var intervals = new[]
        {
            new VisualSemanticEditorialEvidenceInterval("setup", TimeSpan.FromSeconds(times[first]), TimeSpan.FromSeconds(times[first]), setup, VisualSemanticEvidenceBasis.Visual),
            new VisualSemanticEditorialEvidenceInterval("event", TimeSpan.FromSeconds(times[0]), TimeSpan.FromSeconds(times[^1]), centralEvent, VisualSemanticEvidenceBasis.Visual),
            new VisualSemanticEditorialEvidenceInterval("outcome", TimeSpan.FromSeconds(times[last]), TimeSpan.FromSeconds(times[last]), outcome, VisualSemanticEvidenceBasis.Visual)
        };
        var canonical = VisualSemanticEditorialCanonicalizer.Canonicalize(
            [new(setup, VisualSemanticEvidenceBasis.Visual, ["setup"]), new(centralEvent, VisualSemanticEvidenceBasis.Visual, ["event"]),
                new(outcome, VisualSemanticEvidenceBasis.Visual, ["outcome"])], intervals, []);
        VisualSemanticEditorialObservation Make(VisualSemanticEditorialDisposition disposition, VisualSemanticEditorialRejectReason reason,
            IEnumerable<VisualSemanticEditorialUncertainty> uncertainty) => new(
                EnumValue<VisualSemanticObservableContentType>("kind"), distinct, payoff,
                EnumValue<VisualSemanticTernary>("onlyRoutineMovementOrMenus"), EnumValue<VisualSemanticTernary>("needsEarlierContext"), EnumValue<VisualSemanticTernary>("onlyLightingOrCameraChanges"),
                support, canonical.ObservedChanges, canonical.EvidenceIntervals, uncertainty, disposition, reason, outcome);
        var recommendation = EnumValue<VisualSemanticEditorialDisposition>("recommendation");
        var observation = Make(recommendation, recommendation == VisualSemanticEditorialDisposition.Keep
            ? VisualSemanticEditorialRejectReason.None : VisualSemanticEditorialRejectReason.InsufficientEvidence,
            recommendation == VisualSemanticEditorialDisposition.Unsure
                ? [new(VisualSemanticEditorialUncertaintyCode.InsufficientVisualEvidence, "The scene model expressed uncertainty about this moment.")]
                : []);
        return new(request, observation, canonical.Audit with { WireRepresentationVersion = Version }, elapsed,
            value.GetProperty("editorialValue").GetDouble() / 100d);
    }

    internal static void ValidateNeuralValue(JsonElement value, double editorialValue, string expectedVersion = "scene-value-1")
    {
        var margins = value.GetProperty("margins").EnumerateArray().Select(item => item.GetDouble()).ToArray();
        if (value.GetProperty("version").GetString() != expectedVersion || value.GetProperty("calibrated").GetBoolean() ||
            margins.Length != 2 || margins.Any(item => !double.IsFinite(item)))
            throw new InvalidDataException("Neural relevance identity or logits changed.");
        double expected = margins.Average(margin => margin >= 0 ? 1/(1+Math.Exp(-margin)) : Math.Exp(margin)/(1+Math.Exp(margin)));
        double actual = value.GetProperty("value").GetDouble();
        if (!double.IsFinite(actual) || !double.IsFinite(editorialValue) || Math.Abs(actual-expected) > 1e-9 ||
            Math.Abs(editorialValue-expected*100) > 1e-7)
            throw new InvalidDataException("The relevance score does not match the recorded neural comparisons.");
    }
}
