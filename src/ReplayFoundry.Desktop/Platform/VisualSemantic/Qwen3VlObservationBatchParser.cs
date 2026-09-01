using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlObservationBatchParser
{
    internal static Qwen3VlParsedBatchResult Parse(
        string json,
        VisualSemanticBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using JsonDocument document = Open(json);
            JsonElement root = document.RootElement;
            RequireExactProperties(
                root,
                "$",
                "schemaVersion",
                "modelRepository",
                "modelRevision",
                "device",
                "backend",
                "peakAllocatedGpuBytes",
                "totalElapsedSeconds",
                "results",
                "generation",
                "executionTiming");
            RequireExactValue(
                RequireString(root, "schemaVersion", "$", 64),
                Qwen3VlBatchResultParser.BatchSchemaVersion,
                "$.schemaVersion");
            RequireExactValue(
                RequireString(root, "modelRepository", "$", 256),
                request.Model.RepositoryId,
                "$.modelRepository");
            RequireExactValue(
                RequireString(root, "modelRevision", "$", 256),
                request.Model.Revision,
                "$.modelRevision");
            JsonElement[] resultElements =
                RequireArray(root, "results", "$");

            if (resultElements.Length != request.Requests.Count)
            {
                throw Failure(
                    "The Qwen host result count does not match the requested batch.");
            }

            string[] resultCaseIds =
                resultElements
                    .Select(
                        (element, index) =>
                            RequireString(
                                element,
                                "caseId",
                                $"$.results[{index}]",
                                128))
                    .ToArray();

            if (resultCaseIds
                    .Distinct(StringComparer.Ordinal)
                    .Count() != resultCaseIds.Length)
            {
                throw Failure(
                    "The Qwen host returned duplicate case results.");
            }

            string[] expectedCaseIds =
                request.Requests
                    .Select(static value => value.CaseId)
                    .ToArray();

            if (resultCaseIds.Any(
                    value =>
                        !expectedCaseIds.Contains(
                            value,
                            StringComparer.Ordinal)))
            {
                throw Failure(
                    "The Qwen host returned a foreign case result.");
            }

            if (!resultCaseIds.SequenceEqual(
                    expectedCaseIds,
                    StringComparer.Ordinal))
            {
                throw Failure(
                    "The Qwen host changed the stable input order.");
            }

            Qwen3VlParsedCaseResult[] results =
                resultElements
                    .Select(
                        (element, index) =>
                            Qwen3VlObservationCaseParser.Parse(
                                element,
                                request.Requests[index],
                                index + 1,
                                $"$.results[{index}]"))
                    .ToArray();
            VisualSemanticGenerationManifest generation =
                Qwen3VlGenerationManifestParser.Parse(
                    RequireObject(
                        root,
                        "generation",
                        "$"),
                    request);

            for (int index = 0;
                 index < results.Length;
                 index++)
            {
                VisualSemanticOutputNormalizationAudit? audit =
                    results[index].NormalizationAudit;

                if (audit is not null &&
                    !string.Equals(
                        generation.Cases[index]
                            .DecodedTextSha256,
                        audit.RawGeneratedTextSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw Failure(
                        $"$.generation.cases[{index}].decodedTextSha256 does not match the normalization audit's raw generated-text identity.");
                }
            }

            VisualSemanticExecutionTimingManifest executionTiming =
                Qwen3VlExecutionTimingParser.Parse(
                    RequireObject(
                        root,
                        "executionTiming",
                        "$"),
                    request);
            long? peakBytes =
                RequireNullableInt64(
                    root,
                    "peakAllocatedGpuBytes",
                    "$");

            if (peakBytes < 0)
            {
                throw Failure(
                    "$.peakAllocatedGpuBytes cannot be negative.");
            }

            return new Qwen3VlParsedBatchResult(
                RequireString(root, "device", "$", 256),
                RequireString(root, "backend", "$", 128),
                peakBytes,
                Seconds(
                    RequireFiniteDouble(
                        root,
                        "totalElapsedSeconds",
                        "$"),
                    "$.totalElapsedSeconds"),
                Array.AsReadOnly(results),
                generation,
                executionTiming);
        }
        catch (Qwen3VlOutputParseException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                  JsonException or
                  ArgumentException or
                  InvalidOperationException or
                  FormatException or
                  OverflowException)
        {
            throw new Qwen3VlOutputParseException(
                "The Qwen host returned malformed batch output.",
                innerException: exception);
        }
    }

}
