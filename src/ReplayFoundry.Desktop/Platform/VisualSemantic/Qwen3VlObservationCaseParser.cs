using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlObservationCanonicalizer;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonCollections;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlObservationCaseParser
{
    internal static Qwen3VlParsedCaseResult Parse(
        JsonElement value,
        VisualSemanticRequest request,
        int expectedOrdinal,
        string path)
    {
        RejectProhibitedReasoning(value, path);
        RequireExactProperties(
            value,
            path,
            "caseId",
            "candidateId",
            "schemaVersion",
            "observableContentType",
            "visibleStateChange",
            "hasClearBeginning",
            "hasClearOutcome",
            "menuOrTraversalPresent",
            "spokenContentAppearsRelevant",
            "suggestedWorthReviewing",
            "reviewCertainty",
            "evidenceIntervals",
            "uncertainties",
            "limitations",
            "conciseRationale",
            "elapsedSeconds",
            "identityBindingAudit",
            "normalizationAudit");
        string caseId = RequireString(value, "caseId", path, 128);
        string candidateId = RequireString(value, "candidateId", path, 128);
        RequireExactValue(caseId, request.CaseId, $"{path}.caseId");
        RequireExactValue(
            candidateId,
            request.CandidateId,
            $"{path}.candidateId");
        string schemaVersion = RequireString(value, "schemaVersion", path, 64);
        RequireExactValue(
            schemaVersion,
            Qwen3VlBatchResultParser.ObservationSchemaVersion,
            $"{path}.schemaVersion");
        VisualSemanticEvidenceInterval[] intervals = RequireArray(
                value,
                "evidenceIntervals",
                path)
            .Select((element, index) => ParseEvidenceInterval(
                element,
                request.Input.ReviewVideoDuration,
                $"{path}.evidenceIntervals[{index}]"))
            .ToArray();
        VisualSemanticUncertainty[] uncertainties = RequireArray(
                value,
                "uncertainties",
                path)
            .Select((element, index) => ParseUncertainty(
                element,
                $"{path}.uncertainties[{index}]"))
            .ToArray();
        string[] limitations = RequireArray(value, "limitations", path)
            .Select((element, index) => RequireStringValue(
                element,
                $"{path}.limitations[{index}]",
                240))
            .ToArray();
        RequireCanonicalEvidenceIntervals(intervals, $"{path}.evidenceIntervals");
        RequireCanonicalUncertainties(uncertainties, $"{path}.uncertainties");
        RequireCanonicalLimitations(limitations, $"{path}.limitations");

        var observation = new VisualSemanticObservation(
            caseId,
            candidateId,
            schemaVersion,
            RequireEnum<VisualSemanticObservableContentType>(
                value,
                "observableContentType",
                path),
            RequireNullableString(value, "visibleStateChange", path, 320),
            RequireEnum<VisualSemanticTernary>(value, "hasClearBeginning", path),
            RequireEnum<VisualSemanticTernary>(value, "hasClearOutcome", path),
            RequireEnum<VisualSemanticTernary>(
                value,
                "menuOrTraversalPresent",
                path),
            RequireEnum<VisualSemanticRelevance>(
                value,
                "spokenContentAppearsRelevant",
                path),
            RequireEnum<VisualSemanticTernary>(
                value,
                "suggestedWorthReviewing",
                path),
            RequireEnum<VisualSemanticReviewCertainty>(
                value,
                "reviewCertainty",
                path),
            intervals,
            uncertainties,
            limitations,
            RequireString(
                value,
                "conciseRationale",
                path,
                VisualSemanticObservation.MaximumRationaleLength));
        TimeSpan elapsed = Seconds(
            RequireFiniteDouble(value, "elapsedSeconds", path),
            $"{path}.elapsedSeconds");
        VisualSemanticOutputNormalizationAudit? normalizationAudit =
            Qwen3VlNormalizationAuditParser.Parse(
                value,
                caseId,
                intervals.Length,
                limitations.Length,
                uncertainties.Length,
                path);
        VisualSemanticIdentityBindingAudit identityBindingAudit =
            Qwen3VlIdentityBindingAuditParser.Parse(
                value,
                request,
                expectedOrdinal,
                normalizationAudit,
                path);

        return new Qwen3VlParsedCaseResult(
            observation,
            elapsed,
            identityBindingAudit,
            normalizationAudit);
    }

    private static VisualSemanticEvidenceInterval ParseEvidenceInterval(
        JsonElement value,
        TimeSpan reviewDuration,
        string path)
    {
        RequireExactProperties(
            value,
            path,
            "startSeconds",
            "endSeconds",
            "description");
        TimeSpan start = Seconds(
            RequireFiniteDouble(value, "startSeconds", path),
            $"{path}.startSeconds");
        TimeSpan end = Seconds(
            RequireFiniteDouble(value, "endSeconds", path),
            $"{path}.endSeconds");
        if (end > reviewDuration)
        {
            throw Failure($"{path} falls outside the bounded review video.");
        }

        return new VisualSemanticEvidenceInterval(
            start,
            end,
            RequireString(value, "description", path, 240));
    }

    private static VisualSemanticUncertainty ParseUncertainty(
        JsonElement value,
        string path)
    {
        RequireExactProperties(value, path, "code", "description");
        return new VisualSemanticUncertainty(
            RequireEnum<VisualSemanticUncertaintyCode>(value, "code", path),
            RequireString(value, "description", path, 240));
    }
}
