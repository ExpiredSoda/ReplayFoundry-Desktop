using System.Collections.ObjectModel;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal enum Qwen3VlProviderCaseAttemptStatus
{
    Succeeded = 0,
    Failed = 1,
}

internal enum Qwen3VlProviderCaseAttemptStage
{
    Completed = 0,
    VideoSampling = 1,
    Generation = 2,
    OutputSafety = 3,
    OutputValidation = 4,
    MediaRevalidation = 5,
    Unknown = 6,
}

internal sealed record Qwen3VlProviderCaseAttemptFailure
{
    public Qwen3VlProviderCaseAttemptFailure(
        string errorCode,
        string message,
        string? rawGeneratedTextSha256 = null,
        string? providerEchoCaseId = null,
        string? providerEchoCandidateId = null)
    {
        ErrorCode = VisualSemanticContractText.Required(
            errorCode,
            nameof(errorCode),
            128);

        if (ErrorCode is not (
                "InferenceError" or
                "GenerationTokenBudgetExceededError" or
                "UnexpectedGenerationTerminationError"
            ))
        {
            throw new ArgumentException(
                "A provider case attempt may retain only a supported case-local error code.",
                nameof(errorCode));
        }

        Message = VisualSemanticContractText.Required(
            message,
            nameof(message),
            2_000);
        RawGeneratedTextSha256 =
            rawGeneratedTextSha256 is null
                ? null
                : ModelArtifactManifest.Sha256Value(
                    rawGeneratedTextSha256,
                    nameof(rawGeneratedTextSha256));
        ProviderEchoCaseId = VisualSemanticContractText.Optional(
            providerEchoCaseId,
            nameof(providerEchoCaseId),
            128);
        ProviderEchoCandidateId = VisualSemanticContractText.Optional(
            providerEchoCandidateId,
            nameof(providerEchoCandidateId),
            128);

        if (ProviderEchoCaseId is not null &&
                !VisualSemanticIdentityBindingAudit
                    .IsStableProviderIdentifier(
                        ProviderEchoCaseId) ||
            ProviderEchoCandidateId is not null &&
                !VisualSemanticIdentityBindingAudit
                    .IsStableProviderIdentifier(
                        ProviderEchoCandidateId))
        {
            throw new ArgumentException(
                "Retained provider failure echoes must use the exact stable identifier syntax.");
        }
    }

    public string ErrorCode { get; }

    public string Message { get; }

    public string? RawGeneratedTextSha256 { get; }

    public string? ProviderEchoCaseId { get; }

    public string? ProviderEchoCandidateId { get; }
}

internal sealed class Qwen3VlProviderCaseAttempt
{
    public Qwen3VlProviderCaseAttempt(
        string caseId,
        string candidateId,
        int caseOrdinal,
        Qwen3VlProviderCaseAttemptStatus status,
        Qwen3VlProviderCaseAttemptStage stage,
        VisualSemanticObservation? observation,
        TimeSpan? elapsed,
        VisualSemanticIdentityBindingAudit? identityBindingAudit,
        VisualSemanticOutputNormalizationAudit? normalizationAudit,
        VisualSemanticCaseGenerationManifest? generation,
        VisualSemanticCaseExecutionTiming? executionTiming,
        Qwen3VlProviderCaseAttemptFailure? failure)
    {
        if (caseOrdinal <= 0 ||
            !Enum.IsDefined(status) ||
            !Enum.IsDefined(stage) ||
            elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(caseOrdinal));
        }

        CaseId = VisualSemanticContractText.Required(
            caseId,
            nameof(caseId),
            128);
        CandidateId = VisualSemanticContractText.Required(
            candidateId,
            nameof(candidateId),
            128);

        bool succeeded =
            status == Qwen3VlProviderCaseAttemptStatus.Succeeded;
        if (succeeded)
        {
            if (stage != Qwen3VlProviderCaseAttemptStage.Completed ||
                observation is null ||
                elapsed is null ||
                identityBindingAudit is null ||
                generation is null ||
                generation.TerminationReason !=
                    VisualSemanticGenerationTerminationReason
                        .EndOfSequence ||
                executionTiming is null ||
                failure is not null)
            {
                throw new ArgumentException(
                    "A successful provider attempt requires complete observation, identity, EOS generation, and timing data.");
            }
        }
        else if (observation is not null ||
                 identityBindingAudit is not null ||
                 normalizationAudit is not null ||
                 failure is null ||
                 stage == Qwen3VlProviderCaseAttemptStage.Completed)
        {
            throw new ArgumentException(
                "A failed provider attempt must contain only bounded failure and any available execution telemetry.");
        }

        if (!succeeded)
        {
            bool stageAndErrorMatch =
                failure!.ErrorCode switch
                {
                    "InferenceError" =>
                        stage is
                            Qwen3VlProviderCaseAttemptStage
                                .OutputSafety or
                            Qwen3VlProviderCaseAttemptStage
                                .OutputValidation,
                    "GenerationTokenBudgetExceededError" or
                    "UnexpectedGenerationTerminationError" =>
                        stage ==
                        Qwen3VlProviderCaseAttemptStage
                            .Generation,
                    _ => false,
                };

            if (!stageAndErrorMatch)
            {
                throw new ArgumentException(
                    "A failed provider attempt stage must match its supported case-local error code.");
            }

            if (failure.ErrorCode ==
                    "GenerationTokenBudgetExceededError" &&
                (generation is null ||
                 generation.GeneratedTokenCount !=
                    generation.MaximumNewTokens ||
                 generation.TerminationReason is not (
                    VisualSemanticGenerationTerminationReason
                        .MaximumNewTokensReached or
                    VisualSemanticGenerationTerminationReason
                        .EndOfSequence)))
            {
                throw new ArgumentException(
                    "A generation-budget attempt failure requires complete full-budget generation telemetry.");
            }

            if (failure.ErrorCode == "InferenceError" &&
                generation is not null &&
                (generation.TerminationReason !=
                    VisualSemanticGenerationTerminationReason
                        .EndOfSequence ||
                 generation.GeneratedTokenCount >=
                    generation.MaximumNewTokens))
            {
                throw new ArgumentException(
                    "An output-validation inference failure may retain only EOS-complete generation telemetry below the approved ceiling.");
            }

            if (failure.ErrorCode ==
                    "UnexpectedGenerationTerminationError" &&
                (generation is null ||
                 generation.TerminationReason !=
                    VisualSemanticGenerationTerminationReason
                        .UnexpectedStop ||
                 generation.GeneratedTokenCount >=
                    generation.MaximumNewTokens))
            {
                throw new ArgumentException(
                    "An unexpected-generation attempt failure requires complete early non-EOS telemetry.");
            }

            if (failure.RawGeneratedTextSha256 is not null &&
                generation is not null &&
                !string.Equals(
                    failure.RawGeneratedTextSha256,
                    generation.DecodedTextSha256,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Attempt failure raw-text identity must match retained generation telemetry.");
            }
        }

        if (observation is not null &&
            (!string.Equals(
                 observation.CaseId,
                 CaseId,
                 StringComparison.Ordinal) ||
             !string.Equals(
                 observation.CandidateId,
                 CandidateId,
                 StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Attempt observation identity must match the trusted request identity.");
        }

        if (identityBindingAudit is not null &&
            (!string.Equals(
                 identityBindingAudit.TrustedCaseId,
                 CaseId,
                 StringComparison.Ordinal) ||
             !string.Equals(
                 identityBindingAudit.TrustedCandidateId,
                 CandidateId,
                 StringComparison.Ordinal) ||
             identityBindingAudit.CaseOrdinal != caseOrdinal))
        {
            throw new ArgumentException(
                "Attempt identity audit must belong to the same trusted request.");
        }

        if (normalizationAudit is not null &&
            !string.Equals(
                normalizationAudit.CaseId,
                CaseId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Attempt normalization audit must belong to the same trusted request.");
        }

        if (generation is not null &&
            (!string.Equals(
                 generation.CaseId,
                 CaseId,
                 StringComparison.Ordinal) ||
             !string.Equals(
                 generation.CandidateId,
                 CandidateId,
                 StringComparison.Ordinal) ||
             generation.CaseOrdinal != caseOrdinal))
        {
            throw new ArgumentException(
                "Attempt generation telemetry must belong to the same trusted request.");
        }

        if (executionTiming is not null &&
            (!string.Equals(
                 executionTiming.CaseId,
                 CaseId,
                 StringComparison.Ordinal) ||
             !string.Equals(
                 executionTiming.CandidateId,
                 CandidateId,
                 StringComparison.Ordinal) ||
             executionTiming.CaseOrdinal != caseOrdinal))
        {
            throw new ArgumentException(
                "Attempt execution timing must belong to the same trusted request.");
        }

        CaseOrdinal = caseOrdinal;
        Status = status;
        Stage = stage;
        Observation = observation;
        Elapsed = elapsed;
        IdentityBindingAudit = identityBindingAudit;
        NormalizationAudit = normalizationAudit;
        Generation = generation;
        ExecutionTiming = executionTiming;
        Failure = failure;
    }

    public string CaseId { get; }

    public string CandidateId { get; }

    public int CaseOrdinal { get; }

    public Qwen3VlProviderCaseAttemptStatus Status { get; }

    public Qwen3VlProviderCaseAttemptStage Stage { get; }

    public VisualSemanticObservation? Observation { get; }

    public TimeSpan? Elapsed { get; }

    public VisualSemanticIdentityBindingAudit? IdentityBindingAudit { get; }

    public VisualSemanticOutputNormalizationAudit? NormalizationAudit { get; }

    public VisualSemanticCaseGenerationManifest? Generation { get; }

    public VisualSemanticCaseExecutionTiming? ExecutionTiming { get; }

    public Qwen3VlProviderCaseAttemptFailure? Failure { get; }
}

internal sealed class Qwen3VlProviderAttemptBatch
{
    public const string SupportedSchemaVersion =
        "visual-semantic-provider-attempt-batch-1.0";
    public const string SupportedHostVersion = "0.5A.9";

    private readonly ReadOnlyCollection<Qwen3VlProviderCaseAttempt> _cases;

    public Qwen3VlProviderAttemptBatch(
        string schemaVersion,
        string hostVersion,
        string modelRepository,
        string modelRevision,
        string device,
        string backend,
        IEnumerable<Qwen3VlProviderCaseAttempt> cases,
        long? peakAllocatedGpuBytes,
        TimeSpan totalElapsed,
        string canonicalAttemptSha256,
        string validatedJson)
    {
        ArgumentNullException.ThrowIfNull(cases);
        Qwen3VlProviderCaseAttempt[] snapshot = cases.ToArray();

        if (!string.Equals(
                schemaVersion,
                SupportedSchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                hostVersion,
                SupportedHostVersion,
                StringComparison.Ordinal) ||
            snapshot.Length == 0 ||
            snapshot.Any(static value => value is null) ||
            !snapshot.Select(static value => value.CaseOrdinal)
                .SequenceEqual(Enumerable.Range(1, snapshot.Length)) ||
            snapshot.Select(static value => value.CaseId)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            snapshot.Select(static value => value.CandidateId)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            peakAllocatedGpuBytes < 0 ||
            totalElapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Provider attempt batch identity, ordering, counts, or timing is invalid.");
        }

        if (string.IsNullOrWhiteSpace(validatedJson))
        {
            throw new ArgumentException(
                "A provider attempt batch must retain its exact validated JSON root.",
                nameof(validatedJson));
        }

        SchemaVersion = schemaVersion;
        HostVersion = hostVersion;
        ModelRepository = VisualSemanticContractText.Required(
            modelRepository,
            nameof(modelRepository),
            256);
        ModelRevision = VisualSemanticContractText.Required(
            modelRevision,
            nameof(modelRevision),
            256);
        Device = VisualSemanticContractText.Required(
            device,
            nameof(device),
            256);
        Backend = VisualSemanticContractText.Required(
            backend,
            nameof(backend),
            128);
        _cases = Array.AsReadOnly(snapshot);
        PeakAllocatedGpuBytes = peakAllocatedGpuBytes;
        TotalElapsed = totalElapsed;
        CanonicalAttemptSha256 = ModelArtifactManifest.Sha256Value(
            canonicalAttemptSha256,
            nameof(canonicalAttemptSha256));
        ValidatedJson = validatedJson;
    }

    public string SchemaVersion { get; }

    public string HostVersion { get; }

    public string ModelRepository { get; }

    public string ModelRevision { get; }

    public string Device { get; }

    public string Backend { get; }

    public IReadOnlyList<Qwen3VlProviderCaseAttempt> Cases => _cases;

    public int SuccessCount =>
        _cases.Count(
            static value =>
                value.Status ==
                Qwen3VlProviderCaseAttemptStatus.Succeeded);

    public int FailureCount => _cases.Count - SuccessCount;

    public long? PeakAllocatedGpuBytes { get; }

    public TimeSpan TotalElapsed { get; }

    public string CanonicalAttemptSha256 { get; }

    public string ValidatedJson { get; }

    public bool IsCompleteSuccess => FailureCount == 0;
}

internal sealed record Qwen3VlObservationWithAttemptResult(
    VisualSemanticBatchResult Result,
    Qwen3VlProviderAttemptBatch AttemptBatch);
