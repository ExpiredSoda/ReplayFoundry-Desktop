using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataSelection
{
    private static readonly HashSet<string> ValidationRuleCodes =
        new(StringComparer.Ordinal)
        {
            "ThirdPersonCreatorFraming",
            "UnsupportedCreatorEmbodiment",
            "GenericOpening",
            "UnsupportedInterfaceAttribution",
            "UnsupportedMentalState",
            "UnreviewedTranscriptReuse",
            "TitleDescriptionRepetition",
            "RedundantGameIdentity",
            "AnalysisBookkeeping",
            "OutputLanguage",
            "NonRetrospectiveVoice",
            "IncompleteTitle",
            "CrossDraftTitleContamination",
            "UnstableReadableTextReuse",
            "FirstPersonTitleSubject",
            "GameHashtag",
            "UncoupledKnowledgeReference",
            "UnsupportedTag",
            "TagShape",
            "UnsupportedKnowledgeGrounding",
            "GroundedRefinementUnchanged",
            "UnresolvedVisualGrounding",
            "RerollTitleTooSimilar",
            "StrictOutputValidation",
        };

    internal static void ValidateGenerationPassProvenance(
        int generationPassCount,
        int visualDraftCount,
        bool visualEventSelectionApplied,
        bool knowledgeSelectionApplied,
        bool groundingReviewApplied,
        IReadOnlyList<string> rejectedValidationRules,
        int? groundingPassCount = null,
        int? synthesisPassCount = null,
        bool? groundingPacketReused = null,
        bool actorAuthorityAssessmentApplied = false,
        bool duplicateSynthesisRecoverySupported = false,
        bool duplicateSynthesisRecoveryApplied = false,
        int? duplicateSynthesisRecoverySourcePassOrdinal = null,
        int? duplicateSynthesisRecoveryRepeatedPassOrdinal = null,
        string? duplicateSynthesisRecoverySourceRejectedJsonSha256 = null,
        string? duplicateSynthesisRecoveryRepeatedRejectedJsonSha256 = null,
        bool sampledSynthesisSupported = false,
        bool sampledSynthesisApplied = false,
        int? sampledSynthesisPassOrdinal = null,
        string? sampledSynthesisTrigger = null,
        string? sampledSynthesisSourceRejectedJsonSha256 = null,
        bool nonRetrospectiveRetryAnchorSupported = false,
        bool nonRetrospectiveRetryAnchorApplied = false,
        int? nonRetrospectiveRetryAnchorSourcePassOrdinal = null,
        string? nonRetrospectiveRetryAnchorSourceRule = null,
        string? nonRetrospectiveRetryAnchorEnvelopeSha256 = null,
        string? nonRetrospectiveRetryAnchorAuthoritySha256 = null,
        bool synthesisRecoveryPoolSupported = false,
        bool synthesisRecoveryPoolApplied = false,
        int? synthesisRecoveryPoolSourcePassOrdinal = null,
        string? synthesisRecoveryPoolSourceRejectedJsonSha256 = null,
        int synthesisRecoveryPoolAttemptedCandidateCount = 0,
        int? synthesisRecoveryPoolSelectedCandidateOrdinal = null,
        bool conditionalRecoveryPoolSourceSupported = false,
        string? synthesisRecoveryPoolSourceSelectionReason = null,
        bool strictRetryAnchorSourceRuleSupported = false,
        bool fourDraftEventSelectionSupported = false,
        bool creatorAuthorityRetrySourceWithholdingSupported = false,
        bool semanticExhaustionRecoverySupported = false,
        bool editorialRephraseSupported = false,
        bool editorialRephraseAttempted = false,
        bool rejectedLanguageRecovered = false)
    {
        ArgumentNullException.ThrowIfNull(rejectedValidationRules);
        int expectedGroundingPassCount = visualDraftCount +
            (actorAuthorityAssessmentApplied || visualEventSelectionApplied ? 1 : 0) +
            (knowledgeSelectionApplied ? 1 : 0);
        int expectedSynthesisPassCount = rejectedValidationRules.Count +
            (groundingReviewApplied && !rejectedLanguageRecovered ? 1 : 0);
        bool hasPacketProvenance = groundingPassCount is not null ||
            synthesisPassCount is not null ||
            groundingPacketReused is not null;
        int expectedPassCount = hasPacketProvenance
            ? expectedSynthesisPassCount +
                (groundingPacketReused == true ? 0 : expectedGroundingPassCount)
            : expectedGroundingPassCount + expectedSynthesisPassCount;
        expectedPassCount += editorialRephraseAttempted ? 1 : 0;
        bool noDuplicateWitness =
            duplicateSynthesisRecoverySourcePassOrdinal is null &&
            duplicateSynthesisRecoveryRepeatedPassOrdinal is null &&
            duplicateSynthesisRecoverySourceRejectedJsonSha256 is null &&
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256 is null;
        bool validDuplicateExtension = duplicateSynthesisRecoveryApplied
            ? duplicateSynthesisRecoverySupported &&
                (synthesisRecoveryPoolSupported
                    ? synthesisPassCount is >= 4 and <= 7 &&
                        rejectedValidationRules.Count >= 3
                    : synthesisPassCount == 4 &&
                        rejectedValidationRules.Count == 3) &&
                duplicateSynthesisRecoverySourcePassOrdinal == 2 &&
                duplicateSynthesisRecoveryRepeatedPassOrdinal == 3 &&
                duplicateSynthesisRecoverySourceRejectedJsonSha256 is not null &&
                duplicateSynthesisRecoverySourceRejectedJsonSha256.Equals(
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
                    StringComparison.OrdinalIgnoreCase)
            : noDuplicateWitness &&
                (synthesisRecoveryPoolSupported ||
                    rejectedValidationRules.Count <= 2 &&
                    (synthesisPassCount is null || synthesisPassCount <= 3));
        bool noSampledWitness =
            sampledSynthesisPassOrdinal is null &&
            sampledSynthesisSourceRejectedJsonSha256 is null &&
            (sampledSynthesisTrigger is null ||
             sampledSynthesisTrigger.Equals("None", StringComparison.Ordinal));
        bool validSampledSynthesis = sampledSynthesisApplied
            ? sampledSynthesisSupported &&
                duplicateSynthesisRecoveryApplied &&
                synthesisPassCount ==
                    Qwen3VlGroundedMetadataSynthesisDecodingPolicy.PassOrdinal &&
                sampledSynthesisPassOrdinal ==
                    Qwen3VlGroundedMetadataSynthesisDecodingPolicy.PassOrdinal &&
                sampledSynthesisTrigger is not null &&
                sampledSynthesisTrigger.Equals(
                    Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Trigger,
                    StringComparison.Ordinal) &&
                sampledSynthesisSourceRejectedJsonSha256 is not null &&
                sampledSynthesisSourceRejectedJsonSha256.Equals(
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
                    StringComparison.OrdinalIgnoreCase)
            : noSampledWitness &&
                (!sampledSynthesisSupported ||
                 !duplicateSynthesisRecoveryApplied ||
                 synthesisRecoveryPoolApplied);
        bool noRetryAnchorWitness =
            nonRetrospectiveRetryAnchorSourcePassOrdinal is null &&
            nonRetrospectiveRetryAnchorSourceRule is null &&
            nonRetrospectiveRetryAnchorEnvelopeSha256 is null &&
            nonRetrospectiveRetryAnchorAuthoritySha256 is null;
        bool validRetryAnchor = nonRetrospectiveRetryAnchorApplied
            ? nonRetrospectiveRetryAnchorSupported &&
                synthesisPassCount is int retainedSynthesisPassCount &&
                nonRetrospectiveRetryAnchorSourcePassOrdinal is int sourcePass &&
                sourcePass >= 1 &&
                sourcePass < retainedSynthesisPassCount &&
                sourcePass <= rejectedValidationRules.Count &&
                nonRetrospectiveRetryAnchorSourceRule is not null &&
                (!strictRetryAnchorSourceRuleSupported ||
                    nonRetrospectiveRetryAnchorSourceRule.Equals(
                        "NonRetrospectiveVoice",
                        StringComparison.Ordinal)) &&
                (synthesisRecoveryPoolSupported ||
                    nonRetrospectiveRetryAnchorSourceRule.Equals(
                        "NonRetrospectiveVoice",
                        StringComparison.Ordinal)) &&
                rejectedValidationRules[sourcePass - 1].Equals(
                    nonRetrospectiveRetryAnchorSourceRule,
                    StringComparison.Ordinal) &&
                nonRetrospectiveRetryAnchorEnvelopeSha256 is not null &&
                nonRetrospectiveRetryAnchorAuthoritySha256 is not null
            : noRetryAnchorWitness;
        bool noRecoveryPoolWitness =
            synthesisRecoveryPoolSourcePassOrdinal is null &&
            synthesisRecoveryPoolSourceRejectedJsonSha256 is null &&
            synthesisRecoveryPoolSourceSelectionReason is null &&
            synthesisRecoveryPoolAttemptedCandidateCount == 0 &&
            synthesisRecoveryPoolSelectedCandidateOrdinal is null;
        bool recoveryScopeWasNarrowedByCrossDraft =
            semanticExhaustionRecoverySupported
                ? rejectedValidationRules.Take(3).Contains(
                    "CrossDraftTitleContamination",
                    StringComparer.Ordinal)
                : rejectedValidationRules.Count > 0 &&
                    rejectedValidationRules[0].Equals(
                        "CrossDraftTitleContamination",
                        StringComparison.Ordinal);
        bool recoveryStartedWithCreatorAuthorityRejection =
            creatorAuthorityRetrySourceWithholdingSupported &&
            rejectedValidationRules.Count > 0 &&
            rejectedValidationRules[0].Equals(
                "UnsupportedCreatorEmbodiment",
                StringComparison.Ordinal);
        int expectedRecoveryPoolSourcePassOrdinal =
            conditionalRecoveryPoolSourceSupported &&
                recoveryScopeWasNarrowedByCrossDraft
                ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PrimaryOnlyCrossDraftSourcePassOrdinal
                : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .OriginalSourcePassOrdinal;
        string? expectedRecoveryPoolSourceSelectionReason =
            conditionalRecoveryPoolSourceSupported
                ? recoveryScopeWasNarrowedByCrossDraft
                    ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PrimaryOnlyCrossDraftSourceSelectionReason
                    : recoveryStartedWithCreatorAuthorityRejection
                        ? Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .CreatorAuthorityRetrySourceSelectionReason
                    : Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .OriginalSourceSelectionReason
                : null;
        bool semanticExhaustionRecoveryApplied =
            semanticExhaustionRecoverySupported &&
            !duplicateSynthesisRecoveryApplied &&
            rejectedValidationRules.Count >= 3 &&
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejectionSet.Contains(
                    rejectedValidationRules[2]);
        bool validRecoveryPool = synthesisRecoveryPoolApplied
            ? synthesisRecoveryPoolSupported &&
                (duplicateSynthesisRecoveryApplied ||
                    semanticExhaustionRecoveryApplied) &&
                synthesisPassCount is >= 4 and <= 7 &&
                rejectedValidationRules.Count >= 3 &&
                rejectedValidationRules.Count <=
                    (rejectedLanguageRecovered ? 7 : 6) &&
                synthesisRecoveryPoolSourcePassOrdinal ==
                    expectedRecoveryPoolSourcePassOrdinal &&
                string.Equals(
                    synthesisRecoveryPoolSourceSelectionReason,
                    expectedRecoveryPoolSourceSelectionReason,
                    StringComparison.Ordinal) &&
                synthesisRecoveryPoolSourceRejectedJsonSha256 is not null &&
                synthesisRecoveryPoolAttemptedCandidateCount is >= 1 and <=
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PoolSize &&
                (rejectedLanguageRecovered
                    ? synthesisRecoveryPoolSelectedCandidateOrdinal is null
                    : synthesisRecoveryPoolSelectedCandidateOrdinal ==
                        synthesisRecoveryPoolAttemptedCandidateCount) &&
                synthesisPassCount == 3 +
                    synthesisRecoveryPoolAttemptedCandidateCount
            : noRecoveryPoolWitness &&
                (!synthesisRecoveryPoolSupported ||
                    synthesisPassCount is null or <= 3 ||
                    sampledSynthesisApplied);
        int maximumVisualDraftCount = fourDraftEventSelectionSupported ? 4 : 3;
        int maximumGenerationPasses = synthesisRecoveryPoolSupported
            ? fourDraftEventSelectionSupported ? 13 : 12
            : duplicateSynthesisRecoveryApplied ? 9 : 8;
        maximumGenerationPasses += editorialRephraseSupported ? 1 : 0;
        if (visualDraftCount < 1 || visualDraftCount > maximumVisualDraftCount ||
            visualEventSelectionApplied != (visualDraftCount > 1) ||
            generationPassCount < 1 ||
            generationPassCount > maximumGenerationPasses ||
            generationPassCount != expectedPassCount ||
            editorialRephraseAttempted != editorialRephraseSupported ||
            hasPacketProvenance &&
                (groundingPassCount != expectedGroundingPassCount ||
                 synthesisPassCount != expectedSynthesisPassCount ||
                 groundingPacketReused is null) ||
            !validDuplicateExtension ||
            !validSampledSynthesis ||
            !validRetryAnchor ||
            !validRecoveryPool ||
            rejectedValidationRules.Any(static value =>
                !ValidationRuleCodes.Contains(value)))
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen metadata validation-attempt provenance is invalid.");
        }
    }

    internal static void ValidateSynthesisRecoveryPoolProvenance(
        int synthesisPassCount,
        IReadOnlyList<string> rejectedValidationRules,
        string decodedTextSha256,
        bool synthesisRecoveryPoolApplied,
        int? synthesisRecoveryPoolSourcePassOrdinal,
        string? synthesisRecoveryPoolSourceRejectedJsonSha256,
        int synthesisRecoveryPoolAttemptedCandidateCount,
        int? synthesisRecoveryPoolSelectedCandidateOrdinal,
        IReadOnlyList<Qwen3VlGroundedMetadataModuleIdentity> moduleIdentities,
        IReadOnlyList<Qwen3VlGroundedMetadataSynthesisPassAttestation>
            attestations,
        bool duplicateSynthesisRecoveryApplied,
        string? duplicateSynthesisRecoverySourceRejectedJsonSha256,
        string? duplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
        bool nonRetrospectiveRetryAnchorApplied,
        int? nonRetrospectiveRetryAnchorSourcePassOrdinal,
        string? nonRetrospectiveRetryAnchorEnvelopeSha256,
        string? nonRetrospectiveRetryAnchorAuthoritySha256,
        IReadOnlySet<string>? retryableSemanticRejections = null,
        string? synthesisRecoveryPoolSourceSelectionReason = null,
        bool conditionalRecoveryPoolSource = false,
        bool strictRetryAnchorSourceRule = false,
        bool crossDraftRetrySourceWithholding = false,
        bool creatorAuthorityRetrySourceWithholding = false,
        bool semanticExhaustionRecovery = false,
        bool editorialRephraseSupported = true,
        bool rephraseMessageModuleSupported = true,
        Qwen3VlGroundedMetadataEditorialRephraseValidation? editorialRephrase = null)
        => Qwen3VlGroundedMetadataRecoverySelection.Validate(
            synthesisPassCount,
            rejectedValidationRules,
            decodedTextSha256,
            synthesisRecoveryPoolApplied,
            synthesisRecoveryPoolSourcePassOrdinal,
            synthesisRecoveryPoolSourceRejectedJsonSha256,
            synthesisRecoveryPoolAttemptedCandidateCount,
            synthesisRecoveryPoolSelectedCandidateOrdinal,
            moduleIdentities,
            attestations,
            duplicateSynthesisRecoveryApplied,
            duplicateSynthesisRecoverySourceRejectedJsonSha256,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256,
            nonRetrospectiveRetryAnchorApplied,
            nonRetrospectiveRetryAnchorSourcePassOrdinal,
            nonRetrospectiveRetryAnchorEnvelopeSha256,
            nonRetrospectiveRetryAnchorAuthoritySha256,
            retryableSemanticRejections,
            synthesisRecoveryPoolSourceSelectionReason,
            conditionalRecoveryPoolSource,
            strictRetryAnchorSourceRule,
            crossDraftRetrySourceWithholding,
            creatorAuthorityRetrySourceWithholding,
            semanticExhaustionRecovery,
            editorialRephraseSupported,
            rephraseMessageModuleSupported,
            editorialRephrase);

    internal static bool IsKnownValidationRule(string value) =>
        ValidationRuleCodes.Contains(value);

    internal static Qwen3VlGroundedMetadataVisualEventSelectionOutcome
        SelectPrimaryVisualDraft(
            IReadOnlyList<Qwen3VlGroundedMetadataVisualEventAssessment> assessments)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        Qwen3VlGroundedMetadataVisualEventAssessment[] eligible = assessments
            .Where(static value => value.HasDistinctEventSupport)
            .ToArray();
        if (eligible.Length == 0)
        {
            return new(
                Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                    .NoDistinctPrimaryEvent,
                null);
        }

        int primaryOrdinal = eligible
            .OrderByDescending(static value => value.Score)
            .ThenByDescending(static value => value.DistinctAction)
            .ThenByDescending(static value => value.VisibleOutcome)
            .ThenByDescending(static value => value.ObjectInteraction)
            .ThenByDescending(static value => value.ReadableInterfaceChange)
            .ThenBy(static value => value.RoutineOnly || value.Uncertain)
            .ThenByDescending(static value => value.Ordinal)
            .First()
            .Ordinal;
        return new(
            Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                .SelectedDistinctPrimaryEvent,
            primaryOrdinal);
    }

    internal static string SelectKnowledgePassage(
        IReadOnlyList<Qwen3VlGroundedMetadataKnowledgeAssessment> assessments)
    {
        Qwen3VlGroundedMetadataKnowledgeAssessment[] eligible = assessments
            .Where(static value =>
                value.SupportCount >= 2 && !value.MaterialContradiction)
            .ToArray();
        if (eligible.Length == 0)
        {
            return "None";
        }

        int maximum = eligible.Max(static value => value.SupportCount);
        Qwen3VlGroundedMetadataKnowledgeAssessment[] winners = eligible
            .Where(value => value.SupportCount == maximum)
            .ToArray();
        return winners.Length == 1 ? winners[0].PassageId : "None";
    }

    internal static bool IsCurrentKnowledgeCandidate(
        GameKnowledgeMatchStrength strength,
        GameKnowledgeTemporalRelation temporalRelation,
        bool includeClipLinked = true) =>
        temporalRelation == GameKnowledgeTemporalRelation.CurrentEventCandidate &&
        (strength == GameKnowledgeMatchStrength.CandidateForVisualGrounding ||
            includeClipLinked &&
            strength == GameKnowledgeMatchStrength.ClipLinked);

    internal static string[] VisualDraftTextArray(
        JsonElement draft,
        string propertyName,
        int minimumCount,
        int maximumCount,
        int maximumLength)
    {
        string[] values = Qwen3VlEditorialJson.Array(draft, propertyName)
            .Select(value => value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : throw new Qwen3VlOutputParseException(
                    $"Grounded Qwen visual-draft {propertyName} must contain text."))
            .ToArray();
        if (values.Length < minimumCount ||
            values.Length > maximumCount ||
            values.Any(value => value.Length > maximumLength))
        {
            throw new Qwen3VlOutputParseException(
                $"Grounded Qwen visual-draft {propertyName} is invalid.");
        }

        return values;
    }
}
