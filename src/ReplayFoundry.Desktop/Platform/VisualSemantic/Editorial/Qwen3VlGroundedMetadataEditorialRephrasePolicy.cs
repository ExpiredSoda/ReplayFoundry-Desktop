using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataEditorialRephrasePolicy
{
    internal const string Version = "grounded-editorial-rephrase-2.9";
    internal const string Sha256 =
        "F8C050C701DAFCDC6B2FEA4F87868CAED21DEB7732B1308612F409626E787F25";
    internal const string PreviousIsolatedFieldAuthoringVersion = "grounded-editorial-rephrase-2.8";
    internal const string PreviousIsolatedFieldAuthoringSha256 =
        "D62EF6A57F0EAC28213A9AFBA641FE1465D340B69713AD9A85E43430BF7AC50A";
    internal const string PreviousSchemaEnforcedBalancedCopyVersion = "grounded-editorial-rephrase-2.7";
    internal const string PreviousSchemaEnforcedBalancedCopySha256 =
        "322DBAD6D798E33C636D247D686BC2BA8404DC1A97CAD971F5AD6D1BE7ECEBE8";
    internal const string PreviousCompactBalancedCopyVersion = "grounded-editorial-rephrase-2.6";
    internal const string PreviousCompactBalancedCopySha256 =
        "C3AB7339E9322332093D23F37599F2C96E1575769892C3C90D4AF76455250A4E";
    internal const string PreviousBalancedCopyVersion = "grounded-editorial-rephrase-2.5";
    internal const string PreviousBalancedCopySha256 =
        "7FBE117C5865E1BA209D11E48E4FE421AFEA815F3A91492252B6FBC81C3E0C7D";
    internal const string PreviousCommentaryTimingVersion =
        "grounded-editorial-rephrase-2.4";
    internal const string PreviousCommentaryTimingSha256 =
        "8682A789FDAC6EF51963996CFE13F084DD85E8432D7098080875FBAAC1E97CA7";
    internal const string PreviousAudienceFrameVersion =
        "grounded-editorial-rephrase-2.3";
    internal const string PreviousAudienceFrameSha256 =
        "0F255474058669B37D9B70CE1CA07F10AD547A0E860A24F02CBCB8ADC5D6A075";
    internal const string PreviousStoryLedGrammaticalCenterVersion =
        "grounded-editorial-rephrase-2.2";
    internal const string PreviousStoryLedGrammaticalCenterSha256 =
        "AEEE7B7CCA2D7B07C337836C7F5F0061D35D33B27874D43549BD7EBCD2645135";
    internal const string PreviousEditorialFrameAdherenceVersion =
        "grounded-editorial-rephrase-2.1";
    internal const string PreviousEditorialFrameAdherenceSha256 =
        "1569B4B881FAB8ABF5347CDFD01F8C262B24DD5935192BDD090C369532BFF169";
    internal const string PreviousEditorialFramingVersion =
        "grounded-editorial-rephrase-2.0";
    internal const string PreviousEditorialFramingSha256 =
        "556B11AD5535F4D16883A2A43BBD72AD83996520F4D6D8FC87D06615DCCBBA04";
    internal const string PreviousReviewableCopyVersion =
        "grounded-editorial-rephrase-1.9";
    internal const string PreviousReviewableCopySha256 =
        "4F0C689382C68AFC5B5DEDC2C3175B68787B11A9413F6BEB23A1A2C49C6C23C8";
    internal const string PreviousTerminalPeriodVersion =
        "grounded-editorial-rephrase-1.8";
    internal const string PreviousTerminalPeriodSha256 =
        "7FAD4C2ABA040F8DC61B37153DD2084D3B824BE572B4D4C234C975CAEC0EB10D";
    internal const string PreviousNeutralPersonRecoveryVersion =
        "grounded-editorial-rephrase-1.7";
    internal const string PreviousNeutralPersonRecoverySha256 =
        "614477386FE746AE752BCA2D7A1DA4A6D6501F0BDE5C3CDC533F4D8B0C19797C";
    internal const string PreviousRetrospectiveGrammarRecoveryVersion =
        "grounded-editorial-rephrase-1.6";
    internal const string PreviousRetrospectiveGrammarRecoverySha256 =
        "6E36F14675CA40B21FA3A6DF01F804B7F459B40331ACF937810858D255D4B5DF";
    internal const string PreviousLiteralActionRecoveryVersion =
        "grounded-editorial-rephrase-1.5";
    internal const string PreviousLiteralActionRecoverySha256 =
        "415C65A7C1B1902784ABEDD54AF36381C0FC086DCA0950B37E698AA518AEC829";
    internal const string PreviousWithheldEmbodimentCopyVersion =
        "grounded-editorial-rephrase-1.4";
    internal const string PreviousWithheldEmbodimentCopySha256 =
        "030A81279CFD4C9B7FBD454EA845669225B9940A810765036DBD53B6575B30AE";
    internal const string PreviousCreatorEmbodimentRecoveryVersion =
        "grounded-editorial-rephrase-1.3";
    internal const string PreviousCreatorEmbodimentRecoverySha256 =
        "05F833616F1BA519E0DADF9E58E0AE02EEC0DE06EA84C247A53225D6EA6939D8";
    internal const string PreviousTypedLanguageRecoveryVersion =
        "grounded-editorial-rephrase-1.2";
    internal const string PreviousTypedLanguageRecoverySha256 =
        "1B23D128C06AAFADA22821C96C45A5C496B9392C6604325C4DA21AE8FE6EBBE4";
    internal const string PreviousLanguageRecoveryVersion =
        "grounded-editorial-rephrase-1.1";
    internal const string PreviousLanguageRecoverySha256 =
        "F5255DF841A1F732BFE503267E98E758CBB0C99CFD5F8B7AB66E84DA32FF2FCF";
    internal const string PreviousVersion = "grounded-editorial-rephrase-1.0";
    internal const string PreviousSha256 =
        "5B624DA570BC493E25330F8AC66087A525B665077E6255DED9C2BBB14C67B17B";

    internal static Qwen3VlGroundedMetadataEditorialRephraseValidation Parse(
        JsonElement generation,
        bool rejectedLanguageRecoverySupported = true,
        bool typedLanguageRecoverySupported = true,
        bool creatorEmbodimentRecoverySupported = true,
        bool withheldEmbodimentCopyRecoverySupported = true,
        bool literalActionRecoverySupported = true,
        bool retrospectiveGrammarRecoverySupported = true,
        bool neutralPersonRecoverySupported = true,
        bool outputLanguageRecoverySupported = true,
        bool terminalPeriodNormalizationSupported = true,
        bool reviewableAudienceCopySupported = true,
        bool editorialFramingSupported = true,
        bool editorialFrameAdherenceSupported = true,
        bool eligibilitySkipSupported = false,
        bool commentaryTimingSupported = true,
        bool balancedCopySupported = true,
        bool compactBalancedCopySupported = true,
        bool schemaEnforcedBalancedCopySupported = true,
        bool isolatedFieldAuthoringSupported = true,
        bool isolatedFieldAuthoringVerified = false)
    {
        bool currentContract = editorialFrameAdherenceSupported &&
            editorialFramingSupported && reviewableAudienceCopySupported;
        string expectedVersion = currentContract
            ? (commentaryTimingSupported
                ? (balancedCopySupported
                    ? (compactBalancedCopySupported
                        ? (schemaEnforcedBalancedCopySupported
                            ? (isolatedFieldAuthoringSupported ? Version : PreviousIsolatedFieldAuthoringVersion)
                            : PreviousSchemaEnforcedBalancedCopyVersion)
                        : PreviousCompactBalancedCopyVersion)
                    : PreviousBalancedCopyVersion)
                : PreviousCommentaryTimingVersion)
            : editorialFramingSupported && reviewableAudienceCopySupported
                ? PreviousEditorialFrameAdherenceVersion
            : reviewableAudienceCopySupported
                ? PreviousEditorialFramingVersion
            : terminalPeriodNormalizationSupported
                ? PreviousReviewableCopyVersion
            : outputLanguageRecoverySupported
                ? PreviousTerminalPeriodVersion
            : neutralPersonRecoverySupported
                ? PreviousNeutralPersonRecoveryVersion
            : retrospectiveGrammarRecoverySupported
                ? PreviousRetrospectiveGrammarRecoveryVersion
            : literalActionRecoverySupported
                ? PreviousLiteralActionRecoveryVersion
            : withheldEmbodimentCopyRecoverySupported
                ? PreviousWithheldEmbodimentCopyVersion
            : creatorEmbodimentRecoverySupported
                ? PreviousCreatorEmbodimentRecoveryVersion
                : typedLanguageRecoverySupported
                    ? PreviousTypedLanguageRecoveryVersion
                    : rejectedLanguageRecoverySupported
                        ? PreviousLanguageRecoveryVersion
                        : PreviousVersion;
        string expectedSha256 = currentContract
            ? (commentaryTimingSupported
                ? (balancedCopySupported
                    ? (compactBalancedCopySupported
                        ? (schemaEnforcedBalancedCopySupported
                            ? (isolatedFieldAuthoringSupported ? Sha256 : PreviousIsolatedFieldAuthoringSha256)
                            : PreviousSchemaEnforcedBalancedCopySha256)
                        : PreviousCompactBalancedCopySha256)
                    : PreviousBalancedCopySha256)
                : PreviousCommentaryTimingSha256)
            : editorialFramingSupported && reviewableAudienceCopySupported
                ? PreviousEditorialFrameAdherenceSha256
            : reviewableAudienceCopySupported
                ? PreviousEditorialFramingSha256
            : terminalPeriodNormalizationSupported
                ? PreviousReviewableCopySha256
            : outputLanguageRecoverySupported
                ? PreviousTerminalPeriodSha256
            : neutralPersonRecoverySupported
                ? PreviousNeutralPersonRecoverySha256
            : retrospectiveGrammarRecoverySupported
                ? PreviousRetrospectiveGrammarRecoverySha256
            : literalActionRecoverySupported
                ? PreviousLiteralActionRecoverySha256
            : withheldEmbodimentCopyRecoverySupported
                ? PreviousWithheldEmbodimentCopySha256
            : creatorEmbodimentRecoverySupported
                ? PreviousCreatorEmbodimentRecoverySha256
                : typedLanguageRecoverySupported
                    ? PreviousTypedLanguageRecoverySha256
                    : rejectedLanguageRecoverySupported
                        ? PreviousLanguageRecoverySha256
                        : PreviousSha256;
        string actualVersion = Qwen3VlEditorialJson.Text(
            generation,
            "editorialRephrasePolicyVersion");
        string actualSha256 = Qwen3VlEditorialJson.Sha256(
            generation,
            "editorialRephrasePolicySha256");
        bool expectedIdentity = actualVersion.Equals(
                expectedVersion,
                StringComparison.OrdinalIgnoreCase) &&
            actualSha256.Equals(
                expectedSha256,
                StringComparison.OrdinalIgnoreCase);
        bool compatibleHistoricalIdentity = currentContract && !commentaryTimingSupported &&
            ((actualVersion.Equals(
                    PreviousAudienceFrameVersion,
                    StringComparison.OrdinalIgnoreCase) &&
                actualSha256.Equals(
                    PreviousAudienceFrameSha256,
                    StringComparison.OrdinalIgnoreCase)) ||
             (actualVersion.Equals(
                    PreviousStoryLedGrammaticalCenterVersion,
                    StringComparison.OrdinalIgnoreCase) &&
                actualSha256.Equals(
                    PreviousStoryLedGrammaticalCenterSha256,
                    StringComparison.OrdinalIgnoreCase)));
        if (!expectedIdentity && !compatibleHistoricalIdentity)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen editorial-rephrase policy identity changed.");
        }
        bool attempted = Boolean(generation, "editorialRephraseAttempted");
        bool applied = Boolean(generation, "editorialRephraseApplied");
        string outcome = Qwen3VlEditorialJson.Text(
            generation,
            "editorialRephraseOutcome");
        string? source = Qwen3VlEditorialJson.NullableSha256(
            generation,
            "editorialRephraseSourceJsonSha256");
        string? output = Qwen3VlEditorialJson.NullableSha256(
            generation,
            "editorialRephraseOutputJsonSha256");
        string? rejectionCode = Qwen3VlEditorialJson.NullableText(
            generation,
            "editorialRephraseRejectionCode");
        string? canonicalMessages = Qwen3VlEditorialJson.NullableSha256(
            generation,
            "editorialRephraseCanonicalMessagesSha256");
        string? renderedPrompt = Qwen3VlEditorialJson.NullableSha256(
            generation,
            "editorialRephraseRenderedPromptSha256");
        string? inputTokenIds = Qwen3VlEditorialJson.NullableSha256(
            generation,
            "editorialRephraseInputTokenIdsSha256");
        string? rawOutput = Qwen3VlEditorialJson.NullableSha256(
            generation,
            "editorialRephraseRawOutputSha256");
        long? promptBytes = Qwen3VlEditorialJson.NullableInt64(
            generation,
            "editorialRephraseRenderedPromptUtf8ByteCount");
        long? inputTokens = Qwen3VlEditorialJson.NullableInt64(
            generation,
            "editorialRephraseInputTokenCount");
        bool noChange = outcome.Equals(
            "RetainedOriginalNoMaterialChange",
            StringComparison.Ordinal);
        bool semanticRejection = outcome.Equals(
            "RetainedOriginalSemanticRejection",
            StringComparison.Ordinal);
        bool recoveredRejectedLanguage = outcome.Equals(
            "RecoveredRejectedLanguage",
            StringComparison.Ordinal);
        bool eligibilitySkipped = eligibilitySkipSupported &&
            !attempted &&
            !applied &&
            noChange &&
            rejectionCode?.Equals(
                "CaseLocalFactUnavailable",
                StringComparison.Ordinal) == true;
        bool knownRejection = rejectionCode is not null &&
            (Qwen3VlGroundedMetadataSelection.IsKnownValidationRule(
                rejectionCode) ||
             rejectionCode.Equals(
                "ImmutableFieldsChanged",
                StringComparison.Ordinal) ||
             rejectionCode.Equals(
                "RepeatedAnalysisDraft",
                StringComparison.Ordinal));
        bool completeGenerationWitness =
            canonicalMessages is not null &&
            renderedPrompt is not null &&
            inputTokenIds is not null &&
            rawOutput is not null &&
            promptBytes > 0 &&
            inputTokens > 0;
        bool validSkip = eligibilitySkipped &&
            source is not null &&
            source.Equals(output, StringComparison.OrdinalIgnoreCase) &&
            canonicalMessages is null &&
            renderedPrompt is null &&
            inputTokenIds is null &&
            rawOutput is null &&
            promptBytes is null &&
            inputTokens is null;
        bool validGeneration = attempted &&
            completeGenerationWitness &&
            source is not null &&
            output is not null &&
            (applied
                ? (outcome.Equals("Applied", StringComparison.Ordinal) ||
                    !reviewableAudienceCopySupported &&
                    rejectedLanguageRecoverySupported &&
                    recoveredRejectedLanguage) &&
                    rejectionCode is null &&
                    !source.Equals(output, StringComparison.OrdinalIgnoreCase)
                : noChange
                    ? rejectionCode is null &&
                        source.Equals(output, StringComparison.OrdinalIgnoreCase)
                    : semanticRejection && knownRejection);
        bool validIsolatedNonAttempt = isolatedFieldAuthoringSupported && isolatedFieldAuthoringVerified &&
            !attempted && !applied && outcome.Equals("NotAttempted", StringComparison.Ordinal) &&
            source is null && output is null && rejectionCode is null &&
            canonicalMessages is null && renderedPrompt is null && inputTokenIds is null &&
            rawOutput is null && promptBytes is null && inputTokens is null;
        if (!validSkip && !validGeneration && !validIsolatedNonAttempt)
        {
            throw new Qwen3VlOutputParseException(
                "Grounded Qwen editorial-rephrase provenance is invalid.");
        }
        return new(
            attempted,
            applied,
            outcome,
            source,
            output,
            rawOutput,
            rejectionCode,
            recoveredRejectedLanguage,
            eligibilitySkipped);
    }

    internal static Qwen3VlGroundedMetadataEditorialRephraseValidation
        ParseForTesting(
            JsonElement generation,
            bool rejectedLanguageRecoverySupported = true,
        bool typedLanguageRecoverySupported = true,
        bool creatorEmbodimentRecoverySupported = true,
        bool withheldEmbodimentCopyRecoverySupported = true,
        bool literalActionRecoverySupported = true,
        bool retrospectiveGrammarRecoverySupported = true,
        bool neutralPersonRecoverySupported = true,
        bool outputLanguageRecoverySupported = true,
        bool terminalPeriodNormalizationSupported = true,
        bool reviewableAudienceCopySupported = true,
        bool editorialFramingSupported = true,
        bool editorialFrameAdherenceSupported = true,
        bool eligibilitySkipSupported = true,
        bool commentaryTimingSupported = true,
        bool balancedCopySupported = true,
        bool compactBalancedCopySupported = true,
        bool schemaEnforcedBalancedCopySupported = true,
        bool isolatedFieldAuthoringSupported = true) =>
        Parse(
            generation,
            rejectedLanguageRecoverySupported,
            typedLanguageRecoverySupported,
            creatorEmbodimentRecoverySupported,
            withheldEmbodimentCopyRecoverySupported,
            literalActionRecoverySupported,
            retrospectiveGrammarRecoverySupported,
            neutralPersonRecoverySupported,
            outputLanguageRecoverySupported,
            terminalPeriodNormalizationSupported,
            reviewableAudienceCopySupported,
            editorialFramingSupported,
            editorialFrameAdherenceSupported,
            eligibilitySkipSupported,
            commentaryTimingSupported,
            balancedCopySupported,
            compactBalancedCopySupported,
            schemaEnforcedBalancedCopySupported,
            isolatedFieldAuthoringSupported);
}
