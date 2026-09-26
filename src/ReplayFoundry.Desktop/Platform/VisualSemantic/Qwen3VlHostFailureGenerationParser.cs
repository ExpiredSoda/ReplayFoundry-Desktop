using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureArrayReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureGenerationParser
{
    internal static Qwen3VlHostFailureGeneration? ParseGeneration(
        JsonElement root,
        Qwen3VlHostFailureCase? failureCase)
    {
        JsonElement value =
            Property(
                root,
                "generation",
                "$");

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (failureCase is null)
        {
            throw Failure(
                "$.generation requires an attributed failure case.");
        }

        Exact(
            value,
            "$.generation",
            "policyVersion",
            "policySha256",
            "maximumNewTokens",
            "doSample",
            "numberOfBeams",
            "useCache",
            "caseId",
            "candidateId",
            "caseOrdinal",
            "inputTokenCount",
            "generatedTokenCount",
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
            Text(
                value,
                "caseId",
                "$.generation",
                128);
        string candidateId =
            Text(
                value,
                "candidateId",
                "$.generation",
                128);
        int caseOrdinal =
            Integer(
                value,
                "caseOrdinal",
                "$.generation");

        if (!string.Equals(
                caseId,
                failureCase.CaseId,
                StringComparison.Ordinal) ||
            !string.Equals(
                candidateId,
                failureCase.CandidateId,
                StringComparison.Ordinal) ||
            caseOrdinal != failureCase.CaseOrdinal)
        {
            throw Failure(
                "$.generation does not belong to the attributed failure case.");
        }

        try
        {
            return new Qwen3VlHostFailureGeneration(
                Text(
                    value,
                    "policyVersion",
                    "$.generation",
                    128),
                Hash(
                    value,
                    "policySha256",
                    "$.generation"),
                Integer(
                    value,
                    "maximumNewTokens",
                    "$.generation"),
                Boolean(
                    value,
                    "doSample",
                    "$.generation"),
                Integer(
                    value,
                    "numberOfBeams",
                    "$.generation"),
                Boolean(
                    value,
                    "useCache",
                    "$.generation"),
                caseId,
                candidateId,
                caseOrdinal,
                Integer(
                    value,
                    "inputTokenCount",
                    "$.generation"),
                Integer(
                    value,
                    "generatedTokenCount",
                    "$.generation"),
                NullableIntegerArray(
                    value,
                    "endOfSequenceTokenIds",
                    "$.generation",
                    maximumCount: 32) ??
                    throw Failure(
                        "$.generation.endOfSequenceTokenIds cannot be null."),
                NullableInteger(
                    value,
                    "firstEndOfSequenceGeneratedIndex",
                    "$.generation"),
                Integer(
                    value,
                    "terminalTokenId",
                    "$.generation"),
                EnumValue<
                    VisualSemanticGenerationTerminationReason>(
                    value,
                    "terminationReason",
                    "$.generation"),
                Hash(
                    value,
                    "generatedTokenIdsSha256",
                    "$.generation"),
                Integer(
                    value,
                    "legacyPrefixTokenCount",
                    "$.generation"),
                Hash(
                    value,
                    "legacyPrefixTokenIdsSha256",
                    "$.generation"),
                Hash(
                    value,
                    "decodedTextSha256",
                    "$.generation"),
                Integer(
                    value,
                    "decodedTextUtf8ByteCount",
                    "$.generation"));
        }
        catch (Qwen3VlOutputParseException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Failure(
                "$.generation is internally inconsistent.",
                exception);
        }
    }

    internal static Qwen3VlHostFailureIdentity ParseIdentity(
        JsonElement root,
        Qwen3VlHostFailureParseContext request)
    {
        JsonElement value =
            Object(root, "identity", "$");
        Exact(
            value,
            "$.identity",
            "inputBatchSha256",
            "inputCaseSha256",
            "modelManifestSha256",
            "environmentSha256",
            "promptSha256");
        string? inputBatch =
            NullableHash(
                value,
                "inputBatchSha256",
                "$.identity");
        string? inputCase =
            NullableHash(
                value,
                "inputCaseSha256",
                "$.identity");
        string? model =
            NullableHash(
                value,
                "modelManifestSha256",
                "$.identity");
        string? environment =
            NullableHash(
                value,
                "environmentSha256",
                "$.identity");
        string? prompt =
            NullableHash(
                value,
                "promptSha256",
                "$.identity");

        if (model is not null &&
            !string.Equals(
                model,
                request.ModelManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "$.identity.modelManifestSha256 does not match the submitted model.");
        }

        if (prompt is not null &&
            !string.Equals(
                prompt,
                request.PromptSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "$.identity.promptSha256 does not match the submitted prompt.");
        }

        return new Qwen3VlHostFailureIdentity(
            inputBatch,
            inputCase,
            model,
            environment,
            prompt);
    }

    internal static Qwen3VlHostFailureDetails ParseDetails(
        JsonElement root,
        int expectedExitCode)
    {
        JsonElement value =
            Object(root, "failure", "$");
        Exact(
            value,
            "$.failure",
            "errorCode",
            "exitCode",
            "message");
        Qwen3VlHostErrorCode code =
            EnumValue<Qwen3VlHostErrorCode>(
                value,
                "errorCode",
                "$.failure");
        int exitCode =
            Integer(
                value,
                "exitCode",
                "$.failure");

        if (exitCode != expectedExitCode)
        {
            throw Failure(
                "$.failure.exitCode does not match the host process.");
        }

        int requiredExitCode =
            code switch
            {
                Qwen3VlHostErrorCode.UnexpectedHostFailure => 1,
                Qwen3VlHostErrorCode.UsageOrInputError => 2,
                Qwen3VlHostErrorCode.InitializationError => 3,
                Qwen3VlHostErrorCode.NetworkProhibitedError => 3,
                Qwen3VlHostErrorCode.InferenceError => 4,
                Qwen3VlHostErrorCode.OutputError => 5,
                Qwen3VlHostErrorCode.RawAuditCaptured => 6,
                Qwen3VlHostErrorCode
                    .GenerationTokenBudgetExceededError => 7,
                Qwen3VlHostErrorCode
                    .UnexpectedGenerationTerminationError => 8,
                Qwen3VlHostErrorCode
                    .ProviderCaseFailuresDetected => 9,
                Qwen3VlHostErrorCode
                    .GenerationWallClockBudgetExceededError => 10,
                Qwen3VlHostErrorCode.Cancelled => 130,
                _ => throw Failure(
                    "$.failure.errorCode is unsupported."),
            };

        if (exitCode != requiredExitCode)
        {
            throw Failure(
                "$.failure.exitCode does not match the typed host error code.");
        }

        return new Qwen3VlHostFailureDetails(
            code,
            exitCode,
            Text(value, "message", "$.failure", 2048));
    }
}
