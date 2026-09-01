using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlEditorialJson;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlEditorialCanonicalizationAuditParser
{
    public static VisualSemanticEditorialCanonicalizationAudit Read(
        JsonElement value,
        VisualSemanticEditorialObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        string policyVersion = Text(value, "policyVersion");
        bool isCurrentWireAudit = string.Equals(
            policyVersion,
            VisualSemanticEditorialCanonicalizer.PolicyVersion,
            StringComparison.Ordinal);
        bool hasWireAudit = isCurrentWireAudit || string.Equals(
            policyVersion,
            "visual-semantic-editorial-canonicalization-1.2",
            StringComparison.Ordinal);
        bool hasOuterWhitespaceAudit = hasWireAudit || string.Equals(
            policyVersion,
            "visual-semantic-editorial-canonicalization-1.1",
            StringComparison.Ordinal);
        if (!hasOuterWhitespaceAudit &&
            !string.Equals(
                policyVersion,
                "visual-semantic-editorial-canonicalization-1.0",
                StringComparison.Ordinal))
        {
            throw Failure(
                "Prompt 2.3 canonicalization policy is unsupported.");
        }
        Exact(
            value,
            hasWireAudit
                ? [
                    "policyVersion",
                    "observedChanges",
                    "evidenceIntervals",
                    "uncertaintyReasons",
                    "outerWhitespaceTrimmed",
                    "wireRepresentationVersion",
                    "syntacticCanonicalizationCount",
                    "schemaShapeCanonicalizationCount",
                    "semanticRepairCount",
                ]
                : hasOuterWhitespaceAudit
                    ? [
                        "policyVersion",
                        "observedChanges",
                        "evidenceIntervals",
                        "uncertaintyReasons",
                        "outerWhitespaceTrimmed",
                        "syntacticCanonicalizationCount",
                        "schemaShapeCanonicalizationCount",
                        "semanticRepairCount",
                    ]
                : [
                    "policyVersion",
                    "observedChanges",
                    "evidenceIntervals",
                    "uncertaintyReasons",
                    "syntacticCanonicalizationCount",
                    "schemaShapeCanonicalizationCount",
                    "semanticRepairCount",
                ]);
        VisualSemanticEditorialCollectionAudit changes =
            ReadCollectionAudit(
                Object(value, "observedChanges"),
                observation.ObservedChanges.Count,
                VisualSemanticEditorialObservation.MaximumObservedChanges);
        VisualSemanticEditorialCollectionAudit intervals =
            ReadCollectionAudit(
                Object(value, "evidenceIntervals"),
                observation.EvidenceIntervals.Count,
                VisualSemanticEditorialObservation.MaximumEvidenceIntervals);
        VisualSemanticEditorialCollectionAudit uncertainties =
            ReadCollectionAudit(
                Object(value, "uncertaintyReasons"),
                observation.UncertaintyReasons.Count,
                VisualSemanticEditorialObservation.MaximumUncertaintyReasons);
        int changedCollectionCount =
            Changed(changes) + Changed(intervals) + Changed(uncertainties);
        bool outerWhitespaceTrimmed =
            hasOuterWhitespaceAudit &&
            RequiredBoolean(value, "outerWhitespaceTrimmed");
        int syntacticCount =
            Integer(value, "syntacticCanonicalizationCount");
        int shapeCount =
            Integer(value, "schemaShapeCanonicalizationCount");
        int repairCount = Integer(value, "semanticRepairCount");
        string? wireRepresentationVersion = hasWireAudit
            ? NullableText(value, "wireRepresentationVersion")
            : null;

        int minimumSyntacticCount =
            changedCollectionCount + (outerWhitespaceTrimmed ? 1 : 0);
        if (syntacticCount < minimumSyntacticCount ||
            syntacticCount >
                minimumSyntacticCount + changes.RawCount ||
            shapeCount != (wireRepresentationVersion is null ? 0 : 1) ||
            wireRepresentationVersion is not null &&
                !string.Equals(
                    wireRepresentationVersion,
                    isCurrentWireAudit
                        ? "visual-semantic-editorial-wire-1.1"
                        : "visual-semantic-editorial-wire-1.0",
                    StringComparison.Ordinal) ||
            repairCount != 0)
        {
            throw Failure(
                "Prompt 2.3 canonicalization audit is inconsistent with its retained canonical observation.");
        }

        return new(
            policyVersion,
            changes,
            intervals,
            uncertainties,
            outerWhitespaceTrimmed,
            syntacticCount,
            shapeCount,
            repairCount,
            wireRepresentationVersion);
    }

    private static VisualSemanticEditorialCollectionAudit
        ReadCollectionAudit(
            JsonElement value,
            int observedCanonicalCount,
            int maximumRawCount)
    {
        Exact(
            value,
            "rawCount",
            "canonicalCount",
            "exactDuplicateCount",
            "orderChanged");
        int rawCount = Integer(value, "rawCount");
        int canonicalCount = Integer(value, "canonicalCount");
        int duplicateCount = Integer(value, "exactDuplicateCount");
        bool orderChanged = RequiredBoolean(value, "orderChanged");
        if (rawCount < 0 ||
            rawCount > maximumRawCount ||
            canonicalCount != observedCanonicalCount ||
            canonicalCount < 0 ||
            rawCount - canonicalCount != duplicateCount)
        {
            throw Failure(
                "Prompt 2.3 collection canonicalization audit is inconsistent.");
        }

        return new(
            rawCount,
            canonicalCount,
            duplicateCount,
            orderChanged);
    }

    private static bool RequiredBoolean(
        JsonElement parent,
        string name)
    {
        JsonElement value = Property(parent, name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Failure(
                $"Prompt 2.3 '{name}' must be Boolean."),
        };
    }

    private static string? NullableText(JsonElement parent, string name)
    {
        JsonElement value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : Text(parent, name);
    }

    private static int Changed(
        VisualSemanticEditorialCollectionAudit value) =>
        value.ExactDuplicateCount > 0 || value.OrderChanged ? 1 : 0;
}
