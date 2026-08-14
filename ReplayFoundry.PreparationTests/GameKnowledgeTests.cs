using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Platform.GameKnowledge;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.VisualSemantic;

namespace ReplayFoundry.PreparationTests;

internal static class GameKnowledgeTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Game knowledge contracts snapshot values immutably", ContractsAreImmutable),
        new("Game knowledge cache verifies deterministic snapshots", CacheRoundTripsAndRejectsCorruption),
        new("Wikimedia acquisition sends only the confirmed game name", WikimediaSendsOnlyGameName),
        new("Wikimedia related articles are bounded and game-name-only", WikimediaRelatedArticlesAreBounded),
        new("Wikimedia narrative passages remain bounded and ordered", WikimediaNarrativePassagesAreBounded),
        new("Game knowledge retrieval is deterministic and clip-linked", RetrievalIsDeterministic),
        new("Repeated local OCR can clip-link without sending text online", StableOcrCanClipLink),
        new("One authoritative term cannot clip-link a plot passage", OneAuthoritativeTermCannotClipLink),
        new("Clip-linked retrieval retains bounded event and broad game context", ClipLinkedRetrievalKeepsCurrentAndPrior),
        new("Automatic transcripts nominate bounded visual grounding", AutomaticTranscriptNominatesVisualGrounding),
        new("Automatic transcript context never leaks future narrative", AutomaticTranscriptUsesPriorNarrativeOnly),
        new("Generic visuals keep event candidates separate from broad game context", GenericVisualUsesBoundedCandidate),
        new("Broad game context survives when the clip has no lexical anchor", NoAnchorRetainsGeneralContext),
        new("Weak overlap keeps event candidates separate from broad game context", WeakGeneralOverlapDoesNotHideNarrativeCandidates),
        new("Game knowledge requires explicit user opt-in", ServiceRequiresOptIn),
        new("Game knowledge refreshes stale provider snapshots", ServiceRefreshesProviderVersion),
        new("Game knowledge acquisition degrades without metadata failure", ServiceDegrades),
        new("Qwen knowledge grounding rejects foreign references", QwenRejectsForeignGrounding),
        new("Qwen stable readable text requires separate draft agreement", QwenStableReadableTextRequiresAgreement),
        new("Qwen visual-event selection never promotes unsupported later dialogue", QwenVisualEventSelectionRequiresDistinctSupport),
        new("Qwen knowledge selection assesses both authorized current-event strengths", QwenKnowledgeSelectionUsesBothAuthorizedStrengths),
        new("Qwen grounded metadata sampling stays adaptive and backward-readable", QwenGroundedMetadataSamplingIsVersioned),
        new("Qualified Qwen forces its bounded CUDA attention policy", QualifiedQwenCudaAttentionIsStrict),
        new("Qwen recovery-pool failure ledger stays bounded and content-free", QwenRecoveryPoolFailureLedgerIsStrict),
        new("Qwen metadata accepts one strictly validated generation pass", QwenAcceptsOneValidatedPass),
    ];

    private static Task QualifiedQwenCudaAttentionIsStrict()
    {
        const string valid = """
            {
              "policyVersion": "qualified-editorial-cuda-attention-1.0",
              "policySha256": "b0747a0ed7d160315c6fca9fd869a9afec50221e97cccb0bff74b87b92a6c90d",
              "attentionImplementation": "sdpa",
              "sdpaBackend": "CudnnAttention",
              "sdpaBackendForced": true,
              "attentionFallbackPermitted": false,
              "cacheImplementation": "offloaded"
            }
            """;
        using JsonDocument document = JsonDocument.Parse(valid);
        Qwen3VlQualifiedCudaAttentionPolicy.Validate(document.RootElement);

        string fallback = valid.Replace(
            "\"attentionFallbackPermitted\": false",
            "\"attentionFallbackPermitted\": true",
            StringComparison.Ordinal);
        using JsonDocument fallbackDocument = JsonDocument.Parse(fallback);
        _ = TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlQualifiedCudaAttentionPolicy.Validate(
                fallbackDocument.RootElement),
            "Qualified observation parsing must reject attention fallback.");
        return Task.CompletedTask;
    }

    private static Task QwenGroundedMetadataSamplingIsVersioned()
    {
        TestAssert.Equal(
            "1.98.0",
            Qwen3VlGroundedMetadataGenerator.ProviderVersion,
            "General game context and canonical title normalization change provider identity.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.50",
            Qwen3VlGroundedMetadataGenerator.OutputSchema,
            "Current grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.49",
            Qwen3VlGroundedMetadataGenerator
                .PreviousReviewableAudienceCopyOutputSchema,
            "Pre-reviewable-audience-copy output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.48",
            Qwen3VlGroundedMetadataGenerator
                .PreviousTerminalPeriodNormalizationOutputSchema,
            "Pre-terminal-period-normalization output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.47",
            Qwen3VlGroundedMetadataGenerator
                .PreviousOutputLanguageRecoveryOutputSchema,
            "Pre-output-language-recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.46",
            Qwen3VlGroundedMetadataGenerator
                .PreviousNeutralPersonRecoveryOutputSchema,
            "Pre-neutral-person-recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.45",
            Qwen3VlGroundedMetadataGenerator
                .PreviousRetrospectiveGrammarRecoveryOutputSchema,
            "Pre-retrospective-grammar-recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.44",
            Qwen3VlGroundedMetadataGenerator
                .PreviousLiteralActionRecoveryOutputSchema,
            "Pre-literal-action-recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.43",
            Qwen3VlGroundedMetadataGenerator
                .PreviousWithheldEmbodimentCopyOutputSchema,
            "Pre-withheld-embodiment-copy output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.42",
            Qwen3VlGroundedMetadataGenerator
                .PreviousCreatorEmbodimentRecoveryOutputSchema,
            "Pre-creator-embodiment-recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.41",
            Qwen3VlGroundedMetadataGenerator
                .PreviousTypedLanguageRecoveryOutputSchema,
            "Pre-typed-language-recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.40",
            Qwen3VlGroundedMetadataGenerator
                .PreviousLanguageRecoveryOutputSchema,
            "Pre-rejected-language-recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.39",
            Qwen3VlGroundedMetadataGenerator
                .PreviousEditorialRephraseOutputSchema,
            "Pre-editorial-rephrase output remains readable.");
        using JsonDocument editorialRephrase = JsonDocument.Parse(
            """
            {
              "editorialRephrasePolicyVersion": "grounded-editorial-rephrase-2.0",
              "editorialRephrasePolicySha256": "556b11ad5535f4d16883a2a43bbd72ad83996520f4d6d8fc87d06615dccbba04",
              "editorialRephraseAttempted": true,
              "editorialRephraseApplied": true,
              "editorialRephraseOutcome": "Applied",
              "editorialRephraseSourceJsonSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "editorialRephraseOutputJsonSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "editorialRephraseRejectionCode": null,
              "editorialRephraseCanonicalMessagesSha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
              "editorialRephraseRenderedPromptSha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
              "editorialRephraseRenderedPromptUtf8ByteCount": 320,
              "editorialRephraseInputTokenIdsSha256": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
              "editorialRephraseInputTokenCount": 80,
              "editorialRephraseRawOutputSha256": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
            }
            """);
        Qwen3VlGroundedMetadataEditorialRephraseValidation rephraseValidation =
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                editorialRephrase.RootElement);
        TestAssert.True(
            rephraseValidation.Attempted && rephraseValidation.Applied,
            "Current rephrase provenance must prove one applied bounded pass.");
        string recoveredRejectedLanguage = editorialRephrase.RootElement
            .GetRawText()
            .Replace(
                "\"editorialRephraseOutcome\": \"Applied\"",
                "\"editorialRephraseOutcome\": \"RecoveredRejectedLanguage\"",
                StringComparison.Ordinal);
        using JsonDocument recoveredDocument = JsonDocument.Parse(
            recoveredRejectedLanguage);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                recoveredDocument.RootElement),
            "Current reviewable-copy output cannot use the removed language-recovery outcome.");
        string historicalRecoveredRejectedLanguage = recoveredRejectedLanguage
            .Replace(
                "grounded-editorial-rephrase-2.0",
                "grounded-editorial-rephrase-1.9",
                StringComparison.Ordinal)
            .Replace(
                "556b11ad5535f4d16883a2a43bbd72ad83996520f4d6d8fc87d06615dccbba04",
                "4f0c689382c68afc5b5dedc2c3175b68787b11a9413f6beb23a1a2c49c6c23c8",
                StringComparison.Ordinal);
        using JsonDocument historicalRecoveredDocument = JsonDocument.Parse(
            historicalRecoveredRejectedLanguage);
        Qwen3VlGroundedMetadataEditorialRephraseValidation recoveredValidation =
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                historicalRecoveredDocument.RootElement,
                reviewableAudienceCopySupported: false);
        TestAssert.True(
            recoveredValidation.RecoveredRejectedLanguage,
            "Historical provenance must retain the removed recovery outcome.");
        string previousReviewableAudienceCopy = editorialRephrase.RootElement
            .GetRawText()
            .Replace(
                "grounded-editorial-rephrase-2.0",
                "grounded-editorial-rephrase-1.9",
                StringComparison.Ordinal)
            .Replace(
                "556b11ad5535f4d16883a2a43bbd72ad83996520f4d6d8fc87d06615dccbba04",
                "4f0c689382c68afc5b5dedc2c3175b68787b11a9413f6beb23a1a2c49c6c23c8",
                StringComparison.Ordinal);
        using JsonDocument previousReviewableDocument = JsonDocument.Parse(
            previousReviewableAudienceCopy);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousReviewableDocument.RootElement,
            reviewableAudienceCopySupported: false);
        string previousTerminalPeriodNormalization = previousReviewableAudienceCopy
            .Replace(
                "grounded-editorial-rephrase-1.9",
                "grounded-editorial-rephrase-1.8",
                StringComparison.Ordinal)
            .Replace(
                "4f0c689382c68afc5b5dedc2c3175b68787b11a9413f6beb23a1a2c49c6c23c8",
                "7fad4c2aba040f8dc61b37153dd2084d3b824be572b4d4c234c975caec0eb10d",
                StringComparison.Ordinal);
        using JsonDocument previousTerminalPeriodDocument = JsonDocument.Parse(
            previousTerminalPeriodNormalization);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousTerminalPeriodDocument.RootElement,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        string previousNeutralPersonRecovery = previousTerminalPeriodNormalization
            .Replace(
                "grounded-editorial-rephrase-1.8",
                "grounded-editorial-rephrase-1.7",
                StringComparison.Ordinal)
            .Replace(
                "7fad4c2aba040f8dc61b37153dd2084d3b824be572b4d4c234c975caec0eb10d",
                "614477386fe746ae752bca2d7a1da4a6d6501f0bde5c3cdc533f4d8b0c19797c",
                StringComparison.Ordinal);
        using JsonDocument previousNeutralPersonDocument = JsonDocument.Parse(
            previousNeutralPersonRecovery);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousNeutralPersonDocument.RootElement,
            outputLanguageRecoverySupported: false,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        string previousLiteralActionRecovery = previousTerminalPeriodNormalization
            .Replace(
                "grounded-editorial-rephrase-1.8",
                "grounded-editorial-rephrase-1.5",
                StringComparison.Ordinal)
            .Replace(
                "7fad4c2aba040f8dc61b37153dd2084d3b824be572b4d4c234c975caec0eb10d",
                "415c65a7c1b1902784abedd54af36381c0fc086dca0950b37e698aa518aec829",
                StringComparison.Ordinal);
        using JsonDocument previousLiteralActionDocument = JsonDocument.Parse(
            previousLiteralActionRecovery);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousLiteralActionDocument.RootElement,
            retrospectiveGrammarRecoverySupported: false,
            neutralPersonRecoverySupported: false,
            outputLanguageRecoverySupported: false,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        string previousWithheldEmbodimentCopy = previousLiteralActionRecovery
            .Replace(
                "grounded-editorial-rephrase-1.5",
                "grounded-editorial-rephrase-1.4",
                StringComparison.Ordinal)
            .Replace(
                "415c65a7c1b1902784abedd54af36381c0fc086dca0950b37e698aa518aec829",
                "030a81279cfd4c9b7fbd454ea845669225b9940a810765036dbd53b6575b30ae",
                StringComparison.Ordinal);
        using JsonDocument previousWithheldEmbodimentDocument =
            JsonDocument.Parse(previousWithheldEmbodimentCopy);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousWithheldEmbodimentDocument.RootElement,
            literalActionRecoverySupported: false,
            retrospectiveGrammarRecoverySupported: false,
            neutralPersonRecoverySupported: false,
            outputLanguageRecoverySupported: false,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        string previousCreatorEmbodimentRecovery = previousWithheldEmbodimentCopy
            .Replace(
                "grounded-editorial-rephrase-1.4",
                "grounded-editorial-rephrase-1.3",
                StringComparison.Ordinal)
            .Replace(
                "030a81279cfd4c9b7fbd454ea845669225b9940a810765036dbd53b6575b30ae",
                "05f833616f1ba519e0dadf9e58e0ae02eec0de06ea84c247a53225d6ea6939d8",
                StringComparison.Ordinal);
        using JsonDocument previousCreatorEmbodimentDocument =
            JsonDocument.Parse(previousCreatorEmbodimentRecovery);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousCreatorEmbodimentDocument.RootElement,
            withheldEmbodimentCopyRecoverySupported: false,
            literalActionRecoverySupported: false,
            retrospectiveGrammarRecoverySupported: false,
            neutralPersonRecoverySupported: false,
            outputLanguageRecoverySupported: false,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        string previousTypedLanguageRecovery = previousCreatorEmbodimentRecovery
            .Replace(
                "grounded-editorial-rephrase-1.3",
                "grounded-editorial-rephrase-1.2",
                StringComparison.Ordinal)
            .Replace(
                "05f833616f1ba519e0dadf9e58e0ae02eec0de06ea84c247a53225d6ea6939d8",
                "1b23d128c06aafada22821c96c45a5c496b9392c6604325c4da21ae8fe6ebbe4",
                StringComparison.Ordinal);
        using JsonDocument previousTypedLanguageDocument = JsonDocument.Parse(
            previousTypedLanguageRecovery);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousTypedLanguageDocument.RootElement,
            creatorEmbodimentRecoverySupported: false,
            withheldEmbodimentCopyRecoverySupported: false,
            literalActionRecoverySupported: false,
            retrospectiveGrammarRecoverySupported: false,
            neutralPersonRecoverySupported: false,
            outputLanguageRecoverySupported: false,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        string previousLanguageRecovery = previousTypedLanguageRecovery
            .Replace(
                "grounded-editorial-rephrase-1.2",
                "grounded-editorial-rephrase-1.1",
                StringComparison.Ordinal)
            .Replace(
                "1b23d128c06aafada22821c96c45a5c496b9392c6604325c4da21ae8fe6ebbe4",
                "f5255df841a1f732bfe503267e98e758cbb0c99cfd5f8b7ab66e84da32ff2fcf",
                StringComparison.Ordinal);
        using JsonDocument previousLanguageDocument = JsonDocument.Parse(
            previousLanguageRecovery);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousLanguageDocument.RootElement,
            typedLanguageRecoverySupported: false,
            creatorEmbodimentRecoverySupported: false,
            withheldEmbodimentCopyRecoverySupported: false,
            literalActionRecoverySupported: false,
            retrospectiveGrammarRecoverySupported: false,
            neutralPersonRecoverySupported: false,
            outputLanguageRecoverySupported: false,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        string previousRephrase = previousLanguageRecovery
            .Replace(
                "grounded-editorial-rephrase-1.1",
                "grounded-editorial-rephrase-1.0",
                StringComparison.Ordinal)
            .Replace(
                "f5255df841a1f732bfe503267e98e758cbb0c99cfd5f8b7ab66e84da32ff2fcf",
                "5b624da570bc493e25330f8ac66087a525b665077e6255ded9c2bbb14c67b17b",
                StringComparison.Ordinal);
        using JsonDocument previousDocument = JsonDocument.Parse(previousRephrase);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousDocument.RootElement,
            rejectedLanguageRecoverySupported: false,
            typedLanguageRecoverySupported: false,
            creatorEmbodimentRecoverySupported: false,
            withheldEmbodimentCopyRecoverySupported: false,
            literalActionRecoverySupported: false,
            retrospectiveGrammarRecoverySupported: false,
            neutralPersonRecoverySupported: false,
            outputLanguageRecoverySupported: false,
            terminalPeriodNormalizationSupported: false,
            reviewableAudienceCopySupported: false);
        _ = TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                recoveredDocument.RootElement,
                rejectedLanguageRecoverySupported: false,
                typedLanguageRecoverySupported: false,
                creatorEmbodimentRecoverySupported: false,
                withheldEmbodimentCopyRecoverySupported: false,
                literalActionRecoverySupported: false,
                retrospectiveGrammarRecoverySupported: false,
                neutralPersonRecoverySupported: false,
                outputLanguageRecoverySupported: false,
                terminalPeriodNormalizationSupported: false,
                reviewableAudienceCopySupported: false),
            "Output 1.40 must reject the new language-recovery outcome.");
        string unknownRejection = editorialRephrase.RootElement.GetRawText()
            .Replace(
                "\"editorialRephraseApplied\": true",
                "\"editorialRephraseApplied\": false",
                StringComparison.Ordinal)
            .Replace(
                "\"editorialRephraseOutcome\": \"Applied\"",
                "\"editorialRephraseOutcome\": \"RetainedOriginalSemanticRejection\"",
                StringComparison.Ordinal)
            .Replace(
                "\"editorialRephraseRejectionCode\": null",
                "\"editorialRephraseRejectionCode\": \"UnknownRule\"",
                StringComparison.Ordinal);
        using JsonDocument unknownRejectionDocument = JsonDocument.Parse(
            unknownRejection);
        _ = TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy
                .ParseForTesting(unknownRejectionDocument.RootElement),
            "Unknown rephrase rejection codes must fail closed.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.38",
            Qwen3VlGroundedMetadataGenerator
                .PreviousInterfaceCorrectionOutputSchema,
            "Pre-interface-correction output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.37",
            Qwen3VlGroundedMetadataGenerator
                .PreviousInterfaceAttributionOutputSchema,
            "Pre-interface-attribution output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.36",
            Qwen3VlGroundedMetadataGenerator
                .PreviousVisualDraftPromptOutputSchema,
            "Visual-draft prompt 1.2 output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.35",
            Qwen3VlGroundedMetadataGenerator
                .PreviousEffectiveVoiceOutputSchema,
            "Pre-effective-voice grounded output remains readable.");
        TestAssert.Equal(
            "1.4",
            Qwen3VlGroundedMetadataGenerator.VisualDraftPromptVersion,
            "Current visual-draft prompt version.");
        TestAssert.Equal(
            "e07bb76961c9764c12fdbf13b60963928d319af15f5da55cca76bd660754f77b",
            Qwen3VlGroundedMetadataGenerator.VisualDraftPromptSha256,
            "Current visual-draft prompt hash.");
        TestAssert.Equal(
            "1.3",
            Qwen3VlGroundedMetadataGenerator.PreviousVisualDraftPromptVersion,
            "Previous visual-draft prompt version.");
        TestAssert.Equal(
            "1.2",
            Qwen3VlGroundedMetadataGenerator.EarlierVisualDraftPromptVersion,
            "Earlier visual-draft prompt version.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.34",
            Qwen3VlGroundedMetadataGenerator
                .PreviousGroundedJsonWhitespaceOutputSchema,
            "Arbitrary-whitespace grounded output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.33",
            Qwen3VlGroundedMetadataGenerator
                .PreviousCreatorAuthorityOutputSchema,
            "Pre-creator-authority recovery output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.32",
            Qwen3VlGroundedMetadataGenerator
                .PreviousAudienceCopyWithholdingOutputSchema,
            "Pre-semantic-exhaustion output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.31",
            Qwen3VlGroundedMetadataGenerator
                .PreviousCrossDraftRetryOutputSchema,
            "Pre-cross-draft-withholding output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.30",
            Qwen3VlGroundedMetadataGenerator
                .PreviousRootPreloadOutputSchema,
            "Pre-root-preload output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.29",
            Qwen3VlGroundedMetadataGenerator
                .PreviousCudnnAttentionOutputSchema,
            "Pre-cuDNN-attention output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.28",
            Qwen3VlGroundedMetadataGenerator
                .PreviousPositionEmbeddingOutputSchema,
            "Pre-position-embedding fix output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.27",
            Qwen3VlGroundedMetadataGenerator
                .PreviousAccelerateOffloadOutputSchema,
            "Pre-attestation-fix vision-offload output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.26",
            Qwen3VlGroundedMetadataGenerator
                .PreviousVisionOffloadOutputSchema,
            "All-CUDA low-peak output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.25",
            Qwen3VlGroundedMetadataGenerator
                .PreviousLowPeakSamplingOutputSchema,
            "Four-draft sampling 1.1 output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.24",
            Qwen3VlGroundedMetadataGenerator
                .PreviousPeakBoundedSamplingOutputSchema,
            "Peak-bounded three-draft output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.23",
            Qwen3VlGroundedMetadataGenerator.PreviousSamplingOutputSchema,
            "Sampling 1.0 grounded metadata output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.22",
            Qwen3VlGroundedMetadataGenerator.PreWatchdogOutputSchema,
            "Pre-watchdog grounded metadata output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.21",
            Qwen3VlGroundedMetadataGenerator.PreviousOutputSchema,
            "Previous grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.20",
            Qwen3VlGroundedMetadataGenerator.PriorOutputSchema,
            "Prior grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.19",
            Qwen3VlGroundedMetadataGenerator.LegacyOutputSchema,
            "Legacy grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.18",
            Qwen3VlGroundedMetadataGenerator.HistoricalOutputSchema,
            "Historical grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.17",
            Qwen3VlGroundedMetadataGenerator.PriorHistoricalOutputSchema,
            "Prior historical grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.16",
            Qwen3VlGroundedMetadataGenerator.EarlierHistoricalOutputSchema,
            "Earlier historical grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.15",
            Qwen3VlGroundedMetadataGenerator.InitialOutputSchema,
            "Initial grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.14",
            Qwen3VlGroundedMetadataGenerator.OldestOutputSchema,
            "Oldest grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.13",
            Qwen3VlGroundedMetadataGenerator.EarliestOutputSchema,
            "Earliest grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.12",
            Qwen3VlGroundedMetadataGenerator.FoundationalOutputSchema,
            "Foundational grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.11",
            Qwen3VlGroundedMetadataGenerator.OriginalOutputSchema,
            "Original grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.10",
            Qwen3VlGroundedMetadataGenerator.BaselineOutputSchema,
            "Baseline grounded metadata output schema remains readable.");
        TestAssert.Equal(
            "grounded-editorial-sampled-synthesis-1.0",
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Version,
            "Sampled synthesis policy version.");
        TestAssert.Equal(
            3407,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Seed,
            "Sampled synthesis fixed seed.");
        TestAssert.Equal(
            0.7,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Temperature,
            "Sampled synthesis temperature.");
        TestAssert.Equal(
            0.8,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.TopP,
            "Sampled synthesis top-p.");
        TestAssert.Equal(
            20,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.TopK,
            "Sampled synthesis top-k.");
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.9",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Version,
            "Recovery-pool policy version.");
        TestAssert.Equal(
            4,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PoolSize,
            "Recovery-pool size.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Seeds
                .SequenceEqual([3407, 3408, 3409, 3410]),
            "Recovery-pool seeds must remain fixed and ordered.");
        TestAssert.Equal(
            23,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejections.Count,
            "Current recovery policy freezes the 23 non-mechanical semantic rejections.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejectionSet.Contains(
                    "UnsupportedCreatorEmbodiment") &&
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejectionSet.Contains(
                    "UnsupportedInterfaceAttribution") &&
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejectionSet.Contains(
                    "CrossDraftTitleContamination") &&
            !Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .LegacyRetryableSemanticRejectionSet.Contains(
                    "UnsupportedCreatorEmbodiment"),
            "Outputs 1.32 through 1.20 retain the broad semantic set; output 1.19 retains policy 1.0.");
        (string ModuleName, string FileName)[] expectedGroundedModules =
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
        ];
        TestAssert.True(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .GroundedMetadataModules.SequenceEqual(expectedGroundedModules),
            "Every extracted grounded-metadata module must stay attested in exact runtime order.");
        TestAssert.Equal(
            240.0,
            Qwen3VlGenerationWatchdogPolicy
                .MaximumGenerationWallClockSeconds,
            "Grounded Qwen generation watchdog limit.");
        TestAssert.Equal(
            900.0,
            Qwen3VlGenerationWatchdogPolicy
                .MaximumGroundedCaseWallClockSeconds,
            "Grounded Qwen case watchdog limit.");
        string watchdogPolicyText = File.ReadAllText(Path.GetFullPath(
                Path.Combine(
                    "eng",
                    "visual-semantic-host",
                    "replayfoundry-generation-watchdog-policy-1.0.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string watchdogPolicySha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(watchdogPolicyText)))
            .ToLowerInvariant();
        TestAssert.Equal(
            Qwen3VlGenerationWatchdogPolicy.Sha256,
            watchdogPolicySha256,
            "Generation watchdog policy text hash.");
        string memoryPolicyText = File.ReadAllText(Path.GetFullPath(
                Path.Combine(
                    "eng",
                    "visual-semantic-host",
                    "replayfoundry-grounded-editorial-cuda-memory-policy-1.5.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string memoryPolicySha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(memoryPolicyText)))
            .ToLowerInvariant();
        TestAssert.Equal(
            Qwen3VlGroundedMemoryPolicy.Sha256,
            memoryPolicySha256,
            "Grounded CUDA memory policy text hash.");
        const long gibibyte = 1024L * 1024 * 1024;
        long totalDeviceMemoryBytes = 24 * gibibyte;
        long startupFreeMemoryBytes = 20 * gibibyte;
        long allocatorLimitBytes =
            startupFreeMemoryBytes -
            Qwen3VlGroundedMemoryPolicy.ReservedAllocatorHeadroomBytes;
        double allocatorFraction =
            (double)allocatorLimitBytes / totalDeviceMemoryBytes;
        string memoryPolicyJson = JsonSerializer.Serialize(new
        {
            policyVersion = Qwen3VlGroundedMemoryPolicy.Version,
            policySha256 = Qwen3VlGroundedMemoryPolicy.Sha256,
            cudaDeviceIndex = 0,
            cacheImplementation = "offloaded",
            attentionImplementation = "sdpa",
            sdpaBackend = "CudnnAttention",
            sdpaBackendForced = true,
            attentionFallbackPermitted = false,
            allocatorScope = "PyTorchNativeCudaCachingAllocator",
            startupGate = "FreeMemoryMinusReserveExceedsQualificationPeak",
            preGenerationGate = "CurrentFreeMemoryAtLeastFixedReserve",
            totalDeviceMemoryBytes,
            startupFreeMemoryBytes,
            startupExternallyOccupiedMemoryBytes =
                totalDeviceMemoryBytes - startupFreeMemoryBytes,
            requiredStartupFreeMemoryBytes =
                Qwen3VlGroundedMemoryPolicy.ReservedAllocatorHeadroomBytes +
                Qwen3VlGroundedMemoryPolicy.MinimumViableAllocatorLimitBytes,
            reservedAllocatorHeadroomBytes =
                Qwen3VlGroundedMemoryPolicy.ReservedAllocatorHeadroomBytes,
            allocatorLimitBytes,
            minimumViableAllocatorLimitBytes =
                Qwen3VlGroundedMemoryPolicy.MinimumViableAllocatorLimitBytes,
            allocatorFraction,
            observedAllocatorFraction = allocatorFraction,
            qualificationReferencePeakAllocatedBytes =
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferencePeakAllocatedBytes,
            qualificationReferenceArtifactName =
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferenceArtifactName,
            qualificationReferenceArtifactSchema =
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferenceArtifactSchema,
            qualificationReferenceArtifactSha256 =
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferenceArtifactSha256,
            preGenerationAdmissionCount = 1,
            minimumPreGenerationFreeDeviceMemoryBytes = 4 * gibibyte,
            lastPreGenerationFreeDeviceMemoryBytes = 4 * gibibyte,
            peakAllocatedGpuBytes = Qwen3VlGroundedMemoryPolicy
                .QualificationReferencePeakAllocatedBytes,
            peakReservedGpuBytes = 12 * gibibyte,
            endAllocatedGpuBytes = gibibyte,
            endReservedGpuBytes = 2 * gibibyte,
            endFreeDeviceMemoryBytes = 4 * gibibyte,
            runtimeOutcome = "Completed",
            failureReason = (string?)null,
            globalFreeMemoryGuaranteed = false,
            cpuModelOffloadPermitted = true,
            quantizationPermitted = false,
            automaticFallbackPermitted = false,
        });
        using JsonDocument memoryPolicyDocument =
            JsonDocument.Parse(memoryPolicyJson);
        Qwen3VlGroundedMemoryPolicyAudit memoryAudit =
            Qwen3VlGroundedMemoryPolicy.Parse(
                memoryPolicyDocument.RootElement,
                requireCompleted: true,
                expectedPeakAllocatedBytes:
                    Qwen3VlGroundedMemoryPolicy
                        .QualificationReferencePeakAllocatedBytes,
                requireCurrentPolicy: true);
        TestAssert.True(
            memoryAudit.RuntimeOutcome == "Completed" &&
            memoryAudit.PreGenerationAdmissionCount == 1 &&
            memoryAudit.AttentionImplementation == "sdpa" &&
            memoryAudit.SdpaBackend == "CudnnAttention" &&
            memoryAudit.SdpaBackendForced,
            "Completed memory provenance must satisfy every frozen bound.");
        string previousMemoryPolicyJson = memoryPolicyJson
            .Replace(
                Qwen3VlGroundedMemoryPolicy.Version,
                Qwen3VlGroundedMemoryPolicy.PreviousVersion,
                StringComparison.Ordinal)
            .Replace(
                Qwen3VlGroundedMemoryPolicy.Sha256,
                Qwen3VlGroundedMemoryPolicy.PreviousSha256,
                StringComparison.Ordinal);
        using JsonDocument previousMemoryPolicyDocument =
            JsonDocument.Parse(previousMemoryPolicyJson);
        Qwen3VlGroundedMemoryPolicy.Parse(
            previousMemoryPolicyDocument.RootElement,
            requireCompleted: true,
            expectedPeakAllocatedBytes:
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferencePeakAllocatedBytes);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMemoryPolicy.Parse(
                previousMemoryPolicyDocument.RootElement,
                requireCompleted: true,
                expectedPeakAllocatedBytes:
                    Qwen3VlGroundedMemoryPolicy
                        .QualificationReferencePeakAllocatedBytes,
                requireCurrentPolicy: true),
            "Current output must reject the prior root-preload policy.");
        string priorMemoryPolicyJson = previousMemoryPolicyJson
            .Replace(
                Qwen3VlGroundedMemoryPolicy.PreviousVersion,
                Qwen3VlGroundedMemoryPolicy.PriorVersion,
                StringComparison.Ordinal)
            .Replace(
                Qwen3VlGroundedMemoryPolicy.PreviousSha256,
                Qwen3VlGroundedMemoryPolicy.PriorSha256,
                StringComparison.Ordinal)
            .Replace(
                ",\"attentionImplementation\":\"sdpa\",\"sdpaBackend\":\"CudnnAttention\",\"sdpaBackendForced\":true,\"attentionFallbackPermitted\":false",
                string.Empty,
                StringComparison.Ordinal);
        using JsonDocument priorMemoryPolicyDocument =
            JsonDocument.Parse(priorMemoryPolicyJson);
        Qwen3VlGroundedMemoryPolicy.Parse(
            priorMemoryPolicyDocument.RootElement,
            requireCompleted: true,
            expectedPeakAllocatedBytes:
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferencePeakAllocatedBytes);
        string legacyMemoryPolicyJson = priorMemoryPolicyJson
            .Replace(
                Qwen3VlGroundedMemoryPolicy.PriorVersion,
                Qwen3VlGroundedMemoryPolicy.LegacyVersion,
                StringComparison.Ordinal)
            .Replace(
                Qwen3VlGroundedMemoryPolicy.PriorSha256,
                Qwen3VlGroundedMemoryPolicy.LegacySha256,
                StringComparison.Ordinal);
        using JsonDocument legacyMemoryPolicyDocument =
            JsonDocument.Parse(legacyMemoryPolicyJson);
        Qwen3VlGroundedMemoryPolicy.Parse(
            legacyMemoryPolicyDocument.RootElement,
            requireCompleted: true,
            expectedPeakAllocatedBytes:
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferencePeakAllocatedBytes);
        string earlierMemoryPolicyJson = legacyMemoryPolicyJson
            .Replace(
                Qwen3VlGroundedMemoryPolicy.LegacyVersion,
                Qwen3VlGroundedMemoryPolicy.EarlierVersion,
                StringComparison.Ordinal)
            .Replace(
                Qwen3VlGroundedMemoryPolicy.LegacySha256,
                Qwen3VlGroundedMemoryPolicy.EarlierSha256,
                StringComparison.Ordinal);
        using JsonDocument earlierMemoryPolicyDocument =
            JsonDocument.Parse(earlierMemoryPolicyJson);
        Qwen3VlGroundedMemoryPolicy.Parse(
            earlierMemoryPolicyDocument.RootElement,
            requireCompleted: true,
            expectedPeakAllocatedBytes:
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferencePeakAllocatedBytes);
        string originalMemoryPolicyJson = earlierMemoryPolicyJson
            .Replace(
                Qwen3VlGroundedMemoryPolicy.EarlierVersion,
                Qwen3VlGroundedMemoryPolicy.OriginalVersion,
                StringComparison.Ordinal)
            .Replace(
                Qwen3VlGroundedMemoryPolicy.EarlierSha256,
                Qwen3VlGroundedMemoryPolicy.OriginalSha256,
                StringComparison.Ordinal)
            .Replace(
                "\"cpuModelOffloadPermitted\":true",
                "\"cpuModelOffloadPermitted\":false",
                StringComparison.Ordinal);
        using JsonDocument originalMemoryPolicyDocument =
            JsonDocument.Parse(originalMemoryPolicyJson);
        Qwen3VlGroundedMemoryPolicy.Parse(
            originalMemoryPolicyDocument.RootElement,
            requireCompleted: true,
            expectedPeakAllocatedBytes:
                Qwen3VlGroundedMemoryPolicy
                    .QualificationReferencePeakAllocatedBytes);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMemoryPolicy.Parse(
                originalMemoryPolicyDocument.RootElement,
                requireCompleted: true,
                expectedPeakAllocatedBytes:
                    Qwen3VlGroundedMemoryPolicy
                        .QualificationReferencePeakAllocatedBytes,
                requireCurrentPolicy: true),
            "Current output must reject historical all-CUDA memory provenance.");
        string sampledPolicyText = File.ReadAllText(Path.GetFullPath(
                Path.Combine(
                    "eng",
                    "visual-semantic-host",
                    "replayfoundry-grounded-editorial-sampled-synthesis-policy-1.0.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string sampledPolicySha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sampledPolicyText)));
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Sha256,
            sampledPolicySha256,
            "Sampled synthesis policy text hash.");
        string recoveryPoolPolicyText = File.ReadAllText(Path.GetFullPath(
                Path.Combine(
                    "eng",
                    "visual-semantic-host",
                    "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.9.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string recoveryPoolPolicySha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(recoveryPoolPolicyText)));
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Sha256,
            recoveryPoolPolicySha256,
            "Recovery-pool policy text hash.");
        string previousInterfaceCorrectionRecoveryPoolPolicyText =
            File.ReadAllText(
                    Path.GetFullPath(
                        Path.Combine(
                            "eng",
                            "visual-semantic-host",
                            "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.7.txt")))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();
        string previousInterfaceCorrectionRecoveryPoolPolicySha256 =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        previousInterfaceCorrectionRecoveryPoolPolicyText)));
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousInterfaceCorrectionSha256,
            previousInterfaceCorrectionRecoveryPoolPolicySha256,
            "Previous interface-correction recovery policy hash remains exact.");
        string previousEffectiveVoiceRecoveryPoolPolicyText =
            File.ReadAllText(
                    Path.GetFullPath(
                        Path.Combine(
                            "eng",
                            "visual-semantic-host",
                            "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.6.txt")))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();
        string previousEffectiveVoiceRecoveryPoolPolicySha256 =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        previousEffectiveVoiceRecoveryPoolPolicyText)));
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.6",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousEffectiveVoiceVersion,
            "Pre-effective-voice recovery-pool policy remains readable.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousEffectiveVoiceSha256,
            previousEffectiveVoiceRecoveryPoolPolicySha256,
            "Pre-effective-voice recovery-pool policy hash remains exact.");
        string previousCreatorAuthorityRecoveryPoolPolicyText =
            File.ReadAllText(
                    Path.GetFullPath(
                        Path.Combine(
                            "eng",
                            "visual-semantic-host",
                            "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.5.txt")))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();
        string previousCreatorAuthorityRecoveryPoolPolicySha256 =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        previousCreatorAuthorityRecoveryPoolPolicyText)));
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.5",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousCreatorAuthorityVersion,
            "Previous creator-authority recovery-pool policy remains readable.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousCreatorAuthoritySha256,
            previousCreatorAuthorityRecoveryPoolPolicySha256,
            "Previous creator-authority recovery-pool policy hash remains exact.");
        string previousRecoveryPoolPolicyText = File.ReadAllText(
                Path.GetFullPath(
                    Path.Combine(
                        "eng",
                        "visual-semantic-host",
                        "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.4.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string previousRecoveryPoolPolicySha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(previousRecoveryPoolPolicyText)));
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.4",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PreviousVersion,
            "Previous recovery-pool policy version remains readable.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PreviousSha256,
            previousRecoveryPoolPolicySha256,
            "Previous recovery-pool policy text hash remains exact.");
        string priorRecoveryPoolPolicyText = File.ReadAllText(
                Path.GetFullPath(
                    Path.Combine(
                        "eng",
                        "visual-semantic-host",
                        "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.3.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string priorRecoveryPoolPolicySha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(priorRecoveryPoolPolicyText)));
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.3",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PriorVersion,
            "Prior recovery-pool policy version remains readable.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PriorSha256,
            priorRecoveryPoolPolicySha256,
            "Prior recovery-pool policy text hash remains exact.");
        string earlierRecoveryPoolPolicyText = File.ReadAllText(
                Path.GetFullPath(
                    Path.Combine(
                        "eng",
                        "visual-semantic-host",
                        "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.2.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string earlierRecoveryPoolPolicySha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(earlierRecoveryPoolPolicyText)));
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.2",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.EarlierVersion,
            "Earlier recovery-pool policy version remains readable.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.EarlierSha256,
            earlierRecoveryPoolPolicySha256,
            "Earlier recovery-pool policy text hash remains exact.");
        string legacyRecoveryPoolPolicyText = File.ReadAllText(
                Path.GetFullPath(
                    Path.Combine(
                        "eng",
                        "visual-semantic-host",
                        "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.1.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string legacyRecoveryPoolPolicySha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(legacyRecoveryPoolPolicyText)));
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.1",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.LegacyVersion,
            "Legacy recovery-pool policy version remains readable.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.LegacySha256,
            legacyRecoveryPoolPolicySha256,
            "Legacy recovery-pool policy text hash remains exact.");
        string foundationalRecoveryPoolPolicyText = File.ReadAllText(
                Path.GetFullPath(
                    Path.Combine(
                        "eng",
                        "visual-semantic-host",
                        "replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.0.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string foundationalRecoveryPoolPolicySha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(foundationalRecoveryPoolPolicyText)));
        TestAssert.Equal(
            "grounded-editorial-synthesis-recovery-pool-1.0",
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .FoundationalVersion,
            "Foundational recovery-pool policy version remains readable.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .FoundationalSha256,
            foundationalRecoveryPoolPolicySha256,
            "Foundational recovery-pool policy text hash remains exact.");
        string retryableRejectionsJson = JsonSerializer.Serialize(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejections);
        string retryableRejectionsSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(retryableRejectionsJson)));
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejectionsSha256,
            retryableRejectionsSha256,
            "Recovery-pool retryable semantic rejection hash.");
        const string generationHash =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string decodedHash =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var sampledFailureTelemetry = new Qwen3VlHostFailureGeneration(
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Version,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Sha256,
            Qwen3VlGroundedMetadataGenerator.MaximumNewTokens,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.DoSample,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.NumberOfBeams,
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.UseCache,
            "case-1",
            "candidate-1",
            1,
            10,
            1,
            [99],
            0,
            99,
            VisualSemanticGenerationTerminationReason.EndOfSequence,
            generationHash,
            1,
            generationHash,
            decodedHash,
            24);
        TestAssert.True(
            sampledFailureTelemetry.DoSample &&
            sampledFailureTelemetry.PolicyVersion.Equals(
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Version,
                StringComparison.Ordinal),
            "Failure telemetry must identify sampled synthesis honestly.");
        var recoveryPoolFailureTelemetry = new Qwen3VlHostFailureGeneration(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Version,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Sha256,
            Qwen3VlGroundedMetadataGenerator.MaximumNewTokens,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.NumberOfBeams,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache,
            "case-1",
            "candidate-1",
            1,
            10,
            1,
            [99],
            0,
            99,
            VisualSemanticGenerationTerminationReason.EndOfSequence,
            generationHash,
            1,
            generationHash,
            decodedHash,
            24);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.Version,
            recoveryPoolFailureTelemetry.PolicyVersion,
            "Failure telemetry must retain the recovery-pool policy identity.");
        var previousRecoveryPoolFailureTelemetry =
            new Qwen3VlHostFailureGeneration(
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousVersion,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PreviousSha256,
                Qwen3VlGroundedMetadataGenerator.MaximumNewTokens,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .NumberOfBeams,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache,
                "case-1",
                "candidate-1",
                1,
                10,
                1,
                [99],
                0,
                99,
                VisualSemanticGenerationTerminationReason.EndOfSequence,
                generationHash,
                1,
                generationHash,
                decodedHash,
                24);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PreviousVersion,
            previousRecoveryPoolFailureTelemetry.PolicyVersion,
            "Failure envelope telemetry retains policy 1.2 compatibility.");
        var priorRecoveryPoolFailureTelemetry =
            new Qwen3VlHostFailureGeneration(
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PriorVersion,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PriorSha256,
                Qwen3VlGroundedMetadataGenerator.MaximumNewTokens,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.DoSample,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .NumberOfBeams,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.UseCache,
                "case-1",
                "candidate-1",
                1,
                10,
                1,
                [99],
                0,
                99,
                VisualSemanticGenerationTerminationReason.EndOfSequence,
                generationHash,
                1,
                generationHash,
                decodedHash,
                24);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PriorVersion,
            priorRecoveryPoolFailureTelemetry.PolicyVersion,
            "Failure envelope telemetry retains policy 1.1 compatibility.");
        TestAssert.Throws<ArgumentException>(
            () => new Qwen3VlHostFailureGeneration(
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Version,
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Sha256,
                Qwen3VlGroundedMetadataGenerator.MaximumNewTokens,
                doSample: false,
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.NumberOfBeams,
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.UseCache,
                "case-1",
                "candidate-1",
                1,
                10,
                1,
                [99],
                0,
                99,
                VisualSemanticGenerationTerminationReason.EndOfSequence,
                generationHash,
                1,
                generationHash,
                decodedHash,
                24),
            "Sampled failure policy cannot be mislabeled as greedy decoding.");
        TestAssert.Equal(
            512 * 288,
            Qwen3VlGroundedMetadataSamplingPolicy.CoreMaximumPixelsPerFrame,
            "Low-peak core sampling uses an aspect-ratio-preserving pixel budget.");
        TestAssert.Equal(
            "grounded-editorial-adaptive-sampling-1.2",
            Qwen3VlGroundedMetadataSamplingPolicy.Version,
            "Current core sampling is low-peak bounded.");
        TestAssert.Equal(
            "grounded-editorial-adaptive-sampling-1.1",
            Qwen3VlGroundedMetadataSamplingPolicy.PreviousVersion,
            "Sampling 1.1 remains an explicit historical contract.");
        TestAssert.Equal(
            "grounded-editorial-adaptive-sampling-1.0",
            Qwen3VlGroundedMetadataSamplingPolicy.InitialVersion,
            "Sampling 1.0 remains an explicit historical contract.");
        TestAssert.Equal(
            6,
            Qwen3VlGroundedMetadataSamplingPolicy.CoreMaximumFrames,
            "One current core generation may retain at most six frames.");
        TestAssert.Equal(
            8,
            Qwen3VlGroundedMetadataSamplingPolicy.PreviousCoreMaximumFrames,
            "Historical sampling 1.1 retains its exact eight-frame bound.");
        TestAssert.Equal(
            16,
            Qwen3VlGroundedMetadataSamplingPolicy.InitialCoreMaximumFrames,
            "Historical sampling 1.0 retains its exact sixteen-frame bound.");
        TestAssert.Equal(
            2.0,
            Qwen3VlGroundedMetadataSamplingPolicy.CoreWindowOverlapSeconds,
            "Split core windows retain two seconds of action continuity.");
        TestAssert.Equal(
            131_072,
            Qwen3VlGroundedMetadataSamplingPolicy.ContextMaximumPixelsPerFrame,
            "Context sampling stays sparse.");

        using JsonDocument lowPeakCoreDocument = JsonDocument.Parse(
            """
            {
              "policyVersion":"grounded-editorial-adaptive-sampling-1.2",
              "tier":"CandidateCore",
              "framesPerSecond":0.5,
              "minimumFrames":4,
              "maximumFrames":6,
              "maximumPixelsPerFrame":147456,
              "maximumTotalVideoPixels":884736,
              "actualFrameCount":6,
              "actualFrameWidth":512,
              "actualFrameHeight":288,
              "actualPixelsPerFrame":147456,
              "actualTotalVideoPixels":884736
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateDraft(
            lowPeakCoreDocument.RootElement,
            peakBoundedSampling: true,
            lowPeakSampling: true);

        using JsonDocument coreDocument = JsonDocument.Parse(
            """
            {
              "policyVersion":"grounded-editorial-adaptive-sampling-1.1",
              "tier":"CandidateCore",
              "framesPerSecond":0.5,
              "minimumFrames":4,
              "maximumFrames":8,
              "maximumPixelsPerFrame":230400,
              "maximumTotalVideoPixels":1843200,
              "actualFrameCount":6,
              "actualFrameWidth":640,
              "actualFrameHeight":352,
              "actualPixelsPerFrame":225280,
              "actualTotalVideoPixels":1351680
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateDraft(
            coreDocument.RootElement,
            peakBoundedSampling: true);

        using JsonDocument historicalCoreDocument = JsonDocument.Parse(
            """
            {
              "policyVersion":"grounded-editorial-adaptive-sampling-1.0",
              "tier":"CandidateCore",
              "framesPerSecond":0.5,
              "minimumFrames":4,
              "maximumFrames":16,
              "maximumPixelsPerFrame":230400,
              "maximumTotalVideoPixels":3686400,
              "actualFrameCount":12,
              "actualFrameWidth":640,
              "actualFrameHeight":352,
              "actualPixelsPerFrame":225280,
              "actualTotalVideoPixels":2703360
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateDraft(
            historicalCoreDocument.RootElement,
            peakBoundedSampling: false);

        Qwen3VlGroundedMetadataSamplingPolicy.ValidateWindowTimeline(
            previousEnd: 0.0,
            previousTier: null,
            start: 0.0,
            end: 7.0,
            tier: Qwen3VlGroundedMetadataSamplingPolicy.SparseContextTier,
            peakBoundedSampling: true);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateWindowTimeline(
            previousEnd: 7.0,
            previousTier:
                Qwen3VlGroundedMetadataSamplingPolicy.SparseContextTier,
            start: 7.0,
            end: 20.0,
            tier: Qwen3VlGroundedMetadataSamplingPolicy.CandidateCoreTier,
            peakBoundedSampling: true);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateWindowTimeline(
            previousEnd: 20.0,
            previousTier:
                Qwen3VlGroundedMetadataSamplingPolicy.CandidateCoreTier,
            start: 18.0,
            end: 31.0,
            tier: Qwen3VlGroundedMetadataSamplingPolicy.CandidateCoreTier,
            peakBoundedSampling: true);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateWindowTimeline(
            previousEnd: 31.0,
            previousTier:
                Qwen3VlGroundedMetadataSamplingPolicy.CandidateCoreTier,
            start: 31.0,
            end: 38.0,
            tier: Qwen3VlGroundedMetadataSamplingPolicy.SparseContextTier,
            peakBoundedSampling: true);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSamplingPolicy
                .ValidateWindowTimeline(
                    previousEnd: 20.0,
                    previousTier: Qwen3VlGroundedMetadataSamplingPolicy
                        .CandidateCoreTier,
                    start: 19.0,
                    end: 31.0,
                    tier: Qwen3VlGroundedMetadataSamplingPolicy
                        .CandidateCoreTier,
                    peakBoundedSampling: true),
            "Sampling 1.1 adjacent cores require the exact two-second overlap.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSamplingPolicy
                .ValidateWindowTimeline(
                    previousEnd: 0.0,
                    previousTier: null,
                    start: 0.0,
                    end: 17.0,
                    tier: Qwen3VlGroundedMetadataSamplingPolicy
                        .CandidateCoreTier,
                    peakBoundedSampling: true),
            "Sampling 1.1 core windows cannot exceed sixteen seconds.");
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateWindowTimeline(
            previousEnd: 20.0,
            previousTier:
                Qwen3VlGroundedMetadataSamplingPolicy.CandidateCoreTier,
            start: 20.0,
            end: 32.0,
            tier: Qwen3VlGroundedMetadataSamplingPolicy.CandidateCoreTier,
            peakBoundedSampling: false);

        using JsonDocument lowPeakSummaryDocument = JsonDocument.Parse(
            """
            {
              "samplingPolicyVersion":"grounded-editorial-adaptive-sampling-1.2",
              "videoFramesPerSecond":0.5,
              "minimumVideoFrames":4,
              "maximumVideoFrames":6,
              "maximumPixelsPerFrame":147456,
              "maximumTotalVideoPixels":884736
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateSummary(
            lowPeakSummaryDocument.RootElement,
            adaptive: true,
            peakBoundedSampling: true,
            lowPeakSampling: true);

        using JsonDocument currentSummaryDocument = JsonDocument.Parse(
            """
            {
              "samplingPolicyVersion":"grounded-editorial-adaptive-sampling-1.1",
              "videoFramesPerSecond":0.5,
              "minimumVideoFrames":4,
              "maximumVideoFrames":8,
              "maximumPixelsPerFrame":230400,
              "maximumTotalVideoPixels":1843200
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateSummary(
            currentSummaryDocument.RootElement,
            adaptive: true,
            peakBoundedSampling: true);
        using JsonDocument historicalSummaryDocument = JsonDocument.Parse(
            """
            {
              "samplingPolicyVersion":"grounded-editorial-adaptive-sampling-1.0",
              "videoFramesPerSecond":0.5,
              "minimumVideoFrames":4,
              "maximumVideoFrames":16,
              "maximumPixelsPerFrame":230400,
              "maximumTotalVideoPixels":3686400
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateSummary(
            historicalSummaryDocument.RootElement,
            adaptive: true,
            peakBoundedSampling: false);

        using JsonDocument lowPeakContextDocument = JsonDocument.Parse(
            """
            {
              "policyVersion":"grounded-editorial-adaptive-sampling-1.2",
              "tier":"SparseContext",
              "framesPerSecond":0.2,
              "minimumFrames":4,
              "maximumFrames":6,
              "maximumPixelsPerFrame":131072,
              "maximumTotalVideoPixels":786432,
              "actualFrameCount":4,
              "actualFrameWidth":480,
              "actualFrameHeight":272,
              "actualPixelsPerFrame":130560,
              "actualTotalVideoPixels":522240
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateDraft(
            lowPeakContextDocument.RootElement,
            peakBoundedSampling: true,
            lowPeakSampling: true);

        using JsonDocument contextDocument = JsonDocument.Parse(
            """
            {
              "policyVersion":"grounded-editorial-adaptive-sampling-1.1",
              "tier":"SparseContext",
              "framesPerSecond":0.2,
              "minimumFrames":4,
              "maximumFrames":8,
              "maximumPixelsPerFrame":131072,
              "maximumTotalVideoPixels":1048576,
              "actualFrameCount":4,
              "actualFrameWidth":480,
              "actualFrameHeight":272,
              "actualPixelsPerFrame":130560,
              "actualTotalVideoPixels":522240
            }
            """);
        Qwen3VlGroundedMetadataSamplingPolicy.ValidateDraft(
            contextDocument.RootElement,
            peakBoundedSampling: true);

        using JsonDocument forcedSquare = JsonDocument.Parse(
            """
            {
              "policyVersion":"grounded-editorial-adaptive-sampling-1.1",
              "tier":"CandidateCore",
              "framesPerSecond":0.5,
              "minimumFrames":4,
              "maximumFrames":8,
              "maximumPixelsPerFrame":230400,
              "maximumTotalVideoPixels":1843200,
              "actualFrameCount":4,
              "actualFrameWidth":1000,
              "actualFrameHeight":1000,
              "actualPixelsPerFrame":1000000,
              "actualTotalVideoPixels":4000000
            }
            """);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSamplingPolicy.ValidateDraft(
                forcedSquare.RootElement,
                peakBoundedSampling: true),
            "An arbitrary 1000x1000 resize must not pass the measured budget.");

        return Task.CompletedTask;
    }

    private static Task QwenRecoveryPoolFailureLedgerIsStrict()
    {
        const string hashA =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string hashB =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string hashC =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        const string hashD =
            "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        const string hashE =
            "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        object Entry(
            int candidateOrdinal,
            int seed,
            string rejectionCode,
            string renderedPromptSha256 = hashB) => new
            {
                candidateOrdinal,
                seed,
                sourceSelectionReason =
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PrimaryOnlyCrossDraftSourceSelectionReason,
                sourcePassOrdinal =
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PrimaryOnlyCrossDraftSourcePassOrdinal,
                sourceRejectedJsonSha256 = hashA,
                canonicalMessagesSha256 = hashA,
                renderedPromptSha256,
                renderedPromptUtf8ByteCount = 512,
                inputTokenIdsSha256 = hashC,
                inputTokenCount = 128,
                outputSha256 = hashD,
                completedJsonSha256 = hashE,
                rejectionCode,
                accepted = false,
            };

        string validJson = JsonSerializer.Serialize(new
        {
            recoveryPoolLedger = new[]
            {
                Entry(
                    1,
                    3407,
                    "CrossDraftTitleContamination"),
                Entry(
                    2,
                    3408,
                    "UnsupportedCreatorEmbodiment"),
            },
        });
        using JsonDocument validDocument = JsonDocument.Parse(validJson);
        Qwen3VlHostFailureRecoveryPoolLedgerEntry[] parsed =
            Qwen3VlHostFailureRecoveryPoolLedgerParser.Parse(
                validDocument.RootElement,
                sourceSelectionProvenance: true);
        TestAssert.Equal(
            2,
            parsed.Length,
            "Failure envelope 1.3 retains every completed pool candidate.");
        TestAssert.Equal(
            3408,
            parsed[1].Seed,
            "Failure ledger binds candidate order to the frozen seed order.");
        TestAssert.Equal(
            "visual-semantic-host-failure-1.4",
            Qwen3VlHostFailureEnvelope.SupportedSchemaVersion,
            "Current failure envelope reserves watchdog and pool telemetry.");
        TestAssert.Equal(
            "visual-semantic-host-failure-1.3",
            Qwen3VlHostFailureEnvelope.PreviousSupportedSchemaVersion,
            "The previous failure envelope remains explicitly readable.");
        TestAssert.Equal(
            "visual-semantic-host-failure-1.2",
            Qwen3VlHostFailureEnvelope.PriorSupportedSchemaVersion,
            "The prior failure envelope remains explicitly readable.");
        TestAssert.Equal(
            "visual-semantic-host-failure-1.1",
            Qwen3VlHostFailureEnvelope.FoundationalSupportedSchemaVersion,
            "The foundational failure envelope remains explicitly readable.");

        string previousJson = JsonSerializer.Serialize(new
        {
            recoveryPoolLedger = new[]
            {
                new
                {
                    candidateOrdinal = 1,
                    seed = 3407,
                    canonicalMessagesSha256 = hashA,
                    renderedPromptSha256 = hashB,
                    renderedPromptUtf8ByteCount = 512,
                    inputTokenIdsSha256 = hashC,
                    inputTokenCount = 128,
                    outputSha256 = hashD,
                    completedJsonSha256 = hashE,
                    rejectionCode = "CrossDraftTitleContamination",
                    accepted = false,
                },
            },
        });
        using JsonDocument previousDocument = JsonDocument.Parse(previousJson);
        Qwen3VlHostFailureRecoveryPoolLedgerEntry[] previousLedger =
            Qwen3VlHostFailureRecoveryPoolLedgerParser.Parse(
                previousDocument.RootElement,
                sourceSelectionProvenance: false);
        TestAssert.True(
            previousLedger.Length == 1 &&
            previousLedger[0].SourceSelectionReason is null &&
            previousLedger[0].SourcePassOrdinal is null &&
            previousLedger[0].SourceRejectedJsonSha256 is null,
            "Failure envelope 1.2 remains readable without conditional-source fields.");

        string changedPromptJson = JsonSerializer.Serialize(new
        {
            recoveryPoolLedger = new[]
            {
                Entry(1, 3407, "CrossDraftTitleContamination"),
                Entry(
                    2,
                    3408,
                    "UnsupportedCreatorEmbodiment",
                    renderedPromptSha256: hashD),
            },
        });
        using JsonDocument changedPromptDocument =
            JsonDocument.Parse(changedPromptJson);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlHostFailureRecoveryPoolLedgerParser.Parse(
                changedPromptDocument.RootElement,
                sourceSelectionProvenance: true),
            "Failure ledger candidates must bind byte-identical pool inputs.");

        string unknownRuleJson = JsonSerializer.Serialize(new
        {
            recoveryPoolLedger = new[]
            {
                Entry(1, 3407, "UnknownSemanticFailure"),
            },
        });
        using JsonDocument unknownRuleDocument =
            JsonDocument.Parse(unknownRuleJson);
        Qwen3VlHostFailureRecoveryPoolLedgerEntry[] unknownRuleLedger =
            Qwen3VlHostFailureRecoveryPoolLedgerParser.Parse(
                unknownRuleDocument.RootElement,
                sourceSelectionProvenance: true);
        TestAssert.Equal(
            "UnknownSemanticFailure",
            unknownRuleLedger[0].RejectionCode,
            "Failure ledger retains a terminating unknown rejection without treating it as retry authorization.");

        string wrongSeedJson = JsonSerializer.Serialize(new
        {
            recoveryPoolLedger = new[]
            {
                Entry(1, 3410, "CrossDraftTitleContamination"),
            },
        });
        using JsonDocument wrongSeedDocument = JsonDocument.Parse(wrongSeedJson);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlHostFailureRecoveryPoolLedgerParser.Parse(
                wrongSeedDocument.RootElement,
                sourceSelectionProvenance: true),
            "Failure ledger cannot reorder frozen recovery seeds.");
        return Task.CompletedTask;
    }

    private static Task QwenKnowledgeSelectionUsesBothAuthorizedStrengths()
    {
        TestAssert.True(
            Qwen3VlGroundedMetadataSelection.IsCurrentKnowledgeCandidate(
                GameKnowledgeMatchStrength.ClipLinked,
                GameKnowledgeTemporalRelation.CurrentEventCandidate),
            "Clip-linked current-event passages must receive visual assessment.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSelection.IsCurrentKnowledgeCandidate(
                GameKnowledgeMatchStrength.CandidateForVisualGrounding,
                GameKnowledgeTemporalRelation.CurrentEventCandidate),
            "Visual-grounding current-event passages must receive visual assessment.");
        TestAssert.False(
            Qwen3VlGroundedMetadataSelection.IsCurrentKnowledgeCandidate(
                GameKnowledgeMatchStrength.GeneralContext,
                GameKnowledgeTemporalRelation.CurrentEventCandidate),
            "General context cannot be promoted into current-event assessment.");
        TestAssert.False(
            Qwen3VlGroundedMetadataSelection.IsCurrentKnowledgeCandidate(
                GameKnowledgeMatchStrength.ClipLinked,
                GameKnowledgeTemporalRelation.Unspecified),
            "Clip-linked passages without current-event authority remain excluded.");
        TestAssert.False(
            Qwen3VlGroundedMetadataSelection.IsCurrentKnowledgeCandidate(
                GameKnowledgeMatchStrength.CandidateForVisualGrounding,
                GameKnowledgeTemporalRelation.ImmediatelyPriorContext),
            "Immediately-prior context cannot be assessed as the visible current event.");
        TestAssert.False(
            Qwen3VlGroundedMetadataSelection.IsCurrentKnowledgeCandidate(
                GameKnowledgeMatchStrength.ClipLinked,
                GameKnowledgeTemporalRelation.CurrentEventCandidate,
                includeClipLinked: false),
            "Historical output readers must retain the pre-1.57 candidate policy.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Output 1.25 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator
                        .PreviousPeakBoundedSamplingOutputSchema),
            "Historical output 1.24 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator
                        .PreviousSamplingOutputSchema),
            "Historical output 1.23 retains clip-linked current-event selection.");
        TestAssert.False(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.PreWatchdogOutputSchema),
            "Historical output 1.22 retains its refresh-only knowledge-selection policy.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.PreviousOutputSchema),
            "Historical output 1.21 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.PriorOutputSchema),
            "Historical output 1.20 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.LegacyOutputSchema),
            "Historical output 1.19 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.HistoricalOutputSchema),
            "Historical output 1.18 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.PriorHistoricalOutputSchema),
            "Historical output 1.17 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.EarlierHistoricalOutputSchema),
            "Historical output 1.16 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.InitialOutputSchema),
            "Historical output 1.15 retains clip-linked current-event selection.");
        TestAssert.True(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.OldestOutputSchema),
            "Historical output 1.14 retains clip-linked current-event selection.");
        TestAssert.False(
            Qwen3VlGroundedMetadataGenerationParser
                .IncludesClipLinkedKnowledgeSelection(
                    Qwen3VlGroundedMetadataGenerator.EarliestOutputSchema),
            "Output 1.13 retains its pre-1.57 knowledge-selection policy.");

        Qwen3VlGroundedMetadataKnowledgeAssessment[] assessments =
        [
            new(
                "gkp-linked-current",
                SettingSupport: true,
                EntityIdentitySupport: true,
                DistinctiveObjectSupport: false,
                CentralActionSupport: false,
                ChronologySupport: false,
                MaterialContradiction: false),
        ];
        TestAssert.Equal(
            "gkp-linked-current",
            Qwen3VlGroundedMetadataSelection.SelectKnowledgePassage(assessments),
            "The existing two-support, no-conflict gate must apply equally after eligibility.");
        TestAssert.Equal(
            "None",
            Qwen3VlGroundedMetadataSelection.SelectKnowledgePassage(
                assessments.Select(static value => value with
                {
                    MaterialContradiction = true,
                }).ToArray()),
            "A material conflict must still reject an otherwise supported passage.");
        return Task.CompletedTask;
    }

    private static Task ContractsAreImmutable()
    {
        GameKnowledgeSnapshot snapshot = CreateSnapshot();
        TestAssert.Equal(3, snapshot.Passages.Count, "Passage snapshot.");
        TestAssert.Equal(64, snapshot.SnapshotSha256.Length, "Snapshot hash.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<GameKnowledgePassage>)snapshot.Passages).Add(
                snapshot.Passages[0]),
            "Snapshot passages must be read-only.");
        TestAssert.Throws<ArgumentException>(
            () => new GameKnowledgeSource(
                "source",
                GameKnowledgeSourceKind.Wikipedia,
                "Example Quest",
                new Uri("http://example.invalid/wiki"),
                "1",
                DateTimeOffset.UnixEpoch,
                "CC-BY-SA-4.0",
                new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
                "Example attribution",
                new string('a', 64)),
            "Source URLs must use HTTPS.");
        return Task.CompletedTask;
    }

    private static Task CacheRoundTripsAndRejectsCorruption()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameKnowledgeTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonGameKnowledgeSnapshotStore(root);
            GameKnowledgeSnapshot original = CreateSnapshot();
            store.Remember(original);
            GameKnowledgeSnapshot restored = store.Find(original.GameName)!;
            TestAssert.Equal(
                original.SnapshotSha256,
                restored.SnapshotSha256,
                "Cache snapshot hash.");
            TestAssert.Equal(
                original.Passages[0].Text,
                restored.Passages[0].Text,
                "Cache passage text.");
            string path = Directory.GetFiles(root, "*.json").Single();
            string json = File.ReadAllText(path).Replace(
                original.SnapshotSha256,
                new string('b', 64),
                StringComparison.Ordinal);
            File.WriteAllText(path, json);
            TestAssert.Throws<InvalidDataException>(
                () => store.Find(original.GameName),
                "Corrupt knowledge cannot resolve.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        return Task.CompletedTask;
    }

    private static async Task WikimediaSendsOnlyGameName()
    {
        var handler = new RecordingHttpHandler(request =>
        {
            string query = request.RequestUri!.Query;
            string json = query.Contains("list=search", StringComparison.Ordinal)
                ? """
                  {"query":{"search":[{"title":"Example Quest"}]}}
                  """
                : query.Contains("wbgetentities", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q123":{"lastrevid":456,"labels":{"en":{"language":"en","value":"Example Quest"}},"descriptions":{"en":{"language":"en","value":"fictional action game"}},"aliases":{"en":[{"language":"en","value":"Example Adventure"}]}}}}
                  """
                : """
                  {"query":{"pages":[{"pageid":123,"title":"Example Quest","fullurl":"https://en.wikipedia.org/wiki/Example_Quest","extract":"Overview paragraph about a city.\n\n== Plot ==\nAfter an accident, a guide enters the hero. At the clinic, a masked stranger takes the hero's sibling before the journey continues outside.","revisions":[{"revid":789,"timestamp":"2026-01-02T03:04:05Z"}],"pageprops":{"wikibase_item":"Q123"}}]}}
                  """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler);
        var provider = new WikimediaGameKnowledgeProvider(
            client,
            "https://example.test/wikipedia-api",
            "https://example.test/wikidata-api");

        GameKnowledgeSnapshot result = await provider.AcquireAsync(
            "Example\u202E Quest\r\n",
            CancellationToken.None);

        TestAssert.Equal(6, handler.Requests.Count, "Wikimedia calls.");
        TestAssert.True(
            handler.Requests[0].Query.Contains(
                "Example%20Quest",
                StringComparison.Ordinal),
            "Confirmed game query.");
        TestAssert.Equal(
            "Example Quest",
            result.GameName,
            "Online game lookup must remove bidi and line-control syntax before acquisition.");
        string combined = string.Join(" ", handler.Requests);
        foreach (string privateValue in new[]
        {
            "Recording Video Files",
            "candidate",
            "transcript",
            "masked stranger takes",
        })
        {
            TestAssert.False(
                combined.Contains(privateValue, StringComparison.OrdinalIgnoreCase),
                "Only the confirmed game name may leave the computer.");
        }
        TestAssert.Equal(
            "CC-BY-SA-4.0",
            result.Sources[0].LicenseIdentifier,
            "Wikipedia license.");
        TestAssert.Equal("789", result.Sources[0].RevisionId, "Revision.");
        TestAssert.True(
            result.Sources[0].PageUri.Query.Contains("oldid=789"),
            "Exact source revision URL.");
        TestAssert.Equal(
            GameKnowledgeSourceRole.PrimaryArticle,
            result.Sources[0].Role,
            "Primary source role.");
        GameKnowledgeSource structuredIdentity = result.Sources.Single(
            static source => source.Kind == GameKnowledgeSourceKind.Wikidata);
        TestAssert.Equal(
            GameKnowledgeSourceRole.StructuredIdentity,
            structuredIdentity.Role,
            "Wikidata contributes canonical identity rather than clip-event claims.");
        TestAssert.True(
            result.Passages.Any(passage =>
                passage.SourceId == structuredIdentity.Id &&
                passage.Section == "Identity" &&
                passage.Text.Contains("Example Adventure", StringComparison.Ordinal)),
            "Wikidata label and aliases remain available as bounded identity context.");
    }

    private static async Task WikimediaRelatedArticlesAreBounded()
    {
        var handler = new RecordingHttpHandler(request =>
        {
            string query = request.RequestUri!.Query;
            string json;
            if (query.Contains("list=search", StringComparison.Ordinal))
            {
                json = query.Contains("characters", StringComparison.Ordinal)
                    ? "{\"query\":{\"search\":[{\"title\":\"Characters of Example Quest\"}]}}"
                    : "{\"query\":{\"search\":[]}}";
            }
            else if (query.Contains(
                         "Characters%20of%20Example%20Quest",
                         StringComparison.Ordinal))
            {
                json = """
                  {"query":{"pages":[{"pageid":8,"title":"Characters of Example Quest","fullurl":"https://en.wikipedia.org/wiki/Characters_of_Example_Quest","extract":"Characters of Example Quest describes people appearing in the Example Quest video game.\n\n== Characters ==\nA guide and rival appear throughout the story.","revisions":[{"revid":9,"timestamp":"2026-01-03T03:04:05Z"}]}]}}
                  """;
            }
            else
            {
                json = """
                  {"query":{"pages":[{"pageid":7,"title":"Example Quest","fullurl":"https://en.wikipedia.org/wiki/Example_Quest","extract":"Example Quest is a video game.\n\n== Plot ==\nA journey crosses several districts.","revisions":[{"revid":8,"timestamp":"2026-01-02T03:04:05Z"}]}]}}
                  """;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler);
        var provider = new WikimediaGameKnowledgeProvider(
            client,
            "https://example.test/wikipedia-api",
            "https://example.test/wikidata-api");

        GameKnowledgeSnapshot result = await provider.AcquireAsync(
            "Example Quest",
            CancellationToken.None);

        TestAssert.Equal(2, result.Sources.Count, "Primary plus related source.");
        TestAssert.Equal(
            GameKnowledgeSourceRole.RelatedArticle,
            result.Sources[1].Role,
            "Related source role.");
        TestAssert.True(
            result.Sources[1].PageUri.Query.Contains("oldid=9"),
            "Related source exact revision.");
        TestAssert.True(
            result.Passages.Count <= 120,
            "Related retrieval remains inside snapshot bounds.");
        TestAssert.False(
            string.Join(" ", handler.Requests).Contains(
                "transcript",
                StringComparison.OrdinalIgnoreCase),
            "Related discovery must send only the confirmed game name and fixed strategy terms.");
    }

    private static async Task WikimediaNarrativePassagesAreBounded()
    {
        string plot = string.Join(
            " ",
            Enumerable.Range(1, 18).Select(index =>
                $"Event {index} changes the test journey without changing its source order."));
        string extract = $"Overview.\n\n== Plot ==\n{plot}";
        var handler = new RecordingHttpHandler(request =>
        {
            string json = request.RequestUri!.Query.Contains(
                "list=search",
                StringComparison.Ordinal)
                ? "{\"query\":{\"search\":[]}}"
                : "{\"query\":{\"pages\":[{\"pageid\":7,\"title\":\"Example Quest\",\"fullurl\":\"https://en.wikipedia.org/wiki/Example_Quest\",\"extract\":" +
                  JsonSerializer.Serialize(extract) +
                  ",\"revisions\":[{\"revid\":8,\"timestamp\":\"2026-01-02T03:04:05Z\"}]}]}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler);
        var provider = new WikimediaGameKnowledgeProvider(
            client,
            "https://example.test/wikipedia-api",
            "https://example.test/wikidata-api");

        GameKnowledgeSnapshot result = await provider.AcquireAsync(
            "Example Quest",
            CancellationToken.None);
        GameKnowledgePassage[] plotPassages = result.Passages
            .Where(static value => value.Section == "Plot")
            .ToArray();

        TestAssert.True(plotPassages.Length >= 2, "Long plot segmentation.");
        TestAssert.True(
            plotPassages.All(static value =>
                value.Text.Length <= GameKnowledgePassage.MaximumTextLength),
            "Narrative prompt bound.");
        TestAssert.True(
            plotPassages[0].Text.StartsWith("Event 1 ", StringComparison.Ordinal) &&
            plotPassages[^1].Text.Contains("Event 18 ", StringComparison.Ordinal),
            "Narrative source order.");
    }

    private static Task RetrievalIsDeterministic()
    {
        GameKnowledgeSnapshot snapshot = CreateSnapshot();
        ClipEditorialContext context = CreateEditorialContext(
            "I enter the clinic and face a masked stranger beside my sibling.");
        var retriever = new DeterministicGameKnowledgeRetriever();
        ClipGameKnowledgeContext first = retriever.Retrieve(snapshot, context);
        ClipGameKnowledgeContext repeated = retriever.Retrieve(snapshot, context);

        TestAssert.True(first.HasClipLinkedKnowledge, "Clip-linked match.");
        TestAssert.Equal(
            first.Matches[0].Passage.Id,
            repeated.Matches[0].Passage.Id,
            "Deterministic match identity.");
        TestAssert.Equal(
            "visual-change-1",
            first.Matches[0].ClipEvidenceIds[0],
            "Local evidence attribution.");
        TestAssert.True(
            first.Matches[0].Passage.Text.Contains(
                "sibling",
                StringComparison.Ordinal),
            "Relevant story passage.");
        return Task.CompletedTask;
    }

    private static Task OneAuthoritativeTermCannotClipLink()
    {
        ClipEditorialContext context = CreateEditorialContext(
            visualDescription: null,
            contextNotes: "sibling");
        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                CreateSnapshot(),
                context);

        TestAssert.False(
            result.HasClipLinkedKnowledge,
            "One authoritative term can nominate but cannot establish a story link.");
        return Task.CompletedTask;
    }

    private static Task StableOcrCanClipLink()
    {
        ClipEditorialContext context = CreateEditorialContext(
            visualDescription: null);
        var anchor = new VisualTextAnchor(
            "masked clinic",
            "Masked Clinic",
            VisualTextAnchorAuthority.RepeatedAcrossFrames,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
        context = context.WithVisualText(new ClipVisualTextContext(
            context.CandidateId,
            context.SourceFullPath,
            NormalizedRectangle.FullFrame,
            frames: [],
            anchors: [anchor]));

        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                CreateSnapshot(),
                context);

        TestAssert.True(result.HasClipLinkedKnowledge, "Stable OCR linkage.");
        TestAssert.True(
            result.Matches[0].ClipEvidenceIds.Contains(
                anchor.EvidenceId,
                StringComparer.Ordinal),
            "OCR evidence attribution.");
        return Task.CompletedTask;
    }

    private static Task ClipLinkedRetrievalKeepsCurrentAndPrior()
    {
        ClipEditorialContext context = CreateEditorialContext(
            visualDescription: null,
            contextNotes:
                "A masked visitor reaches Mari inside the hospital before Akito.");
        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                CreateOrderedNarrativeSnapshot(),
                context);

        TestAssert.Equal(2, result.Matches.Count, "Current and prior count.");
        TestAssert.Equal(
            "gkp-current",
            result.Matches[0].Passage.Id,
            "Clip-linked current passage.");
        TestAssert.True(
            result.Matches[0].Strength ==
                GameKnowledgeMatchStrength.ClipLinked,
            "Current passage authority.");
        TestAssert.Equal(
            "gkp-prior",
            result.Matches[1].Passage.Id,
            "Immediately prior passage.");
        TestAssert.Equal(
            GameKnowledgeTemporalRelation.ImmediatelyPriorContext,
            result.Matches[1].TemporalRelation,
            "Prior relation.");
        TestAssert.False(
            result.Matches.Any(static value =>
                value.Passage.Id == "gkp-future"),
            "Future narrative is excluded.");
        return Task.CompletedTask;
    }

    private static Task AutomaticTranscriptNominatesVisualGrounding()
    {
        GameKnowledgeSnapshot snapshot = CreateSnapshot();
        ClipEditorialContext context = CreateEditorialContext(
            visualDescription: null,
            automaticTranscript:
                "I like his mask, though. That's an interesting thing.");
        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                snapshot,
                context);

        TestAssert.False(
            result.HasClipLinkedKnowledge,
            "Unreviewed ASR cannot authorize an exact story claim.");
        TestAssert.True(
            result.Matches[0].Strength ==
                GameKnowledgeMatchStrength.CandidateForVisualGrounding,
            "ASR may nominate a passage for bounded visual review.");
        TestAssert.Equal(
            "gkp-plot",
            result.Matches[0].Passage.Id,
            "The locally nominated passage must rank first.");
        TestAssert.Equal(
            GameKnowledgeTemporalRelation.CurrentEventCandidate,
            result.Matches[0].TemporalRelation,
            "Automatic transcript nomination marks the candidate relation.");
        TestAssert.Equal(
            0,
            result.Matches[0].ClipEvidenceIds.Count,
            "ASR does not become authoritative clip evidence.");
        TestAssert.True(
            result.Matches.Any(static value => value.Strength ==
                GameKnowledgeMatchStrength.GeneralContext &&
                value.Passage.Id == "gkp-overview"),
            "Broad game context stays available without becoming event evidence.");
        return Task.CompletedTask;
    }

    private static Task AutomaticTranscriptUsesPriorNarrativeOnly()
    {
        GameKnowledgeSnapshot snapshot = CreateOrderedNarrativeSnapshot();
        ClipEditorialContext context = CreateEditorialContext(
            visualDescription: null,
            automaticTranscript:
                "The masked visitor reaches Mari inside the hospital.");
        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                snapshot,
                context);

        TestAssert.Equal(
            "gkp-current",
            result.Matches[0].Passage.Id,
            "The nominated current event ranks first.");
        TestAssert.Equal(
            "gkp-prior",
            result.Matches[1].Passage.Id,
            "Only immediately preceding narrative context follows it.");
        TestAssert.Equal(
            GameKnowledgeTemporalRelation.ImmediatelyPriorContext,
            result.Matches[1].TemporalRelation,
            "Prior context is explicit rather than inferred from array order.");
        TestAssert.False(
            result.Matches.Any(static value =>
                value.Passage.Id == "gkp-future"),
            "A future event cannot enter prior-cause context.");
        return Task.CompletedTask;
    }

    private static Task GenericVisualUsesBoundedCandidate()
    {
        ClipEditorialContext context = CreateEditorialContext(
            "A visual evidence point supports the observation.");
        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                CreateSnapshot(),
                context);

        TestAssert.False(
            result.HasClipLinkedKnowledge,
            "A canonical placeholder cannot establish story linkage.");
        GameKnowledgeMatch[] eventCandidates = result.Matches
            .Where(static value => value.Strength ==
                GameKnowledgeMatchStrength.CandidateForVisualGrounding)
            .ToArray();
        TestAssert.True(
            eventCandidates.All(static value =>
                value.Passage.Section == "Plot"),
            "Only narrative passages may enter bounded visual grounding.");
        TestAssert.True(
            eventCandidates.Length <=
                DeterministicGameKnowledgeRetriever
                    .MaximumVisualGroundingCandidates,
            "Bounded visual grounding candidate count.");
        TestAssert.True(
            result.Matches.Any(static value =>
                value.Strength == GameKnowledgeMatchStrength.GeneralContext &&
                value.Passage.Section == "Overview"),
            "General game context is retained separately from clip-event evidence.");
        return Task.CompletedTask;
    }

    private static Task WeakGeneralOverlapDoesNotHideNarrativeCandidates()
    {
        const string sourceId = "gks-weak-general";
        const string plot = "A masked visitor crossed the clinic and reached the locked room.";
        const string overview = "The episodes released at one monthly interval.";
        var source = new GameKnowledgeSource(
            sourceId,
            GameKnowledgeSourceKind.Wikipedia,
            "Example Quest",
            new Uri("https://example.test/wiki/Example_Quest?oldid=44"),
            "44",
            DateTimeOffset.UnixEpoch,
            "CC-BY-SA-4.0",
            new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
            "Example contributors, revision 44.",
            GameKnowledgePassage.ComputeSha256(plot + overview));
        var snapshot = new GameKnowledgeSnapshot(
            "Example Quest",
            new GameKnowledgeProviderIdentity("Test knowledge", "1.0"),
            DateTimeOffset.UnixEpoch,
            [source],
            [
                new GameKnowledgePassage(
                    "gkp-weak-plot",
                    sourceId,
                    "Plot",
                    plot,
                    GameKnowledgePassage.ComputeSha256(plot)),
                new GameKnowledgePassage(
                    "gkp-weak-overview",
                    sourceId,
                    "Overview",
                    overview,
                    GameKnowledgePassage.ComputeSha256(overview)),
            ]);
        var context = new ClipEditorialContext(
            "candidate-weak",
            Path.Combine(Path.GetTempPath(), "Example Quest", "weak.mkv"),
            "Example Quest",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMinutes(1),
            75,
            "The deterministic interval was retained.",
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "deterministic-weak",
                    ClipEditorialEvidenceKind.DeterministicMoment,
                    "The deterministic interval was retained."),
            ],
            gameContext: new ClipEditorialGameContext(
                "Example Quest",
                "#ExampleQuest",
                null,
                ClipEditorialGameContextSource.UserConfirmed,
                useOpenGameKnowledge: true));

        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(snapshot, context);

        TestAssert.True(
            result.Matches.Any(static match => match.Strength ==
                GameKnowledgeMatchStrength.CandidateForVisualGrounding &&
                match.Passage.Section == "Plot"),
            "One weak general word must still retain bounded narrative candidates.");
        TestAssert.True(
            result.Matches.Any(static match =>
                match.Strength == GameKnowledgeMatchStrength.GeneralContext &&
                match.Passage.Section == "Overview"),
            "Broad overview context remains typed separately and cannot establish the event.");
        return Task.CompletedTask;
    }

    private static Task NoAnchorRetainsGeneralContext()
    {
        ClipGameKnowledgeContext result =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                CreateSnapshot(),
                CreateEditorialContext(visualDescription: null));

        TestAssert.True(
            result.Matches.Any(static match =>
                match.Strength == GameKnowledgeMatchStrength.GeneralContext &&
                match.Passage.Id == "gkp-overview" &&
                match.MatchedTerms.Count == 0 &&
                match.ClipEvidenceIds.Count == 0),
            "Broad canonical context needs no fake clip match or evidence binding.");
        TestAssert.False(
            result.HasClipLinkedKnowledge,
            "Broad canonical context never establishes this clip's exact event.");
        return Task.CompletedTask;
    }

    private static async Task ServiceRequiresOptIn()
    {
        var provider = new FakeProvider(CreateSnapshot());
        var store = new MemoryStore();
        var service = new GenerationGameKnowledgeService(provider, store);
        ClipEditorialContext disabled = CreateEditorialContext(
            "The clinic contains a masked stranger.",
            useOpenKnowledge: false);
        ClipEditorialContext unchanged = await service.EnrichAsync(
            disabled,
            CancellationToken.None);
        TestAssert.Same(disabled, unchanged, "Opt-out context.");
        TestAssert.Equal(0, provider.Calls, "Opt-out network calls.");

        ClipEditorialContext enabled = CreateEditorialContext(
            "The clinic contains a masked stranger.",
            useOpenKnowledge: true);
        ClipEditorialContext enriched = await service.EnrichAsync(
            enabled,
            CancellationToken.None);
        ClipEditorialContext cached = await service.EnrichAsync(
            enabled,
            CancellationToken.None);
        TestAssert.Equal(1, provider.Calls, "One acquisition per game.");
        TestAssert.True(
            enriched.GameKnowledge?.HasClipLinkedKnowledge == true,
            "Enabled game knowledge.");
        TestAssert.Equal(
            enriched.GameKnowledge!.Snapshot!.SnapshotSha256,
            cached.GameKnowledge!.Snapshot!.SnapshotSha256,
            "Cached snapshot reuse.");
    }

    private static async Task ServiceDegrades()
    {
        var service = new GenerationGameKnowledgeService(
            new FakeProvider(new HttpRequestException("offline")),
            new MemoryStore());
        ClipEditorialContext context = CreateEditorialContext(
            "The clinic contains a masked stranger.",
            useOpenKnowledge: true);
        ClipEditorialContext result = await service.EnrichAsync(
            context,
            CancellationToken.None);
        TestAssert.True(
            result.GameKnowledge?.Warnings.Single().Code ==
                GameKnowledgeWarningCode.Unavailable,
            "Network failure must be explicit and non-fatal.");
    }

    private static async Task ServiceRefreshesProviderVersion()
    {
        var store = new MemoryStore();
        store.Remember(CreateSnapshot("Test knowledge", "1.0"));
        var provider = new FakeProvider(
            CreateSnapshot("Test knowledge", "1.1"));
        var service = new GenerationGameKnowledgeService(provider, store);

        ClipEditorialContext result = await service.EnrichAsync(
            CreateEditorialContext(
                "The clinic contains a masked stranger.",
                useOpenKnowledge: true),
            CancellationToken.None);

        TestAssert.Equal(1, provider.Calls, "Stale provider refresh.");
        TestAssert.Equal(
            "1.1",
            result.GameKnowledge!.Snapshot!.Provider.Version,
            "Current provider snapshot.");
    }

    private static Task QwenRejectsForeignGrounding()
    {
        ClipEditorialContext context = CreateEditorialContext(
            "The clinic contains a masked stranger.",
            useOpenKnowledge: true);
        ClipGameKnowledgeContext knowledge =
            new DeterministicGameKnowledgeRetriever().Retrieve(
                CreateSnapshot(),
                context);
        context = context.WithGameKnowledge(knowledge);
        var request = new ClipEditorialMetadataRequest(
            context,
            ClipEditorialProfile.Default,
            attempt: 0,
            ClipEditorialGenerationPreference.AiRequired);
        using JsonDocument metadata = JsonDocument.Parse(
            """
            {
              "title":"Masked stranger at the clinic #ExampleQuest",
              "description":"I find the masked stranger waiting beside my sibling.",
              "tags":["clinic"],
              "grounding":[{
                "audienceField":"Description",
                "knowledgeReferenceIds":["foreign-passage"],
                "clipEvidenceReferenceIds":["visual-change-1"]
              }]
            }
            """);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateGrounding(
                metadata.RootElement,
                request,
                "Masked stranger at the clinic #ExampleQuest",
                "I find the masked stranger waiting beside my sibling."),
            "Foreign passage references must reject.");
        return Task.CompletedTask;
    }

    private static Task QwenAcceptsOneValidatedPass()
    {
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 1,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: []);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 2,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: [],
            actorAuthorityAssessmentApplied: true);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 3,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: ["UnsupportedCreatorEmbodiment"],
            actorAuthorityAssessmentApplied: true);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 6,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: []);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 7,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: ["GroundedRefinementUnchanged"]);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 6,
            visualDraftCount: 4,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: true,
            rejectedValidationRules: []);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 6,
                    visualDraftCount: 4,
                    visualEventSelectionApplied: true,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: true,
                    rejectedValidationRules: [],
                    fourDraftEventSelectionSupported: false),
            "Historical output contracts remain bounded to three visual drafts.");
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 2,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: ["UncoupledKnowledgeReference"]);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 2,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: ["NonRetrospectiveVoice"]);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 2,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: ["FirstPersonTitleSubject"]);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 2,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: ["UnstableReadableTextReuse"]);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 8,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules:
            [
                "UnreviewedTranscriptReuse",
                "TitleDescriptionRepetition",
            ]);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 8,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules:
            [
                "CrossDraftTitleContamination",
                "CrossDraftTitleContamination",
            ]);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 6,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: [],
            groundingPassCount: 5,
            synthesisPassCount: 1,
            groundingPacketReused: false);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 1,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: [],
            groundingPassCount: 5,
            synthesisPassCount: 1,
            groundingPacketReused: true);
        const string duplicateSha256 =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 4,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: true,
            rejectedValidationRules:
            [
                "NonRetrospectiveVoice",
                "ThirdPersonCreatorFraming",
                "ThirdPersonCreatorFraming",
            ],
            groundingPassCount: 2,
            synthesisPassCount: 4,
            groundingPacketReused: true,
            actorAuthorityAssessmentApplied: true,
            duplicateSynthesisRecoveryApplied: true,
            duplicateSynthesisRecoverySourcePassOrdinal: 2,
            duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
            duplicateSynthesisRecoverySourceRejectedJsonSha256:
                duplicateSha256,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                duplicateSha256,
            sampledSynthesisApplied: true,
            sampledSynthesisPassOrdinal: 4,
            sampledSynthesisTrigger:
                Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Trigger,
            sampledSynthesisSourceRejectedJsonSha256: duplicateSha256,
            nonRetrospectiveRetryAnchorApplied: true,
            nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
            nonRetrospectiveRetryAnchorSourceRule: "NonRetrospectiveVoice",
            nonRetrospectiveRetryAnchorEnvelopeSha256: duplicateSha256,
            nonRetrospectiveRetryAnchorAuthoritySha256: duplicateSha256);
        string[] recoveryPoolRejectedRules =
        [
            "ThirdPersonCreatorFraming",
            "GenericOpening",
            "GenericOpening",
            "UnsupportedCreatorEmbodiment",
            "TitleDescriptionRepetition",
            "RerollTitleTooSimilar",
        ];
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 12,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: recoveryPoolRejectedRules,
            groundingPassCount: 5,
            synthesisPassCount: 7,
            groundingPacketReused: false,
            actorAuthorityAssessmentApplied: true,
            duplicateSynthesisRecoveryApplied: true,
            duplicateSynthesisRecoverySourcePassOrdinal: 2,
            duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
            duplicateSynthesisRecoverySourceRejectedJsonSha256:
                duplicateSha256,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                duplicateSha256,
            nonRetrospectiveRetryAnchorApplied: true,
            nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
            nonRetrospectiveRetryAnchorSourceRule:
                "ThirdPersonCreatorFraming",
            nonRetrospectiveRetryAnchorEnvelopeSha256: duplicateSha256,
            nonRetrospectiveRetryAnchorAuthoritySha256: duplicateSha256,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 1,
            synthesisRecoveryPoolSourceRejectedJsonSha256: duplicateSha256,
            synthesisRecoveryPoolAttemptedCandidateCount: 4,
            synthesisRecoveryPoolSelectedCandidateOrdinal: 4);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 12,
                    visualDraftCount: 3,
                    visualEventSelectionApplied: true,
                    knowledgeSelectionApplied: true,
                    groundingReviewApplied: true,
                    rejectedValidationRules: recoveryPoolRejectedRules,
                    groundingPassCount: 5,
                    synthesisPassCount: 7,
                    groundingPacketReused: false,
                    actorAuthorityAssessmentApplied: true,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourcePassOrdinal: 2,
                    duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256:
                        duplicateSha256,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                        duplicateSha256,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorSourceRule:
                        "ThirdPersonCreatorFraming",
                    nonRetrospectiveRetryAnchorEnvelopeSha256: duplicateSha256,
                    nonRetrospectiveRetryAnchorAuthoritySha256: duplicateSha256,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 1,
                    synthesisRecoveryPoolSourceRejectedJsonSha256:
                        duplicateSha256,
                    synthesisRecoveryPoolAttemptedCandidateCount: 4,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 4,
                    strictRetryAnchorSourceRuleSupported: true),
            "Output 1.22 must not let ThirdPersonCreatorFraming supply a sticky grammar target.");
        ValidateRecoveryPoolAttestationContract(recoveryPoolRejectedRules);
        ValidateConditionalRecoveryPoolSourceContract();
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 4,
                    visualDraftCount: 1,
                    visualEventSelectionApplied: false,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: true,
                    rejectedValidationRules:
                    [
                        "NonRetrospectiveVoice",
                        "ThirdPersonCreatorFraming",
                        "ThirdPersonCreatorFraming",
                    ],
                    groundingPassCount: 2,
                    synthesisPassCount: 4,
                    groundingPacketReused: true,
                    actorAuthorityAssessmentApplied: true,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourcePassOrdinal: 2,
                    duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256:
                        duplicateSha256,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                        duplicateSha256,
                    sampledSynthesisApplied: true,
                    sampledSynthesisPassOrdinal: 4,
                    sampledSynthesisTrigger:
                        Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Trigger,
                    sampledSynthesisSourceRejectedJsonSha256: duplicateSha256,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 2,
                    nonRetrospectiveRetryAnchorSourceRule:
                        "NonRetrospectiveVoice",
                    nonRetrospectiveRetryAnchorEnvelopeSha256: duplicateSha256,
                    nonRetrospectiveRetryAnchorAuthoritySha256: duplicateSha256),
            "The sticky anchor source pass must identify its exact first tense rejection.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 2,
                    visualDraftCount: 1,
                    visualEventSelectionApplied: false,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: true,
                    rejectedValidationRules: ["NonRetrospectiveVoice"],
                    groundingPassCount: 2,
                    synthesisPassCount: 2,
                    groundingPacketReused: true,
                    actorAuthorityAssessmentApplied: true,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorSourceRule:
                        "NonRetrospectiveVoice",
                    nonRetrospectiveRetryAnchorEnvelopeSha256: duplicateSha256),
            "Applied retry-anchor provenance requires both envelope and authority hashes.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 1,
                    visualDraftCount: 1,
                    visualEventSelectionApplied: false,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: true,
                    rejectedValidationRules: [],
                    groundingPassCount: 2,
                    synthesisPassCount: 1,
                    groundingPacketReused: true,
                    actorAuthorityAssessmentApplied: true,
                    sampledSynthesisApplied: true,
                    sampledSynthesisPassOrdinal: 4,
                    sampledSynthesisTrigger:
                        Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Trigger,
                    sampledSynthesisSourceRejectedJsonSha256: duplicateSha256),
            "Sampled synthesis requires the exact pass-2/pass-3 duplicate witness.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 4,
                    visualDraftCount: 1,
                    visualEventSelectionApplied: false,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: true,
                    rejectedValidationRules:
                    [
                        "NonRetrospectiveVoice",
                        "ThirdPersonCreatorFraming",
                        "UnsupportedCreatorEmbodiment",
                    ],
                    groundingPassCount: 2,
                    synthesisPassCount: 4,
                    groundingPacketReused: true,
                    actorAuthorityAssessmentApplied: true),
            "A fourth pass requires exact duplicate-recovery witnesses.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 4,
                    visualDraftCount: 1,
                    visualEventSelectionApplied: false,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: true,
                    rejectedValidationRules:
                    [
                        "NonRetrospectiveVoice",
                        "ThirdPersonCreatorFraming",
                        "ThirdPersonCreatorFraming",
                    ],
                    groundingPassCount: 2,
                    synthesisPassCount: 4,
                    groundingPacketReused: true,
                    actorAuthorityAssessmentApplied: true,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourcePassOrdinal: 2,
                    duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256:
                        duplicateSha256,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
            "A fourth pass requires equal duplicate-output SHA witnesses.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 6,
                    visualDraftCount: 3,
                    visualEventSelectionApplied: true,
                    knowledgeSelectionApplied: true,
                    groundingReviewApplied: true,
                    rejectedValidationRules: [],
                    groundingPassCount: 5,
                    synthesisPassCount: 1,
                    groundingPacketReused: true),
            "A reused packet cannot claim the grounding passes ran again.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 1,
                    visualDraftCount: 1,
                    visualEventSelectionApplied: false,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: false,
                    rejectedValidationRules: ["GenericTitle"]),
            "A retained validation rejection requires its retry pass.");
        return Task.CompletedTask;
    }

    private static void ValidateRecoveryPoolAttestationContract(
        IReadOnlyList<string> rejectedRules)
    {
        const string hashA =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string hashB =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string hashC =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        const string hashD =
            "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        const string hashE =
            "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        const string hashF =
            "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        const string finalHash =
            "1111111111111111111111111111111111111111111111111111111111111111";

        Qwen3VlGroundedMetadataModuleIdentity[] modules =
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .GroundedMetadataModules
                .Select(module => new Qwen3VlGroundedMetadataModuleIdentity(
                    module.ModuleName,
                    module.FileName,
                    hashA))
                .ToArray();
        Qwen3VlGroundedMetadataSynthesisPassAttestation[] attestations =
        [
            GreedyAttestation(
                logicalPassOrdinal: 1,
                outputSha256: hashA,
                rejectionCode: rejectedRules[0],
                retryAnchorCaptured: true,
                retryAnchorApplied: false),
            GreedyAttestation(
                logicalPassOrdinal: 2,
                outputSha256: hashB,
                rejectionCode: rejectedRules[1],
                retryAnchorCaptured: false,
                retryAnchorApplied: true),
            GreedyAttestation(
                logicalPassOrdinal: 3,
                outputSha256: hashB,
                rejectionCode: rejectedRules[2],
                retryAnchorCaptured: false,
                retryAnchorApplied: true),
            PoolAttestation(1, hashD, rejectedRules[3], accepted: false),
            PoolAttestation(2, hashE, rejectedRules[4], accepted: false),
            PoolAttestation(3, hashF, rejectedRules[5], accepted: false),
            PoolAttestation(4, finalHash, rejectionCode: null, accepted: true),
        ];

        Qwen3VlGroundedMetadataSelection.ValidateSynthesisRecoveryPoolProvenance(
            synthesisPassCount: 7,
            rejectedValidationRules: rejectedRules,
            decodedTextSha256: finalHash,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 1,
            synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
            synthesisRecoveryPoolAttemptedCandidateCount: 4,
            synthesisRecoveryPoolSelectedCandidateOrdinal: 4,
            moduleIdentities: modules,
            attestations,
            duplicateSynthesisRecoveryApplied: true,
            duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
            nonRetrospectiveRetryAnchorApplied: true,
            nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
            nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
            nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
            retryableSemanticRejections:
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .RetryableSemanticRejectionSet);

        string[] languageRecoveryRules =
        [
            "NonRetrospectiveVoice",
            "UnsupportedCreatorEmbodiment",
            "NonRetrospectiveVoice",
            "NonRetrospectiveVoice",
            "UnsupportedCreatorEmbodiment",
            "NonRetrospectiveVoice",
            "NonRetrospectiveVoice",
        ];
        Qwen3VlGroundedMetadataSynthesisPassAttestation[]
            exhaustedLanguageAttestations = attestations
                .Select((value, index) => value with
                {
                    RejectionCode = languageRecoveryRules[index],
                    Accepted = false,
                })
                .ToArray();
        var languageRecovery =
            new Qwen3VlGroundedMetadataEditorialRephraseValidation(
                Attempted: true,
                Applied: true,
                Outcome: "RecoveredRejectedLanguage",
                SourceJsonSha256: finalHash,
                OutputJsonSha256: hashD,
                RawOutputSha256: hashE,
                RejectionCode: null,
                RecoveredRejectedLanguage: true);
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 8,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: languageRecoveryRules,
            groundingPassCount: 5,
            synthesisPassCount: 7,
            groundingPacketReused: true,
            actorAuthorityAssessmentApplied: true,
            duplicateSynthesisRecoveryApplied: true,
            duplicateSynthesisRecoverySourcePassOrdinal: 2,
            duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
            duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 1,
            synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
            synthesisRecoveryPoolAttemptedCandidateCount: 4,
            synthesisRecoveryPoolSelectedCandidateOrdinal: null,
            editorialRephraseSupported: true,
            editorialRephraseAttempted: true,
            rejectedLanguageRecovered: true);
        Qwen3VlGroundedMetadataSelection.ValidateSynthesisRecoveryPoolProvenance(
            synthesisPassCount: 7,
            rejectedValidationRules: languageRecoveryRules,
            decodedTextSha256: hashE,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 1,
            synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
            synthesisRecoveryPoolAttemptedCandidateCount: 4,
            synthesisRecoveryPoolSelectedCandidateOrdinal: null,
            moduleIdentities: modules,
            attestations: exhaustedLanguageAttestations,
            duplicateSynthesisRecoveryApplied: true,
            duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
            nonRetrospectiveRetryAnchorApplied: true,
            nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
            nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
            nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
            retryableSemanticRejections:
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .RetryableSemanticRejectionSet,
            editorialRephraseSupported: true,
            editorialRephrase: languageRecovery);

        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSelection
                .ValidateSynthesisRecoveryPoolProvenance(
                    synthesisPassCount: 7,
                    rejectedValidationRules: rejectedRules,
                    decodedTextSha256: finalHash,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 1,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
                    synthesisRecoveryPoolAttemptedCandidateCount: 4,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 3,
                    moduleIdentities: modules,
                    attestations,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
                    nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
                    retryableSemanticRejections:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .RetryableSemanticRejectionSet),
            "The pool must select the first accepted, final attempted candidate.");

        Qwen3VlGroundedMetadataModuleIdentity[] reorderedModules =
            modules.Reverse().ToArray();
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSelection
                .ValidateSynthesisRecoveryPoolProvenance(
                    synthesisPassCount: 7,
                    rejectedValidationRules: rejectedRules,
                    decodedTextSha256: finalHash,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 1,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
                    synthesisRecoveryPoolAttemptedCandidateCount: 4,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 4,
                    moduleIdentities: reorderedModules,
                    attestations,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
                    nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
                    retryableSemanticRejections:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .RetryableSemanticRejectionSet),
            "Module identities must preserve the exact attested implementation order.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation[] alteredPrompt =
            attestations.ToArray();
        alteredPrompt[4] = alteredPrompt[4] with
        {
            RenderedPromptUtf8ByteCount = 0,
        };
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSelection
                .ValidateSynthesisRecoveryPoolProvenance(
                    synthesisPassCount: 7,
                    rejectedValidationRules: rejectedRules,
                    decodedTextSha256: finalHash,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 1,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
                    synthesisRecoveryPoolAttemptedCandidateCount: 4,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 4,
                    moduleIdentities: modules,
                    attestations: alteredPrompt,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
                    nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
                    retryableSemanticRejections:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .RetryableSemanticRejectionSet),
            "Every attestation requires a non-empty rendered prompt witness.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation[] alteredGreedySource =
            attestations.ToArray();
        alteredGreedySource[1] = alteredGreedySource[1] with
        {
            SourcePassOrdinal = null,
            SourceRejectedJsonSha256 = null,
        };
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSelection
                .ValidateSynthesisRecoveryPoolProvenance(
                    synthesisPassCount: 7,
                    rejectedValidationRules: rejectedRules,
                    decodedTextSha256: finalHash,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 1,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
                    synthesisRecoveryPoolAttemptedCandidateCount: 4,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 4,
                    moduleIdentities: modules,
                    attestations: alteredGreedySource,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
                    nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
                    retryableSemanticRejections:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .RetryableSemanticRejectionSet),
            "Each greedy retry must bind the immediately prior canonical JSON.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation[] alteredCanonicalSource =
            attestations.ToArray();
        alteredCanonicalSource[0] = alteredCanonicalSource[0] with
        {
            CompletedJsonSha256 = hashD,
        };
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataSelection
                .ValidateSynthesisRecoveryPoolProvenance(
                    synthesisPassCount: 7,
                    rejectedValidationRules: rejectedRules,
                    decodedTextSha256: finalHash,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 1,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
                    synthesisRecoveryPoolAttemptedCandidateCount: 4,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 4,
                    moduleIdentities: modules,
                    attestations: alteredCanonicalSource,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
                    nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
                    retryableSemanticRejections:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .RetryableSemanticRejectionSet),
            "The pool source must bind pass one's canonical completed JSON, not raw text.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation GreedyAttestation(
            int logicalPassOrdinal,
            string outputSha256,
            string rejectionCode,
            bool retryAnchorCaptured,
            bool retryAnchorApplied) =>
            new(
                logicalPassOrdinal,
                CandidateOrdinal: null,
                Qwen3VlGroundedMetadataSynthesisDecoding.Greedy,
                Seed: 0,
                SourcePassOrdinal:
                    logicalPassOrdinal == 1 ? null : logicalPassOrdinal - 1,
                SourceRejectedJsonSha256: logicalPassOrdinal switch
                {
                    1 => null,
                    2 => hashA,
                    _ => hashB,
                },
                SourceSelectionReason: null,
                CanonicalMessagesSha256: hashA,
                RenderedPromptSha256: hashB,
                RenderedPromptUtf8ByteCount: 100,
                InputTokenIdsSha256: hashC,
                InputTokenCount: 20,
                outputSha256,
                CompletedJsonSha256: outputSha256,
                rejectionCode,
                Accepted: false,
                retryAnchorCaptured,
                retryAnchorApplied,
                RetryAnchorDisabledReason: null,
                RetryAnchorEnvelopeSha256: hashE,
                RetryAnchorAuthoritySha256:
                    retryAnchorApplied ? hashF : null);

        Qwen3VlGroundedMetadataSynthesisPassAttestation PoolAttestation(
            int candidateOrdinal,
            string outputSha256,
            string? rejectionCode,
            bool accepted) =>
            new(
                LogicalPassOrdinal: 4,
                candidateOrdinal,
                Qwen3VlGroundedMetadataSynthesisDecoding.RecoveryPool,
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .Seeds[candidateOrdinal - 1],
                SourcePassOrdinal: 1,
                SourceRejectedJsonSha256: hashA,
                SourceSelectionReason: null,
                CanonicalMessagesSha256: hashA,
                RenderedPromptSha256: hashB,
                RenderedPromptUtf8ByteCount: 100,
                InputTokenIdsSha256: hashC,
                InputTokenCount: 20,
                outputSha256,
                CompletedJsonSha256: outputSha256,
                rejectionCode,
                accepted,
                RetryAnchorCaptured: false,
                RetryAnchorApplied: true,
                RetryAnchorDisabledReason: null,
                RetryAnchorEnvelopeSha256: hashE,
                RetryAnchorAuthoritySha256: hashF);
    }

    private static void ValidateConditionalRecoveryPoolSourceContract()
    {
        const string hashA =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string hashB =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string hashC =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        const string finalHash =
            "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        string[] rejectedRules =
        [
            "CrossDraftTitleContamination",
            "ThirdPersonCreatorFraming",
            "ThirdPersonCreatorFraming",
        ];
        Qwen3VlGroundedMetadataModuleIdentity[] modules =
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .GroundedMetadataModules
                .Select(module => new Qwen3VlGroundedMetadataModuleIdentity(
                    module.ModuleName,
                    module.FileName,
                    hashA))
                .ToArray();
        Qwen3VlGroundedMetadataSynthesisPassAttestation[] attestations =
        [
            GreedyAttestation(1, null, null, hashA, rejectedRules[0]),
            GreedyAttestation(
                2,
                1,
                hashA,
                hashB,
                rejectedRules[1],
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .CrossDraftRetrySourceSelectionReason),
            GreedyAttestation(3, 2, hashB, hashB, rejectedRules[2]),
            new(
                LogicalPassOrdinal: 4,
                CandidateOrdinal: 1,
                Qwen3VlGroundedMetadataSynthesisDecoding.RecoveryPool,
                Seed: 3407,
                SourcePassOrdinal: 3,
                SourceRejectedJsonSha256: hashB,
                SourceSelectionReason:
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PrimaryOnlyCrossDraftSourceSelectionReason,
                CanonicalMessagesSha256: hashC,
                RenderedPromptSha256: hashC,
                RenderedPromptUtf8ByteCount: 256,
                InputTokenIdsSha256: hashC,
                InputTokenCount: 64,
                OutputSha256: finalHash,
                CompletedJsonSha256: finalHash,
                RejectionCode: null,
                Accepted: true,
                RetryAnchorCaptured: false,
                RetryAnchorApplied: false,
                RetryAnchorDisabledReason: null,
                RetryAnchorEnvelopeSha256: null,
                RetryAnchorAuthoritySha256: null),
        ];

        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 4,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: rejectedRules,
            groundingPassCount: 5,
            synthesisPassCount: 4,
            groundingPacketReused: true,
            actorAuthorityAssessmentApplied: true,
            duplicateSynthesisRecoveryApplied: true,
            duplicateSynthesisRecoverySourcePassOrdinal: 2,
            duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
            duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: hashB,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 3,
            synthesisRecoveryPoolSourceRejectedJsonSha256: hashB,
            synthesisRecoveryPoolAttemptedCandidateCount: 1,
            synthesisRecoveryPoolSelectedCandidateOrdinal: 1,
            conditionalRecoveryPoolSourceSupported: true,
            synthesisRecoveryPoolSourceSelectionReason:
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PrimaryOnlyCrossDraftSourceSelectionReason,
            strictRetryAnchorSourceRuleSupported: true);
        Validate(attestations);

        string[] semanticExhaustionRules =
        [
            "CrossDraftTitleContamination",
            "ThirdPersonCreatorFraming",
            "NonRetrospectiveVoice",
        ];
        Qwen3VlGroundedMetadataSynthesisPassAttestation[]
            semanticExhaustionAttestations =
        [
            GreedyAttestation(
                1,
                null,
                null,
                hashA,
                semanticExhaustionRules[0]),
            GreedyAttestation(
                2,
                1,
                hashA,
                hashB,
                semanticExhaustionRules[1],
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .CrossDraftRetrySourceSelectionReason),
            GreedyAttestation(
                3,
                2,
                hashB,
                hashC,
                semanticExhaustionRules[2]),
            attestations[3] with
            {
                SourceRejectedJsonSha256 = hashC,
            },
        ];
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 4,
            visualDraftCount: 3,
            visualEventSelectionApplied: true,
            knowledgeSelectionApplied: true,
            groundingReviewApplied: true,
            rejectedValidationRules: semanticExhaustionRules,
            groundingPassCount: 5,
            synthesisPassCount: 4,
            groundingPacketReused: true,
            actorAuthorityAssessmentApplied: true,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 3,
            synthesisRecoveryPoolSourceRejectedJsonSha256: hashC,
            synthesisRecoveryPoolAttemptedCandidateCount: 1,
            synthesisRecoveryPoolSelectedCandidateOrdinal: 1,
            conditionalRecoveryPoolSourceSupported: true,
            synthesisRecoveryPoolSourceSelectionReason:
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .PrimaryOnlyCrossDraftSourceSelectionReason,
            strictRetryAnchorSourceRuleSupported: true,
            semanticExhaustionRecoverySupported: true);
        Qwen3VlGroundedMetadataSelection
            .ValidateSynthesisRecoveryPoolProvenance(
                synthesisPassCount: 4,
                rejectedValidationRules: semanticExhaustionRules,
                decodedTextSha256: finalHash,
                synthesisRecoveryPoolApplied: true,
                synthesisRecoveryPoolSourcePassOrdinal: 3,
                synthesisRecoveryPoolSourceRejectedJsonSha256: hashC,
                synthesisRecoveryPoolAttemptedCandidateCount: 1,
                synthesisRecoveryPoolSelectedCandidateOrdinal: 1,
                moduleIdentities: modules,
                attestations: semanticExhaustionAttestations,
                duplicateSynthesisRecoveryApplied: false,
                duplicateSynthesisRecoverySourceRejectedJsonSha256: null,
                duplicateSynthesisRecoveryRepeatedRejectedJsonSha256: null,
                nonRetrospectiveRetryAnchorApplied: false,
                nonRetrospectiveRetryAnchorSourcePassOrdinal: null,
                nonRetrospectiveRetryAnchorEnvelopeSha256: null,
                nonRetrospectiveRetryAnchorAuthoritySha256: null,
                retryableSemanticRejections:
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .RetryableSemanticRejectionSet,
                synthesisRecoveryPoolSourceSelectionReason:
                    Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                        .PrimaryOnlyCrossDraftSourceSelectionReason,
                conditionalRecoveryPoolSource: true,
                strictRetryAnchorSourceRule: true,
                crossDraftRetrySourceWithholding: true,
                semanticExhaustionRecovery: true);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 4,
                    visualDraftCount: 3,
                    visualEventSelectionApplied: true,
                    knowledgeSelectionApplied: true,
                    groundingReviewApplied: true,
                    rejectedValidationRules: semanticExhaustionRules,
                    groundingPassCount: 5,
                    synthesisPassCount: 4,
                    groundingPacketReused: true,
                    actorAuthorityAssessmentApplied: true,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 3,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashC,
                    synthesisRecoveryPoolAttemptedCandidateCount: 1,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 1,
                    conditionalRecoveryPoolSourceSupported: true,
                    synthesisRecoveryPoolSourceSelectionReason:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .PrimaryOnlyCrossDraftSourceSelectionReason,
                    strictRetryAnchorSourceRuleSupported: true,
                    semanticExhaustionRecoverySupported: false),
            "Historical output 1.32 must reject non-duplicate semantic-exhaustion recovery.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation[]
            missingWithheldReason = attestations.ToArray();
        missingWithheldReason[1] = missingWithheldReason[1] with
        {
            SourceSelectionReason = null,
        };
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Validate(missingWithheldReason),
            "Current CrossDraft retries must attest that rejected audience copy was withheld.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation[]
            thirdPersonStickyCapture = attestations.ToArray();
        thirdPersonStickyCapture[1] = thirdPersonStickyCapture[1] with
        {
            RetryAnchorCaptured = true,
            RetryAnchorEnvelopeSha256 = hashA,
        };
        Validate(thirdPersonStickyCapture, strictRetryAnchorSourceRule: false);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Validate(thirdPersonStickyCapture),
            "Output 1.22 must reject a sticky envelope captured from ThirdPersonCreatorFraming while output 1.21 remains historically readable.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation[] wrongReason =
            attestations.ToArray();
        wrongReason[3] = wrongReason[3] with
        {
            SourceSelectionReason =
                Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                    .OriginalSourceSelectionReason,
        };
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Validate(wrongReason),
            "Primary-only CrossDraft recovery must retain its exact source-selection reason.");

        Qwen3VlGroundedMetadataSynthesisPassAttestation[] wrongSource =
            attestations.ToArray();
        wrongSource[3] = wrongSource[3] with
        {
            SourcePassOrdinal = 1,
            SourceRejectedJsonSha256 = hashA,
        };
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Validate(wrongSource),
            "Primary-only CrossDraft recovery cannot regress to the contaminated pass-one JSON.");

        void Validate(
            IReadOnlyList<Qwen3VlGroundedMetadataSynthesisPassAttestation>
                actual,
            bool strictRetryAnchorSourceRule = true) =>
            Qwen3VlGroundedMetadataSelection
                .ValidateSynthesisRecoveryPoolProvenance(
                    synthesisPassCount: 4,
                    rejectedValidationRules: rejectedRules,
                    decodedTextSha256: finalHash,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 3,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashB,
                    synthesisRecoveryPoolAttemptedCandidateCount: 1,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 1,
                    moduleIdentities: modules,
                    attestations: actual,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                        hashB,
                    nonRetrospectiveRetryAnchorApplied: false,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: null,
                    nonRetrospectiveRetryAnchorEnvelopeSha256: null,
                    nonRetrospectiveRetryAnchorAuthoritySha256: null,
                    retryableSemanticRejections:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .RetryableSemanticRejectionSet,
                    synthesisRecoveryPoolSourceSelectionReason:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .PrimaryOnlyCrossDraftSourceSelectionReason,
                    conditionalRecoveryPoolSource: true,
                    strictRetryAnchorSourceRule:
                        strictRetryAnchorSourceRule,
                    crossDraftRetrySourceWithholding: true);

        Qwen3VlGroundedMetadataSynthesisPassAttestation GreedyAttestation(
            int pass,
            int? sourcePass,
            string? sourceHash,
            string completedHash,
            string rejectionCode,
            string? sourceSelectionReason = null) =>
            new(
                LogicalPassOrdinal: pass,
                CandidateOrdinal: null,
                Qwen3VlGroundedMetadataSynthesisDecoding.Greedy,
                Seed: 0,
                SourcePassOrdinal: sourcePass,
                SourceRejectedJsonSha256: sourceHash,
                SourceSelectionReason: sourceSelectionReason,
                CanonicalMessagesSha256: hashC,
                RenderedPromptSha256: hashC,
                RenderedPromptUtf8ByteCount: 256,
                InputTokenIdsSha256: hashC,
                InputTokenCount: 64,
                OutputSha256: completedHash,
                CompletedJsonSha256: completedHash,
                RejectionCode: rejectionCode,
                Accepted: false,
                RetryAnchorCaptured: false,
                RetryAnchorApplied: false,
                RetryAnchorDisabledReason: null,
                RetryAnchorEnvelopeSha256: null,
                RetryAnchorAuthoritySha256: null);
    }

    private static Task QwenVisualEventSelectionRequiresDistinctSupport()
    {
        Qwen3VlGroundedMetadataVisualEventAssessment[] assessments =
        [
            new(
                1,
                DistinctAction: true,
                ObjectInteraction: true,
                VisibleOutcome: false,
                ReadableInterfaceChange: false,
                RoutineOnly: false,
                Uncertain: false),
            new(
                2,
                DistinctAction: false,
                ObjectInteraction: false,
                VisibleOutcome: false,
                ReadableInterfaceChange: false,
                RoutineOnly: false,
                Uncertain: false),
            new(
                3,
                DistinctAction: false,
                ObjectInteraction: false,
                VisibleOutcome: false,
                ReadableInterfaceChange: false,
                RoutineOnly: false,
                Uncertain: false),
        ];

        Qwen3VlGroundedMetadataVisualEventSelectionOutcome selected =
            Qwen3VlGroundedMetadataSelection.SelectPrimaryVisualDraft(
                assessments);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                .SelectedDistinctPrimaryEvent,
            selected.Code,
            "Earlier concrete action should remain primary over later unsupported dialogue.");
        TestAssert.Equal(
            1,
            selected.PrimaryVisualDraftOrdinal,
            "Ordinal must not override distinct event support.");

        Qwen3VlGroundedMetadataVisualEventSelectionOutcome unsupported =
            Qwen3VlGroundedMetadataSelection.SelectPrimaryVisualDraft(
                assessments
                    .Select(static assessment => assessment with
                    {
                        DistinctAction = false,
                        ObjectInteraction = false,
                    })
                    .ToArray());
        TestAssert.Equal(
            Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                .NoDistinctPrimaryEvent,
            unsupported.Code,
            "All-unsupported assessments must produce a typed terminal outcome.");
        TestAssert.Equal<int?>(
            null,
            unsupported.PrimaryVisualDraftOrdinal,
            "No unsupported draft may be promoted by ordinal.");
        return Task.CompletedTask;
    }

    private static Task QwenStableReadableTextRequiresAgreement()
    {
        Qwen3VlGroundedMetadataVisualDraft[] drafts =
        [
            new(
                1,
                0,
                10,
                "Interior",
                false,
                ["A door"],
                ["A hand opens the door"],
                [
                    "  OBJECTIVE   UPDATED  ",
                    "SINGLE DRAFT",
                    "single draft",
                    "71",
                ],
                []),
            new(
                2,
                10,
                20,
                "Interior",
                false,
                ["An open door"],
                ["A person enters"],
                ["objective updated", "UNSTABLE LABEL"],
                []),
        ];
        IReadOnlyList<string> result =
            Qwen3VlGroundedMetadataReadableText.FindStable(drafts);
        TestAssert.Equal(1, result.Count, "Stable readable-text count.");
        TestAssert.Equal(
            "OBJECTIVE UPDATED",
            result[0],
            "First normalized readable-text spelling.");
        return Task.CompletedTask;
    }

    private static GameKnowledgeSnapshot CreateSnapshot(
        string providerName = "Test knowledge",
        string providerVersion = "1.0")
    {
        const string sourceId = "gks-source-01";
        string plot =
            "After a vehicle accident, a spirit guide enters the hero. At the clinic, a masked stranger takes the hero's sibling before the journey continues outside.";
        string overview =
            "Players control Nia, a courier crossing a large modern city in this fictional action game.";
        string later =
            "Later, the hero crosses a flooded bridge and reaches a tower.";
        var source = new GameKnowledgeSource(
            sourceId,
            GameKnowledgeSourceKind.Wikipedia,
            "Example Quest",
            new Uri("https://example.test/wiki/Example_Quest?oldid=42"),
            "42",
            DateTimeOffset.UnixEpoch,
            "CC-BY-SA-4.0",
            new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
            "Example contributors, revision 42.",
            GameKnowledgePassage.ComputeSha256(plot + overview + later));
        return new GameKnowledgeSnapshot(
            "Example Quest",
            new GameKnowledgeProviderIdentity(
                providerName,
                providerVersion),
            DateTimeOffset.UnixEpoch,
            [source],
            [
                new GameKnowledgePassage(
                    "gkp-plot",
                    sourceId,
                    "Plot",
                    plot,
                    GameKnowledgePassage.ComputeSha256(plot)),
                new GameKnowledgePassage(
                    "gkp-overview",
                    sourceId,
                    "Overview",
                    overview,
                    GameKnowledgePassage.ComputeSha256(overview)),
                new GameKnowledgePassage(
                    "gkp-later-plot",
                    sourceId,
                    "Plot",
                    later,
                    GameKnowledgePassage.ComputeSha256(later)),
            ]);
    }

    private static GameKnowledgeSnapshot CreateOrderedNarrativeSnapshot()
    {
        const string sourceId = "gks-source-ordered";
        string prior =
            "After a motorcycle collision, KK enters Akito and keeps him alive.";
        string current =
            "The masked visitor Hannya reaches Mari inside the hospital before Akito.";
        string future =
            "Much later, Akito reaches the tower and confronts the visitor again.";
        var source = new GameKnowledgeSource(
            sourceId,
            GameKnowledgeSourceKind.Wikipedia,
            "Example Quest",
            new Uri("https://example.test/wiki/Example_Quest?oldid=43"),
            "43",
            DateTimeOffset.UnixEpoch,
            "CC-BY-SA-4.0",
            new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
            "Example contributors, revision 43.",
            GameKnowledgePassage.ComputeSha256(prior + current + future));
        return new GameKnowledgeSnapshot(
            "Example Quest",
            new GameKnowledgeProviderIdentity("Test knowledge", "1.0"),
            DateTimeOffset.UnixEpoch,
            [source],
            [
                CreatePassage("gkp-prior", sourceId, prior),
                CreatePassage("gkp-current", sourceId, current),
                CreatePassage("gkp-future", sourceId, future),
            ]);
    }

    private static GameKnowledgePassage CreatePassage(
        string id,
        string sourceId,
        string text) =>
        new(
            id,
            sourceId,
            "Plot",
            text,
            GameKnowledgePassage.ComputeSha256(text));

    private static ClipEditorialContext CreateEditorialContext(
        string? visualDescription,
        string? automaticTranscript = null,
        bool useOpenKnowledge = true,
        string? contextNotes = null)
    {
        ClipEditorialTranscriptContext[] transcripts =
            automaticTranscript is null
                ? []
                :
                [
                    new ClipEditorialTranscriptContext(
                        1,
                        AudioContentRoleAssignment.Unknown,
                        automaticTranscript),
                ];
        ClipEditorialEvidenceReference[] evidence =
            visualDescription is null
                ? []
                :
                [
                    new ClipEditorialEvidenceReference(
                        "visual-change-1",
                        ClipEditorialEvidenceKind.VisualObservation,
                        visualDescription),
                ];
        return new ClipEditorialContext(
            "candidate-1",
            Path.Combine(Path.GetTempPath(), "Example Quest", "clip.mkv"),
            "Example Quest",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            80,
            "Localized evidence change.",
            transcripts,
            evidence,
            new ClipEditorialGameContext(
                "Example Quest",
                "#ExampleQuest",
                contextNotes,
                ClipEditorialGameContextSource.UserConfirmed,
                useOpenKnowledge));
    }

    private sealed class RecordingHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeProvider : IGameKnowledgeSnapshotProvider
    {
        private readonly GameKnowledgeSnapshot? _snapshot;
        private readonly Exception? _exception;

        public FakeProvider(GameKnowledgeSnapshot snapshot) =>
            _snapshot = snapshot;

        public FakeProvider(Exception exception) =>
            _exception = exception;

        public int Calls { get; private set; }

        public GameKnowledgeProviderIdentity Identity =>
            _snapshot?.Provider ?? new("Fake knowledge", "1.0");

        public Task<GameKnowledgeSnapshot> AcquireAsync(
            string confirmedGameName,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return _exception is null
                ? Task.FromResult(_snapshot!)
                : Task.FromException<GameKnowledgeSnapshot>(_exception);
        }
    }

    private sealed class MemoryStore : IGameKnowledgeSnapshotStore
    {
        private GameKnowledgeSnapshot? _snapshot;

        public GameKnowledgeSnapshot? Find(string confirmedGameName) =>
            _snapshot is not null && _snapshot.GameName.Equals(
                confirmedGameName,
                StringComparison.OrdinalIgnoreCase)
                    ? _snapshot
                    : null;

        public void Remember(GameKnowledgeSnapshot snapshot) =>
            _snapshot = snapshot;
    }
}
