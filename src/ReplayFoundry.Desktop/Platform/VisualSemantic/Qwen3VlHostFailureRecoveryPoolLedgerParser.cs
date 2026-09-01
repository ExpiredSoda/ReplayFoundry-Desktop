using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureJsonReader;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlHostFailureParserValidation;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlHostFailureRecoveryPoolLedgerParser
{
    internal static Qwen3VlHostFailureRecoveryPoolLedgerEntry[] Parse(
        JsonElement root,
        bool sourceSelectionProvenance)
    {
        JsonElement value = Property(root, "recoveryPoolLedger", "$");
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Failure("$.recoveryPoolLedger must be an array.");
        }

        JsonElement[] entries = value.EnumerateArray().ToArray();
        if (entries.Length >
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PoolSize)
        {
            throw Failure(
                "$.recoveryPoolLedger exceeds the frozen recovery-pool size.");
        }

        var result = new Qwen3VlHostFailureRecoveryPoolLedgerEntry[
            entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            string path = $"$.recoveryPoolLedger[{index}]";
            JsonElement entry = entries[index];
            string[] fields =
            [
                "candidateOrdinal",
                "seed",
                .. (sourceSelectionProvenance
                    ? new[]
                    {
                        "sourceSelectionReason",
                        "sourcePassOrdinal",
                        "sourceRejectedJsonSha256",
                    }
                    : Array.Empty<string>()),
                "canonicalMessagesSha256",
                "renderedPromptSha256",
                "renderedPromptUtf8ByteCount",
                "inputTokenIdsSha256",
                "inputTokenCount",
                "outputSha256",
                "completedJsonSha256",
                "rejectionCode",
                "accepted",
            ];
            Exact(entry, path, fields);

            int candidateOrdinal = Integer(
                entry,
                "candidateOrdinal",
                path);
            int seed = Integer(entry, "seed", path);
            string? sourceSelectionReason = sourceSelectionProvenance
                ? Text(
                    entry,
                    "sourceSelectionReason",
                    path,
                    maximumLength: 64)
                : null;
            int? sourcePassOrdinal = sourceSelectionProvenance
                ? Integer(entry, "sourcePassOrdinal", path)
                : null;
            string? sourceRejectedJsonSha256 = sourceSelectionProvenance
                ? Hash(entry, "sourceRejectedJsonSha256", path)
                : null;
            int renderedPromptUtf8ByteCount = Integer(
                entry,
                "renderedPromptUtf8ByteCount",
                path);
            int inputTokenCount = Integer(
                entry,
                "inputTokenCount",
                path);
            string? rejectionCode = NullableText(
                entry,
                "rejectionCode",
                path,
                maximumLength: 128);
            bool accepted = Boolean(entry, "accepted", path);

            if (candidateOrdinal != index + 1 ||
                seed != Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .Seeds[index] ||
                renderedPromptUtf8ByteCount <= 0 ||
                inputTokenCount <= 0 ||
                accepted == (rejectionCode is not null) ||
                accepted && index != entries.Length - 1 ||
                sourceSelectionProvenance &&
                    !ValidSourceSelection(
                        sourceSelectionReason,
                        sourcePassOrdinal))
            {
                throw Failure(
                    $"{path} does not preserve frozen recovery-pool provenance.");
            }

            result[index] = new Qwen3VlHostFailureRecoveryPoolLedgerEntry(
                candidateOrdinal,
                seed,
                sourceSelectionReason,
                sourcePassOrdinal,
                sourceRejectedJsonSha256,
                Hash(entry, "canonicalMessagesSha256", path),
                Hash(entry, "renderedPromptSha256", path),
                renderedPromptUtf8ByteCount,
                Hash(entry, "inputTokenIdsSha256", path),
                inputTokenCount,
                Hash(entry, "outputSha256", path),
                Hash(entry, "completedJsonSha256", path),
                rejectionCode,
                accepted);
        }

        if (result.Length > 1)
        {
            Qwen3VlHostFailureRecoveryPoolLedgerEntry source = result[0];
            if (result.Skip(1).Any(entry =>
                    !entry.CanonicalMessagesSha256.Equals(
                        source.CanonicalMessagesSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    !entry.RenderedPromptSha256.Equals(
                        source.RenderedPromptSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    entry.RenderedPromptUtf8ByteCount !=
                        source.RenderedPromptUtf8ByteCount ||
                    !entry.InputTokenIdsSha256.Equals(
                        source.InputTokenIdsSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    entry.InputTokenCount != source.InputTokenCount) ||
                sourceSelectionProvenance &&
                    result.Skip(1).Any(entry =>
                        !string.Equals(
                            entry.SourceSelectionReason,
                            source.SourceSelectionReason,
                            StringComparison.Ordinal) ||
                        entry.SourcePassOrdinal !=
                            source.SourcePassOrdinal ||
                        !string.Equals(
                            entry.SourceRejectedJsonSha256,
                            source.SourceRejectedJsonSha256,
                            StringComparison.OrdinalIgnoreCase)))
            {
                throw Failure(
                    "$.recoveryPoolLedger does not preserve immutable pool input provenance.");
            }
        }

        return result;
    }

    private static bool ValidSourceSelection(
        string? reason,
        int? sourcePassOrdinal) =>
        reason switch
        {
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .OriginalSourceSelectionReason =>
                    sourcePassOrdinal ==
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .OriginalSourcePassOrdinal,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PrimaryOnlyCrossDraftSourceSelectionReason =>
                    sourcePassOrdinal ==
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .PrimaryOnlyCrossDraftSourcePassOrdinal,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .CreatorAuthorityRetrySourceSelectionReason =>
                    sourcePassOrdinal ==
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .OriginalSourcePassOrdinal,
            _ => false,
        };
}
