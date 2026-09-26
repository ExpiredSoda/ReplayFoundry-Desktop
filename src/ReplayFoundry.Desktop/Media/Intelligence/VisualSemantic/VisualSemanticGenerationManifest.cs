using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public enum VisualSemanticGenerationTerminationReason
{
    EndOfSequence = 0,
    MaximumNewTokensReached = 1,
    UnexpectedStop = 2,
}

public sealed class VisualSemanticCaseGenerationManifest
{
    private readonly ReadOnlyCollection<int> _endOfSequenceTokenIds;

    public VisualSemanticCaseGenerationManifest(
        string caseId,
        string candidateId,
        int caseOrdinal,
        int inputTokenCount,
        int generatedTokenCount,
        int maximumNewTokens,
        IEnumerable<int> endOfSequenceTokenIds,
        int? firstEndOfSequenceGeneratedIndex,
        int terminalTokenId,
        VisualSemanticGenerationTerminationReason terminationReason,
        string generatedTokenIdsSha256,
        int legacyPrefixTokenCount,
        string legacyPrefixTokenIdsSha256,
        string decodedTextSha256,
        int decodedTextUtf8ByteCount)
        : this(
            caseId,
            candidateId,
            caseOrdinal,
            inputTokenCount,
            generatedTokenCount,
            maximumNewTokens,
            endOfSequenceTokenIds,
            firstEndOfSequenceGeneratedIndex,
            terminalTokenId,
            terminationReason,
            generatedTokenIdsSha256,
            legacyPrefixTokenCount,
            legacyPrefixTokenIdsSha256,
            decodedTextSha256,
            decodedTextUtf8ByteCount,
            failureTelemetry: false)
    {
    }

    internal static VisualSemanticCaseGenerationManifest
        CreateFailureTelemetry(
            string caseId,
            string candidateId,
            int caseOrdinal,
            int inputTokenCount,
            int generatedTokenCount,
            int maximumNewTokens,
            IEnumerable<int> endOfSequenceTokenIds,
            int? firstEndOfSequenceGeneratedIndex,
            int terminalTokenId,
            VisualSemanticGenerationTerminationReason
                terminationReason,
            string generatedTokenIdsSha256,
            int legacyPrefixTokenCount,
            string legacyPrefixTokenIdsSha256,
            string decodedTextSha256,
            int decodedTextUtf8ByteCount) =>
        new(
            caseId,
            candidateId,
            caseOrdinal,
            inputTokenCount,
            generatedTokenCount,
            maximumNewTokens,
            endOfSequenceTokenIds,
            firstEndOfSequenceGeneratedIndex,
            terminalTokenId,
            terminationReason,
            generatedTokenIdsSha256,
            legacyPrefixTokenCount,
            legacyPrefixTokenIdsSha256,
            decodedTextSha256,
            decodedTextUtf8ByteCount,
            failureTelemetry: true);

    private VisualSemanticCaseGenerationManifest(
        string caseId,
        string candidateId,
        int caseOrdinal,
        int inputTokenCount,
        int generatedTokenCount,
        int maximumNewTokens,
        IEnumerable<int> endOfSequenceTokenIds,
        int? firstEndOfSequenceGeneratedIndex,
        int terminalTokenId,
        VisualSemanticGenerationTerminationReason terminationReason,
        string generatedTokenIdsSha256,
        int legacyPrefixTokenCount,
        string legacyPrefixTokenIdsSha256,
        string decodedTextSha256,
        int decodedTextUtf8ByteCount,
        bool failureTelemetry)
    {
        ArgumentNullException.ThrowIfNull(endOfSequenceTokenIds);

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

        if (caseOrdinal <= 0 ||
            inputTokenCount <= 0 ||
            generatedTokenCount <= 0 ||
            maximumNewTokens is not (
                VisualSemanticGenerationBudgetPolicy
                    .LegacyDiagnosticMaximumNewTokens or
                VisualSemanticGenerationBudgetPolicy
                    .ActiveMaximumNewTokens) ||
            generatedTokenCount > maximumNewTokens ||
            suppliedEndOfSequenceTokenIds.Length == 0 ||
            suppliedEndOfSequenceTokenIds.Any(
                static value => value < 0) ||
            !suppliedEndOfSequenceTokenIds.SequenceEqual(
                canonicalEndOfSequenceTokenIds) ||
            terminalTokenId < 0 ||
            legacyPrefixTokenCount !=
                expectedLegacyPrefixTokenCount ||
            decodedTextUtf8ByteCount <
                (failureTelemetry ? 0 : 1) ||
            !Enum.IsDefined(terminationReason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(generatedTokenCount),
                "Generation telemetry must be positive, bounded, canonical, and internally consistent.");
        }

        bool endOfSequenceObserved =
            firstEndOfSequenceGeneratedIndex.HasValue;
        bool terminalTokenIsEndOfSequence =
            canonicalEndOfSequenceTokenIds.Contains(
                terminalTokenId);

        switch (terminationReason)
        {
            case VisualSemanticGenerationTerminationReason
                .EndOfSequence:
                if (!endOfSequenceObserved ||
                    firstEndOfSequenceGeneratedIndex !=
                        generatedTokenCount - 1 ||
                    !terminalTokenIsEndOfSequence ||
                    !failureTelemetry &&
                    generatedTokenCount >= maximumNewTokens)
                {
                    throw new ArgumentException(
                        "EndOfSequence requires the terminal generated token to be a configured EOS token before the configured ceiling.",
                        nameof(terminationReason));
                }

                break;

            case VisualSemanticGenerationTerminationReason
                .MaximumNewTokensReached:
                if (endOfSequenceObserved ||
                    terminalTokenIsEndOfSequence ||
                    generatedTokenCount != maximumNewTokens)
                {
                    throw new ArgumentException(
                        "MaximumNewTokensReached requires a full budget and no EOS token.",
                        nameof(terminationReason));
                }

                break;

            case VisualSemanticGenerationTerminationReason
                .UnexpectedStop:
                if (endOfSequenceObserved ||
                    terminalTokenIsEndOfSequence ||
                    generatedTokenCount >= maximumNewTokens)
                {
                    throw new ArgumentException(
                        "UnexpectedStop requires an early non-EOS termination.",
                        nameof(terminationReason));
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
        MaximumNewTokens = maximumNewTokens;
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

        if (decodedTextUtf8ByteCount == 0 &&
            !string.Equals(
                DecodedTextSha256,
                "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Zero-byte decoded generation telemetry requires the SHA-256 of empty UTF-8 text.",
                nameof(decodedTextSha256));
        }

        DecodedTextUtf8ByteCount = decodedTextUtf8ByteCount;
    }

    public string CaseId { get; }

    public string CandidateId { get; }

    public int CaseOrdinal { get; }

    public int InputTokenCount { get; }

    public int GeneratedTokenCount { get; }

    public int MaximumNewTokens { get; }

    public IReadOnlyList<int> EndOfSequenceTokenIds =>
        _endOfSequenceTokenIds;

    public int? FirstEndOfSequenceGeneratedIndex { get; }

    public int TerminalTokenId { get; }

    public VisualSemanticGenerationTerminationReason
        TerminationReason
    { get; }

    public bool EndOfSequenceObserved =>
        FirstEndOfSequenceGeneratedIndex.HasValue;

    public string GeneratedTokenIdsSha256 { get; }

    public int LegacyPrefixTokenCount { get; }

    public string LegacyPrefixTokenIdsSha256 { get; }

    public string DecodedTextSha256 { get; }

    public int DecodedTextUtf8ByteCount { get; }
}

public sealed class VisualSemanticGenerationManifest
{
    public const string SupportedSchemaVersion =
        "visual-semantic-generation-manifest-1.0";

    private readonly ReadOnlyCollection<
        VisualSemanticCaseGenerationManifest> _cases;

    public VisualSemanticGenerationManifest(
        string policyVersion,
        string policySha256,
        int maximumNewTokens,
        bool doSample,
        int numberOfBeams,
        bool useCache,
        IEnumerable<VisualSemanticCaseGenerationManifest> cases,
        string canonicalGenerationSha256)
    {
        ArgumentNullException.ThrowIfNull(cases);

        VisualSemanticCaseGenerationManifest[] snapshot =
            cases.ToArray();

        if (!string.Equals(
                policyVersion,
                VisualSemanticGenerationBudgetPolicy.Version,
                StringComparison.Ordinal) ||
            !string.Equals(
                policySha256,
                VisualSemanticGenerationBudgetPolicy.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            maximumNewTokens !=
                VisualSemanticGenerationBudgetPolicy
                    .ActiveMaximumNewTokens ||
            doSample !=
                VisualSemanticGenerationBudgetPolicy.DoSample ||
            numberOfBeams !=
                VisualSemanticGenerationBudgetPolicy.NumberOfBeams ||
            useCache !=
                VisualSemanticGenerationBudgetPolicy.UseCache ||
            snapshot.Length == 0 ||
            snapshot.Any(static value => value is null) ||
            snapshot.Select(static value => value.CaseId)
                .Distinct(StringComparer.Ordinal)
                .Count() != snapshot.Length ||
            snapshot.Select(static value => value.CandidateId)
                .Distinct(StringComparer.Ordinal)
                .Count() != snapshot.Length ||
            !snapshot.Select(static value => value.CaseOrdinal)
                .SequenceEqual(
                    Enumerable.Range(1, snapshot.Length)) ||
            snapshot.Any(
                value =>
                    !value.EndOfSequenceTokenIds.SequenceEqual(
                        snapshot[0].EndOfSequenceTokenIds)) ||
            snapshot.Any(
                static value =>
                    value.MaximumNewTokens !=
                        VisualSemanticGenerationBudgetPolicy
                            .ActiveMaximumNewTokens ||
                    value.TerminationReason !=
                        VisualSemanticGenerationTerminationReason
                            .EndOfSequence ||
                    value.GeneratedTokenCount >=
                        VisualSemanticGenerationBudgetPolicy
                            .ActiveMaximumNewTokens))
        {
            throw new ArgumentException(
                "A completed generation manifest requires the exact active policy and EOS-complete ordered cases.",
                nameof(cases));
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
        _cases = Array.AsReadOnly(snapshot);
        CanonicalGenerationSha256 =
            ModelArtifactManifest.Sha256Value(
                canonicalGenerationSha256,
                nameof(canonicalGenerationSha256));
    }

    public string SchemaVersion => SupportedSchemaVersion;

    public string PolicyVersion { get; }

    public string PolicySha256 { get; }

    public int MaximumNewTokens { get; }

    public bool DoSample { get; }

    public int NumberOfBeams { get; }

    public bool UseCache { get; }

    public int CaseCount => _cases.Count;

    public IReadOnlyList<VisualSemanticCaseGenerationManifest> Cases =>
        _cases;

    public string CanonicalGenerationSha256 { get; }
}
