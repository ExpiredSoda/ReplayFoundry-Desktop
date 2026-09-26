namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

public enum VisualSemanticIdentityBindingSource
{
    HostRequest = 0,
}

public sealed class VisualSemanticIdentityBindingAudit
{
    public const string SupportedPolicyVersion =
        "visual-semantic-trusted-identity-binding-1.0";

    public const string SupportedPolicySha256 =
        "3512B5E94CAAA50F8EB6D241D02048A02424EBB078076489FE84599349B309C6";

    public VisualSemanticIdentityBindingAudit(
        string trustedCaseId,
        string trustedCandidateId,
        int caseOrdinal,
        string providerEchoCaseId,
        string providerEchoCandidateId,
        bool caseEchoMatched,
        bool candidateEchoMatched,
        VisualSemanticIdentityBindingSource source,
        string providerPayloadSha256,
        string trustedBoundPayloadSha256,
        DateTimeOffset boundAtUtc)
    {
        if (caseOrdinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(caseOrdinal));
        }

        if (!Enum.IsDefined(source) ||
            source != VisualSemanticIdentityBindingSource.HostRequest)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        ModelArtifactManifest.RequireUtc(boundAtUtc, nameof(boundAtUtc));

        TrustedCaseId = VisualSemanticContractText.Required(
            trustedCaseId,
            nameof(trustedCaseId),
            128);
        TrustedCandidateId = VisualSemanticContractText.Required(
            trustedCandidateId,
            nameof(trustedCandidateId),
            128);
        ProviderEchoCaseId = VisualSemanticContractText.Required(
            providerEchoCaseId,
            nameof(providerEchoCaseId),
            128);
        ProviderEchoCandidateId = VisualSemanticContractText.Required(
            providerEchoCandidateId,
            nameof(providerEchoCandidateId),
            128);

        if (!IsStableProviderIdentifier(ProviderEchoCaseId) ||
            !IsStableProviderIdentifier(ProviderEchoCandidateId))
        {
            throw new ArgumentException(
                "Provider identity echoes must use the exact stable identifier syntax.");
        }

        bool expectedCaseMatch = string.Equals(
            TrustedCaseId,
            ProviderEchoCaseId,
            StringComparison.Ordinal);
        bool expectedCandidateMatch = string.Equals(
            TrustedCandidateId,
            ProviderEchoCandidateId,
            StringComparison.Ordinal);

        if (caseEchoMatched != expectedCaseMatch ||
            candidateEchoMatched != expectedCandidateMatch)
        {
            throw new ArgumentException(
                "Identity echo match flags do not match the trusted and provider identifiers.");
        }

        ProviderPayloadSha256 = ModelArtifactManifest.Sha256Value(
            providerPayloadSha256,
            nameof(providerPayloadSha256));
        TrustedBoundPayloadSha256 = ModelArtifactManifest.Sha256Value(
            trustedBoundPayloadSha256,
            nameof(trustedBoundPayloadSha256));

        bool anyMismatch = !caseEchoMatched || !candidateEchoMatched;
        bool hashesEqual = string.Equals(
            ProviderPayloadSha256,
            TrustedBoundPayloadSha256,
            StringComparison.Ordinal);

        if (anyMismatch == hashesEqual)
        {
            throw new ArgumentException(
                "Identity-binding hashes are inconsistent with the recorded echo match.");
        }

        CaseOrdinal = caseOrdinal;
        CaseEchoMatched = caseEchoMatched;
        CandidateEchoMatched = candidateEchoMatched;
        Source = source;
        BoundAtUtc = boundAtUtc;
    }

    public string TrustedCaseId { get; }

    public string TrustedCandidateId { get; }

    public int CaseOrdinal { get; }

    public string ProviderEchoCaseId { get; }

    public string ProviderEchoCandidateId { get; }

    public bool CaseEchoMatched { get; }

    public bool CandidateEchoMatched { get; }

    public bool AnyEchoMismatch =>
        !CaseEchoMatched ||
        !CandidateEchoMatched;

    public VisualSemanticIdentityBindingSource Source { get; }

    public string ProviderPayloadSha256 { get; }

    public string TrustedBoundPayloadSha256 { get; }

    public DateTimeOffset BoundAtUtc { get; }

    internal static bool IsStableProviderIdentifier(
        string value) =>
        IsAsciiAlphaNumeric(value[0]) &&
        value.All(
            static character =>
                IsAsciiAlphaNumeric(character) ||
                character is '.' or '_' or ':' or '-');

    private static bool IsAsciiAlphaNumeric(
        char value) =>
        value is >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or
            >= '0' and <= '9';
}
