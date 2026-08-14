using System.Text.Json;
using static ReplayFoundry.Desktop.Platform.VisualSemantic.Qwen3VlGroundedMetadataJson;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataEditorialRephrasePolicy
{
    internal const string Version = "grounded-editorial-rephrase-2.0";
    internal const string Sha256 =
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
        bool reviewableAudienceCopySupported = true)
    {
        RequireText(
            generation,
            "editorialRephrasePolicyVersion",
            reviewableAudienceCopySupported
                ? Version
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
                            : PreviousVersion);
        RequireText(
            generation,
            "editorialRephrasePolicySha256",
            reviewableAudienceCopySupported
                ? Sha256
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
                            : PreviousSha256);
        bool attempted = Boolean(generation, "editorialRephraseAttempted");
        bool applied = Boolean(generation, "editorialRephraseApplied");
        string outcome = Qwen3VlEditorialJson.Text(
            generation,
            "editorialRephraseOutcome");
        string source = Qwen3VlEditorialJson.Sha256(
            generation,
            "editorialRephraseSourceJsonSha256");
        string output = Qwen3VlEditorialJson.Sha256(
            generation,
            "editorialRephraseOutputJsonSha256");
        string? rejectionCode = Qwen3VlEditorialJson.NullableText(
            generation,
            "editorialRephraseRejectionCode");
        _ = Qwen3VlEditorialJson.Sha256(
            generation,
            "editorialRephraseCanonicalMessagesSha256");
        _ = Qwen3VlEditorialJson.Sha256(
            generation,
            "editorialRephraseRenderedPromptSha256");
        _ = Qwen3VlEditorialJson.Sha256(
            generation,
            "editorialRephraseInputTokenIdsSha256");
        string rawOutput = Qwen3VlEditorialJson.Sha256(
            generation,
            "editorialRephraseRawOutputSha256");
        int promptBytes = Qwen3VlEditorialJson.Integer(
            generation,
            "editorialRephraseRenderedPromptUtf8ByteCount");
        int inputTokens = Qwen3VlEditorialJson.Integer(
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
        bool knownRejection = rejectionCode is not null &&
            (Qwen3VlGroundedMetadataSelection.IsKnownValidationRule(
                rejectionCode) ||
             rejectionCode.Equals(
                "ImmutableFieldsChanged",
                StringComparison.Ordinal) ||
             rejectionCode.Equals(
                "RepeatedAnalysisDraft",
                StringComparison.Ordinal));
        bool valid = attempted && promptBytes > 0 && inputTokens > 0 &&
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
                    : semanticRejection &&
                        knownRejection);
        if (!valid)
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
            recoveredRejectedLanguage);
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
        bool reviewableAudienceCopySupported = true) =>
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
            reviewableAudienceCopySupported);
}
