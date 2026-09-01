using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public enum VisualSemanticOutputNormalizationKind
{
    LimitationsCanonicalized = 0,
    UncertaintiesCanonicalized = 1,
    EvidenceIntervalsCanonicalized = 2,
}

public sealed class VisualSemanticOutputNormalizationAudit
{
    public const string SupportedPolicyVersion =
        "visual-semantic-output-normalization-1.1";

    public const string SupportedPolicySha256 =
        "51A3D6B67CA18546B38AA4C63D698BD1F499FC2D7330BF9090C83DFA429C98D8";

    private readonly ReadOnlyCollection<
        VisualSemanticOutputNormalizationKind> _normalizationKinds;

    public VisualSemanticOutputNormalizationAudit(
        string caseId,
        string rawGeneratedTextSha256,
        string rawOutputSha256,
        string canonicalOutputSha256,
        string normalizationPolicyVersion,
        IEnumerable<VisualSemanticOutputNormalizationKind>
            normalizationKinds,
        int rawEvidenceIntervalCount,
        int canonicalEvidenceIntervalCount,
        int exactDuplicateEvidenceIntervalCount,
        bool evidenceIntervalOrderChanged,
        int rawLimitationCount,
        int canonicalLimitationCount,
        int exactDuplicateLimitationCount,
        bool limitationOrderChanged,
        int rawUncertaintyCount,
        int canonicalUncertaintyCount,
        int exactDuplicateUncertaintyCount,
        bool uncertaintyOrderChanged,
        bool semanticTextChanged,
        DateTimeOffset normalizedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(normalizationKinds);

        VisualSemanticOutputNormalizationKind[] kindSnapshot =
            normalizationKinds.ToArray();

        if (!string.Equals(
                normalizationPolicyVersion,
                SupportedPolicyVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The normalization policy must be exactly '{SupportedPolicyVersion}'.",
                nameof(normalizationPolicyVersion));
        }

        if (semanticTextChanged)
        {
            throw new ArgumentException(
                "Audited output normalization may not change semantic text.",
                nameof(semanticTextChanged));
        }

        ModelArtifactManifest.RequireUtc(
            normalizedAtUtc,
            nameof(normalizedAtUtc));
        ValidateCounts(
            rawEvidenceIntervalCount,
            canonicalEvidenceIntervalCount,
            exactDuplicateEvidenceIntervalCount,
            evidenceIntervalOrderChanged,
            VisualSemanticObservation.MaximumEvidenceIntervals,
            "evidence-interval");
        ValidateCounts(
            rawLimitationCount,
            canonicalLimitationCount,
            exactDuplicateLimitationCount,
            limitationOrderChanged,
            VisualSemanticObservation.MaximumLimitations,
            "limitation");
        ValidateCounts(
            rawUncertaintyCount,
            canonicalUncertaintyCount,
            exactDuplicateUncertaintyCount,
            uncertaintyOrderChanged,
            VisualSemanticObservation.MaximumUncertainties,
            "uncertainty");

        var expectedKinds =
            new List<VisualSemanticOutputNormalizationKind>(3);

        if (Changed(
                exactDuplicateEvidenceIntervalCount,
                evidenceIntervalOrderChanged))
        {
            expectedKinds.Add(
                VisualSemanticOutputNormalizationKind
                    .EvidenceIntervalsCanonicalized);
        }

        if (Changed(
                exactDuplicateLimitationCount,
                limitationOrderChanged))
        {
            expectedKinds.Add(
                VisualSemanticOutputNormalizationKind
                    .LimitationsCanonicalized);
        }

        if (Changed(
                exactDuplicateUncertaintyCount,
                uncertaintyOrderChanged))
        {
            expectedKinds.Add(
                VisualSemanticOutputNormalizationKind
                    .UncertaintiesCanonicalized);
        }

        if (expectedKinds.Count == 0 ||
            !kindSnapshot.SequenceEqual(expectedKinds))
        {
            throw new ArgumentException(
                "Normalization kinds must exactly describe changed collections in canonical order.",
                nameof(normalizationKinds));
        }

        string rawOutputHash =
            ModelArtifactManifest.Sha256Value(
                rawOutputSha256,
                nameof(rawOutputSha256));
        string canonicalOutputHash =
            ModelArtifactManifest.Sha256Value(
                canonicalOutputSha256,
                nameof(canonicalOutputSha256));

        if (string.Equals(
                rawOutputHash,
                canonicalOutputHash,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Normalized output must have distinct raw and canonical SHA-256 values.",
                nameof(canonicalOutputSha256));
        }

        CaseId = VisualSemanticContractText.Required(
            caseId,
            nameof(caseId),
            128);
        RawGeneratedTextSha256 =
            ModelArtifactManifest.Sha256Value(
                rawGeneratedTextSha256,
                nameof(rawGeneratedTextSha256));
        RawOutputSha256 = rawOutputHash;
        CanonicalOutputSha256 = canonicalOutputHash;
        NormalizationPolicyVersion =
            normalizationPolicyVersion;
        _normalizationKinds = Array.AsReadOnly(kindSnapshot);
        RawEvidenceIntervalCount = rawEvidenceIntervalCount;
        CanonicalEvidenceIntervalCount =
            canonicalEvidenceIntervalCount;
        ExactDuplicateEvidenceIntervalCount =
            exactDuplicateEvidenceIntervalCount;
        EvidenceIntervalOrderChanged =
            evidenceIntervalOrderChanged;
        RawLimitationCount = rawLimitationCount;
        CanonicalLimitationCount = canonicalLimitationCount;
        ExactDuplicateLimitationCount =
            exactDuplicateLimitationCount;
        LimitationOrderChanged = limitationOrderChanged;
        RawUncertaintyCount = rawUncertaintyCount;
        CanonicalUncertaintyCount = canonicalUncertaintyCount;
        ExactDuplicateUncertaintyCount =
            exactDuplicateUncertaintyCount;
        UncertaintyOrderChanged = uncertaintyOrderChanged;
        SemanticTextChanged = semanticTextChanged;
        NormalizedAtUtc = normalizedAtUtc;
    }

    public string CaseId { get; }

    public string RawGeneratedTextSha256 { get; }

    public string RawOutputSha256 { get; }

    public string CanonicalOutputSha256 { get; }

    public string NormalizationPolicyVersion { get; }

    public IReadOnlyList<VisualSemanticOutputNormalizationKind>
        NormalizationKinds =>
        _normalizationKinds;

    public int RawEvidenceIntervalCount { get; }

    public int CanonicalEvidenceIntervalCount { get; }

    public int ExactDuplicateEvidenceIntervalCount { get; }

    public bool EvidenceIntervalOrderChanged { get; }

    public int RawLimitationCount { get; }

    public int CanonicalLimitationCount { get; }

    public int ExactDuplicateLimitationCount { get; }

    public bool LimitationOrderChanged { get; }

    public int RawUncertaintyCount { get; }

    public int CanonicalUncertaintyCount { get; }

    public int ExactDuplicateUncertaintyCount { get; }

    public bool UncertaintyOrderChanged { get; }

    public bool SemanticTextChanged { get; }

    public DateTimeOffset NormalizedAtUtc { get; }

    private static bool Changed(
        int exactDuplicateCount,
        bool orderChanged) =>
        exactDuplicateCount > 0 ||
        orderChanged;

    private static void ValidateCounts(
        int rawCount,
        int canonicalCount,
        int exactDuplicateCount,
        bool orderChanged,
        int maximumRawCount,
        string collectionName)
    {
        if (rawCount < 0 ||
            rawCount > maximumRawCount ||
            canonicalCount < 0 ||
            canonicalCount > rawCount ||
            exactDuplicateCount < 0 ||
            (rawCount > 0 && canonicalCount == 0) ||
            (exactDuplicateCount > 0 && rawCount < 2) ||
            rawCount - exactDuplicateCount != canonicalCount ||
            (orderChanged && canonicalCount < 2))
        {
            throw new ArgumentException(
                $"The {collectionName} normalization counts and ordering flag are inconsistent.");
        }
    }
}
