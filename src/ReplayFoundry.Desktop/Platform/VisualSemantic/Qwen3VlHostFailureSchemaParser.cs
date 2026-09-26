using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlHostFailureSchema(
    string Version,
    bool IsCurrent,
    bool HasRecoveryPoolLedger,
    bool RequiresCurrentRecoveryPoolLedger);

internal static class Qwen3VlHostFailureSchemaParser
{
    internal static Qwen3VlHostFailureSchema Parse(JsonElement root)
    {
        string version = Text(root, "schemaVersion", "$", 128);
        bool current = version.Equals(
            Qwen3VlHostFailureEnvelope.SupportedSchemaVersion,
            StringComparison.Ordinal);
        bool previous = version.Equals(
            Qwen3VlHostFailureEnvelope.PreviousSupportedSchemaVersion,
            StringComparison.Ordinal);
        bool prior = version.Equals(
            Qwen3VlHostFailureEnvelope.PriorSupportedSchemaVersion,
            StringComparison.Ordinal);
        if (!current && !previous && !prior &&
            !version.Equals(
                Qwen3VlHostFailureEnvelope.FoundationalSupportedSchemaVersion,
                StringComparison.Ordinal))
        {
            throw Failure("$.schemaVersion is unsupported.");
        }

        string[] fields =
        [
            "schemaVersion", "hostVersion", "command", "stage", "case",
            "videoArtifact", "timing", "sampling", "generation", "identity",
            "failure", "createdAtUtc", "diagnostics",
        ];
        Exact(
            root,
            "$",
            current
                ? [
                    .. fields,
                    "generationWatchdog",
                    "groundedMemoryPolicy",
                    "recoveryPoolLedger",
                ]
                : previous || prior
                    ? [.. fields, "recoveryPoolLedger"]
                    : fields);
        return new Qwen3VlHostFailureSchema(
            version,
            current,
            current || previous || prior,
            current || previous);
    }
}
