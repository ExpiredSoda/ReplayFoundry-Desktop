using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataRecoveryPolicyParser
{
    internal static void Validate(
        JsonElement generation,
        string outputSchema,
        IReadOnlyList<string> retryableSemanticRejections,
        string? retryableSemanticRejectionsSha256)
    {
        bool currentPolicy = Qwen3VlGroundedMetadataSchemaCapabilities
            .IsNewerThan(outputSchema, PreviousEffectiveVoiceOutputSchema);
        bool previousEffectiveVoicePolicy = outputSchema.Equals(
            PreviousEffectiveVoiceOutputSchema,
            StringComparison.Ordinal);
        bool previousCreatorAuthorityPolicy = outputSchema.Equals(
            PreviousCreatorAuthorityOutputSchema,
            StringComparison.Ordinal);
        bool previousPolicy = outputSchema.Equals(
            PreviousAudienceCopyWithholdingOutputSchema,
            StringComparison.Ordinal);
        bool priorPolicy = outputSchema.Equals(
                PreviousCrossDraftRetryOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousRootPreloadOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousCudnnAttentionOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousPositionEmbeddingOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousAccelerateOffloadOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousVisionOffloadOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousLowPeakSamplingOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousPeakBoundedSamplingOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(
                PreviousSamplingOutputSchema,
                StringComparison.Ordinal) ||
            outputSchema.Equals(PreWatchdogOutputSchema, StringComparison.Ordinal);
        bool earlierPolicy = outputSchema.Equals(
            PreviousOutputSchema,
            StringComparison.Ordinal);
        bool legacyPolicy = outputSchema.Equals(
            PriorOutputSchema,
            StringComparison.Ordinal);
        bool foundationalPolicy = outputSchema.Equals(
            LegacyOutputSchema,
            StringComparison.Ordinal);
        bool latestPolicy = outputSchema.Equals(
            OutputSchema,
            StringComparison.Ordinal);
        bool terminalPeriodPolicy = !latestPolicy &&
            Qwen3VlGroundedMetadataSchemaCapabilities.IsNewerThan(
                outputSchema,
                PreviousInterfaceCorrectionOutputSchema);
        bool policy17 = currentPolicy && !latestPolicy &&
            !terminalPeriodPolicy;
        string expectedVersion = outputSchema.Equals(
                OutputSchema,
                StringComparison.Ordinal)
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Version
            : terminalPeriodPolicy
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousTerminalPeriodVersion
            : policy17
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousInterfaceCorrectionVersion
            : currentPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Version
            : previousEffectiveVoicePolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousEffectiveVoiceVersion
            : previousCreatorAuthorityPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousCreatorAuthorityVersion
            : previousPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousVersion
                : priorPolicy
                    ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PriorVersion
                    : earlierPolicy
                        ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .EarlierVersion
                        : legacyPolicy
                            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                                .LegacyVersion
                            : foundationalPolicy
                                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                                    .FoundationalVersion
                                : throw new Qwen3VlOutputParseException(
                                    "Grounded Qwen recovery-pool schema is unsupported.");
        string expectedSha256 = outputSchema.Equals(
                OutputSchema,
                StringComparison.Ordinal)
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Sha256
            : terminalPeriodPolicy
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousTerminalPeriodSha256
            : policy17
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousInterfaceCorrectionSha256
            : currentPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Sha256
            : previousEffectiveVoicePolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousEffectiveVoiceSha256
            : previousCreatorAuthorityPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousCreatorAuthoritySha256
            : previousPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousSha256
                : priorPolicy
                    ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PriorSha256
                    : earlierPolicy
                        ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .EarlierSha256
                        : legacyPolicy
                            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                                .LegacySha256
                            : foundationalPolicy
                                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                                    .FoundationalSha256
                                : throw new Qwen3VlOutputParseException(
                                    "Grounded Qwen recovery-pool schema is unsupported.");
        RequireText(generation, "synthesisDecodingPolicyVersion", expectedVersion);
        RequireText(generation, "synthesisDecodingPolicySha256", expectedSha256);
        RequireText(
            generation,
            "synthesisRecoveryPoolPolicyVersion",
            expectedVersion);
        RequireText(
            generation,
            "synthesisRecoveryPoolPolicySha256",
            expectedSha256);
        string computedRetryableSha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(retryableSemanticRejections))));
        IReadOnlyList<string> expectedRetryableSemanticRejections = latestPolicy
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejections
            : terminalPeriodPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousTerminalPeriodRetryableSemanticRejections
            : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousRetryableSemanticRejections;
        string expectedRetryableSemanticRejectionsSha256 = latestPolicy
            ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejectionsSha256
            : terminalPeriodPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousTerminalPeriodRetryableSemanticRejectionsSha256
            : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousRetryableSemanticRejectionsSha256;
        bool validRetryablePolicy = currentPolicy || previousPolicy ||
            priorPolicy || earlierPolicy || legacyPolicy
            ? retryableSemanticRejections.SequenceEqual(
                    expectedRetryableSemanticRejections,
                    StringComparer.Ordinal) &&
                retryableSemanticRejectionsSha256 is not null &&
                retryableSemanticRejectionsSha256.Equals(
                    expectedRetryableSemanticRejectionsSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                computedRetryableSha256.Equals(
                    retryableSemanticRejectionsSha256,
                    StringComparison.OrdinalIgnoreCase)
            : retryableSemanticRejections.Count == 0 &&
                retryableSemanticRejectionsSha256 is null;
        int[] seeds = Qwen3VlEditorialJson.Array(
                generation,
                "synthesisRecoveryPoolSeeds")
            .Select(static value => value.TryGetInt32(out int seed)
                ? seed
                : throw new Qwen3VlOutputParseException(
                    "Grounded Qwen recovery-pool seed is invalid."))
            .ToArray();
        double temperature = Qwen3VlEditorialJson.Finite(
            generation,
            "synthesisRecoveryPoolTemperature");
        double topP = Qwen3VlEditorialJson.Finite(
            generation,
            "synthesisRecoveryPoolTopP");
        bool recoveryPoolApplied = Boolean(
            generation,
            "synthesisRecoveryPoolApplied");
        string expectedTrigger = recoveryPoolApplied
            ? currentPolicy
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Trigger
                : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousTrigger
            : "None";
        if (!validRetryablePolicy ||
            !Qwen3VlEditorialJson.Text(
                generation,
                "synthesisRecoveryPoolTrigger").Equals(
                    expectedTrigger,
                    StringComparison.Ordinal) ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "synthesisRecoveryPoolLogicalPassOrdinal") !=
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .LogicalPassOrdinal ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "synthesisRecoveryPoolSize") !=
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PoolSize ||
            !seeds.SequenceEqual(
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Seeds) ||
            Math.Abs(
                temperature -
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Temperature) >=
                    0.000001 ||
            Math.Abs(
                topP -
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.TopP) >=
                    0.000001 ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "synthesisRecoveryPoolBatchSize") !=
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.BatchSize ||
            Boolean(generation, "synthesisRecoveryPoolDoSample") !=
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "synthesisRecoveryPoolTopK") !=
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.TopK ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "synthesisRecoveryPoolNumberOfBeams") !=
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .NumberOfBeams ||
            Boolean(generation, "synthesisRecoveryPoolUseCache") !=
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache ||
            Boolean(generation, "synthesisRecoveryPoolFreshMatcher") !=
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.FreshMatcher ||
            Boolean(
                generation,
                "synthesisRecoveryPoolUnconstrainedFallbackUsed") ||
            Boolean(generation, "synthesisRecoveryPoolSemanticRepairApplied"))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen synthesis recovery-pool policy is invalid.");
        }
    }

    internal static IReadOnlyList<Qwen3VlGroundedMetadataModuleIdentity>
        ParseModuleIdentities(JsonElement generation) =>
        Qwen3VlEditorialJson.Array(
                generation,
                "groundedMetadataModuleIdentities")
            .Select(static identity =>
            {
                Qwen3VlEditorialJson.Exact(
                    identity,
                    "moduleName",
                    "fileName",
                    "sha256");
                return new Qwen3VlGroundedMetadataModuleIdentity(
                    Qwen3VlEditorialJson.Text(identity, "moduleName"),
                    Qwen3VlEditorialJson.Text(identity, "fileName"),
                    Qwen3VlEditorialJson.Sha256(identity, "sha256"));
            })
            .ToArray();

    internal static IReadOnlyList<string> ParseRetryableSemanticRejections(
        JsonElement generation) =>
        Qwen3VlEditorialJson.Array(
                generation,
                "synthesisRecoveryPoolRetryableSemanticRejections")
            .Select(static value =>
            {
                if (value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(value.GetString()) ||
                    value.GetString()!.Length > 128 ||
                    !value.GetString()!.Equals(
                        value.GetString()!.Trim(),
                        StringComparison.Ordinal))
                {
                    throw new Qwen3VlOutputParseException(
                        "Grounded Qwen recovery-pool retryable rejection is invalid.");
                }

                return value.GetString()!;
            })
            .ToArray();

    internal static IReadOnlyList<Qwen3VlGroundedMetadataSynthesisPassAttestation>
        ParseSynthesisPassAttestations(
            JsonElement generation,
            bool sourceSelectionProvenance) =>
        Qwen3VlEditorialJson.Array(generation, "synthesisPassAttestations")
            .Select(attestation =>
            {
                string[] attestationFields =
                [
                    "logicalPassOrdinal",
                    "candidateOrdinal",
                    "decoding",
                    "seed",
                    "sourcePassOrdinal",
                    "sourceRejectedJsonSha256",
                    .. (sourceSelectionProvenance
                        ? new[] { "sourceSelectionReason" }
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
                    "retryAnchorCaptured",
                    "retryAnchorApplied",
                    "retryAnchorDisabledReason",
                    "retryAnchorEnvelopeSha256",
                    "retryAnchorAuthoritySha256",
                ];
                Qwen3VlEditorialJson.Exact(attestation, attestationFields);
                if (!Enum.TryParse(
                        Qwen3VlEditorialJson.Text(attestation, "decoding"),
                        ignoreCase: false,
                        out Qwen3VlGroundedMetadataSynthesisDecoding decoding) ||
                    !Enum.IsDefined(decoding))
                {
                    throw new Qwen3VlOutputParseException(
                        "Grounded Qwen synthesis attestation decoding is invalid.");
                }
                return new Qwen3VlGroundedMetadataSynthesisPassAttestation(
                    Qwen3VlEditorialJson.Integer(
                        attestation,
                        "logicalPassOrdinal"),
                    NullableInteger(attestation, "candidateOrdinal"),
                    decoding,
                    Qwen3VlEditorialJson.Integer(attestation, "seed"),
                    NullableInteger(attestation, "sourcePassOrdinal"),
                    NullableSha256(attestation, "sourceRejectedJsonSha256"),
                    sourceSelectionProvenance
                        ? Qwen3VlEditorialJson.NullableText(
                            attestation,
                            "sourceSelectionReason")
                        : null,
                    Qwen3VlEditorialJson.Sha256(
                        attestation,
                        "canonicalMessagesSha256"),
                    Qwen3VlEditorialJson.Sha256(
                        attestation,
                        "renderedPromptSha256"),
                    Qwen3VlEditorialJson.Integer(
                        attestation,
                        "renderedPromptUtf8ByteCount"),
                    Qwen3VlEditorialJson.Sha256(
                        attestation,
                        "inputTokenIdsSha256"),
                    Qwen3VlEditorialJson.Integer(attestation, "inputTokenCount"),
                    Qwen3VlEditorialJson.Sha256(attestation, "outputSha256"),
                    Qwen3VlEditorialJson.Sha256(
                        attestation,
                        "completedJsonSha256"),
                    Qwen3VlEditorialJson.NullableText(attestation, "rejectionCode"),
                    Boolean(attestation, "accepted"),
                    Boolean(attestation, "retryAnchorCaptured"),
                    Boolean(attestation, "retryAnchorApplied"),
                    Qwen3VlEditorialJson.NullableText(
                        attestation,
                        "retryAnchorDisabledReason"),
                    NullableSha256(attestation, "retryAnchorEnvelopeSha256"),
                    NullableSha256(attestation, "retryAnchorAuthoritySha256"));
            })
            .ToArray();

    internal static int? NullableInteger(JsonElement value, string propertyName)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, propertyName);
        return property.ValueKind == JsonValueKind.Null
            ? null
            : property.TryGetInt32(out int result)
                ? result
                : throw new Qwen3VlOutputParseException(
                    $"Grounded Qwen {propertyName} is invalid.");
    }

    internal static string? NullableSha256(
        JsonElement value,
        string propertyName) =>
        Qwen3VlEditorialJson.Property(value, propertyName).ValueKind ==
            JsonValueKind.Null
            ? null
            : Qwen3VlEditorialJson.Sha256(value, propertyName);

    internal static bool? NullableBoolean(JsonElement value, string propertyName)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, propertyName);
        return property.ValueKind == JsonValueKind.Null
            ? null
            : Boolean(value, propertyName);
    }

    internal static double? NullableFinite(JsonElement value, string propertyName)
    {
        JsonElement property = Qwen3VlEditorialJson.Property(value, propertyName);
        return property.ValueKind == JsonValueKind.Null
            ? null
            : Qwen3VlEditorialJson.Finite(value, propertyName);
    }

    internal static Qwen3VlGroundedMetadataActorAuthority ActorAuthority(
        JsonElement value,
        string propertyName) =>
        Enum.TryParse(
            Qwen3VlEditorialJson.Text(value, propertyName),
            ignoreCase: false,
            out Qwen3VlGroundedMetadataActorAuthority result) &&
        Enum.IsDefined(result)
            ? result
            : throw new Qwen3VlOutputParseException(
                "Grounded Qwen actor authority is invalid.");

    internal static Qwen3VlGroundedMetadataCreatorExperienceRelation
        CreatorExperienceRelation(JsonElement value, string propertyName) =>
        Enum.TryParse(
            Qwen3VlEditorialJson.Text(value, propertyName),
            ignoreCase: false,
            out Qwen3VlGroundedMetadataCreatorExperienceRelation result) &&
        Enum.IsDefined(result)
            ? result
            : throw new Qwen3VlOutputParseException(
                "Grounded Qwen creator-experience relation is invalid.");
}
