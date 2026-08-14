namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
{
    internal const string Version =
        "grounded-editorial-synthesis-recovery-pool-1.9";
    internal const string Sha256 =
        "65D105BCCF11E28C5FE15EDF8B8B2D62B14437D8D654A03F960810F1A2AE1AF2";
    internal const string PreviousTerminalPeriodVersion =
        "grounded-editorial-synthesis-recovery-pool-1.8";
    internal const string PreviousTerminalPeriodSha256 =
        "1FA1722487231DBE6D985401C577DA4DF5626038E0760F01F936DD808492CDEB";
    internal const string PreviousTerminalPeriodRetryableSemanticRejectionsSha256 =
        "7C0511699244BEE9E30CC285ACC4A0B75783B610CD7B36A5EE39DBF7B417BF20";
    internal const string PreviousInterfaceCorrectionVersion =
        "grounded-editorial-synthesis-recovery-pool-1.7";
    internal const string PreviousInterfaceCorrectionSha256 =
        "4E85987BB9BA6B3CA50EB738D257DF1AE8D51F7977805941C7C99678477168E4";
    internal const string PreviousEffectiveVoiceVersion =
        "grounded-editorial-synthesis-recovery-pool-1.6";
    internal const string PreviousEffectiveVoiceSha256 =
        "19285C318359872E1A1A07EB467E0FB7294645F17AE592DA810CE5B4636FB5DC";
    internal const string PreviousCreatorAuthorityVersion =
        "grounded-editorial-synthesis-recovery-pool-1.5";
    internal const string PreviousCreatorAuthoritySha256 =
        "11AC16A7B65842CE79B68E2B96C5906F448E5D7CE97627023818BEA7B6BC4805";
    internal const string PreviousVersion =
        "grounded-editorial-synthesis-recovery-pool-1.4";
    internal const string PreviousSha256 =
        "A48CCD8816849035E5B142BBD64F55F81AC1EF6701DA9F4AB551CBE41CC4128D";
    internal const string PriorVersion =
        "grounded-editorial-synthesis-recovery-pool-1.3";
    internal const string PriorSha256 =
        "503DEF0A3D171469CBC1B3BF744BF3B25CE2DF73A45CCDA8F0F20B737C82B3A4";
    internal const string EarlierVersion =
        "grounded-editorial-synthesis-recovery-pool-1.2";
    internal const string EarlierSha256 =
        "E5760F7FE140B69C27ACEC0EB3E8F5F38D5FFBC85E94012AC8BCAE9358879C96";
    internal const string LegacyVersion =
        "grounded-editorial-synthesis-recovery-pool-1.1";
    internal const string LegacySha256 =
        "5E98F3DF2A63DAF14B4AC99F9F68827A2874EFFDE89814FA203C3D1CC0DED877";
    internal const string FoundationalVersion =
        "grounded-editorial-synthesis-recovery-pool-1.0";
    internal const string FoundationalSha256 =
        "4B68391602E7F9A8E53A96985460645FD525B31FA32F6C71C05714E960D83734";
    internal const string RetryableSemanticRejectionsSha256 =
        "DDEADE8875FAF6B75A0AE8A474A7DE63493473497B16AB0A37FDED81D1B473B7";
    internal const string PreviousRetryableSemanticRejectionsSha256 =
        "7FC5522402738F12D323F28534C3653CF68CCA41B6980A3B27B1A67D2F7706BD";
    internal const int LogicalPassOrdinal = 4;
    internal const int OriginalSourcePassOrdinal = 1;
    internal const int PrimaryOnlyCrossDraftSourcePassOrdinal = 3;
    internal const string OriginalSourceSelectionReason =
        "OriginalFirstRejected";
    internal const string PrimaryOnlyCrossDraftSourceSelectionReason =
        "PrimaryOnlyCrossDraftAudienceCopyWithheld";
    internal const string PreviousPrimaryOnlyCrossDraftSourceSelectionReason =
        "PrimaryOnlyCrossDraftRepeatedGreedy";
    internal const string CrossDraftRetrySourceSelectionReason =
        "CrossDraftRejectedAudienceCopyWithheld";
    internal const string CreatorAuthorityRetrySourceSelectionReason =
        "CreatorAuthorityRejectedAudienceCopyWithheld";
    internal const int PoolSize = 4;
    internal const string Trigger = "BoundedSemanticRecoveryActivated";
    internal const string PreviousTrigger = "DuplicateGreedyRecoveryActivated";
    internal const int BatchSize = 1;
    internal const bool DoSample = true;
    internal const double Temperature = 0.7;
    internal const double TopP = 0.8;
    internal const int TopK = 20;
    internal const int NumberOfBeams = 1;
    internal const bool UseCache = true;
    internal const bool FreshMatcher = true;
    internal const bool UnconstrainedFallbackPermitted = false;
    internal const bool SemanticRepairPermitted = false;

    internal static IReadOnlyList<int> Seeds { get; } =
        Array.AsReadOnly([3407, 3408, 3409, 3410]);

    internal static IReadOnlyList<string> RetryableSemanticRejections { get; } =
        Array.AsReadOnly(
        [
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
        ]);

    internal static IReadOnlySet<string> RetryableSemanticRejectionSet { get; } =
        new HashSet<string>(
            RetryableSemanticRejections,
            StringComparer.Ordinal);

    internal static IReadOnlyList<string>
        PreviousTerminalPeriodRetryableSemanticRejections
    { get; } =
        Array.AsReadOnly(
        [
            .. RetryableSemanticRejections,
            "TerminalTitlePeriod",
        ]);

    internal static IReadOnlyList<string>
        PreviousRetryableSemanticRejections
    { get; } = Array.AsReadOnly(
        PreviousTerminalPeriodRetryableSemanticRejections
            .Where(static value => !value.Equals(
                "UnsupportedInterfaceAttribution",
                StringComparison.Ordinal))
            .ToArray());

    internal static IReadOnlySet<string>
        LegacyRetryableSemanticRejectionSet
    { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ThirdPersonCreatorFraming",
            "GenericOpening",
            "NonRetrospectiveVoice",
            "IncompleteTitle",
            "FirstPersonTitleSubject",
            "TitleDescriptionRepetition",
            "RedundantGameIdentity",
            "AnalysisBookkeeping",
            "OutputLanguage",
            "GameHashtag",
            "UnsupportedTag",
            "TagShape",
            "RerollTitleTooSimilar",
        };

    internal static IReadOnlyList<(string ModuleName, string FileName)>
        GroundedMetadataModules
    { get; } = Array.AsReadOnly<(
            string ModuleName,
            string FileName)>(
        [
            ("pipeline", "grounded_metadata_pipeline.py"),
            ("pipelineContract", "grounded_metadata_pipeline_contract.py"),
            ("pipelineAttestation", "grounded_metadata_pipeline_attestation.py"),
            ("pipelineGrounding", "grounded_metadata_pipeline_grounding.py"),
            ("pipelineState", "grounded_metadata_pipeline_state.py"),
            ("pipelineRefinement", "grounded_metadata_pipeline_refinement.py"),
            ("pipelineRecovery", "grounded_metadata_pipeline_recovery.py"),
            (
                "pipelineRecoveryCandidates",
                "grounded_metadata_pipeline_recovery_candidates.py"),
            ("pipelineResult", "grounded_metadata_pipeline_result.py"),
            ("editorialRephrase", "grounded_metadata_rephrase.py"),
            (
                "editorialRephraseMessages",
                "grounded_metadata_rephrase_messages.py"),
            ("synthesis", "grounded_metadata_synthesis.py"),
            ("synthesisMessages", "grounded_metadata_synthesis_messages.py"),
            ("generation", "grounded_metadata_generation.py"),
            ("jsonWhitespace", "grounded_metadata_json_whitespace.py"),
            ("validation", "grounded_metadata_validation.py"),
            ("audienceValidation", "grounded_metadata_audience_validation.py"),
            ("creatorAuthority", "grounded_metadata_creator_authority.py"),
            ("groundingValidation", "grounded_metadata_grounding_validation.py"),
            ("structuredDecoding", "structured_decoding.py"),
            ("recoveryPoolPolicy", "grounded_metadata_synthesis_decoding.py"),
        ]);

    internal static IReadOnlyList<(string ModuleName, string FileName)>
        PreviousEditorialRephraseGroundedMetadataModules
    { get; } =
            Array.AsReadOnly(
                GroundedMetadataModules
                    .Where(static value => !value.ModuleName.Equals(
                        "editorialRephraseMessages",
                        StringComparison.Ordinal))
                    .ToArray());

    internal static IReadOnlyList<(string ModuleName, string FileName)>
        PreviousGroundedMetadataModules
    { get; } = Array.AsReadOnly(
            GroundedMetadataModules
                .Where(static value => !value.ModuleName.StartsWith(
                    "editorialRephrase",
                    StringComparison.Ordinal))
                .ToArray());
}
