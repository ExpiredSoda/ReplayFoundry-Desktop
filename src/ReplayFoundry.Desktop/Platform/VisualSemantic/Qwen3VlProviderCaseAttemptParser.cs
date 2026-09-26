using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlProviderAttemptJsonReader;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlProviderCaseAttemptParser
{
    internal static Qwen3VlProviderCaseAttempt Parse(
        JsonElement value,
        VisualSemanticRequest request,
        int expectedOrdinal,
        VisualSemanticVideoInputPolicy videoPolicy,
        string path)
    {
        Exact(
            value,
            path,
            "caseId",
            "candidateId",
            "caseOrdinal",
            "status",
            "stage",
            "observation",
            "identityBindingAudit",
            "normalizationAudit",
            "generation",
            "executionTiming",
            "elapsedSeconds",
            "failure");
        string caseId = Text(value, "caseId", path, 128);
        string candidateId =
            Text(value, "candidateId", path, 128);
        int ordinal = Integer(value, "caseOrdinal", path);
        RequireEqual(
            caseId,
            request.CaseId,
            $"{path}.caseId");
        RequireEqual(
            candidateId,
            request.CandidateId,
            $"{path}.candidateId");

        if (ordinal != expectedOrdinal)
        {
            throw Failure(
                $"{path}.caseOrdinal changed stable request order.");
        }

        Qwen3VlProviderCaseAttemptStatus status =
            EnumValue<Qwen3VlProviderCaseAttemptStatus>(
                value,
                "status",
                path);
        Qwen3VlProviderCaseAttemptStage stage =
            EnumValue<Qwen3VlProviderCaseAttemptStage>(
                value,
                "stage",
                path);
        JsonElement observationElement =
            Property(value, "observation", path);
        JsonElement identityElement =
            Property(value, "identityBindingAudit", path);
        JsonElement normalizationElement =
            Property(value, "normalizationAudit", path);
        JsonElement generationElement =
            Property(value, "generation", path);
        JsonElement timingElement =
            Property(value, "executionTiming", path);
        JsonElement failureElement =
            Property(value, "failure", path);
        TimeSpan? elapsed =
            NullableSeconds(value, "elapsedSeconds", path);

        if (status ==
            Qwen3VlProviderCaseAttemptStatus.Succeeded)
        {
            RequireObject(
                observationElement,
                $"{path}.observation");
            RequireObject(
                identityElement,
                $"{path}.identityBindingAudit");
            RequireObjectOrNull(
                normalizationElement,
                $"{path}.normalizationAudit");
            RequireObject(
                generationElement,
                $"{path}.generation");
            RequireObject(
                timingElement,
                $"{path}.executionTiming");
            RequireNull(
                failureElement,
                $"{path}.failure");

            Qwen3VlParsedCaseResult parsed =
                Qwen3VlBatchResultParser.ParseCase(
                    observationElement,
                    request,
                    expectedOrdinal,
                    $"{path}.observation");
            JsonElement nestedIdentity =
                Property(
                    observationElement,
                    "identityBindingAudit",
                    $"{path}.observation");
            JsonElement nestedNormalization =
                Property(
                    observationElement,
                    "normalizationAudit",
                    $"{path}.observation");

            if (!JsonElement.DeepEquals(
                    identityElement,
                    nestedIdentity) ||
                !JsonElement.DeepEquals(
                    normalizationElement,
                    nestedNormalization))
            {
                throw Failure(
                    $"{path} duplicates identity or normalization telemetry inconsistently.");
            }

            VisualSemanticCaseGenerationManifest generation =
                Qwen3VlBatchResultParser.ParseGenerationCase(
                    generationElement,
                    request,
                    expectedOrdinal,
                    $"{path}.generation");
            VisualSemanticCaseExecutionTiming timing =
                Qwen3VlBatchResultParser.ParseExecutionTimingCase(
                    timingElement,
                    request,
                    expectedOrdinal,
                    Qwen3VlProviderAttemptFailureParser.FrozenTimingPolicy(),
                    videoPolicy,
                    $"{path}.executionTiming");

            if (elapsed != parsed.Elapsed)
            {
                throw Failure(
                    $"{path}.elapsedSeconds does not match the completed observation.");
            }

            return new Qwen3VlProviderCaseAttempt(
                caseId,
                candidateId,
                ordinal,
                status,
                stage,
                parsed.Observation,
                elapsed,
                parsed.IdentityBindingAudit,
                parsed.NormalizationAudit,
                generation,
                timing,
                failure: null);
        }

        RequireNull(
            observationElement,
            $"{path}.observation");
        RequireNull(
            identityElement,
            $"{path}.identityBindingAudit");
        RequireNull(
            normalizationElement,
            $"{path}.normalizationAudit");
        RequireObjectOrNull(
            generationElement,
            $"{path}.generation");
        RequireObjectOrNull(
            timingElement,
            $"{path}.executionTiming");
        RequireObject(
            failureElement,
            $"{path}.failure");

        VisualSemanticCaseGenerationManifest? failedGeneration =
            generationElement.ValueKind == JsonValueKind.Null
                ? null
                : Qwen3VlBatchResultParser.ParseGenerationCase(
                    generationElement,
                    request,
                    expectedOrdinal,
                    $"{path}.generation",
                    failureTelemetry: true);
        VisualSemanticCaseExecutionTiming? failedTiming =
            timingElement.ValueKind == JsonValueKind.Null
                ? null
                : Qwen3VlBatchResultParser.ParseExecutionTimingCase(
                    timingElement,
                    request,
                    expectedOrdinal,
                    Qwen3VlProviderAttemptFailureParser.FrozenTimingPolicy(),
                    videoPolicy,
                    $"{path}.executionTiming");

        return new Qwen3VlProviderCaseAttempt(
            caseId,
            candidateId,
            ordinal,
            status,
            stage,
            observation: null,
            elapsed,
            identityBindingAudit: null,
            normalizationAudit: null,
            failedGeneration,
            failedTiming,
            Qwen3VlProviderAttemptFailureParser.Parse(
                failureElement,
                $"{path}.failure"));
    }
}
