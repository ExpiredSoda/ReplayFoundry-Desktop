using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed class Qwen3VlQualifiedEditorialSettings
{
    public Qwen3VlQualifiedEditorialSettings(
        Qwen3VlBatchHostSettings host,
        string qualificationLockPath,
        string qualificationLockCanonicalHash)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        if (string.IsNullOrWhiteSpace(qualificationLockPath) ||
            !Path.IsPathFullyQualified(qualificationLockPath) ||
            !File.Exists(qualificationLockPath))
        {
            throw new ArgumentException(
                "Qualified Qwen requires an explicit existing qualification lock.",
                nameof(qualificationLockPath));
        }

        QualificationLockPath = Path.GetFullPath(qualificationLockPath);
        QualificationLockCanonicalHash =
            string.IsNullOrWhiteSpace(qualificationLockCanonicalHash) ||
            qualificationLockCanonicalHash.Length != 64 ||
            qualificationLockCanonicalHash.Any(static value =>
                !Uri.IsHexDigit(value))
                ? throw new ArgumentException(
                    "Qualified Qwen requires the verified qualification-lock canonical hash.",
                    nameof(qualificationLockCanonicalHash))
                : qualificationLockCanonicalHash.ToLowerInvariant();
    }

    public Qwen3VlBatchHostSettings Host { get; }

    public string QualificationLockPath { get; }

    public string QualificationLockCanonicalHash { get; }
}

public sealed class Qwen3VlQualifiedEditorialProvider :
    IVisualSemanticEditorialProvider
{
    private const int MaximumCases = 8;
    private readonly Qwen3VlQualifiedEditorialSettings _settings;
    private readonly IProcessRunner _processRunner;
    private readonly IQwen3VlBatchWorkspaceFactory _workspaceFactory;

    public Qwen3VlQualifiedEditorialProvider(
        Qwen3VlQualifiedEditorialSettings settings)
        : this(
            settings,
            new WindowsProcessRunner(),
            new SystemQwen3VlBatchWorkspaceFactory())
    {
    }

    internal Qwen3VlQualifiedEditorialProvider(
        Qwen3VlQualifiedEditorialSettings settings,
        IProcessRunner processRunner,
        IQwen3VlBatchWorkspaceFactory workspaceFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
    }

    public InferenceProviderIdentity Identity { get; } = new(
        "Qwen3-VL qualified compact editorial",
        "2.7",
        "0.10.0");

    public async Task<VisualSemanticEditorialBatchResult> ObserveAsync(
        VisualSemanticBatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Requests.Count > MaximumCases ||
            !string.Equals(
                request.Prompt.Version,
                VisualSemanticPromptManifest.QualifiedEditorialVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Prompt.Sha256,
                Qwen3VlQualifiedEditorialProtocol.PromptTextSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                request.Model.ManifestSha256,
                Qwen3VlQualifiedEditorialProtocol.ModelManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Qualified Qwen accepts at most eight cases with its frozen prompt and model.",
                nameof(request));
        }

        Qwen3VlBatchWorkspace workspace = _workspaceFactory.Create();
        try
        {
            foreach (VisualSemanticRequest item in request.Requests)
            {
                await item.Input.VerifyIntegrityAsync(cancellationToken);
            }
            await VerifyInstalledModelAsync(
                request.Model,
                cancellationToken);
            await Qwen3VlBatchRequestJsonWriter.WriteQualifiedEditorialAsync(
                workspace.InputBatchPath,
                request,
                cancellationToken);
            Qwen3VlBatchCommand command =
                Qwen3VlBatchCommandBuilder.BuildQualifiedEditorialRun(
                    _settings.Host,
                    workspace,
                    _settings.QualificationLockPath);
            ProcessRunResult process = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    _settings.Host.PythonExecutablePath,
                    command.Arguments,
                    _settings.Host.ProcessTimeout,
                    workspace.DirectoryPath,
                    _settings.Host.MaximumStandardOutputCharacters,
                    _settings.Host.MaximumStandardErrorCharacters,
                    _settings.Host.EnvironmentVariables),
                cancellationToken);
            if (!process.Succeeded)
            {
                throw new Qwen3VlInferenceException(
                    "The qualified local Qwen observation batch failed.",
                    Qwen3VlProcessOutputReader.Diagnostics(process));
            }

            string json = await Qwen3VlProcessOutputReader.ReadAsync(
                workspace.OutputBatchPath,
                _settings.Host.MaximumStructuredOutputBytes,
                cancellationToken);
            VisualSemanticEditorialBatchResult result = Parse(
                json,
                request,
                _settings.QualificationLockCanonicalHash);
            foreach (VisualSemanticRequest item in request.Requests)
            {
                await item.Input.VerifyIntegrityAsync(cancellationToken);
            }
            await VerifyInstalledModelAsync(
                request.Model,
                cancellationToken);
            return result;
        }
        finally
        {
            workspace.Cleanup();
        }
    }

    private static Task VerifyInstalledModelAsync(
        VisualSemanticModelManifest model,
        CancellationToken cancellationToken) =>
        RunModelIntegrityVerificationAsync(
            model.VerifyInstalledFiles,
            cancellationToken);

    internal static Task RunModelIntegrityVerificationAsync(
        Action<CancellationToken> verify,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verify);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => verify(cancellationToken),
            cancellationToken);
    }

    internal static VisualSemanticEditorialBatchResult Parse(
        string json,
        VisualSemanticBatchRequest request) => Parse(
            json,
            request,
            Qwen3VlQualifiedEditorialProtocol
                .QualificationLockCanonicalHash);

    internal static VisualSemanticEditorialBatchResult Parse(
        string json,
        VisualSemanticBatchRequest request,
        string expectedQualificationLockCanonicalHash)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Qwen3VlEditorialJson.Exact(
            root,
            "schemaVersion",
            "policyVersion",
            "qualificationLockCanonicalHash",
            "cudaAttentionPolicy",
            "attemptCanonicalHash",
            "results",
            "peakAllocatedGpuBytes",
            "totalElapsedSeconds",
            "canonicalHash");
        RequireText(root, "schemaVersion", "visual-semantic-qualified-observation-batch-1.1");
        RequireText(root, "policyVersion", Qwen3VlEditorialStructuredDecodingPolicy.Version);
        RequireText(
            root,
            "qualificationLockCanonicalHash",
            expectedQualificationLockCanonicalHash);
        Qwen3VlQualifiedCudaAttentionPolicy.Validate(
            Qwen3VlEditorialJson.Object(root, "cudaAttentionPolicy"));
        string canonicalHash = Qwen3VlEditorialJson.Text(root, "canonicalHash");
        string computed = Qwen3VlCanonicalJson.ComputeObjectSha256(root, "canonicalHash");
        if (!string.Equals(canonicalHash, computed, StringComparison.OrdinalIgnoreCase))
        {
            throw new Qwen3VlOutputParseException(
                "Qualified Qwen output canonical hash is invalid.");
        }

        JsonElement resultsElement = Qwen3VlEditorialJson.Property(root, "results");
        if (resultsElement.ValueKind != JsonValueKind.Array ||
            resultsElement.GetArrayLength() != request.Requests.Count)
        {
            throw new Qwen3VlOutputParseException(
                "Qualified Qwen output does not preserve every request.");
        }

        var results = new List<VisualSemanticEditorialResult>();
        int index = 0;
        foreach (JsonElement row in resultsElement.EnumerateArray())
        {
            VisualSemanticRequest expected = request.Requests[index];
            Qwen3VlEditorialJson.Exact(
                row,
                "caseId", "candidateId", "caseOrdinal", "runKind", "status", "stage",
                "observation", "canonicalizationAudit", "requestBinding", "generation",
                "executionTiming", "sampling", "elapsedSeconds", "failure", "notRunReason",
                "structuredDecodingAudit");
            RequireText(row, "caseId", expected.CaseId);
            RequireText(row, "candidateId", expected.CandidateId);
            RequireText(row, "status", "Succeeded");
            RequireText(row, "stage", "Completed");
            ValidateCompletedGeneration(row, expected, index + 1);
            JsonElement observationElement = Qwen3VlEditorialJson.Property(row, "observation");
            Qwen3VlParsedEditorialObservation parsed =
                Qwen3VlBatchResultParser.ParseEditorialObservation(
                    observationElement.GetRawText(),
                    expected.Input.ReviewVideoDuration,
                    expected.CandidateStartRelative,
                    expected.CandidateEndRelative);
            VisualSemanticEditorialCanonicalizationAudit audit =
                Qwen3VlEditorialCanonicalizationAuditParser.Read(
                    Qwen3VlEditorialJson.Property(row, "canonicalizationAudit"),
                    parsed.Observation);
            results.Add(new VisualSemanticEditorialResult(
                expected,
                parsed.Observation,
                audit,
                Seconds(row, "elapsedSeconds")));
            index++;
        }

        long? peak = Qwen3VlEditorialJson.Property(root, "peakAllocatedGpuBytes")
            .ValueKind == JsonValueKind.Null
                ? null
                : Qwen3VlEditorialJson.Property(root, "peakAllocatedGpuBytes").GetInt64();
        return new VisualSemanticEditorialBatchResult(
            request,
            results,
            Seconds(root, "totalElapsedSeconds"),
            peak);
    }

    private static void ValidateCompletedGeneration(
        JsonElement row,
        VisualSemanticRequest expected,
        int expectedOrdinal)
    {
        if (Qwen3VlEditorialJson.Integer(row, "caseOrdinal") != expectedOrdinal ||
            !string.Equals(
                Qwen3VlEditorialJson.Text(row, "runKind"),
                "Primary",
                StringComparison.Ordinal) ||
            Qwen3VlEditorialJson.Property(row, "failure").ValueKind != JsonValueKind.Null ||
            Qwen3VlEditorialJson.Property(row, "notRunReason").ValueKind != JsonValueKind.Null)
        {
            throw new Qwen3VlOutputParseException(
                "Qualified Qwen output changed case ownership or reported a hidden failure.");
        }

        JsonElement binding = Qwen3VlEditorialJson.Object(row, "requestBinding");
        Qwen3VlEditorialJson.Exact(
            binding,
            "caseId", "candidateId", "caseOrdinal", "runKind",
            "semanticPayloadSha256", "trustedEnvelopeSha256", "boundAtUtc");
        RequireText(binding, "caseId", expected.CaseId);
        RequireText(binding, "candidateId", expected.CandidateId);
        RequireText(binding, "runKind", "Primary");
        if (Qwen3VlEditorialJson.Integer(binding, "caseOrdinal") != expectedOrdinal)
        {
            throw new Qwen3VlOutputParseException(
                "Qualified Qwen request binding changed case order.");
        }
        _ = Qwen3VlEditorialJson.Sha256(binding, "semanticPayloadSha256");
        _ = Qwen3VlEditorialJson.Sha256(binding, "trustedEnvelopeSha256");
        _ = Qwen3VlEditorialJson.Utc(binding, "boundAtUtc");

        JsonElement generation = Qwen3VlEditorialJson.Object(row, "generation");
        Qwen3VlEditorialJson.Exact(
            generation,
            "caseId", "candidateId", "caseOrdinal", "inputTokenCount",
            "generatedTokenCount", "maximumNewTokens", "endOfSequenceTokenIds",
            "firstEndOfSequenceGeneratedIndex", "terminalTokenId",
            "terminationReason", "generatedTokenIdsSha256",
            "legacyPrefixTokenCount", "legacyPrefixTokenIdsSha256",
            "decodedTextSha256", "decodedTextUtf8ByteCount");
        RequireText(generation, "caseId", expected.CaseId);
        RequireText(generation, "candidateId", expected.CandidateId);
        int generated = Qwen3VlEditorialJson.Integer(generation, "generatedTokenCount");
        int maximum = Qwen3VlEditorialJson.Integer(generation, "maximumNewTokens");
        int firstEos = Qwen3VlEditorialJson.Integer(
            generation,
            "firstEndOfSequenceGeneratedIndex");
        int terminal = Qwen3VlEditorialJson.Integer(generation, "terminalTokenId");
        int[] eosIds = Qwen3VlEditorialJson.Array(generation, "endOfSequenceTokenIds")
            .Select(value => value.TryGetInt32(out int parsed)
                ? parsed
                : throw new Qwen3VlOutputParseException(
                    "Qualified Qwen EOS IDs must be integers."))
            .ToArray();
        if (Qwen3VlEditorialJson.Integer(generation, "caseOrdinal") != expectedOrdinal ||
            generated <= 0 ||
            maximum != VisualSemanticGenerationBudgetPolicy.ActiveMaximumNewTokens ||
            generated >= maximum ||
            firstEos != generated - 1 ||
            !eosIds.Contains(terminal) ||
            !string.Equals(
                Qwen3VlEditorialJson.Text(generation, "terminationReason"),
                "EndOfSequence",
                StringComparison.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                "Qualified Qwen generation was not bounded EOS completion.");
        }
        _ = Qwen3VlEditorialJson.Sha256(generation, "generatedTokenIdsSha256");
        _ = Qwen3VlEditorialJson.Sha256(generation, "legacyPrefixTokenIdsSha256");
        _ = Qwen3VlEditorialJson.Sha256(generation, "decodedTextSha256");

        JsonElement audit = Qwen3VlEditorialJson.Object(row, "structuredDecodingAudit");
        Qwen3VlEditorialJson.Exact(
            audit,
            "policyVersion", "backendName", "backendVersion", "schemaVersion",
            "schemaSha256", "representation", "cudaMaskBackend",
            "compileElapsedSeconds", "generatedTokenCount",
            "grammarTerminationState", "strictParserAccepted",
            "unconstrainedFallbackUsed", "semanticRepairApplied");
        RequireText(audit, "policyVersion", Qwen3VlEditorialStructuredDecodingPolicy.Version);
        RequireText(audit, "backendName", Qwen3VlEditorialStructuredDecodingPolicy.BackendName);
        RequireText(audit, "backendVersion", Qwen3VlEditorialStructuredDecodingPolicy.BackendVersion);
        RequireText(audit, "schemaVersion", Qwen3VlEditorialStructuredDecodingPolicy.SchemaVersion);
        RequireText(audit, "representation", Qwen3VlEditorialStructuredDecodingPolicy.Representation.ToString());
        RequireText(audit, "cudaMaskBackend", Qwen3VlEditorialStructuredDecodingPolicy.CudaMaskBackend);
        _ = Qwen3VlEditorialJson.Sha256(audit, "schemaSha256");
        _ = Qwen3VlEditorialJson.Finite(audit, "compileElapsedSeconds");
        if (Qwen3VlEditorialJson.Integer(audit, "generatedTokenCount") != generated ||
            !string.Equals(
                Qwen3VlEditorialJson.Text(audit, "grammarTerminationState"),
                "EndOfSequence",
                StringComparison.Ordinal) ||
            !Boolean(audit, "strictParserAccepted") ||
            Boolean(audit, "unconstrainedFallbackUsed") ||
            Boolean(audit, "semanticRepairApplied"))
        {
            throw new Qwen3VlOutputParseException(
                "Qualified Qwen structured decoding was not strict or reported fallback/repair.");
        }
    }

    private static bool Boolean(JsonElement value, string name)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, name);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new Qwen3VlOutputParseException(
                $"Qualified Qwen '{name}' must be Boolean."),
        };
    }

    private static void RequireText(JsonElement value, string name, string expected)
    {
        string actual = Qwen3VlEditorialJson.Text(value, name);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new Qwen3VlOutputParseException(
                $"Qualified Qwen '{name}' changed.");
        }
    }

    private static TimeSpan Seconds(JsonElement value, string name)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, name);
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out double seconds) ||
            !double.IsFinite(seconds) || seconds < 0)
        {
            throw new Qwen3VlOutputParseException(
                $"Qualified Qwen '{name}' must be non-negative seconds.");
        }
        return TimeSpan.FromSeconds(seconds);
    }
}
