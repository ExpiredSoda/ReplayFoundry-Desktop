using System.Collections.ObjectModel;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonPrimitives;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlStrictJsonValues;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlBatchResultParser
{
    public const string ProbeSchemaVersion =
        "qwen3-vl-host-probe-1.0";

    public const string BatchSchemaVersion =
        "visual-semantic-observation-batch-1.5";

    public const string ObservationSchemaVersion =
        "visual-semantic-observation-1.0";

    public static Qwen3VlParsedBatchResult ParseBatch(
        string json,
        VisualSemanticBatchRequest request) =>
        Qwen3VlObservationBatchParser.Parse(json, request);

    internal static Qwen3VlParsedCaseResult ParseCase(
        JsonElement value,
        VisualSemanticRequest request,
        int expectedOrdinal,
        string path) =>
        Qwen3VlObservationCaseParser.Parse(
            value,
            request,
            expectedOrdinal,
            path);

    internal static VisualSemanticCaseGenerationManifest
        ParseGenerationCase(
            JsonElement value,
            VisualSemanticRequest request,
            int expectedOrdinal,
            string path,
            bool failureTelemetry = false) =>
        Qwen3VlGenerationManifestParser.ParseCase(
            value,
            request,
            expectedOrdinal,
            path,
            failureTelemetry);

    internal static VisualSemanticCaseExecutionTiming
        ParseExecutionTimingCase(
            JsonElement value,
            VisualSemanticRequest request,
            int expectedOrdinal,
            VisualSemanticExecutionTimingCoveragePolicy policy,
            VisualSemanticVideoInputPolicy videoPolicy,
            string path) =>
        Qwen3VlCaseExecutionTimingParser.Parse(
            value,
            request,
            expectedOrdinal,
            policy,
            videoPolicy,
            path);

    public static Qwen3VlParsedEditorialObservation
        ParseEditorialObservation(
            string json,
            TimeSpan reviewDuration,
            TimeSpan candidateStart,
            TimeSpan candidateEnd) =>
        Qwen3VlEditorialObservationParser.Parse(
            json,
            reviewDuration,
            candidateStart,
            candidateEnd);

    public static Qwen3VlProbeResult ParseProbe(
        string json,
        VisualSemanticModelManifest model)
    {
        ArgumentNullException.ThrowIfNull(model);

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
                "packages");
            string schemaVersion =
                RequireString(root, "schemaVersion", "$", 64);
            RequireExactValue(
                schemaVersion,
                ProbeSchemaVersion,
                "$.schemaVersion");
            string repository =
                RequireString(root, "modelRepository", "$", 256);
            string revision =
                RequireString(root, "modelRevision", "$", 256);
            RequireExactValue(
                repository,
                model.RepositoryId,
                "$.modelRepository");
            RequireExactValue(
                revision,
                model.Revision,
                "$.modelRevision");
            JsonElement packages =
                RequireObject(root, "packages", "$");
            var packageSnapshot =
                new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (JsonProperty property in packages.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) ||
                    property.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(property.Value.GetString()) ||
                    !packageSnapshot.TryAdd(
                        property.Name,
                        property.Value.GetString()!.Trim()))
                {
                    throw Failure(
                        "$.packages must contain unique nonblank string versions.");
                }
            }

            return new Qwen3VlProbeResult(
                schemaVersion,
                repository,
                revision,
                RequireString(root, "device", "$", 256),
                RequireString(root, "backend", "$", 128),
                new ReadOnlyDictionary<string, string>(
                    packageSnapshot
                        .OrderBy(
                            static value => value.Key,
                            StringComparer.Ordinal)
                        .ToDictionary(
                            static value => value.Key,
                            static value => value.Value,
                            StringComparer.Ordinal)));
        }
        catch (Qwen3VlInitializationException)
        {
            throw;
        }
        catch (Qwen3VlInferenceException exception)
        {
            throw new Qwen3VlInitializationException(
                "The Qwen host returned invalid probe output.",
                innerException: exception);
        }
        catch (Exception exception)
            when (exception is
                  JsonException or
                  ArgumentException or
                  InvalidOperationException or
                  FormatException or
                  OverflowException)
        {
            throw new Qwen3VlInitializationException(
                "The Qwen host returned malformed probe output.",
                innerException: exception);
        }
    }
}
