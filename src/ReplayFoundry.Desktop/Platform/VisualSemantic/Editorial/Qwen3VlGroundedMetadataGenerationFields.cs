namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataGenerationFields
{
    internal static readonly string[] RetryAnchorFields =
    [
        "nonRetrospectiveRetryAnchorApplied",
        "nonRetrospectiveRetryAnchorSourcePassOrdinal",
        "nonRetrospectiveRetryAnchorSourceRule",
        "nonRetrospectiveRetryAnchorEnvelopeSha256",
        "nonRetrospectiveRetryAnchorAuthoritySha256",
    ];

    internal static readonly string[] DuplicateRecoveryFields =
    [
        "duplicateSynthesisRecoveryApplied",
        "duplicateSynthesisRecoverySourcePassOrdinal",
        "duplicateSynthesisRecoveryRepeatedPassOrdinal",
        "duplicateSynthesisRecoverySourceRejectedJsonSha256",
        "duplicateSynthesisRecoveryRepeatedRejectedJsonSha256",
    ];

    internal static readonly string[] SampledSynthesisFields =
    [
        "synthesisDecodingPolicyVersion",
        "synthesisDecodingPolicySha256",
        "sampledSynthesisApplied",
        "sampledSynthesisPassOrdinal",
        "sampledSynthesisTrigger",
        "sampledSynthesisSourceRejectedJsonSha256",
        "sampledSynthesisBatchSize",
        "sampledSynthesisDoSample",
        "sampledSynthesisNumberOfBeams",
        "sampledSynthesisUseCache",
        "sampledSynthesisSeed",
        "sampledSynthesisTemperature",
        "sampledSynthesisTopP",
        "sampledSynthesisTopK",
        "sampledSynthesisFreshMatcher",
        "sampledSynthesisUnconstrainedFallbackUsed",
        "sampledSynthesisSemanticRepairApplied",
    ];

    internal static readonly string[] RecoveryPoolFields =
    [
        "synthesisDecodingPolicyVersion",
        "synthesisDecodingPolicySha256",
        "synthesisRecoveryPoolPolicyVersion",
        "synthesisRecoveryPoolPolicySha256",
        "synthesisRecoveryPoolApplied",
        "synthesisRecoveryPoolSourcePassOrdinal",
        "synthesisRecoveryPoolSourceRejectedJsonSha256",
        "synthesisRecoveryPoolAttemptedCandidateCount",
        "synthesisRecoveryPoolSelectedCandidateOrdinal",
        "synthesisRecoveryPoolTrigger",
        "synthesisRecoveryPoolLogicalPassOrdinal",
        "synthesisRecoveryPoolSize",
        "synthesisRecoveryPoolSeeds",
        "synthesisRecoveryPoolBatchSize",
        "synthesisRecoveryPoolDoSample",
        "synthesisRecoveryPoolTemperature",
        "synthesisRecoveryPoolTopP",
        "synthesisRecoveryPoolTopK",
        "synthesisRecoveryPoolNumberOfBeams",
        "synthesisRecoveryPoolUseCache",
        "synthesisRecoveryPoolFreshMatcher",
        "synthesisRecoveryPoolUnconstrainedFallbackUsed",
        "synthesisRecoveryPoolSemanticRepairApplied",
        "groundedMetadataModuleIdentities",
        "synthesisPassAttestations",
    ];
}
