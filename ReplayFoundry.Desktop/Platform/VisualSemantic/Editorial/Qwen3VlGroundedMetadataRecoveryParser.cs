using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataGenerator;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataRecoveryPolicyParser;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataRecoveryParser
{
    internal static Qwen3VlGroundedMetadataRecoveryValidation Parse(
        JsonElement generation,
        ClipEditorialMetadataRequest request,
        string outputSchema,
        Qwen3VlGroundedMetadataGenerationSchemaProfile profile)
    {
        int generated = Qwen3VlEditorialJson.Integer(
            generation,
            "generatedTokenCount");
        int maximum = Qwen3VlEditorialJson.Integer(generation, "maximumNewTokens");
        if (generated <= 0 ||
            maximum != MaximumNewTokens ||
            generated >= maximum ||
            Qwen3VlEditorialJson.Integer(
                generation,
                "firstEndOfSequenceGeneratedIndex") != generated - 1 ||
            !Qwen3VlEditorialJson.Text(generation, "terminationReason")
                .Equals("EndOfSequence", StringComparison.Ordinal))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata generation did not reach bounded EOS completion.");
        }
        string decodedTextSha256 = Qwen3VlEditorialJson.Sha256(
            generation,
            "decodedTextSha256");
        bool metadataReviewRequired = profile.ReviewableAudienceCopy &&
            Boolean(generation, "metadataReviewRequired");
        string[] metadataReviewIssues = profile.ReviewableAudienceCopy
            ? TextArray(generation, "metadataReviewIssues", 8)
            : [];
        if (profile.ReviewableAudienceCopy &&
            (metadataReviewRequired != (metadataReviewIssues.Length > 0) ||
             metadataReviewIssues.Any(code =>
                 !Qwen3VlGroundedMetadataSelection.IsKnownValidationRule(code))))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata-review provenance is invalid.");
        }
        int generationPassCount = Qwen3VlEditorialJson.Integer(
            generation,
            "generationPassCount");
        int? groundingPassCount = profile.PacketReuse
            ? Qwen3VlEditorialJson.Integer(generation, "groundingPassCount")
            : null;
        int? synthesisPassCount = profile.PacketReuse
            ? Qwen3VlEditorialJson.Integer(generation, "synthesisPassCount")
            : null;
        bool duplicateApplied = profile.BoundedDuplicateRefinement && Boolean(
            generation,
            "duplicateSynthesisRecoveryApplied");
        int? duplicateSourcePass = profile.BoundedDuplicateRefinement
            ? NullableInteger(
                generation,
                "duplicateSynthesisRecoverySourcePassOrdinal")
            : null;
        int? duplicateRepeatedPass = profile.BoundedDuplicateRefinement
            ? NullableInteger(
                generation,
                "duplicateSynthesisRecoveryRepeatedPassOrdinal")
            : null;
        string? duplicateSourceHash = profile.BoundedDuplicateRefinement
            ? NullableSha256(
                generation,
                "duplicateSynthesisRecoverySourceRejectedJsonSha256")
            : null;
        string? duplicateRepeatedHash = profile.BoundedDuplicateRefinement
            ? NullableSha256(
                generation,
                "duplicateSynthesisRecoveryRepeatedRejectedJsonSha256")
            : null;
        bool sampledApplied = profile.SampledSynthesis && Boolean(
            generation,
            "sampledSynthesisApplied");
        int? sampledPass = profile.SampledSynthesis
            ? NullableInteger(generation, "sampledSynthesisPassOrdinal")
            : null;
        string? sampledTrigger = profile.SampledSynthesis
            ? Qwen3VlEditorialJson.Text(generation, "sampledSynthesisTrigger")
            : null;
        string? sampledSourceHash = profile.SampledSynthesis
            ? NullableSha256(
                generation,
                "sampledSynthesisSourceRejectedJsonSha256")
            : null;
        int? sampledBatchSize = profile.SampledSynthesis
            ? NullableInteger(generation, "sampledSynthesisBatchSize")
            : null;
        bool? sampledDoSample = profile.SampledSynthesis
            ? NullableBoolean(generation, "sampledSynthesisDoSample")
            : null;
        int? sampledBeams = profile.SampledSynthesis
            ? NullableInteger(generation, "sampledSynthesisNumberOfBeams")
            : null;
        bool? sampledUseCache = profile.SampledSynthesis
            ? NullableBoolean(generation, "sampledSynthesisUseCache")
            : null;
        int? sampledSeed = profile.SampledSynthesis
            ? NullableInteger(generation, "sampledSynthesisSeed")
            : null;
        double? sampledTemperature = profile.SampledSynthesis
            ? NullableFinite(generation, "sampledSynthesisTemperature")
            : null;
        double? sampledTopP = profile.SampledSynthesis
            ? NullableFinite(generation, "sampledSynthesisTopP")
            : null;
        int? sampledTopK = profile.SampledSynthesis
            ? NullableInteger(generation, "sampledSynthesisTopK")
            : null;
        bool? sampledFreshMatcher = profile.SampledSynthesis
            ? NullableBoolean(generation, "sampledSynthesisFreshMatcher")
            : null;
        bool? sampledFallback = profile.SampledSynthesis
            ? NullableBoolean(
                generation,
                "sampledSynthesisUnconstrainedFallbackUsed")
            : null;
        bool? sampledRepair = profile.SampledSynthesis
            ? NullableBoolean(
                generation,
                "sampledSynthesisSemanticRepairApplied")
            : null;
        bool retryAnchorApplied = profile.NonRetrospectiveRetryAnchor && Boolean(
            generation,
            "nonRetrospectiveRetryAnchorApplied");
        int? retryAnchorSourcePass = profile.NonRetrospectiveRetryAnchor
            ? NullableInteger(
                generation,
                "nonRetrospectiveRetryAnchorSourcePassOrdinal")
            : null;
        string? retryAnchorSourceRule = profile.NonRetrospectiveRetryAnchor
            ? Qwen3VlEditorialJson.NullableText(
                generation,
                "nonRetrospectiveRetryAnchorSourceRule")
            : null;
        string? retryAnchorEnvelope = profile.NonRetrospectiveRetryAnchor
            ? NullableSha256(
                generation,
                "nonRetrospectiveRetryAnchorEnvelopeSha256")
            : null;
        string? retryAnchorAuthority = profile.NonRetrospectiveRetryAnchor
            ? NullableSha256(
                generation,
                "nonRetrospectiveRetryAnchorAuthoritySha256")
            : null;
        bool poolApplied = profile.SynthesisRecoveryPool && Boolean(
            generation,
            "synthesisRecoveryPoolApplied");
        int? poolSourcePass = profile.SynthesisRecoveryPool
            ? NullableInteger(
                generation,
                "synthesisRecoveryPoolSourcePassOrdinal")
            : null;
        string? poolSourceHash = profile.SynthesisRecoveryPool
            ? NullableSha256(
                generation,
                "synthesisRecoveryPoolSourceRejectedJsonSha256")
            : null;
        string? poolSourceReason = profile.ConditionalRecoveryPoolSource
            ? Qwen3VlEditorialJson.NullableText(
                generation,
                "synthesisRecoveryPoolSourceSelectionReason")
            : null;
        int poolAttemptedCount = profile.SynthesisRecoveryPool
            ? Qwen3VlEditorialJson.Integer(
                generation,
                "synthesisRecoveryPoolAttemptedCandidateCount")
            : 0;
        int? poolSelectedOrdinal = profile.SynthesisRecoveryPool
            ? NullableInteger(
                generation,
                "synthesisRecoveryPoolSelectedCandidateOrdinal")
            : null;
        IReadOnlyList<Qwen3VlGroundedMetadataModuleIdentity> moduleIdentities =
            profile.SynthesisRecoveryPool
                ? ParseModuleIdentities(generation)
                : [];
        IReadOnlyList<Qwen3VlGroundedMetadataSynthesisPassAttestation>
            attestations = profile.SynthesisRecoveryPool
                ? ParseSynthesisPassAttestations(
                    generation,
                    profile.ConditionalRecoveryPoolSource)
                : [];
        IReadOnlyList<string> retryableRejections =
            profile.RetryableContinuationRecoveryPool
                ? ParseRetryableSemanticRejections(generation)
                : [];
        string? retryableRejectionsSha256 =
            profile.RetryableContinuationRecoveryPool
                ? Qwen3VlEditorialJson.Sha256(
                    generation,
                    "synthesisRecoveryPoolRetryableSemanticRejectionsSha256")
                : null;
        if (profile.SynthesisRecoveryPool)
        {
            Qwen3VlGroundedMetadataRecoveryPolicyParser.Validate(
                generation,
                outputSchema,
                retryableRejections,
                retryableRejectionsSha256);
        }
        if (profile.SampledSynthesis)
        {
            ValidateSampledSynthesis(
                generation,
                sampledApplied,
                sampledPass,
                sampledTrigger,
                sampledSourceHash,
                sampledBatchSize,
                sampledDoSample,
                sampledBeams,
                sampledUseCache,
                sampledSeed,
                sampledTemperature,
                sampledTopP,
                sampledTopK,
                sampledFreshMatcher,
                sampledFallback,
                sampledRepair);
        }
        string? packetRequestHash = profile.PacketReuse
            ? Qwen3VlEditorialJson.Sha256(
                generation,
                "groundingPacketRequestSha256")
            : null;
        string? packetFactHash = profile.PacketReuse
            ? Qwen3VlEditorialJson.Sha256(
                generation,
                "groundingPacketFactSha256")
            : null;
        int? packetSourceAttempt = profile.PacketReuse
            ? Qwen3VlEditorialJson.Integer(
                generation,
                "groundingPacketSourceAttempt")
            : null;
        bool? packetReused = profile.PacketReuse
            ? Boolean(generation, "groundingPacketReused")
            : null;
        bool? primaryOnlyEvidence = profile.EvidenceIsolation
            ? Boolean(generation, "primaryOnlySynthesisEvidenceApplied")
            : null;
        bool actorAssessmentApplied = profile.ActorAuthority && Boolean(
            generation,
            "actorAuthorityAssessmentApplied");
        Qwen3VlGroundedMetadataActorAuthority primaryActorAuthority =
            profile.ActorAuthority
                ? ActorAuthority(generation, "primaryActorAuthority")
                : Qwen3VlGroundedMetadataActorAuthority.Unknown;
        Qwen3VlGroundedMetadataCreatorExperienceRelation primaryRelation =
            profile.ActorAuthority
                ? CreatorExperienceRelation(
                    generation,
                    "primaryCreatorExperienceRelation")
                : Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished;
        ParseRerollDiversity(
            generation,
            profile,
            out int? priorTitleCount,
            out Qwen3VlGroundedMetadataRerollTitleDiversityCode? diversityCode,
            out int? jaccardNumerator,
            out int? jaccardDenominator);
        Qwen3VlGroundedMetadataEditorialRephraseValidation? editorialRephrase =
            profile.EditorialRephrase
                ? Qwen3VlGroundedMetadataEditorialRephrasePolicy.Parse(
                    generation,
                    profile.RejectedLanguageRecovery,
                    profile.TypedLanguageRecovery,
                    profile.CreatorEmbodimentRecovery,
                    profile.WithheldEmbodimentCopyRecovery,
                    profile.LiteralActionRecovery,
                    profile.RetrospectiveGrammarRecovery,
                    profile.NeutralPersonRecovery,
                    profile.OutputLanguageRecovery,
                    profile.TerminalPeriodNormalization,
                    profile.ReviewableAudienceCopy)
                : null;
        if (profile.PacketReuse)
        {
            RequireText(
                generation,
                "groundingPacketSchemaVersion",
                GroundingPacketSchemaVersion);
            if (packetSourceAttempt is < 0 or > 100 ||
                packetReused == false && packetSourceAttempt != request.Attempt ||
                packetReused == true && packetSourceAttempt == request.Attempt)
            {
                throw new Qwen3VlOutputParseException(
                    "Grounded Qwen grounding-packet source provenance is invalid.");
            }
        }
        return new(
            generated,
            decodedTextSha256,
            generationPassCount,
            groundingPassCount,
            synthesisPassCount,
            duplicateApplied,
            duplicateSourcePass,
            duplicateRepeatedPass,
            duplicateSourceHash,
            duplicateRepeatedHash,
            sampledApplied,
            sampledPass,
            sampledTrigger,
            sampledSourceHash,
            retryAnchorApplied,
            retryAnchorSourcePass,
            retryAnchorSourceRule,
            retryAnchorEnvelope,
            retryAnchorAuthority,
            poolApplied,
            poolSourcePass,
            poolSourceHash,
            poolSourceReason,
            poolAttemptedCount,
            poolSelectedOrdinal,
            retryableRejections,
            retryableRejectionsSha256,
            moduleIdentities,
            attestations,
            packetRequestHash,
            packetFactHash,
            packetSourceAttempt,
            packetReused,
            primaryOnlyEvidence,
            actorAssessmentApplied,
            primaryActorAuthority,
            primaryRelation,
            priorTitleCount,
            diversityCode,
            jaccardNumerator,
            jaccardDenominator,
            metadataReviewRequired,
            metadataReviewIssues,
            editorialRephrase);
    }

    private static void ValidateSampledSynthesis(
        JsonElement generation,
        bool applied,
        int? passOrdinal,
        string? trigger,
        string? sourceHash,
        int? batchSize,
        bool? doSample,
        int? beams,
        bool? useCache,
        int? seed,
        double? temperature,
        double? topP,
        int? topK,
        bool? freshMatcher,
        bool? fallback,
        bool? repair)
    {
        RequireText(
            generation,
            "synthesisDecodingPolicyVersion",
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Version);
        RequireText(
            generation,
            "synthesisDecodingPolicySha256",
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Sha256);
        bool noConfiguration = passOrdinal is null && sourceHash is null &&
            batchSize is null && doSample is null && beams is null &&
            useCache is null && seed is null && temperature is null &&
            topP is null && topK is null && freshMatcher is null &&
            fallback is null && repair is null &&
            trigger is not null && trigger.Equals("None", StringComparison.Ordinal);
        bool exactConfiguration = passOrdinal ==
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.PassOrdinal &&
            sourceHash is not null &&
            batchSize == Qwen3VlGroundedMetadataSynthesisDecodingPolicy.BatchSize &&
            doSample == Qwen3VlGroundedMetadataSynthesisDecodingPolicy.DoSample &&
            beams == Qwen3VlGroundedMetadataSynthesisDecodingPolicy.NumberOfBeams &&
            useCache == Qwen3VlGroundedMetadataSynthesisDecodingPolicy.UseCache &&
            seed == Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Seed &&
            temperature is double actualTemperature &&
            Math.Abs(
                actualTemperature -
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Temperature) <
                0.000001 &&
            topP is double actualTopP &&
            Math.Abs(
                actualTopP - Qwen3VlGroundedMetadataSynthesisDecodingPolicy.TopP) <
                0.000001 &&
            topK == Qwen3VlGroundedMetadataSynthesisDecodingPolicy.TopK &&
            freshMatcher == true && fallback == false && repair == false &&
            trigger is not null && trigger.Equals(
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Trigger,
                StringComparison.Ordinal);
        if (applied ? !exactConfiguration : !noConfiguration)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen sampled-synthesis provenance is invalid.");
        }
    }

    private static void ParseRerollDiversity(
        JsonElement generation,
        Qwen3VlGroundedMetadataGenerationSchemaProfile profile,
        out int? priorAcceptedTitleCount,
        out Qwen3VlGroundedMetadataRerollTitleDiversityCode? diversityCode,
        out int? numerator,
        out int? denominator)
    {
        priorAcceptedTitleCount = null;
        diversityCode = null;
        numerator = null;
        denominator = null;
        if (!profile.RerollDiversity)
        {
            return;
        }
        RequireText(
            generation,
            "rerollDiversityPolicyVersion",
            RerollDiversityPolicyVersion);
        priorAcceptedTitleCount = Qwen3VlEditorialJson.Integer(
            generation,
            "priorAcceptedTitleCount");
        if (!Enum.TryParse(
                Qwen3VlEditorialJson.Text(generation, "rerollTitleDiversityCode"),
                ignoreCase: false,
                out Qwen3VlGroundedMetadataRerollTitleDiversityCode parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen reroll-diversity code is invalid.");
        }
        diversityCode = parsed;
        numerator = Qwen3VlEditorialJson.Integer(
            generation,
            "rerollTitleTokenJaccardNumerator");
        denominator = Qwen3VlEditorialJson.Integer(
            generation,
            "rerollTitleTokenJaccardDenominator");
        bool noComparable = parsed ==
            Qwen3VlGroundedMetadataRerollTitleDiversityCode.NoComparablePrior;
        bool acceptedDistinct = parsed ==
            Qwen3VlGroundedMetadataRerollTitleDiversityCode.MateriallyDistinct;
        if (priorAcceptedTitleCount is < 0 or > MaximumCases - 1 ||
            numerator < 0 || denominator <= 0 || numerator > denominator ||
            noComparable != (priorAcceptedTitleCount == 0) ||
            !noComparable && !acceptedDistinct ||
            noComparable && (numerator != 0 || denominator != 1) ||
            acceptedDistinct &&
                (long)numerator * Qwen3VlGroundedMetadataRerollDiversityPolicy
                    .SimilarityThresholdDenominator >=
                (long)Qwen3VlGroundedMetadataRerollDiversityPolicy
                    .SimilarityThresholdNumerator * denominator)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen accepted invalid reroll-diversity provenance.");
        }
    }

    private static string[] TextArray(
        JsonElement value,
        string name,
        int maximum)
    {
        JsonElement[] array = Qwen3VlEditorialJson.Array(value, name);
        string[] results = array.Select(item =>
                item.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(item.GetString())
                    ? item.GetString()!
                    : throw new Qwen3VlOutputParseException(
                        $"Grounded Qwen '{name}' entries must be text."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (results.Length != array.Length || results.Length > maximum)
        {
            throw new Qwen3VlOutputParseException(
                $"Grounded Qwen '{name}' is invalid.");
        }
        return results;
    }
}
