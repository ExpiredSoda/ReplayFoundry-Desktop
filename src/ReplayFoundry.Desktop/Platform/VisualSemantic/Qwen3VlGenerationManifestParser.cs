using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonCollections;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGenerationManifestParser
{
    internal static VisualSemanticGenerationManifest Parse(
        JsonElement value,
        VisualSemanticBatchRequest request)
    {
        const string path = "$.generation";
        RequireExactProperties(
            value,
            path,
            "schemaVersion",
            "policyVersion",
            "policySha256",
            "maximumNewTokens",
            "doSample",
            "numberOfBeams",
            "useCache",
            "caseCount",
            "cases",
            "canonicalGenerationSha256");
        RequireExactValue(
            RequireString(
                value,
                "schemaVersion",
                path,
                128),
            VisualSemanticGenerationManifest
                .SupportedSchemaVersion,
            $"{path}.schemaVersion");
        JsonElement[] caseElements =
            RequireArray(
                value,
                "cases",
                path);
        int caseCount =
            RequireInt32(
                value,
                "caseCount",
                path);

        if (caseCount != request.Requests.Count ||
            caseElements.Length != caseCount)
        {
            throw Failure(
                $"{path}.caseCount and cases must match the submitted batch.");
        }

        VisualSemanticCaseGenerationManifest[] cases =
            caseElements
                .Select(
                    (element, index) =>
                        ParseCase(
                            element,
                            request.Requests[index],
                            index + 1,
                            $"{path}.cases[{index}]"))
                .ToArray();
        string canonicalHash =
            RequireLowerSha256(
                value,
                "canonicalGenerationSha256",
                path);
        string independentlyComputedHash =
            Qwen3VlCanonicalJson.ComputeObjectSha256(
                value,
                "canonicalGenerationSha256");

        if (!string.Equals(
                canonicalHash,
                independentlyComputedHash,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"{path}.canonicalGenerationSha256 does not match its canonical payload.");
        }

        return new VisualSemanticGenerationManifest(
            RequireString(
                value,
                "policyVersion",
                path,
                128),
            RequireLowerSha256(
                value,
                "policySha256",
                path),
            RequireInt32(
                value,
                "maximumNewTokens",
                path),
            RequireBoolean(
                value,
                "doSample",
                path),
            RequireInt32(
                value,
                "numberOfBeams",
                path),
            RequireBoolean(
                value,
                "useCache",
                path),
            cases,
            canonicalHash);
    }

    internal static VisualSemanticCaseGenerationManifest
        ParseCase(
            JsonElement value,
            VisualSemanticRequest request,
            int expectedOrdinal,
            string path,
            bool failureTelemetry = false)
    {
        RequireExactProperties(
            value,
            path,
            "caseId",
            "candidateId",
            "caseOrdinal",
            "inputTokenCount",
            "generatedTokenCount",
            "maximumNewTokens",
            "endOfSequenceTokenIds",
            "firstEndOfSequenceGeneratedIndex",
            "terminalTokenId",
            "terminationReason",
            "generatedTokenIdsSha256",
            "legacyPrefixTokenCount",
            "legacyPrefixTokenIdsSha256",
            "decodedTextSha256",
            "decodedTextUtf8ByteCount");
        string caseId =
            RequireString(
                value,
                "caseId",
                path,
                128);
        string candidateId =
            RequireString(
                value,
                "candidateId",
                path,
                128);
        RequireExactValue(
            caseId,
            request.CaseId,
            $"{path}.caseId");
        RequireExactValue(
            candidateId,
            request.CandidateId,
            $"{path}.candidateId");
        int caseOrdinal =
            RequireInt32(
                value,
                "caseOrdinal",
                path);

        if (caseOrdinal != expectedOrdinal)
        {
            throw Failure(
                $"{path}.caseOrdinal changed stable request order.");
        }

        int inputTokenCount =
            RequireInt32(
                value,
                "inputTokenCount",
                path);
        int generatedTokenCount =
            RequireInt32(
                value,
                "generatedTokenCount",
                path);
        int maximumNewTokens =
            RequireInt32(
                value,
                "maximumNewTokens",
                path);
        int[] endOfSequenceTokenIds =
            RequireInt32Array(
                value,
                "endOfSequenceTokenIds",
                path);
        int? firstEndOfSequenceGeneratedIndex =
            RequireNullableInt32(
                value,
                "firstEndOfSequenceGeneratedIndex",
                path);
        int terminalTokenId =
            RequireInt32(
                value,
                "terminalTokenId",
                path);
        VisualSemanticGenerationTerminationReason
            terminationReason =
                RequireEnum<
                    VisualSemanticGenerationTerminationReason>(
                    value,
                    "terminationReason",
                    path);
        string generatedTokenIdsSha256 =
            RequireLowerSha256(
                value,
                "generatedTokenIdsSha256",
                path);
        int legacyPrefixTokenCount =
            RequireInt32(
                value,
                "legacyPrefixTokenCount",
                path);
        string legacyPrefixTokenIdsSha256 =
            RequireLowerSha256(
                value,
                "legacyPrefixTokenIdsSha256",
                path);
        string decodedTextSha256 =
            RequireLowerSha256(
                value,
                "decodedTextSha256",
                path);
        int decodedTextUtf8ByteCount =
            RequireInt32(
                value,
                "decodedTextUtf8ByteCount",
                path);

        return failureTelemetry
            ? VisualSemanticCaseGenerationManifest
                .CreateFailureTelemetry(
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
                    decodedTextUtf8ByteCount)
            : new VisualSemanticCaseGenerationManifest(
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
                decodedTextUtf8ByteCount);
    }

}
