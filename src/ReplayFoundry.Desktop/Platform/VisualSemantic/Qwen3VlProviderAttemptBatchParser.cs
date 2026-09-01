using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlProviderAttemptJsonReader;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlProviderAttemptBatchParser
{
    public static Qwen3VlProviderAttemptBatch Parse(
        string json,
        VisualSemanticBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using JsonDocument document = Open(json);
            JsonElement root = document.RootElement;
            Exact(
                root,
                "$",
                "schemaVersion",
                "hostVersion",
                "modelRepository",
                "modelRevision",
                "device",
                "backend",
                "requestCount",
                "successCount",
                "failureCount",
                "peakAllocatedGpuBytes",
                "totalElapsedSeconds",
                "outcomes",
                "canonicalAttemptSha256");
            string schemaVersion =
                Text(root, "schemaVersion", "$", 128);
            string hostVersion =
                Text(root, "hostVersion", "$", 128);
            RequireEqual(
                schemaVersion,
                Qwen3VlProviderAttemptBatch.SupportedSchemaVersion,
                "$.schemaVersion");
            RequireEqual(
                hostVersion,
                Qwen3VlProviderAttemptBatch.SupportedHostVersion,
                "$.hostVersion");
            RequireEqual(
                Text(root, "modelRepository", "$", 256),
                request.Model.RepositoryId,
                "$.modelRepository");
            RequireEqual(
                Text(root, "modelRevision", "$", 256),
                request.Model.Revision,
                "$.modelRevision");

            JsonElement[] outcomes = Array(root, "outcomes", "$");
            int requestCount = Integer(root, "requestCount", "$");

            if (requestCount != request.Requests.Count ||
                outcomes.Length != requestCount)
            {
                throw Failure(
                    "$.requestCount and outcomes must match the submitted batch.");
            }

            Qwen3VlProviderCaseAttempt[] cases =
                outcomes
                    .Select(
                        (value, index) =>
                            Qwen3VlProviderCaseAttemptParser.Parse(
                                value,
                                request.Requests[index],
                                index + 1,
                                request.VideoPolicy,
                                $"$.outcomes[{index}]"))
                    .ToArray();
            int reportedSuccess =
                Integer(root, "successCount", "$");
            int reportedFailure =
                Integer(root, "failureCount", "$");
            int actualSuccess =
                cases.Count(
                    static value =>
                        value.Status ==
                        Qwen3VlProviderCaseAttemptStatus.Succeeded);

            if (reportedSuccess != actualSuccess ||
                reportedFailure != cases.Length - actualSuccess ||
                reportedSuccess + reportedFailure != requestCount)
            {
                throw Failure(
                    "Provider attempt success and failure counts do not reconcile.");
            }

            string canonicalHash =
                LowerSha256(
                    root,
                    "canonicalAttemptSha256",
                    "$");
            string independentlyComputedHash =
                Qwen3VlCanonicalJson.ComputeObjectSha256(
                    root,
                    "canonicalAttemptSha256");

            if (!string.Equals(
                    canonicalHash,
                    independentlyComputedHash,
                    StringComparison.Ordinal))
            {
                throw Failure(
                    "$.canonicalAttemptSha256 does not match its canonical payload.");
            }

            return new Qwen3VlProviderAttemptBatch(
                schemaVersion,
                hostVersion,
                Text(root, "modelRepository", "$", 256),
                Text(root, "modelRevision", "$", 256),
                Text(root, "device", "$", 256),
                Text(root, "backend", "$", 128),
                cases,
                NullableInt64(
                    root,
                    "peakAllocatedGpuBytes",
                    "$"),
                Seconds(
                    FiniteDouble(
                        root,
                        "totalElapsedSeconds",
                        "$"),
                    "$.totalElapsedSeconds"),
                canonicalHash,
                root.GetRawText());
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
                "The Qwen host returned a malformed provider-attempt batch.",
                innerException: exception);
        }
    }
}
