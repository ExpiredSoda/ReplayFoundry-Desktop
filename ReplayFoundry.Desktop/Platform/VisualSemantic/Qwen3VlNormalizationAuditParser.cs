using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlObservationCanonicalizer;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonCollections;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlNormalizationAuditParser
{
    internal static VisualSemanticOutputNormalizationAudit? Parse(
        JsonElement parent,
        string caseId,
        int canonicalEvidenceIntervalCount,
        int canonicalLimitationCount,
        int canonicalUncertaintyCount,
        string path)
    {
        if (!parent.TryGetProperty("normalizationAudit", out JsonElement value))
        {
            throw Failure($"{path}.normalizationAudit is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string auditPath = $"{path}.normalizationAudit";
        RequireExactProperties(
            value,
            auditPath,
            "caseId",
            "rawGeneratedTextSha256",
            "rawOutputSha256",
            "canonicalOutputSha256",
            "normalizationPolicyVersion",
            "normalizationKinds",
            "rawEvidenceIntervalCount",
            "canonicalEvidenceIntervalCount",
            "exactDuplicateEvidenceIntervalCount",
            "evidenceIntervalOrderChanged",
            "rawLimitationCount",
            "canonicalLimitationCount",
            "exactDuplicateLimitationCount",
            "limitationOrderChanged",
            "rawUncertaintyCount",
            "canonicalUncertaintyCount",
            "exactDuplicateUncertaintyCount",
            "uncertaintyOrderChanged",
            "semanticTextChanged",
            "normalizedAtUtc");
        string auditCaseId = RequireString(value, "caseId", auditPath, 128);
        RequireExactValue(auditCaseId, caseId, $"{auditPath}.caseId");
        VisualSemanticOutputNormalizationKind[] kinds = RequireArray(
                value,
                "normalizationKinds",
                auditPath)
            .Select((element, index) =>
                RequireEnumValue<VisualSemanticOutputNormalizationKind>(
                    element,
                    $"{auditPath}.normalizationKinds[{index}]"))
            .ToArray();
        int reportedCanonicalEvidenceIntervals = RequireInt32(
            value,
            "canonicalEvidenceIntervalCount",
            auditPath);
        int reportedCanonicalLimitations = RequireInt32(
            value,
            "canonicalLimitationCount",
            auditPath);
        int reportedCanonicalUncertainties = RequireInt32(
            value,
            "canonicalUncertaintyCount",
            auditPath);
        if (reportedCanonicalEvidenceIntervals != canonicalEvidenceIntervalCount ||
            reportedCanonicalLimitations != canonicalLimitationCount ||
            reportedCanonicalUncertainties != canonicalUncertaintyCount)
        {
            throw Failure(
                $"{auditPath} canonical counts do not match the emitted observation.");
        }

        var audit = new VisualSemanticOutputNormalizationAudit(
            auditCaseId,
            RequireUntrimmedString(
                value,
                "rawGeneratedTextSha256",
                auditPath,
                64),
            RequireUntrimmedString(value, "rawOutputSha256", auditPath, 64),
            RequireUntrimmedString(
                value,
                "canonicalOutputSha256",
                auditPath,
                64),
            RequireUntrimmedString(
                value,
                "normalizationPolicyVersion",
                auditPath,
                128),
            kinds,
            RequireInt32(value, "rawEvidenceIntervalCount", auditPath),
            reportedCanonicalEvidenceIntervals,
            RequireInt32(
                value,
                "exactDuplicateEvidenceIntervalCount",
                auditPath),
            RequireBoolean(value, "evidenceIntervalOrderChanged", auditPath),
            RequireInt32(value, "rawLimitationCount", auditPath),
            reportedCanonicalLimitations,
            RequireInt32(
                value,
                "exactDuplicateLimitationCount",
                auditPath),
            RequireBoolean(value, "limitationOrderChanged", auditPath),
            RequireInt32(value, "rawUncertaintyCount", auditPath),
            reportedCanonicalUncertainties,
            RequireInt32(
                value,
                "exactDuplicateUncertaintyCount",
                auditPath),
            RequireBoolean(value, "uncertaintyOrderChanged", auditPath),
            RequireBoolean(value, "semanticTextChanged", auditPath),
            RequireUtcDateTimeOffset(value, "normalizedAtUtc", auditPath));
        string emittedCanonicalSha256 =
            ComputeCanonicalObservationSha256(parent);
        if (!string.Equals(
                audit.CanonicalOutputSha256,
                emittedCanonicalSha256,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{auditPath}.canonicalOutputSha256 does not match the " +
                "emitted canonical observation.");
        }

        return audit;
    }
}
