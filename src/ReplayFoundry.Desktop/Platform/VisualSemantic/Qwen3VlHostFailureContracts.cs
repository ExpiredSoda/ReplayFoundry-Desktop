using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public enum Qwen3VlHostCommand
{
    Probe = 0,
    Run = 1,
    AuditVideoSampling = 2,
}

public enum Qwen3VlHostFailureStage
{
    ArgumentValidation = 0,
    PathValidation = 1,
    LibraryConfiguration = 2,
    InputLoading = 3,
    InputValidation = 4,
    RuntimeInitialization = 5,
    VideoSampling = 6,
    DirectTorchCodecDecode = 7,
    SamplingComparison = 8,
    ModelInitialization = 9,
    Inference = 10,
    Generation = 11,
    OutputSafety = 12,
    OutputValidation = 13,
    MediaRevalidation = 14,
    OutputWrite = 15,
}

public enum Qwen3VlHostErrorCode
{
    UsageOrInputError = 0,
    InitializationError = 1,
    InferenceError = 2,
    OutputError = 3,
    RawAuditCaptured = 4,
    Cancelled = 5,
    UnexpectedHostFailure = 6,
    NetworkProhibitedError = 7,
    GenerationTokenBudgetExceededError = 8,
    UnexpectedGenerationTerminationError = 9,
    ProviderCaseFailuresDetected = 10,
    GenerationWallClockBudgetExceededError = 11,
}

public sealed record Qwen3VlHostFailureCase
{
    internal Qwen3VlHostFailureCase(
        string caseId,
        string candidateId,
        int caseOrdinal)
    {
        CaseId = caseId;
        CandidateId = candidateId;
        CaseOrdinal = caseOrdinal;
    }

    public string CaseId { get; }

    public string CandidateId { get; }

    public int CaseOrdinal { get; }
}

public sealed record Qwen3VlHostFailureGenerationWatchdog
{
    internal Qwen3VlHostFailureGenerationWatchdog(
        string policyVersion,
        string policySha256,
        double maximumGenerationWallClockSeconds,
        double maximumGroundedCaseWallClockSeconds,
        string timeoutBehavior,
        string? caseId,
        string? candidateId,
        int? caseOrdinal,
        int generationInvocationOrdinal,
        double? effectiveMaximumGenerationWallClockSeconds,
        double? elapsedGenerationWallClockSeconds,
        double? elapsedCaseWallClockSeconds,
        bool triggered,
        string? timeoutReason)
    {
        PolicyVersion = policyVersion;
        PolicySha256 = policySha256;
        MaximumGenerationWallClockSeconds =
            maximumGenerationWallClockSeconds;
        MaximumGroundedCaseWallClockSeconds =
            maximumGroundedCaseWallClockSeconds;
        TimeoutBehavior = timeoutBehavior;
        CaseId = caseId;
        CandidateId = candidateId;
        CaseOrdinal = caseOrdinal;
        GenerationInvocationOrdinal = generationInvocationOrdinal;
        EffectiveMaximumGenerationWallClockSeconds =
            effectiveMaximumGenerationWallClockSeconds;
        ElapsedGenerationWallClockSeconds =
            elapsedGenerationWallClockSeconds;
        ElapsedCaseWallClockSeconds = elapsedCaseWallClockSeconds;
        Triggered = triggered;
        TimeoutReason = timeoutReason;
    }

    public string PolicyVersion { get; }

    public string PolicySha256 { get; }

    public double MaximumGenerationWallClockSeconds { get; }

    public double MaximumGroundedCaseWallClockSeconds { get; }

    public string TimeoutBehavior { get; }

    public string? CaseId { get; }

    public string? CandidateId { get; }

    public int? CaseOrdinal { get; }

    public int GenerationInvocationOrdinal { get; }

    public double? EffectiveMaximumGenerationWallClockSeconds { get; }

    public double? ElapsedGenerationWallClockSeconds { get; }

    public double? ElapsedCaseWallClockSeconds { get; }

    public bool Triggered { get; }

    public string? TimeoutReason { get; }
}

public sealed record Qwen3VlHostFailureRecoveryPoolLedgerEntry
{
    internal Qwen3VlHostFailureRecoveryPoolLedgerEntry(
        int candidateOrdinal,
        int seed,
        string? sourceSelectionReason,
        int? sourcePassOrdinal,
        string? sourceRejectedJsonSha256,
        string canonicalMessagesSha256,
        string renderedPromptSha256,
        int renderedPromptUtf8ByteCount,
        string inputTokenIdsSha256,
        int inputTokenCount,
        string outputSha256,
        string completedJsonSha256,
        string? rejectionCode,
        bool accepted)
    {
        CandidateOrdinal = candidateOrdinal;
        Seed = seed;
        SourceSelectionReason = sourceSelectionReason;
        SourcePassOrdinal = sourcePassOrdinal;
        SourceRejectedJsonSha256 = sourceRejectedJsonSha256;
        CanonicalMessagesSha256 = canonicalMessagesSha256;
        RenderedPromptSha256 = renderedPromptSha256;
        RenderedPromptUtf8ByteCount = renderedPromptUtf8ByteCount;
        InputTokenIdsSha256 = inputTokenIdsSha256;
        InputTokenCount = inputTokenCount;
        OutputSha256 = outputSha256;
        CompletedJsonSha256 = completedJsonSha256;
        RejectionCode = rejectionCode;
        Accepted = accepted;
    }

    public int CandidateOrdinal { get; }

    public int Seed { get; }

    public string? SourceSelectionReason { get; }

    public int? SourcePassOrdinal { get; }

    public string? SourceRejectedJsonSha256 { get; }

    public string CanonicalMessagesSha256 { get; }

    public string RenderedPromptSha256 { get; }

    public int RenderedPromptUtf8ByteCount { get; }

    public string InputTokenIdsSha256 { get; }

    public int InputTokenCount { get; }

    public string OutputSha256 { get; }

    public string CompletedJsonSha256 { get; }

    public string? RejectionCode { get; }

    public bool Accepted { get; }
}

public sealed class Qwen3VlHostFailureGeneration
{
    private readonly ReadOnlyCollection<int>
        _endOfSequenceTokenIds;

    internal Qwen3VlHostFailureGeneration(
        string policyVersion,
        string policySha256,
        int maximumNewTokens,
        bool doSample,
        int numberOfBeams,
        bool useCache,
        string caseId,
        string candidateId,
        int caseOrdinal,
        int inputTokenCount,
        int generatedTokenCount,
        int[] endOfSequenceTokenIds,
        int? firstEndOfSequenceGeneratedIndex,
        int terminalTokenId,
        VisualSemanticGenerationTerminationReason
            terminationReason,
        string generatedTokenIdsSha256,
        int legacyPrefixTokenCount,
        string legacyPrefixTokenIdsSha256,
        string decodedTextSha256,
        int decodedTextUtf8ByteCount)
    {
        ArgumentNullException.ThrowIfNull(
            endOfSequenceTokenIds);
        int[] suppliedEndOfSequenceTokenIds =
            endOfSequenceTokenIds.ToArray();
        int[] canonicalEndOfSequenceTokenIds =
            suppliedEndOfSequenceTokenIds
                .Distinct()
                .OrderBy(static value => value)
                .ToArray();
        int expectedLegacyPrefixTokenCount =
            Math.Min(
                VisualSemanticGenerationBudgetPolicy
                    .LegacyDiagnosticMaximumNewTokens,
                generatedTokenCount);
        bool terminalTokenIsEndOfSequence =
            canonicalEndOfSequenceTokenIds.Contains(
                terminalTokenId);
        bool standardGenerationPolicy = string.Equals(
                policyVersion,
                VisualSemanticGenerationBudgetPolicy.Version,
                StringComparison.Ordinal) &&
            string.Equals(
                policySha256,
                VisualSemanticGenerationBudgetPolicy.Sha256,
                StringComparison.OrdinalIgnoreCase) &&
            doSample == VisualSemanticGenerationBudgetPolicy.DoSample &&
            numberOfBeams ==
                VisualSemanticGenerationBudgetPolicy.NumberOfBeams &&
            useCache == VisualSemanticGenerationBudgetPolicy.UseCache;
        bool groundedSampledSynthesisPolicy = string.Equals(
                policyVersion,
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Version,
                StringComparison.Ordinal) &&
            string.Equals(
                policySha256,
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Sha256,
                StringComparison.OrdinalIgnoreCase) &&
            doSample ==
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.DoSample &&
            numberOfBeams ==
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.NumberOfBeams &&
            useCache ==
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.UseCache;
        bool groundedSynthesisRecoveryPoolPolicy = string.Equals(
                policyVersion,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Version,
                StringComparison.Ordinal) &&
            string.Equals(
                policySha256,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Sha256,
                StringComparison.OrdinalIgnoreCase) &&
            doSample ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample &&
            numberOfBeams ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .NumberOfBeams &&
            useCache ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache;
        bool previousCreatorAuthorityRecoveryPoolPolicy = string.Equals(
                policyVersion,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousCreatorAuthorityVersion,
                StringComparison.Ordinal) &&
            string.Equals(
                policySha256,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousCreatorAuthoritySha256,
                StringComparison.OrdinalIgnoreCase) &&
            doSample ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample &&
            numberOfBeams ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .NumberOfBeams &&
            useCache ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache;
        bool previousGroundedSynthesisRecoveryPoolPolicy = string.Equals(
                policyVersion,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousVersion,
                StringComparison.Ordinal) &&
            string.Equals(
                policySha256,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousSha256,
                StringComparison.OrdinalIgnoreCase) &&
            doSample ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample &&
            numberOfBeams ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .NumberOfBeams &&
            useCache ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache;
        bool priorGroundedSynthesisRecoveryPoolPolicy = string.Equals(
                policyVersion,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PriorVersion,
                StringComparison.Ordinal) &&
            string.Equals(
                policySha256,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PriorSha256,
                StringComparison.OrdinalIgnoreCase) &&
            doSample ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample &&
            numberOfBeams ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .NumberOfBeams &&
            useCache ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache;
        bool legacyGroundedSynthesisRecoveryPoolPolicy = string.Equals(
                policyVersion,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .LegacyVersion,
                StringComparison.Ordinal) &&
            string.Equals(
                policySha256,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .LegacySha256,
                StringComparison.OrdinalIgnoreCase) &&
            doSample ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample &&
            numberOfBeams ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .NumberOfBeams &&
            useCache ==
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache;

        if (!standardGenerationPolicy &&
                !groundedSampledSynthesisPolicy &&
                !groundedSynthesisRecoveryPoolPolicy &&
                !previousCreatorAuthorityRecoveryPoolPolicy &&
                !previousGroundedSynthesisRecoveryPoolPolicy &&
                !priorGroundedSynthesisRecoveryPoolPolicy &&
                !legacyGroundedSynthesisRecoveryPoolPolicy ||
            caseOrdinal <= 0 ||
            inputTokenCount <= 0 ||
            generatedTokenCount <= 0 ||
            standardGenerationPolicy && maximumNewTokens is not (
                    VisualSemanticGenerationBudgetPolicy
                        .LegacyDiagnosticMaximumNewTokens or
                    VisualSemanticGenerationBudgetPolicy
                        .ActiveMaximumNewTokens) ||
            (groundedSampledSynthesisPolicy ||
                groundedSynthesisRecoveryPoolPolicy ||
                previousCreatorAuthorityRecoveryPoolPolicy ||
                previousGroundedSynthesisRecoveryPoolPolicy ||
                priorGroundedSynthesisRecoveryPoolPolicy ||
                legacyGroundedSynthesisRecoveryPoolPolicy) &&
                maximumNewTokens !=
                    VisualSemanticGenerationBudgetPolicy
                        .LegacyDiagnosticMaximumNewTokens ||
            generatedTokenCount > maximumNewTokens ||
            suppliedEndOfSequenceTokenIds.Length == 0 ||
            suppliedEndOfSequenceTokenIds.Any(
                static value => value < 0) ||
            !suppliedEndOfSequenceTokenIds.SequenceEqual(
                canonicalEndOfSequenceTokenIds) ||
            terminalTokenId < 0 ||
            legacyPrefixTokenCount !=
                expectedLegacyPrefixTokenCount ||
            decodedTextUtf8ByteCount <= 0 ||
            !Enum.IsDefined(terminationReason))
        {
            throw new ArgumentException(
                "Failure generation telemetry must retain the exact approved generation policy and canonical bounded values.");
        }

        switch (terminationReason)
        {
            case VisualSemanticGenerationTerminationReason
                .EndOfSequence:
                if (firstEndOfSequenceGeneratedIndex !=
                        generatedTokenCount - 1 ||
                    !terminalTokenIsEndOfSequence)
                {
                    throw new ArgumentException(
                        "EndOfSequence failure telemetry requires the terminal generated token to be a configured EOS token.");
                }

                break;

            case VisualSemanticGenerationTerminationReason
                .MaximumNewTokensReached:
                if (firstEndOfSequenceGeneratedIndex.HasValue ||
                    terminalTokenIsEndOfSequence ||
                    generatedTokenCount != maximumNewTokens)
                {
                    throw new ArgumentException(
                        "MaximumNewTokensReached failure telemetry requires a full budget and no EOS token.");
                }

                break;

            case VisualSemanticGenerationTerminationReason
                .UnexpectedStop:
                if (firstEndOfSequenceGeneratedIndex.HasValue ||
                    terminalTokenIsEndOfSequence ||
                    generatedTokenCount >= maximumNewTokens)
                {
                    throw new ArgumentException(
                        "UnexpectedStop failure telemetry requires an early non-EOS termination.");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(terminationReason));
        }

        string generatedHash =
            ModelArtifactManifest.Sha256Value(
                generatedTokenIdsSha256,
                nameof(generatedTokenIdsSha256));
        string legacyPrefixHash =
            ModelArtifactManifest.Sha256Value(
                legacyPrefixTokenIdsSha256,
                nameof(legacyPrefixTokenIdsSha256));

        if (legacyPrefixTokenCount == generatedTokenCount &&
            !string.Equals(
                generatedHash,
                legacyPrefixHash,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The complete generated-token hash must equal the legacy-prefix hash when the complete output fits inside the legacy prefix.",
                nameof(legacyPrefixTokenIdsSha256));
        }

        PolicyVersion = policyVersion;
        PolicySha256 =
            ModelArtifactManifest.Sha256Value(
                policySha256,
                nameof(policySha256));
        MaximumNewTokens = maximumNewTokens;
        DoSample = doSample;
        NumberOfBeams = numberOfBeams;
        UseCache = useCache;
        CaseId = VisualSemanticContractText.Required(
            caseId,
            nameof(caseId),
            128);
        CandidateId = VisualSemanticContractText.Required(
            candidateId,
            nameof(candidateId),
            128);
        CaseOrdinal = caseOrdinal;
        InputTokenCount = inputTokenCount;
        GeneratedTokenCount = generatedTokenCount;
        _endOfSequenceTokenIds =
            Array.AsReadOnly(
                canonicalEndOfSequenceTokenIds);
        FirstEndOfSequenceGeneratedIndex =
            firstEndOfSequenceGeneratedIndex;
        TerminalTokenId = terminalTokenId;
        TerminationReason = terminationReason;
        GeneratedTokenIdsSha256 = generatedHash;
        LegacyPrefixTokenCount = legacyPrefixTokenCount;
        LegacyPrefixTokenIdsSha256 = legacyPrefixHash;
        DecodedTextSha256 =
            ModelArtifactManifest.Sha256Value(
                decodedTextSha256,
                nameof(decodedTextSha256));
        DecodedTextUtf8ByteCount =
            decodedTextUtf8ByteCount;
    }

    public string PolicyVersion { get; }

    public string PolicySha256 { get; }

    public int MaximumNewTokens { get; }

    public bool DoSample { get; }

    public int NumberOfBeams { get; }

    public bool UseCache { get; }

    public string CaseId { get; }

    public string CandidateId { get; }

    public int CaseOrdinal { get; }

    public int InputTokenCount { get; }

    public int GeneratedTokenCount { get; }

    public IReadOnlyList<int> EndOfSequenceTokenIds =>
        _endOfSequenceTokenIds;

    public int? FirstEndOfSequenceGeneratedIndex { get; }

    public int TerminalTokenId { get; }

    public VisualSemanticGenerationTerminationReason
        TerminationReason
    { get; }

    public string GeneratedTokenIdsSha256 { get; }

    public int LegacyPrefixTokenCount { get; }

    public string LegacyPrefixTokenIdsSha256 { get; }

    public string DecodedTextSha256 { get; }

    public int DecodedTextUtf8ByteCount { get; }
}

public sealed record Qwen3VlHostFailureVideoArtifact
{
    internal Qwen3VlHostFailureVideoArtifact(
        string sha256,
        long byteLength,
        TimeSpan reviewDuration)
    {
        Sha256 = sha256;
        ByteLength = byteLength;
        ReviewDuration = reviewDuration;
    }

    public string Sha256 { get; }

    public long ByteLength { get; }

    public TimeSpan ReviewDuration { get; }
}

public sealed record Qwen3VlHostFailureTiming
{
    internal Qwen3VlHostFailureTiming(
        double sourceAbsoluteOffsetSeconds,
        double reviewStartSeconds,
        double reviewEndSeconds,
        double candidateRelativeStartSeconds,
        double candidateRelativeEndSeconds,
        double candidateAbsoluteStartSeconds,
        double candidateAbsoluteEndSeconds)
    {
        SourceAbsoluteOffsetSeconds =
            sourceAbsoluteOffsetSeconds;
        ReviewStartSeconds = reviewStartSeconds;
        ReviewEndSeconds = reviewEndSeconds;
        CandidateRelativeStartSeconds =
            candidateRelativeStartSeconds;
        CandidateRelativeEndSeconds =
            candidateRelativeEndSeconds;
        CandidateAbsoluteStartSeconds =
            candidateAbsoluteStartSeconds;
        CandidateAbsoluteEndSeconds =
            candidateAbsoluteEndSeconds;
        SourceAbsoluteOffset =
            TimeSpan.FromSeconds(sourceAbsoluteOffsetSeconds);
        ReviewStart =
            TimeSpan.FromSeconds(reviewStartSeconds);
        ReviewEnd =
            TimeSpan.FromSeconds(reviewEndSeconds);
        CandidateRelativeStart =
            TimeSpan.FromSeconds(candidateRelativeStartSeconds);
        CandidateRelativeEnd =
            TimeSpan.FromSeconds(candidateRelativeEndSeconds);
        CandidateAbsoluteStart =
            TimeSpan.FromSeconds(candidateAbsoluteStartSeconds);
        CandidateAbsoluteEnd =
            TimeSpan.FromSeconds(candidateAbsoluteEndSeconds);
    }

    internal double SourceAbsoluteOffsetSeconds { get; }

    internal double ReviewStartSeconds { get; }

    internal double ReviewEndSeconds { get; }

    internal double CandidateRelativeStartSeconds { get; }

    internal double CandidateRelativeEndSeconds { get; }

    internal double CandidateAbsoluteStartSeconds { get; }

    internal double CandidateAbsoluteEndSeconds { get; }

    public TimeSpan SourceAbsoluteOffset { get; }

    public TimeSpan ReviewStart { get; }

    public TimeSpan ReviewEnd { get; }

    public TimeSpan CandidateRelativeStart { get; }

    public TimeSpan CandidateRelativeEnd { get; }

    public TimeSpan CandidateAbsoluteStart { get; }

    public TimeSpan CandidateAbsoluteEnd { get; }
}

public sealed class Qwen3VlHostFailureSampling
{
    private readonly ReadOnlyCollection<int>? _frameIndices;
    private readonly ReadOnlyCollection<double>? _inferredTimestampsSeconds;
    private readonly ReadOnlyCollection<double>? _actualPtsSeconds;
    private readonly ReadOnlyCollection<double>? _actualFrameDurationsSeconds;

    internal Qwen3VlHostFailureSampling(
        string? backend,
        double? sourceAverageFramesPerSecond,
        int[]? frameIndices,
        double[]? inferredTimestampsSeconds,
        double[]? actualPtsSeconds,
        double[]? actualFrameDurationsSeconds,
        int? frameCount,
        int? candidateIntersectingFrameCount)
    {
        Backend = backend;
        SourceAverageFramesPerSecond =
            sourceAverageFramesPerSecond;
        _frameIndices =
            frameIndices is null
                ? null
                : Array.AsReadOnly(frameIndices);
        _inferredTimestampsSeconds =
            inferredTimestampsSeconds is null
                ? null
                : Array.AsReadOnly(inferredTimestampsSeconds);
        _actualPtsSeconds =
            actualPtsSeconds is null
                ? null
                : Array.AsReadOnly(actualPtsSeconds);
        _actualFrameDurationsSeconds =
            actualFrameDurationsSeconds is null
                ? null
                : Array.AsReadOnly(actualFrameDurationsSeconds);
        FrameCount = frameCount;
        CandidateIntersectingFrameCount =
            candidateIntersectingFrameCount;
    }

    public string? Backend { get; }

    public double? SourceAverageFramesPerSecond { get; }

    public IReadOnlyList<int>? FrameIndices => _frameIndices;

    public IReadOnlyList<double>? InferredTimestampsSeconds =>
        _inferredTimestampsSeconds;

    public IReadOnlyList<double>? ActualPtsSeconds =>
        _actualPtsSeconds;

    public IReadOnlyList<double>? ActualFrameDurationsSeconds =>
        _actualFrameDurationsSeconds;

    public int? FrameCount { get; }

    public int? CandidateIntersectingFrameCount { get; }
}

public sealed record Qwen3VlHostFailureIdentity
{
    internal Qwen3VlHostFailureIdentity(
        string? inputBatchSha256,
        string? inputCaseSha256,
        string? modelManifestSha256,
        string? environmentSha256,
        string? promptSha256)
    {
        InputBatchSha256 = inputBatchSha256;
        InputCaseSha256 = inputCaseSha256;
        ModelManifestSha256 = modelManifestSha256;
        EnvironmentSha256 = environmentSha256;
        PromptSha256 = promptSha256;
    }

    public string? InputBatchSha256 { get; }

    public string? InputCaseSha256 { get; }

    public string? ModelManifestSha256 { get; }

    public string? EnvironmentSha256 { get; }

    public string? PromptSha256 { get; }
}

public sealed record Qwen3VlHostFailureDetails
{
    internal Qwen3VlHostFailureDetails(
        Qwen3VlHostErrorCode errorCode,
        int exitCode,
        string message)
    {
        ErrorCode = errorCode;
        ExitCode = exitCode;
        Message = message;
    }

    public Qwen3VlHostErrorCode ErrorCode { get; }

    public int ExitCode { get; }

    public string Message { get; }
}
