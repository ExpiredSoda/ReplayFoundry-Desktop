using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlObservationCanonicalizer;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonCollections;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlIdentityBindingAuditParser
{
    internal static VisualSemanticIdentityBindingAudit Parse(
        JsonElement parent,
        VisualSemanticRequest request,
        int expectedOrdinal,
        VisualSemanticOutputNormalizationAudit? normalizationAudit,
        string path)
    {
        JsonElement value = RequireObject(parent, "identityBindingAudit", path);
        string auditPath = $"{path}.identityBindingAudit";
        RequireExactProperties(
            value,
            auditPath,
            "policyVersion",
            "policySha256",
            "source",
            "caseOrdinal",
            "trustedCaseId",
            "trustedCandidateId",
            "providerEchoCaseId",
            "providerEchoCandidateId",
            "caseEchoMatched",
            "candidateEchoMatched",
            "providerPayloadSha256",
            "trustedBoundPayloadSha256",
            "boundAtUtc");
        string policyVersion = RequireString(
            value,
            "policyVersion",
            auditPath,
            128);
        string policySha256 = RequireLowerSha256(
            value,
            "policySha256",
            auditPath);
        RequireExactValue(
            policyVersion,
            VisualSemanticIdentityBindingAudit.SupportedPolicyVersion,
            $"{auditPath}.policyVersion");
        if (!string.Equals(
                policySha256,
                VisualSemanticIdentityBindingAudit.SupportedPolicySha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                $"{auditPath}.policySha256 does not match the trusted " +
                "identity-binding policy.");
        }

        int ordinal = RequireInt32(value, "caseOrdinal", auditPath);
        string trustedCaseId = RequireString(
            value,
            "trustedCaseId",
            auditPath,
            128);
        string trustedCandidateId = RequireString(
            value,
            "trustedCandidateId",
            auditPath,
            128);
        RequireExactValue(
            trustedCaseId,
            request.CaseId,
            $"{auditPath}.trustedCaseId");
        RequireExactValue(
            trustedCandidateId,
            request.CandidateId,
            $"{auditPath}.trustedCandidateId");
        if (ordinal != expectedOrdinal)
        {
            throw Failure(
                $"{auditPath}.caseOrdinal changed stable request order.");
        }

        var audit = new VisualSemanticIdentityBindingAudit(
            trustedCaseId,
            trustedCandidateId,
            ordinal,
            RequireString(value, "providerEchoCaseId", auditPath, 128),
            RequireString(value, "providerEchoCandidateId", auditPath, 128),
            RequireBoolean(value, "caseEchoMatched", auditPath),
            RequireBoolean(value, "candidateEchoMatched", auditPath),
            RequireEnum<VisualSemanticIdentityBindingSource>(
                value,
                "source",
                auditPath),
            RequireLowerSha256(value, "providerPayloadSha256", auditPath),
            RequireLowerSha256(
                value,
                "trustedBoundPayloadSha256",
                auditPath),
            RequireUtcDateTimeOffset(value, "boundAtUtc", auditPath));
        string independentlyBoundSha256 =
            ComputeCanonicalObservationSha256(parent);
        if (!string.Equals(
                audit.TrustedBoundPayloadSha256,
                independentlyBoundSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                $"{auditPath}.trustedBoundPayloadSha256 does not match the " +
                "trusted observation payload.");
        }

        string independentlyProviderSha256 = ComputeCanonicalObservationSha256(
            parent,
            audit.ProviderEchoCaseId,
            audit.ProviderEchoCandidateId);
        if (!string.Equals(
                audit.ProviderPayloadSha256,
                independentlyProviderSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                $"{auditPath}.providerPayloadSha256 does not match the " +
                "independently reconstructed provider-echo payload.");
        }

        return audit;
    }
}
