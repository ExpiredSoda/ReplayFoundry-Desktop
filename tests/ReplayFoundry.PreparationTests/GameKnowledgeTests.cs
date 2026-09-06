using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.GameContext;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Platform.GameKnowledge;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static class GameKnowledgeTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Game knowledge contracts snapshot values immutably", ContractsAreImmutable),
        new("Game knowledge cache verifies deterministic snapshots", CacheRoundTripsAndRejectsCorruption),
        new("Game knowledge cache loads legacy snapshots without re-saving them", CacheLoadsLegacyBoundedly),
        new("Game identity candidates require explicit selection", CandidateSelectionIsExplicit),
        new("Control game identity stays distinct from the generic concept", ControlIdentityIsDisambiguated),
        new("The Last of Us editions stay distinct from the television series", LastOfUsIdentitiesRemainDistinct),
        new("Wikimedia HTTP-200 maxlag responses retry without blocking interactive discovery", WikimediaApplicationMaxLagRetries),
        new("Game context notes never confirm a game identity", NotesNeverConfirmIdentity),
        new("Remembered Wikimedia permission updates privacy state", WikimediaPermissionStatusIsObservable),
        new("Local-only game context makes zero Wikimedia requests", LocalOnlyMakesZeroWikimediaRequests),
        new("Wikimedia acquisition sends only the confirmed game name", WikimediaSendsOnlyGameName),
        new("Wikimedia related articles are bounded and game-name-only", WikimediaRelatedArticlesAreBounded),
        new("Wikimedia narrative passages remain bounded and ordered", WikimediaNarrativePassagesAreBounded),
        new("Wikimedia component refresh honors Retry-After independently", WikimediaComponentsRefreshIndependently),
        new("StrategyWiki remains an explicit no-network capability", StrategyWikiIsDisabled),
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
        new("AI metadata uses grounded game context independently of scan depth", AnalysisDepthControlsNarrativeKnowledge),
        new("Game knowledge component retries respect their 24-hour schedule", ServiceRespectsComponentRetrySchedule),
        new("Game knowledge receipts expose bounded attributable context", ContextReceiptIsBoundedAndRemovable),
        new("Game knowledge refreshes stale provider snapshots", ServiceRefreshesProviderVersion),
        new("Game knowledge uses only bounded offline stale fallback", ServiceUsesBoundedOfflineFallback),
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
            "2.2.0",
            Qwen3VlGroundedMetadataGenerator.ProviderVersion,
            "Creator-voice title separation changes provider identity.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.61",
            Qwen3VlGroundedMetadataGenerator.OutputSchema,
            "Current grounded metadata output schema.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.60",
            Qwen3VlGroundedMetadataGenerator.PreviousResponsibilitySplitOutputSchema,
            "The shipped compact authoring identity remains readable after responsibility extraction.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCompactIsolatedFieldAuthoring(
                Qwen3VlGroundedMetadataGenerator.PreviousResponsibilitySplitOutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities.SupportsEditorialResponsibilityModules(
                Qwen3VlGroundedMetadataGenerator.PreviousResponsibilitySplitOutputSchema) &&
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsEditorialResponsibilityModules(
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Only the new schema requires the expanded responsibility roster; prompt semantics stay compact.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.59",
            Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema,
            "The original isolated-authoring output remains readable.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsIsolatedFieldAuthoring(
                Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCompactIsolatedFieldAuthoring(
                Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema) &&
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCompactIsolatedFieldAuthoring(
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Output 1.59 retains its original component-prompt contract.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.58",
            Qwen3VlGroundedMetadataGenerator.PreviousIsolatedFieldAuthoringOutputSchema,
            "The previous full-object authoring output remains readable.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsSchemaEnforcedBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.PreviousIsolatedFieldAuthoringOutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities.SupportsIsolatedFieldAuthoring(
                Qwen3VlGroundedMetadataGenerator.PreviousIsolatedFieldAuthoringOutputSchema),
            "Output 1.58 retains its grammar contract without claiming isolated component generation.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.57",
            Qwen3VlGroundedMetadataGenerator.PreviousSchemaEnforcedBalancedCopyOutputSchema,
            "Output before schema-enforced attribution remains readable.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCompactBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.PreviousSchemaEnforcedBalancedCopyOutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities.SupportsSchemaEnforcedBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.PreviousSchemaEnforcedBalancedCopyOutputSchema) &&
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsSchemaEnforcedBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Output 1.57 retains the compact brief without claiming schema-enforced attribution.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.56",
            Qwen3VlGroundedMetadataGenerator.PreviousCompactBalancedCopyOutputSchema,
            "Output before compact balanced authoring remains readable.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.PreviousCompactBalancedCopyOutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCompactBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.PreviousCompactBalancedCopyOutputSchema) &&
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCompactBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Output 1.56 retains balance without claiming the compact brief.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.55",
            Qwen3VlGroundedMetadataGenerator.PreviousBalancedCopyOutputSchema,
            "Output before the typed balanced objective remains readable.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCommentaryTiming(
                Qwen3VlGroundedMetadataGenerator.PreviousBalancedCopyOutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities.SupportsBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.PreviousBalancedCopyOutputSchema) &&
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsBalancedCopy(
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Output 1.55 retains commentary timing without claiming balanced-copy policy.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.54",
            Qwen3VlGroundedMetadataGenerator.PreviousCommentaryTimingOutputSchema,
            "Output before bounded automatic commentary and timing remains readable.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCommentaryTiming(
                Qwen3VlGroundedMetadataGenerator.OutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities.SupportsCommentaryTiming(
                Qwen3VlGroundedMetadataGenerator.PreviousCommentaryTimingOutputSchema),
            "Historical output must retain its original commentary authority.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.53",
            Qwen3VlGroundedMetadataGenerator
                .PreviousCreatorVoiceOutputSchema,
            "Pre-creator-voice output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.52",
            Qwen3VlGroundedMetadataGenerator
                .PreviousEditorialFrameAdherenceOutputSchema,
            "Pre-editorial-frame-adherence output remains readable.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.51",
            Qwen3VlGroundedMetadataGenerator
                .PreviousEditorialFramingOutputSchema,
            "Pre-editorial-framing output remains readable.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsEditorialFrameAdherence(
                    Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Current output enables editorial-frame adherence.");
        TestAssert.True(
            !Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsEditorialFrameAdherence(
                    Qwen3VlGroundedMetadataGenerator
                        .PreviousEditorialFrameAdherenceOutputSchema),
            "Output 1.52 retains its original pre-adherence contract.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsEditorialFraming(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousEditorialFrameAdherenceOutputSchema),
            "Output 1.52 retains editorial framing while omitting adherence.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsBestAvailableVisualEvidence(
                    Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Current output accepts the strongest grounded observation when no " +
            "distinct event was asserted.");
        TestAssert.True(
            !Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsBestAvailableVisualEvidence(
                    Qwen3VlGroundedMetadataGenerator
                        .PreviousEditorialFrameAdherenceOutputSchema),
            "Output 1.52 retains its original distinct-event requirement.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsRecoveryCandidateSelectionModule(
                    Qwen3VlGroundedMetadataGenerator.OutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsRecoveryCandidateSelectionModule(
                    Qwen3VlGroundedMetadataGenerator
                        .PreviousCreatorVoiceOutputSchema),
            "Output 1.54 and newer attest the extracted candidate-selection module.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsRecoveryCandidateSelectionModule(
                Qwen3VlGroundedMetadataGenerator.PreviousCommentaryTimingOutputSchema),
            "Output 1.54 retains candidate-selection provenance.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsSeparatedTitleTags(
                    Qwen3VlGroundedMetadataGenerator.OutputSchema) &&
            !Qwen3VlGroundedMetadataSchemaCapabilities
                .SupportsSeparatedTitleTags(
                    Qwen3VlGroundedMetadataGenerator
                        .PreviousCreatorVoiceOutputSchema),
            "Output 1.54 and newer separate the final title from generated game tags.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSchemaCapabilities.SupportsSeparatedTitleTags(
                Qwen3VlGroundedMetadataGenerator.PreviousCommentaryTimingOutputSchema),
            "Output 1.54 retains separated game tags.");
        TestAssert.Equal(
            "grounded-editorial-metadata-output-batch-1.50",
            Qwen3VlGroundedMetadataGenerator
                .PreviousWholeBatchOutputSchema,
            "Pre-fail-soft whole-batch output remains readable.");
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
              "editorialRephrasePolicyVersion": "grounded-editorial-rephrase-2.4",
              "editorialRephrasePolicySha256": "8682a789fdac6ef51963996cfe13f084dd85e8432d7098080875fbaac1e97ca7",
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
                editorialRephrase.RootElement,
                commentaryTimingSupported: false);
        TestAssert.True(
            rephraseValidation.Attempted && rephraseValidation.Applied,
            "Output 1.54 rephrase provenance must prove one applied bounded pass.");
        using JsonDocument currentRephrase = JsonDocument.Parse(
            editorialRephrase.RootElement.GetRawText()
                .Replace("grounded-editorial-rephrase-2.4",
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.Version,
                    StringComparison.Ordinal)
                .Replace("8682a789fdac6ef51963996cfe13f084dd85e8432d7098080875fbaac1e97ca7",
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.Sha256,
                    StringComparison.Ordinal));
        TestAssert.True(
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                currentRephrase.RootElement).Applied,
            "Current output accepts its exact rephrase identity.");
        using JsonDocument balanceRejectedRephrase = JsonDocument.Parse(
            currentRephrase.RootElement.GetRawText()
                .Replace("\"editorialRephraseApplied\": true", "\"editorialRephraseApplied\": false", StringComparison.Ordinal)
                .Replace("\"Applied\"", "\"RetainedOriginalSemanticRejection\"", StringComparison.Ordinal)
                .Replace("\"editorialRephraseRejectionCode\": null",
                    "\"editorialRephraseRejectionCode\": \"BalanceNotSatisfied\"", StringComparison.Ordinal));
        var balanceRejection = Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            balanceRejectedRephrase.RootElement);
        TestAssert.True(balanceRejection.Attempted && !balanceRejection.Applied,
            "A completed rephrase rejected for balance remains readable without claiming acceptance.");
        using JsonDocument previousIsolatedRephrase = JsonDocument.Parse(
            currentRephrase.RootElement.GetRawText()
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Version,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousIsolatedFieldAuthoringVersion, StringComparison.Ordinal)
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Sha256,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousIsolatedFieldAuthoringSha256, StringComparison.Ordinal));
        TestAssert.True(Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousIsolatedRephrase.RootElement, isolatedFieldAuthoringSupported: false).Applied,
            "Output 1.58 retains its exact 2.8 rephrase identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(() =>
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(previousIsolatedRephrase.RootElement),
            "Current output cannot relabel a 2.8 rephrase as the isolated policy.");
        TestAssert.Throws<Qwen3VlOutputParseException>(() =>
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(currentRephrase.RootElement, isolatedFieldAuthoringSupported: false),
            "Historical output cannot claim the new rephrase policy.");
        using JsonDocument previousSchemaEnforcedRephrase = JsonDocument.Parse(
            currentRephrase.RootElement.GetRawText()
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Version,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousSchemaEnforcedBalancedCopyVersion,
                    StringComparison.Ordinal)
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Sha256,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousSchemaEnforcedBalancedCopySha256,
                    StringComparison.Ordinal));
        TestAssert.True(
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                previousSchemaEnforcedRephrase.RootElement, schemaEnforcedBalancedCopySupported: false).Applied,
            "Output 1.57 accepts its exact 2.7 rephrase identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(previousSchemaEnforcedRephrase.RootElement),
            "Current output cannot claim schema enforcement with an older identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                currentRephrase.RootElement, schemaEnforcedBalancedCopySupported: false),
            "Output 1.57 cannot claim the 2.8 rephrase policy.");
        using JsonDocument previousCompactRephrase = JsonDocument.Parse(
            currentRephrase.RootElement.GetRawText()
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Version,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousCompactBalancedCopyVersion,
                    StringComparison.Ordinal)
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Sha256,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousCompactBalancedCopySha256,
                    StringComparison.Ordinal));
        TestAssert.True(
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                previousCompactRephrase.RootElement, compactBalancedCopySupported: false).Applied,
            "Output 1.56 accepts its exact 2.6 rephrase identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(previousCompactRephrase.RootElement),
            "Current output cannot claim the compact brief with an older identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                currentRephrase.RootElement, compactBalancedCopySupported: false),
            "Output 1.56 cannot claim the 2.7 rephrase policy.");
        using JsonDocument previousBalancedRephrase = JsonDocument.Parse(
            currentRephrase.RootElement.GetRawText()
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Version,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousBalancedCopyVersion,
                    StringComparison.Ordinal)
                .Replace(Qwen3VlGroundedMetadataEditorialRephrasePolicy.Sha256,
                    Qwen3VlGroundedMetadataEditorialRephrasePolicy.PreviousBalancedCopySha256,
                    StringComparison.Ordinal));
        TestAssert.True(
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                previousBalancedRephrase.RootElement, balancedCopySupported: false).Applied,
            "Output 1.55 accepts its exact 2.5 rephrase identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                previousBalancedRephrase.RootElement),
            "Current output cannot claim balanced-copy policy with an older identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                currentRephrase.RootElement, balancedCopySupported: false),
            "Output 1.55 cannot claim the 2.6 rephrase policy.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                editorialRephrase.RootElement),
            "Current output cannot claim new commentary policy with an old rephrase identity.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                currentRephrase.RootElement, commentaryTimingSupported: false),
            "Historical output cannot claim the new rephrase policy.");
        using JsonDocument skippedRephrase = JsonDocument.Parse(
            """
            {
              "editorialRephrasePolicyVersion": "grounded-editorial-rephrase-2.4",
              "editorialRephrasePolicySha256": "8682a789fdac6ef51963996cfe13f084dd85e8432d7098080875fbaac1e97ca7",
              "editorialRephraseAttempted": false,
              "editorialRephraseApplied": false,
              "editorialRephraseOutcome": "RetainedOriginalNoMaterialChange",
              "editorialRephraseSourceJsonSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "editorialRephraseOutputJsonSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "editorialRephraseRejectionCode": "CaseLocalFactUnavailable",
              "editorialRephraseCanonicalMessagesSha256": null,
              "editorialRephraseRenderedPromptSha256": null,
              "editorialRephraseRenderedPromptUtf8ByteCount": null,
              "editorialRephraseInputTokenIdsSha256": null,
              "editorialRephraseInputTokenCount": null,
              "editorialRephraseRawOutputSha256": null
            }
            """);
        Qwen3VlGroundedMetadataEditorialRephraseValidation skippedValidation =
            Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                skippedRephrase.RootElement,
                commentaryTimingSupported: false);
        TestAssert.True(
            skippedValidation.EligibilitySkipped &&
            !skippedValidation.Attempted &&
            !skippedValidation.Applied,
            "Current output explicitly validates a safe no-fact rephrase skip.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                skippedRephrase.RootElement,
                eligibilitySkipSupported: false,
                commentaryTimingSupported: false),
            "Historical output schemas cannot acquire the current skip contract retroactively.");
        string previousAudienceFrame = editorialRephrase.RootElement
            .GetRawText()
            .Replace(
                "grounded-editorial-rephrase-2.4",
                "grounded-editorial-rephrase-2.3",
                StringComparison.Ordinal)
            .Replace(
                "8682a789fdac6ef51963996cfe13f084dd85e8432d7098080875fbaac1e97ca7",
                "0f255474058669b37d9b70ce1ca07f10ad547a0e860a24f02cbcb8adc5d6a075",
                StringComparison.Ordinal);
        using JsonDocument previousAudienceFrameDocument =
            JsonDocument.Parse(previousAudienceFrame);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousAudienceFrameDocument.RootElement,
            commentaryTimingSupported: false);
        string previousStoryLedGrammaticalCenter = previousAudienceFrame
            .Replace(
                "grounded-editorial-rephrase-2.3",
                "grounded-editorial-rephrase-2.2",
                StringComparison.Ordinal)
            .Replace(
                "0f255474058669b37d9b70ce1ca07f10ad547a0e860a24f02cbcb8adc5d6a075",
                "aeee7b7cca2d7b07c337836c7f5f0061d35d33b27874d43549bd7ebcd2645135",
                StringComparison.Ordinal);
        using JsonDocument previousStoryLedGrammaticalCenterDocument =
            JsonDocument.Parse(previousStoryLedGrammaticalCenter);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousStoryLedGrammaticalCenterDocument.RootElement,
            commentaryTimingSupported: false);
        string mismatchedStoryLedGrammaticalCenter = editorialRephrase
            .RootElement
            .GetRawText()
            .Replace(
                "grounded-editorial-rephrase-2.4",
                "grounded-editorial-rephrase-2.2",
                StringComparison.Ordinal);
        using JsonDocument mismatchedStoryLedGrammaticalCenterDocument =
            JsonDocument.Parse(mismatchedStoryLedGrammaticalCenter);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
                mismatchedStoryLedGrammaticalCenterDocument.RootElement,
                commentaryTimingSupported: false),
            "Historical rephrase provenance requires its exact policy hash.");
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
                recoveredDocument.RootElement,
                commentaryTimingSupported: false),
            "Current reviewable-copy output cannot use the removed language-recovery outcome.");
        string historicalRecoveredRejectedLanguage = recoveredRejectedLanguage
            .Replace(
                "grounded-editorial-rephrase-2.4",
                "grounded-editorial-rephrase-1.9",
                StringComparison.Ordinal)
            .Replace(
                "8682a789fdac6ef51963996cfe13f084dd85e8432d7098080875fbaac1e97ca7",
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
        string previousEditorialFrameAdherence =
            previousStoryLedGrammaticalCenter
            .Replace(
                "grounded-editorial-rephrase-2.2",
                "grounded-editorial-rephrase-2.1",
                StringComparison.Ordinal)
            .Replace(
                "aeee7b7cca2d7b07c337836c7f5f0061d35d33b27874d43549bd7ebcd2645135",
                "1569b4b881fab8abf5347cdfd01f8c262b24dd5935192bdd090c369532bff169",
                StringComparison.Ordinal);
        using JsonDocument previousEditorialFrameAdherenceDocument =
            JsonDocument.Parse(previousEditorialFrameAdherence);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousEditorialFrameAdherenceDocument.RootElement,
            editorialFrameAdherenceSupported: false);
        string previousEditorialFraming = previousEditorialFrameAdherence
            .Replace(
                "grounded-editorial-rephrase-2.1",
                "grounded-editorial-rephrase-2.0",
                StringComparison.Ordinal)
            .Replace(
                "1569b4b881fab8abf5347cdfd01f8c262b24dd5935192bdd090c369532bff169",
                "556b11ad5535f4d16883a2a43bbd72ad83996520f4d6d8fc87d06615dccbba04",
                StringComparison.Ordinal);
        using JsonDocument previousEditorialFramingDocument = JsonDocument.Parse(
            previousEditorialFraming);
        Qwen3VlGroundedMetadataEditorialRephrasePolicy.ParseForTesting(
            previousEditorialFramingDocument.RootElement,
            editorialFramingSupported: false);
        string previousReviewableAudienceCopy = previousEditorialFraming
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
                .ParseForTesting(unknownRejectionDocument.RootElement,
                    commentaryTimingSupported: false),
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
            24,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejections.Count,
            "Current recovery policy freezes the 24 non-mechanical semantic rejections.");
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
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .RetryableSemanticRejectionSet.Contains(
                    "CaseLocalFactRetention") &&
            !Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .LegacyRetryableSemanticRejectionSet.Contains(
                    "UnsupportedCreatorEmbodiment"),
            "Outputs 1.32 through 1.20 retain the broad semantic set; output 1.19 retains policy 1.0.");
        (string ModuleName, string FileName)[] expectedHistoricalGroundedModules =
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
            (
                "pipelineRecoveryCandidateSelection",
                "grounded_metadata_recovery_candidate_selection.py"),
            ("pipelineResult", "grounded_metadata_pipeline_result.py"),
            ("editorialRephrase", "grounded_metadata_rephrase.py"),
            (
                "editorialRephraseMessages",
                "grounded_metadata_rephrase_messages.py"),
            ("synthesis", "grounded_metadata_synthesis.py"),
            (
                "synthesisSanitization",
                "grounded_metadata_synthesis_sanitization.py"),
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
        (string ModuleName, string FileName)[] expectedGroundedModules =
        [
            expectedHistoricalGroundedModules[0],
            ("isolatedFieldAuthoring", "grounded_metadata_isolated_fields.py"),
            ("groundingPacketHandoff", "grounded_packet_handoff.py"),
            .. expectedHistoricalGroundedModules.Skip(1),
            ("automaticCommentary", "grounded_metadata_automatic_commentary.py"),
            ("editorialFraming", "grounded_metadata_editorial_framing.py"),
            ("rephraseCorrections", "grounded_metadata_rephrase_corrections.py"),
            ("rephraseFrame", "grounded_metadata_rephrase_frame.py"),
        ];
        TestAssert.True(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .GroundedMetadataModules.SequenceEqual(expectedGroundedModules),
            "Every extracted grounded-metadata module must stay attested in exact runtime order.");
        TestAssert.True(
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousIsolatedFieldAuthoringGroundedMetadataModules.SequenceEqual(expectedHistoricalGroundedModules),
            "Schema 1.58 retains its exact roster without isolated authoring or handoff modules.");
        TestAssert.Equal(
            expectedHistoricalGroundedModules.Length - 1,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousRecoveryCandidateSelectionGroundedMetadataModules.Count,
            "Schema 1.53 retains its pre-candidate-selection module roster.");
        TestAssert.True(
            !Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousRecoveryCandidateSelectionGroundedMetadataModules.Any(
                    static value => value.ModuleName.Equals(
                        "pipelineRecoveryCandidateSelection",
                        StringComparison.Ordinal)),
            "Historical schemas must not claim the current candidate-selection module.");
        TestAssert.Equal(
            expectedHistoricalGroundedModules.Length - 2,
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousSynthesisSanitizationGroundedMetadataModules.Count,
            "Schema 1.52 retains its pre-sanitization module roster.");
        TestAssert.True(
            !Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousSynthesisSanitizationGroundedMetadataModules.Any(
                    static value => value.ModuleName.Equals(
                        "synthesisSanitization",
                        StringComparison.Ordinal)) &&
            !Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousSynthesisSanitizationGroundedMetadataModules.Any(
                    static value => value.ModuleName.Equals(
                        "pipelineRecoveryCandidateSelection",
                        StringComparison.Ordinal)),
            "Historical schemas must not be retroactively assigned the 1.53 sanitizer module.");
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
        string watchdogPolicyText = File.ReadAllText(RepositoryLayout.VisualSemanticHostPath("replayfoundry-generation-watchdog-policy-1.0.txt"))
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
        string memoryPolicyText = File.ReadAllText(RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-cuda-memory-policy-1.5.txt"))
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
        string sampledPolicyText = File.ReadAllText(RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-sampled-synthesis-policy-1.0.txt"))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        string sampledPolicySha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sampledPolicyText)));
        TestAssert.Equal(
            Qwen3VlGroundedMetadataSynthesisDecodingPolicy.Sha256,
            sampledPolicySha256,
            "Sampled synthesis policy text hash.");
        string recoveryPoolPolicyText = File.ReadAllText(RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.9.txt"))
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
                    RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.7.txt"))
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
                    RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.6.txt"))
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
                    RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.5.txt"))
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
                RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.4.txt"))
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
                RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.3.txt"))
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
                RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.2.txt"))
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
                RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.1.txt"))
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
                RepositoryLayout.VisualSemanticHostPath("replayfoundry-grounded-editorial-synthesis-recovery-pool-policy-1.0.txt"))
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
            store.Remember(
                original,
                WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);
            GameKnowledgeSnapshot restored = store.Find(CacheKey(original))!;
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
                () => store.Find(CacheKey(original)),
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

    private static Task CacheLoadsLegacyBoundedly()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameKnowledgeLegacyTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            const string text =
                "Players control a courier who travels through a fictional city.";
            var source = new GameKnowledgeSource(
                "gks-legacy",
                GameKnowledgeSourceKind.Wikipedia,
                "Example Quest",
                new Uri("https://example.test/wiki/Example_Quest?oldid=7"),
                "7",
                DateTimeOffset.UnixEpoch,
                "CC-BY-SA-4.0",
                new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
                "Example contributors, revision 7.",
                GameKnowledgePassage.ComputeSha256(text));
#pragma warning disable CS0618
            var legacy = new GameKnowledgeSnapshot(
                "Example Quest",
                new GameKnowledgeProviderIdentity("Legacy provider", "1.0"),
                DateTimeOffset.UnixEpoch,
                [source],
                [
                    new GameKnowledgePassage(
                        "gkp-legacy",
                        source.Id,
                        "Overview",
                        text,
                        GameKnowledgePassage.ComputeSha256(text)),
                ]);
#pragma warning restore CS0618
            string fileName = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes("EXAMPLE QUEST"))) + ".json";
            File.WriteAllText(
                Path.Combine(root, fileName),
                JsonSerializer.Serialize(new
                {
                    SchemaVersion = GameKnowledgeSnapshot.LegacySchemaVersion,
                    legacy.GameName,
                    Provider = new
                    {
                        legacy.Provider.Name,
                        legacy.Provider.Version,
                    },
                    RetrievedAtUtc = legacy.RetrievedAtUtc.ToString("O"),
                    Sources = legacy.Sources.Select(value => new
                    {
                        value.Id,
                        Kind = value.Kind.ToString(),
                        Role = value.Role.ToString(),
                        value.Title,
                        PageUri = value.PageUri.AbsoluteUri,
                        value.RevisionId,
                        RevisionTimestampUtc =
                            value.RevisionTimestampUtc.ToString("O"),
                        value.LicenseIdentifier,
                        LicenseUri = value.LicenseUri.AbsoluteUri,
                        value.Attribution,
                        value.ContentSha256,
                    }),
                    Passages = legacy.Passages.Select(value => new
                    {
                        value.Id,
                        value.SourceId,
                        value.Section,
                        value.Text,
                        value.ContentSha256,
                    }),
                    legacy.SnapshotSha256,
                }));

            var store = new JsonGameKnowledgeSnapshotStore(root);
            GameKnowledgeSnapshot restored = store.Find(new GameKnowledgeCacheKey(
                ConfirmedIdentity(),
                new GameKnowledgeProviderIdentity("Legacy provider", "1.0"),
                GameKnowledgeSnapshot.SchemaVersion,
                WikimediaGameKnowledgeProvider.RetrievalPolicyVersion))!;
            TestAssert.True(restored.IsLegacy, "Legacy marker.");
            TestAssert.True(
                restored.Passages.Count <= 48,
                "Legacy context is bounded during load.");
            TestAssert.Throws<InvalidOperationException>(
                () => store.Remember(
                    restored,
                    WikimediaGameKnowledgeProvider.RetrievalPolicyVersion),
                "Legacy cache cannot be re-saved as a current snapshot.");
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

    private static async Task CandidateSelectionIsExplicit()
    {
        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            "Example Quest",
            "clip.mkv");
        var provider = new FakeCandidateProvider(
            new GameIdentityCandidate(
                "Q123",
                "Example Quest",
                "Definitive Edition",
                2024,
                "Example Studio",
                "Example Series",
                GameIdentityAuthority.Wikidata,
                "Wikidata",
                "en"));
        var refreshProvider = new FakeProvider(CreateSnapshot());
        var gameKnowledge = new GenerationGameKnowledgeService(
            refreshProvider,
            new MemoryStore());
        var source = new GameContextSourceViewModel(
            GenerationSourceGameContext.CreatePathHint(sourcePath),
            static () => { },
            provider,
            gameKnowledge);

        TestAssert.True(
            source.IsPublicLookupAvailable &&
            source.PublicLookupStatusText == "Available" &&
            !source.CanUsePublicLookup,
            "Game Details should report stable lookup capability while keeping opt-in disabled until the game name is confirmed.");
        TestAssert.True(
            source.IsValid,
            "An unconfirmed folder suggestion must not block local-only generation.");
        TestAssert.False(
            source.CreateContext().IsUserGrounded,
            "An unconfirmed folder suggestion must not become audience game context.");
        source.ConfirmGameCommand.Execute(null);
        TestAssert.True(
            source.CanUsePublicLookup,
            "Confirming the game name should enable the separately labeled public lookup choice when the provider is available.");
        TestAssert.True(
            source.IsValid,
            "Explicitly confirming a meaningful name permits local-only generation.");
        GenerationSourceGameContext confirmedLocalContext =
            source.CreateContext();
        TestAssert.True(
            confirmedLocalContext.IsUserGrounded,
            "Explicit confirmation retains game-specific audience authority.");
        TestAssert.Equal(
            "#ExampleQuest",
            confirmedLocalContext.GameHashtag,
            "A meaningful confirmed game retains its canonical hashtag behavior.");
        source.UseOpenGameKnowledge = true;
        TestAssert.False(
            source.IsValid,
            "Online intent cannot finish the wizard before exact identity selection.");
        await ((AsyncDelegateCommand)source.DiscoverCandidatesCommand)
            .ExecuteAsync();
        TestAssert.Equal(1, source.Candidates.Count, "Candidate card count.");
        TestAssert.True(
            source.ConfirmedIdentity is null,
            "Candidate discovery must never silently accept the first result.");
        TestAssert.True(
            source.SelectedCandidate is null,
            "Candidate discovery must leave the visual single-choice state unselected.");

        source.SelectedCandidate = source.Candidates[0];
        TestAssert.Equal(
            "Q123",
            source.ConfirmedIdentity!.WikidataEntityId,
            "Explicit candidate QID.");
        TestAssert.True(
            ReferenceEquals(source.Candidates[0], source.SelectedCandidate),
            "Explicit identity selection must persist the matching selected card state.");
        TestAssert.Equal(
            "Example Quest selected. This video is ready.",
            source.CandidateStatus,
            "The selected state should announce that this video is ready in ordinary language.");
        TestAssert.True(source.IsValid, "Explicit selection completes identity.");
        TestAssert.Equal(1, provider.Calls, "One opted-in discovery call.");

        source.SelectedCandidate = null;
        TestAssert.True(
            source.ConfirmedIdentity is null && source.SelectedCandidate is null,
            "Keyboard or modified-click deselection clears both the confirmed identity and visual selection.");
        TestAssert.False(
            source.IsValid,
            "Deselecting the exact match disables wizard progression while open knowledge remains enabled.");
        TestAssert.Equal(
            "Selection cleared. Choose the matching game below, or continue without public information.",
            source.CandidateStatus,
            "Deselecting a card explains the two available recovery actions.");

        source.SelectedCandidate = source.Candidates[0];
        TestAssert.True(
            source.IsValid && source.ConfirmedIdentity is not null,
            "The same card can be selected again after an explicit deselection.");
        TestAssert.Throws<ArgumentException>(
            () => source.SelectedCandidate =
                new GameIdentityCandidateViewModel(provider.Candidate),
            "A candidate view model outside the displayed result set cannot create a confirmed identity without a selected card.");

        GameIdentityCandidateViewModel priorSelection =
            source.SelectedCandidate!;
        bool collectionResetObserved = false;
        NotifyCollectionChangedEventHandler resetSelection = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                collectionResetObserved = true;
                source.SelectedCandidate = null;
            }
        };
        ((INotifyCollectionChanged)source.Candidates).CollectionChanged +=
            resetSelection;
        await ((AsyncDelegateCommand)source.DiscoverCandidatesCommand)
            .ExecuteAsync();
        ((INotifyCollectionChanged)source.Candidates).CollectionChanged -=
            resetSelection;
        TestAssert.True(
            collectionResetObserved &&
            !ReferenceEquals(priorSelection, source.SelectedCandidate),
            "A successful repeat search replaces the displayed candidate instances.");
        TestAssert.True(
            source.IsValid &&
            source.ConfirmedIdentity?.WikidataEntityId == "Q123" &&
            ReferenceEquals(source.Candidates[0], source.SelectedCandidate),
            "A collection-reset selection null preserves and rebinds the matching confirmed QID.");

        await ((AsyncDelegateCommand)source.RefreshConfirmedContextCommand)
            .ExecuteAsync();
        TestAssert.Equal(
            1,
            refreshProvider.Calls,
            "Setup refresh invokes the shared cache service exactly once.");
        TestAssert.Equal(
            "Q123",
            refreshProvider.LastRequest!.Identity.WikidataEntityId,
            "Setup refresh is QID-bound to the exact identity the user selected.");
        TestAssert.True(
            refreshProvider.LastRequest.ForceRefresh,
            "The explicit Setup action refreshes instead of reusing a fresh cache silently.");

        provider.Failure = new HttpRequestException("offline");
        await ((AsyncDelegateCommand)source.DiscoverCandidatesCommand)
            .ExecuteAsync();
        TestAssert.Equal(
            "Q123",
            source.ConfirmedIdentity!.WikidataEntityId,
            "A transient repeat-search failure preserves the already confirmed identity.");
        TestAssert.True(
            ReferenceEquals(source.Candidates[0], source.SelectedCandidate),
            "A transient repeat-search failure preserves the visible selected card.");

        provider.Failure = null;
        provider.Candidate = new GameIdentityCandidate(
            "Q999",
            "Different Quest",
            null,
            2025,
            "Different Studio",
            "Different Series",
            GameIdentityAuthority.Wikidata,
            "Wikidata",
            "en");
        await ((AsyncDelegateCommand)source.DiscoverCandidatesCommand)
            .ExecuteAsync();
        TestAssert.True(
            source.ConfirmedIdentity is null && source.SelectedCandidate is null,
            "A successful repeat search that omits the old QID clears both identity and visual selection.");
        TestAssert.False(
            source.IsValid,
            "The wizard is invalid until a candidate from the refreshed result is explicitly selected.");
        TestAssert.Equal(
            4,
            provider.Calls,
            "Initial, same-QID refresh, failed refresh, and changed-result discovery calls.");

        var generic = new GameContextSourceViewModel(
            new GenerationSourceGameContext(
                Path.Combine(Path.GetTempPath(), "Gameplay", "clip.mkv"),
                GenerationGamePathHintPolicy.UnconfirmedGameName,
                contextNotes: null,
                GenerationGameContextOrigin.SourcePathHint),
            static () => { });
        TestAssert.True(
            !generic.IsPublicLookupAvailable &&
            generic.PublicLookupStatusText == "Unavailable" &&
            !generic.CanUsePublicLookup,
            "Game Details should report lookup as unavailable when no provider exists instead of presenting a transient offline permission state.");
        generic.UseOpenGameKnowledge = true;
        TestAssert.False(
            generic.UseOpenGameKnowledge,
            "An unavailable public lookup cannot be enabled into a blocking wizard state.");
        TestAssert.True(
            generic.IsValid,
            "A generic path hint must allow local-only generation.");
        TestAssert.Equal(
            "No game selected",
            generic.DisplayGameName,
            "The generic internal sentinel must not look like a selected game in Setup.");
        TestAssert.False(
            generic.ConfirmGameCommand.CanExecute(null),
            "A generic path hint cannot be promoted into a user-confirmed game.");
        TestAssert.False(
            generic.CreateContext().IsUserGrounded,
            "A generic path hint must remain excluded from game-specific audience metadata.");

        generic.GameName = string.Empty;
        TestAssert.True(
            generic.IsValid,
            "A blank optional game name must allow local-only generation.");
        GenerationSourceGameContext blankContext = generic.CreateContext();
        TestAssert.Equal(
            GenerationGamePathHintPolicy.UnconfirmedGameName,
            blankContext.GameName,
            "A blank UI value uses only the internal unconfirmed sentinel.");
        TestAssert.False(
            blankContext.IsUserGrounded,
            "The internal sentinel must never become confirmed game context.");
    }

    private static async Task ControlIdentityIsDisambiguated()
    {
        var handler = new RecordingHttpHandler(request =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            string json = query.Contains("action=wbsearchentities", StringComparison.Ordinal)
                ? """
                  {"search":[{"id":"Q54935655"},{"id":"Q999001"}]}
                  """
                : query.Contains("ids=Q54935655", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q54935655":{"lastrevid":101,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"language":"en","value":"Control"}},"descriptions":{"en":{"language":"en","value":"2019 action-adventure video game developed by Remedy Entertainment"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"Control (video game)"}},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7889"}}}}],"P178":[{"mainsnak":{"datavalue":{"value":{"id":"Q929051"}}}}],"P577":[{"mainsnak":{"datavalue":{"value":{"time":"+2019-08-27T00:00:00Z"}}}}]}}}}
                  """
                : query.Contains("ids=Q999001", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q999001":{"lastrevid":102,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"language":"en","value":"Control"}},"descriptions":{"en":{"language":"en","value":"ability to direct or regulate something"}},"aliases":{"en":[]},"sitelinks":{},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q151885"}}}}]}}}}
                  """
                : """
                  {"entities":{"Q7889":{"labels":{"en":{"language":"en","value":"video game"}}},"Q929051":{"labels":{"en":{"language":"en","value":"Remedy Entertainment"}}},"Q151885":{"labels":{"en":{"language":"en","value":"concept"}}}}}
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

        GameIdentityCandidateSet result = await provider.DiscoverAsync(
            DiscoveryRequest("Control"),
            CancellationToken.None);

        TestAssert.Equal(1, result.Candidates.Count, "Only the game candidate remains.");
        GameIdentityCandidate candidate = result.Candidates.Single();
        TestAssert.Equal("Q54935655", candidate.WikidataEntityId, "Control game QID.");
        TestAssert.Equal("Control", candidate.CanonicalTitle, "Canonical title.");
        TestAssert.Equal(2019, candidate.ReleaseYear!.Value, "Release year.");
        TestAssert.Equal(
            "Remedy Entertainment",
            candidate.Developer!,
            "Developer disambiguates the game from the generic concept.");
        TestAssert.True(
            handler.Requests.All(static uri => !uri.Query.Contains(
                "maxlag",
                StringComparison.OrdinalIgnoreCase)),
            "User-clicked candidate discovery must not opt into background maxlag rejection.");
    }

    private static async Task LastOfUsIdentitiesRemainDistinct()
    {
        var handler = new RecordingHttpHandler(request =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            string json = query.Contains("action=wbsearchentities", StringComparison.Ordinal)
                ? """
                  {"search":[{"id":"Q1986744"},{"id":"Q113377532"},{"id":"Q87131973"}]}
                  """
                : query.Contains("ids=Q1986744", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q1986744":{"lastrevid":201,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"language":"en","value":"The Last of Us"}},"descriptions":{"en":{"language":"en","value":"2013 action-adventure video game developed by Naughty Dog"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"The Last of Us"}},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7889"}}}}],"P178":[{"mainsnak":{"datavalue":{"value":{"id":"Q483706"}}}}],"P179":[{"mainsnak":{"datavalue":{"value":{"id":"Q101167219"}}}}],"P577":[{"mainsnak":{"datavalue":{"value":{"time":"+2013-06-14T00:00:00Z"}}}}]}}}}
                  """
                : query.Contains("ids=Q113377532", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q113377532":{"lastrevid":202,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"language":"en","value":"The Last of Us Part I"}},"descriptions":{"en":{"language":"en","value":"2022 remake edition of the 2013 action-adventure video game"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"The Last of Us Part I"}},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7889"}}}}],"P178":[{"mainsnak":{"datavalue":{"value":{"id":"Q483706"}}}}],"P179":[{"mainsnak":{"datavalue":{"value":{"id":"Q101167219"}}}}],"P577":[{"mainsnak":{"datavalue":{"value":{"time":"+2022-09-02T00:00:00Z"}}}}]}}}}
                  """
                : query.Contains("ids=Q87131973", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q87131973":{"lastrevid":203,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"language":"en","value":"The Last of Us"}},"descriptions":{"en":{"language":"en","value":"2023 American television series"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"The Last of Us (TV series)"}},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q5398426"}}}}]}}}}
                  """
                : """
                  {"entities":{"Q7889":{"labels":{"en":{"language":"en","value":"video game"}}},"Q483706":{"labels":{"en":{"language":"en","value":"Naughty Dog"}}},"Q101167219":{"labels":{"en":{"language":"en","value":"The Last of Us series"}}},"Q5398426":{"labels":{"en":{"language":"en","value":"television series"}}}}}
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

        GameIdentityCandidateSet result = await provider.DiscoverAsync(
            DiscoveryRequest("The Last of Us"),
            CancellationToken.None);

        TestAssert.Equal(2, result.Candidates.Count, "Only video-game identities remain.");
        GameIdentityCandidate original = result.Candidates.Single(candidate =>
            candidate.WikidataEntityId == "Q1986744");
        GameIdentityCandidate remake = result.Candidates.Single(candidate =>
            candidate.WikidataEntityId == "Q113377532");
        TestAssert.Equal(2013, original.ReleaseYear!.Value, "Original release year.");
        TestAssert.Equal(2022, remake.ReleaseYear!.Value, "Part I release year.");
        TestAssert.True(
            remake.Edition?.Contains("remake", StringComparison.OrdinalIgnoreCase) == true,
            "Part I retains its edition/remake qualifier.");
        TestAssert.False(
            result.Candidates.Any(candidate => candidate.WikidataEntityId == "Q87131973"),
            "The same-named television series is not offered as a game identity.");
    }

    private static async Task WikimediaApplicationMaxLagRetries()
    {
        int searchAttempts = 0;
        var handler = new RecordingHttpHandler(request =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            string json;
            if (query.Contains("action=wbsearchentities", StringComparison.Ordinal))
            {
                searchAttempts++;
                json = searchAttempts == 1
                    ? """
                      {"error":{"code":"maxlag","info":"Waiting for a replica","lag":6}}
                      """
                    : """
                      {"search":[{"id":"Q123"}]}
                      """;
            }
            else if (query.Contains("ids=Q123", StringComparison.Ordinal))
            {
                json = """
                  {"entities":{"Q123":{"lastrevid":101,"modified":"2026-01-01T00:00:00Z","labels":{"en":{"language":"en","value":"Example Quest"}},"descriptions":{"en":{"language":"en","value":"fictional video game"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"Example Quest"}},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7889"}}}}]}}}}
                  """;
            }
            else
            {
                json = """
                  {"entities":{"Q7889":{"labels":{"en":{"language":"en","value":"video game"}}}}}
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

        GameIdentityCandidateSet result = await provider.DiscoverAsync(
            DiscoveryRequest("Example Quest"),
            CancellationToken.None);

        TestAssert.Equal(2, searchAttempts, "HTTP-200 maxlag response retry count.");
        TestAssert.Equal(1, result.Candidates.Count, "Candidate survives bounded retry.");
        TestAssert.True(
            handler.Requests.All(static uri => !uri.Query.Contains(
                "maxlag",
                StringComparison.OrdinalIgnoreCase)),
            "Every request in user-clicked discovery remains interactive.");
    }

    private static GameIdentityDiscoveryRequest DiscoveryRequest(string title) =>
        new(
            title,
            "en",
            GameKnowledgeSourcePermissions.WikimediaDefault);

    private static Task NotesNeverConfirmIdentity()
    {
        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            "Gameplay",
            "clip.mkv");
        var source = new GameContextSourceViewModel(
            new GenerationSourceGameContext(
                sourcePath,
                "Gameplay",
                contextNotes: null,
                GenerationGameContextOrigin.SourcePathHint),
            static () => { });

        source.ContextNotes = "This may be a late mission.";
        TestAssert.False(
            source.IsConfirmed,
            "Notes cannot confirm a path hint.");
        TestAssert.True(
            source.ConfirmedIdentity is null,
            "Notes cannot create an online identity.");
        TestAssert.False(
            source.CanConfirmGame,
            "Generic Gameplay cannot be promoted by adding notes.");
        return Task.CompletedTask;
    }

    private static Task WikimediaPermissionStatusIsObservable()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-WikimediaPermissionTests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "game-context-memory.json");
        try
        {
            var memory = new JsonGenerationGameContextMemory(path);
            int changes = 0;
            memory.Changed += (_, _) => changes++;
            string sourcePath = Path.Combine(root, "Example Quest", "clip.mkv");
            memory.Remember(
            [
                new GenerationSourceGameContext(
                    sourcePath,
                    "Example Quest",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed,
                    useOpenGameKnowledge: true,
                    ConfirmedIdentity()),
            ]);
            TestAssert.True(
                memory.HasRememberedWikimediaPermission,
                "Privacy state must report remembered Wikimedia permission.");
            memory.Remember(
            [
                new GenerationSourceGameContext(
                    sourcePath,
                    "Example Quest",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed),
            ]);
            TestAssert.False(
                memory.HasRememberedWikimediaPermission,
                "Replacing the remembered context with local-only must clear the status.");
            TestAssert.Equal(2, changes, "Observable permission changes.");
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
                : query.Contains("ids=Q7889", StringComparison.Ordinal) ||
                  query.Contains("ids=Q999", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q7889":{"labels":{"en":{"language":"en","value":"video game"}}},"Q999":{"labels":{"en":{"language":"en","value":"Example Studio"}}},"Q998":{"labels":{"en":{"language":"en","value":"Example Series"}}}}}
                  """
                : query.Contains("wbgetentities", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q123":{"lastrevid":456,"modified":"2026-01-01T03:04:05Z","labels":{"en":{"language":"en","value":"Example Quest"}},"descriptions":{"en":{"language":"en","value":"fictional video game"}},"aliases":{"en":[{"language":"en","value":"Example Adventure"}]},"sitelinks":{"enwiki":{"site":"enwiki","title":"Example Quest"}},"claims":{"P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q7889"}}}}],"P178":[{"mainsnak":{"datavalue":{"value":{"id":"Q999"}}}}],"P179":[{"mainsnak":{"datavalue":{"value":{"id":"Q998"}}}}],"P999":[{"mainsnak":{"datavalue":{"value":{"id":"Q997"}}}}]}}}}
                  """
                : """
                  {"query":{"pages":[{"pageid":123,"title":"Example Quest","fullurl":"https://en.wikipedia.org/wiki/Example_Quest","extract":"Overview paragraph about a city.\n\n== Gameplay ==\nPlayers travel between districts and complete missions.\n\n== Plot ==\nAfter an accident, a guide enters the hero. At the clinic, a masked stranger takes the hero's sibling before the journey continues outside.\n\n== Reception ==\nReviewers awarded the game a perfect score.","revisions":[{"revid":789,"timestamp":"2026-01-02T03:04:05Z"}],"pageprops":{"wikibase_item":"Q123"}}]}}
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
            new GameKnowledgeRefreshRequest(
                ConfirmedIdentity("Example\u202E Quest\r\n")),
            CancellationToken.None);

        TestAssert.Equal(6, handler.Requests.Count, "Wikimedia calls.");
        TestAssert.True(
            handler.Requests.All(static uri => uri.Query.Contains(
                "maxlag=5",
                StringComparison.OrdinalIgnoreCase)),
            "Background context retrieval retains Wikimedia's considerate maxlag policy.");
        TestAssert.True(
            handler.Requests[0].Query.Contains(
                "Q123",
                StringComparison.Ordinal),
            "Explicitly selected QID query.");
        TestAssert.Equal(
            "Example Quest",
            result.GameName,
            "Online game lookup must remove bidi and line-control syntax before acquisition.");
        string combined = string.Join(" ", handler.Requests);
        TestAssert.True(
            handler.Requests.Any(uri => Uri.UnescapeDataString(uri.Query)
                .Contains("Example Quest", StringComparison.Ordinal)),
            "Only the confirmed canonical title may be used for fixed related-article discovery.");
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
        GameKnowledgeSource primarySource = result.Sources.Single(
            static source =>
                source.Role == GameKnowledgeSourceRole.PrimaryArticle);
        TestAssert.Equal(
            "CC-BY-SA-4.0",
            primarySource.LicenseIdentifier,
            "Wikipedia license.");
        TestAssert.Equal("789", primarySource.RevisionId, "Revision.");
        TestAssert.True(
            primarySource.PageUri.Query.Contains("oldid=789"),
            "Exact source revision URL.");
        TestAssert.Equal(
            GameKnowledgeSourceRole.PrimaryArticle,
            primarySource.Role,
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
        TestAssert.True(
            result.Passages.Any(static passage =>
                passage.Section == "Developer" &&
                passage.Text.Contains("Example Studio", StringComparison.Ordinal)),
            "Allowlisted Wikidata developer claims are resolved to labels.");
        TestAssert.True(
            result.Passages.Any(static passage =>
                passage.Section == "Series" &&
                passage.Text.Contains("Example Series", StringComparison.Ordinal)),
            "Allowlisted Wikidata series claims are retained.");
        TestAssert.False(
            result.Passages.Any(static passage =>
                passage.Section == "Reception" ||
                passage.Text.Contains("perfect score", StringComparison.Ordinal)),
            "Non-allowlisted Wikipedia sections are not persisted.");
        TestAssert.False(
            string.Join(" ", handler.Requests).Contains("Q997", StringComparison.Ordinal),
            "Unallowlisted Wikidata claims are neither resolved nor persisted.");
    }

    private static async Task WikimediaRelatedArticlesAreBounded()
    {
        var handler = new RecordingHttpHandler(request =>
        {
            string query = request.RequestUri!.Query;
            string json;
            if (query.Contains("wbgetentities", StringComparison.Ordinal))
            {
                json = """
                  {"entities":{"Q123":{"lastrevid":456,"modified":"2026-01-01T03:04:05Z","labels":{"en":{"language":"en","value":"Example Quest"}},"descriptions":{"en":{"language":"en","value":"fictional video game"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"Example Quest"}},"claims":{}}}}
                  """;
            }
            else if (query.Contains("list=search", StringComparison.Ordinal))
            {
                json = query.Contains("characters", StringComparison.Ordinal)
                    ? "{\"query\":{\"search\":[{\"title\":\"Characters of Example Quest\"},{\"title\":\"Example Quest control characters\"}]}}"
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
            else if (query.Contains(
                         "Example%20Quest%20control%20characters",
                         StringComparison.Ordinal))
            {
                json = """
                  {"query":{"pages":[{"pageid":9,"title":"Example Quest control characters","fullurl":"https://en.wikipedia.org/wiki/Example_Quest_control_characters","extract":"Example Quest control characters are computer codes used for device signaling.\n\n== Characters ==\nControl characters affect terminal behavior.","revisions":[{"revid":10,"timestamp":"2026-01-03T03:04:05Z"}]}]}}
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
            new GameKnowledgeRefreshRequest(ConfirmedIdentity()),
            CancellationToken.None);

        TestAssert.Equal(3, result.Sources.Count, "Identity, primary, and related source.");
        TestAssert.False(
            result.Sources.Any(static source =>
                source.Title == "Example Quest control characters"),
            "A same-token computer article is not accepted as game context.");
        GameKnowledgeSource relatedSource = result.Sources.Single(
            static source =>
                source.Role == GameKnowledgeSourceRole.RelatedArticle);
        TestAssert.Equal(
            GameKnowledgeSourceRole.RelatedArticle,
            relatedSource.Role,
            "Related source role.");
        TestAssert.True(
            relatedSource.PageUri.Query.Contains("oldid=9"),
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
            string query = request.RequestUri!.Query;
            string json = query.Contains("wbgetentities", StringComparison.Ordinal)
                ? """
                  {"entities":{"Q123":{"lastrevid":456,"modified":"2026-01-01T03:04:05Z","labels":{"en":{"language":"en","value":"Example Quest"}},"descriptions":{"en":{"language":"en","value":"fictional video game"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"Example Quest"}},"claims":{}}}}
                  """
                : query.Contains("list=search", StringComparison.Ordinal)
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
            new GameKnowledgeRefreshRequest(ConfirmedIdentity()),
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

    private static async Task WikimediaComponentsRefreshIndependently()
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        var handler = new RecordingHttpHandler(request =>
        {
            string query = request.RequestUri!.Query;
            if (query.Contains("wbgetentities", StringComparison.Ordinal))
            {
                var throttled = new HttpResponseMessage(
                    HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{}"),
                };
                throttled.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(
                        TimeSpan.FromHours(30));
                return throttled;
            }
            string json = query.Contains("list=search", StringComparison.Ordinal)
                ? "{\"query\":{\"search\":[]}}"
                : """
                  {"query":{"pages":[{"pageid":123,"title":"Example Quest","fullurl":"https://en.wikipedia.org/wiki/Example_Quest","extract":"Example Quest is a video game.\n\n== Gameplay ==\nPlayers cross a city and complete missions.\n\n== Plot ==\nA courier begins a new journey through the city.","revisions":[{"revid":808,"timestamp":"2026-01-02T03:04:05Z"}]}]}}
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
            new GameKnowledgeRefreshRequest(
                ConfirmedIdentity(),
                CreateSnapshot(),
                forceRefresh: true),
            CancellationToken.None);

        GameKnowledgeComponentState wikidata = result.Components.Single(
            static value =>
                value.Kind == GameKnowledgeComponentKind.WikidataClaims);
        GameKnowledgeComponentState wikipedia = result.Components.Single(
            static value =>
                value.Kind == GameKnowledgeComponentKind.WikipediaPrimary);
        TestAssert.Equal(
            GameKnowledgeComponentCompleteness.TransientFailure,
            wikidata.Completeness,
            "Failed Wikidata component remains explicitly retryable.");
        TestAssert.True(
            wikidata.RetryAfterUtc >= startedAtUtc.AddHours(29),
            "A longer server Retry-After is preserved instead of truncated.");
        TestAssert.Equal(
            GameKnowledgeComponentCompleteness.Complete,
            wikipedia.Completeness,
            "Successful Wikipedia refresh remains independently complete.");
        TestAssert.Equal(
            "808",
            result.Sources.Single(static source =>
                source.Role == GameKnowledgeSourceRole.PrimaryArticle).RevisionId,
            "Successful Wikipedia revision replaces the cached component.");
        TestAssert.Equal(
            1,
            handler.Requests.Count(uri => uri.Query.Contains(
                "wbgetentities",
                StringComparison.Ordinal)),
            "A long Retry-After is persisted rather than immediately retried.");
        TestAssert.True(
            handler.UserAgents.All(static value =>
                value.Contains("ReplayFoundry/2.0", StringComparison.Ordinal) &&
                value.Contains("replayfoundry.com", StringComparison.Ordinal)),
            "Every Wikimedia request carries an identified contact User-Agent.");

        var inverseHandler = new RecordingHttpHandler(request =>
        {
            if (request.RequestUri!.Query.Contains(
                    "wbgetentities",
                    StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"entities":{"Q123":{"lastrevid":909,"modified":"2026-01-01T03:04:05Z","labels":{"en":{"language":"en","value":"Example Quest"}},"descriptions":{"en":{"language":"en","value":"fictional video game"}},"aliases":{"en":[]},"sitelinks":{"enwiki":{"site":"enwiki","title":"Example Quest"}},"claims":{}}}}
                        """,
                        Encoding.UTF8,
                        "application/json"),
                };
            }
            var throttled = new HttpResponseMessage(
                HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}"),
            };
            throttled.Headers.RetryAfter =
                new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromHours(30));
            return throttled;
        });
        using var inverseClient = new HttpClient(inverseHandler);
        var inverseProvider = new WikimediaGameKnowledgeProvider(
            inverseClient,
            "https://example.test/wikipedia-api",
            "https://example.test/wikidata-api");
        GameKnowledgeSnapshot inverse = await inverseProvider.AcquireAsync(
            new GameKnowledgeRefreshRequest(
                ConfirmedIdentity(),
                CreateSnapshot(),
                forceRefresh: true),
            CancellationToken.None);
        TestAssert.Equal(
            GameKnowledgeComponentCompleteness.Complete,
            inverse.Components.Single(static value =>
                value.Kind == GameKnowledgeComponentKind.WikidataClaims)
                .Completeness,
            "Successful Wikidata remains complete when Wikipedia refresh fails.");
        TestAssert.Equal(
            GameKnowledgeComponentCompleteness.TransientFailure,
            inverse.Components.Single(static value =>
                value.Kind == GameKnowledgeComponentKind.WikipediaPrimary)
                .Completeness,
            "Failed Wikipedia is explicit and cannot be hidden by Wikidata success.");
        TestAssert.Equal(
            "42",
            inverse.Sources.Single(static source =>
                source.Role == GameKnowledgeSourceRole.PrimaryArticle).RevisionId,
            "The last validated Wikipedia component remains available while retrying.");
        TestAssert.True(
            inverse.Sources.Any(static source =>
                source.Kind == GameKnowledgeSourceKind.Wikidata &&
                source.RevisionId == "909"),
            "The successful Wikidata revision is merged with retained Wikipedia.");

        DateTimeOffset defaultRetryStartedAtUtc = DateTimeOffset.UtcNow;
        var defaultRetryHandler = new RecordingHttpHandler(request =>
        {
            string query = request.RequestUri!.Query;
            if (query.Contains("wbgetentities", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(
                    HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{}"),
                };
            }
            string json = query.Contains("list=search", StringComparison.Ordinal)
                ? "{\"query\":{\"search\":[]}}"
                : """
                  {"query":{"pages":[{"pageid":123,"title":"Example Quest","fullurl":"https://en.wikipedia.org/wiki/Example_Quest","extract":"Example Quest is a video game.\n\n== Gameplay ==\nPlayers cross a city and complete missions.","revisions":[{"revid":810,"timestamp":"2026-01-02T03:04:05Z"}]}]}}
                  """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });
        using var defaultRetryClient = new HttpClient(defaultRetryHandler);
        var defaultRetryProvider = new WikimediaGameKnowledgeProvider(
            defaultRetryClient,
            "https://example.test/wikipedia-api",
            "https://example.test/wikidata-api");
        GameKnowledgeSnapshot defaultRetry =
            await defaultRetryProvider.AcquireAsync(
                new GameKnowledgeRefreshRequest(
                    ConfirmedIdentity(),
                    CreateSnapshot(),
                    forceRefresh: true),
                CancellationToken.None);
        DateTimeOffset defaultRetryAt = defaultRetry.Components.Single(
            static value =>
                value.Kind == GameKnowledgeComponentKind.WikidataClaims)
            .RetryAfterUtc!.Value;
        TestAssert.True(
            defaultRetryAt >= defaultRetryStartedAtUtc.AddHours(23) &&
            defaultRetryAt <= DateTimeOffset.UtcNow.AddHours(25),
            "A transient Wikimedia component without server guidance receives the bounded 24-hour retry schedule.");
    }

    private static Task StrategyWikiIsDisabled()
    {
        GameKnowledgeCapabilityProbe capability =
            StrategyWikiGameKnowledgeCapability.Probe();
        TestAssert.False(capability.Enabled, "StrategyWiki must remain disabled.");
        TestAssert.False(
            capability.NetworkAccessPermitted,
            "The disabled probe cannot authorize scraping or network access.");
        TestAssert.True(
            capability.Reason.Contains("will not scrape", StringComparison.Ordinal),
            "The capability records the no-scraping policy.");
        return Task.CompletedTask;
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

    private static async Task LocalOnlyMakesZeroWikimediaRequests()
    {
        var handler = new RecordingHttpHandler(_ =>
            throw new InvalidOperationException(
                "A local-only context must not reach Wikimedia."));
        using var client = new HttpClient(handler);
        var provider = new WikimediaGameKnowledgeProvider(
            client,
            "https://example.test/wikipedia-api",
            "https://example.test/wikidata-api");
        var service = new GenerationGameKnowledgeService(
            provider,
            new MemoryStore());

        ClipEditorialContext context = CreateEditorialContext(
            "The clip remains entirely local.",
            useOpenKnowledge: false);
        ClipEditorialContext result = await service.EnrichAsync(
            context,
            CancellationToken.None);

        TestAssert.Same(context, result, "Local-only context remains unchanged.");
        TestAssert.Equal(0, handler.Requests.Count, "Zero Wikimedia HTTP requests.");
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

    private static async Task AnalysisDepthControlsNarrativeKnowledge()
    {
        async Task<(GenerationEditorialMetadataResult Result, int Calls)> Run(
            GenerationAnalysisDepth depth)
        {
            GenerationMomentFindingRequest baseline =
                GenerationMomentFindingTests.CreateRequest(
                    sourceCount: 1,
                    desiredCount: 1,
                    analysisDepth: depth);
            GenerationSetupOptions original = baseline.Setup;
            string sourcePath = baseline.Sources[0]
                .PreparedSource.Media.FullPath;
            var gameSettings = new GenerationGameContextSettings(
            [
                new GenerationSourceGameContext(
                    sourcePath,
                    "Example Quest",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed,
                    useOpenGameKnowledge: true,
                    ConfirmedIdentity()),
            ]);
            var setup = new GenerationSetupOptions(
                original.Mode,
                original.DetectionMethod,
                original.AudioSelectionMode,
                original.DesiredResultCount,
                original.QualityThreshold,
                original.ContentEmphasis,
                original.ClipFulfillmentPreference,
                original.MomentGuidance,
                original.CaptionSettings,
                original.ResultCountMode,
                depth,
                gameSettings,
                original.MaximumClipDuration);
            GenerationMomentFindingResult moments =
                new GenerationMomentFindingService(
                    new GenerationMomentFindingTests.RecordingMomentFinder(
                        [[90]]))
                .Find(new GenerationMomentFindingRequest(
                    baseline.EvidenceAnalysis,
                    setup));
            var knowledge = new RecordingNarrativeKnowledgeService();
            var service = new GenerationEditorialMetadataService(
                new PassThroughMetadataService(),
                new ClipEditorialProfileSession(),
                knowledge);
            GenerationEditorialMetadataResult result =
                await service.GenerateAsync(
                    moments,
                    captions: null,
                    CancellationToken.None);
            return (result, knowledge.EnrichCalls);
        }

        (GenerationEditorialMetadataResult quick, int quickCalls) =
            await Run(GenerationAnalysisDepth.Fast);
        ClipEditorialContext quickContext = quick.Candidates.Single().Context;
        TestAssert.Equal(
            1,
            quickCalls,
            "Fast scanning must still ground AI-authored metadata when the user enabled public game context.");
        TestAssert.Equal(
            "Q123",
            quickContext.GameContext.ConfirmedIdentity!.WikidataEntityId,
            "Quick still retains canonical spelling and identity.");
        TestAssert.True(
            quickContext.GameKnowledge?.Snapshot is not null,
            "AI authorship receives bounded narrative context independently of scan cadence.");

        (GenerationEditorialMetadataResult thorough, int thoroughCalls) =
            await Run(GenerationAnalysisDepth.Thorough);
        TestAssert.Equal(1, thoroughCalls, "Thorough performs one context lookup.");
        TestAssert.True(
            thorough.Candidates.Single().Context.GameKnowledge?.Snapshot is not null,
            "Thorough receives bounded narrative knowledge.");
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

    private static async Task ServiceRespectsComponentRetrySchedule()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        GameKnowledgeSnapshot current = CreateSnapshot(
            retrievedAtUtc: nowUtc);
        var notDue = new GameKnowledgeSnapshot(
            current.ConfirmedIdentity!,
            current.Provider,
            nowUtc,
            current.Sources,
            current.Passages,
            [
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.WikipediaPrimary,
                    GameKnowledgeComponentCompleteness.TransientFailure,
                    nowUtc.AddHours(-1),
                    nowUtc.AddHours(23),
                    current.Sources[0].RevisionId,
                    current.Sources[0].LicenseIdentifier,
                    current.Sources[0].Attribution,
                    current.Sources[0].ContentSha256),
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.StrategyWiki,
                    GameKnowledgeComponentCompleteness.Disabled,
                    nowUtc),
            ]);
        var retainedStore = new MemoryStore();
        retainedStore.Remember(
            notDue,
            WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);
        var retainedProvider = new FakeProvider(current);
        var retainedService = new GenerationGameKnowledgeService(
            retainedProvider,
            retainedStore);

        await retainedService.EnrichAsync(
            CreateEditorialContext(
                "The clinic contains a masked stranger.",
                useOpenKnowledge: true),
            CancellationToken.None);
        TestAssert.Equal(
            0,
            retainedProvider.Calls,
            "A transient component must not be retried before its persisted 24-hour schedule.");

        var due = new GameKnowledgeSnapshot(
            current.ConfirmedIdentity!,
            current.Provider,
            nowUtc,
            current.Sources,
            current.Passages,
            [
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.WikipediaPrimary,
                    GameKnowledgeComponentCompleteness.TransientFailure,
                    nowUtc.AddHours(-25),
                    nowUtc.AddHours(-1),
                    current.Sources[0].RevisionId,
                    current.Sources[0].LicenseIdentifier,
                    current.Sources[0].Attribution,
                    current.Sources[0].ContentSha256),
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.StrategyWiki,
                    GameKnowledgeComponentCompleteness.Disabled,
                    nowUtc),
            ]);
        var dueStore = new MemoryStore();
        dueStore.Remember(
            due,
            WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);
        var dueProvider = new FakeProvider(current);
        var dueService = new GenerationGameKnowledgeService(
            dueProvider,
            dueStore);

        await dueService.EnrichAsync(
            CreateEditorialContext(
                "The clinic contains a masked stranger.",
                useOpenKnowledge: true),
            CancellationToken.None);
        TestAssert.Equal(
            1,
            dueProvider.Calls,
            "A component is refreshed once its persisted retry time is due.");
    }

    private static Task ContextReceiptIsBoundedAndRemovable()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        GameKnowledgeSnapshot snapshot = CreateReceiptSnapshot(nowUtc);
        ClipEditorialContext context = CreateEditorialContext(
            "The clinic contains a masked stranger.",
            useOpenKnowledge: true);
        context = context.WithGameKnowledge(
            new DeterministicGameKnowledgeRetriever().Retrieve(
                snapshot,
                context));
        var store = new MemoryStore();
        store.Remember(
            snapshot,
            WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);
        var service = new GenerationGameKnowledgeService(
            new FakeProvider(snapshot),
            store);

        GameKnowledgeContextReceipt receipt = service.Inspect(context);
        TestAssert.Equal("Q123", receipt.WikidataEntityId, "Receipt QID.");
        TestAssert.Equal(
            GameKnowledgeContextFreshness.Fresh,
            receipt.Freshness,
            "Current component receipt freshness.");
        TestAssert.True(
            receipt.Sources.Any(source =>
                source.Kind == GameKnowledgeSourceKind.Wikidata &&
                source.RevisionId == "314" &&
                source.Attribution.Contains(
                    "Wikidata contributors",
                    StringComparison.Ordinal)),
            "The presentation receipt retains source revision and attribution.");
        TestAssert.True(
            receipt.SupportedClaims.Any(claim =>
                claim.Label == "Developer" &&
                claim.Value.Contains(
                    "Example Studio",
                    StringComparison.Ordinal)),
            "The presentation receipt exposes selected allowlisted claims.");
        TestAssert.True(
            receipt.SupportedClaims.Count <=
                GameKnowledgeContextReceipt.MaximumDisplayedClaims,
            "The presentation receipt remains bounded.");

        ClipEditorialContext removed = service.RemoveCachedContext(context);
        TestAssert.True(
            removed.GameKnowledge is null && !store.HasSnapshot,
            "One remove operation clears both the retained clip projection and its cache without touching local clip evidence.");
        return Task.CompletedTask;
    }

    private static async Task ServiceRefreshesProviderVersion()
    {
        var store = new MemoryStore();
        store.Remember(
            CreateSnapshot("Test knowledge", "1.0"),
            WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);
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

    private static async Task ServiceUsesBoundedOfflineFallback()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        var retainedStore = new MemoryStore();
        retainedStore.Remember(
            CreateSnapshot(
                "Fake knowledge",
                "1.0",
                nowUtc.AddDays(-100)),
            WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);
        var retainedService = new GenerationGameKnowledgeService(
            new FakeProvider(new HttpRequestException("offline")),
            retainedStore);
        ClipEditorialContext retained = await retainedService.EnrichAsync(
            CreateEditorialContext(
                "The clinic contains a masked stranger.",
                useOpenKnowledge: true),
            CancellationToken.None);
        TestAssert.True(
            retained.GameKnowledge?.Snapshot is not null,
            "A validated 100-day snapshot remains available offline.");

        var expiredStore = new MemoryStore();
        expiredStore.Remember(
            CreateSnapshot(
                "Fake knowledge",
                "1.0",
                nowUtc.AddDays(-181)),
            WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);
        var expiredService = new GenerationGameKnowledgeService(
            new FakeProvider(new HttpRequestException("offline")),
            expiredStore);
        ClipEditorialContext expired = await expiredService.EnrichAsync(
            CreateEditorialContext(
                "The clinic contains a masked stranger.",
                useOpenKnowledge: true),
            CancellationToken.None);
        TestAssert.True(
            expired.GameKnowledge is { Snapshot: null } expiredKnowledge &&
            expiredKnowledge.Warnings.Single().Code ==
                GameKnowledgeWarningCode.Unavailable,
            "A snapshot older than 180 days is not used as offline context.");
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
            generationPassCount: 1,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: false,
            rejectedValidationRules: [],
            editorialRephraseSupported: true,
            editorialRephraseAttempted: false,
            editorialRephraseEligibilitySkipSupported: true,
            editorialRephraseEligibilitySkipped: true);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator
                .ValidateGenerationPassProvenance(
                    generationPassCount: 1,
                    visualDraftCount: 1,
                    visualEventSelectionApplied: false,
                    knowledgeSelectionApplied: false,
                    groundingReviewApplied: false,
                    rejectedValidationRules: [],
                    editorialRephraseSupported: true,
                    editorialRephraseAttempted: false,
                    editorialRephraseEligibilitySkipSupported: false,
                    editorialRephraseEligibilitySkipped: true),
            "Only the current schema may omit an ineligible rephrase model pass.");
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
        Qwen3VlGroundedMetadataGenerator.ValidateGenerationPassProvenance(
            generationPassCount: 7,
            visualDraftCount: 1,
            visualEventSelectionApplied: false,
            knowledgeSelectionApplied: false,
            groundingReviewApplied: true,
            rejectedValidationRules: recoveryPoolRejectedRules,
            groundingPassCount: 1,
            synthesisPassCount: 7,
            groundingPacketReused: true,
            duplicateSynthesisRecoveryApplied: true,
            duplicateSynthesisRecoverySourcePassOrdinal: 2,
            duplicateSynthesisRecoveryRepeatedPassOrdinal: 3,
            duplicateSynthesisRecoverySourceRejectedJsonSha256:
                duplicateSha256,
            duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                duplicateSha256,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 1,
            synthesisRecoveryPoolSourceRejectedJsonSha256: duplicateSha256,
            synthesisRecoveryPoolAttemptedCandidateCount: 4,
            synthesisRecoveryPoolSelectedCandidateOrdinal: 3);
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

        ValidateModuleRoster(
            modules,
            recoveryCandidateSelectionSupported: true,
            synthesisSanitizationSupported: true);
        Qwen3VlGroundedMetadataModuleIdentity[] previousResponsibilityModules =
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy.PreviousResponsibilitySplitGroundedMetadataModules
                .Select(module => new Qwen3VlGroundedMetadataModuleIdentity(module.ModuleName, module.FileName, hashA)).ToArray();
        ValidateModuleRoster(previousResponsibilityModules, true, true, editorialResponsibilityModulesSupported: false);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => ValidateModuleRoster(previousResponsibilityModules, true, true),
            "Current ordinary synthesis must attest the extracted validation modules.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => ValidateModuleRoster(modules, true, true, editorialResponsibilityModulesSupported: false),
            "Schemas 1.59 and 1.60 cannot be relabeled with the later module roster.");
        Qwen3VlGroundedMetadataModuleIdentity[] previousCandidateModules =
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousRecoveryCandidateSelectionGroundedMetadataModules
                .Select(module => new Qwen3VlGroundedMetadataModuleIdentity(
                    module.ModuleName,
                    module.FileName,
                    hashA))
                .ToArray();
        ValidateModuleRoster(
            previousCandidateModules,
            recoveryCandidateSelectionSupported: false,
            synthesisSanitizationSupported: true);
        Qwen3VlGroundedMetadataModuleIdentity[] historicalModules =
            Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                .PreviousSynthesisSanitizationGroundedMetadataModules
                .Select(module => new Qwen3VlGroundedMetadataModuleIdentity(
                    module.ModuleName,
                    module.FileName,
                    hashA))
                .ToArray();
        ValidateModuleRoster(
            historicalModules,
            recoveryCandidateSelectionSupported: false,
            synthesisSanitizationSupported: false);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => ValidateModuleRoster(
                historicalModules,
                recoveryCandidateSelectionSupported: false,
                synthesisSanitizationSupported: true),
            "Current output 1.53 requires the signed synthesis sanitizer module.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => ValidateModuleRoster(
                previousCandidateModules,
                recoveryCandidateSelectionSupported: true,
                synthesisSanitizationSupported: true),
            "Current output 1.54 requires the signed candidate-selection module.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => ValidateModuleRoster(
                modules,
                recoveryCandidateSelectionSupported: false,
                synthesisSanitizationSupported: false),
            "Historical output 1.52 must retain its exact module roster.");

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

        Qwen3VlGroundedMetadataSynthesisPassAttestation[]
            earlierSelectedAttestations = attestations.ToArray();
        earlierSelectedAttestations[5] = earlierSelectedAttestations[5] with
        {
            RejectionCode = null,
            Accepted = true,
        };
        earlierSelectedAttestations[6] = earlierSelectedAttestations[6] with
        {
            RejectionCode = rejectedRules[5],
            Accepted = false,
        };
        Qwen3VlGroundedMetadataSelection.ValidateSynthesisRecoveryPoolProvenance(
            synthesisPassCount: 7,
            rejectedValidationRules: rejectedRules,
            decodedTextSha256: hashF,
            synthesisRecoveryPoolApplied: true,
            synthesisRecoveryPoolSourcePassOrdinal: 1,
            synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
            synthesisRecoveryPoolAttemptedCandidateCount: 4,
            synthesisRecoveryPoolSelectedCandidateOrdinal: 3,
            moduleIdentities: modules,
            attestations: earlierSelectedAttestations,
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
            "The selected ordinal must match the accepted attestation and output hash.");

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

        void ValidateModuleRoster(
            IReadOnlyList<Qwen3VlGroundedMetadataModuleIdentity> actualModules,
            bool recoveryCandidateSelectionSupported,
            bool synthesisSanitizationSupported,
            bool editorialResponsibilityModulesSupported = true) =>
            Qwen3VlGroundedMetadataSelection
                .ValidateSynthesisRecoveryPoolProvenance(
                    synthesisPassCount: 7,
                    rejectedValidationRules: rejectedRules,
                    decodedTextSha256: finalHash,
                    synthesisRecoveryPoolApplied: true,
                    synthesisRecoveryPoolSourcePassOrdinal: 1,
                    synthesisRecoveryPoolSourceRejectedJsonSha256: hashA,
                    synthesisRecoveryPoolAttemptedCandidateCount: 4,
                    synthesisRecoveryPoolSelectedCandidateOrdinal: 4,
                    moduleIdentities: actualModules,
                    attestations,
                    duplicateSynthesisRecoveryApplied: true,
                    duplicateSynthesisRecoverySourceRejectedJsonSha256: hashB,
                    duplicateSynthesisRecoveryRepeatedRejectedJsonSha256:
                        hashB,
                    nonRetrospectiveRetryAnchorApplied: true,
                    nonRetrospectiveRetryAnchorSourcePassOrdinal: 1,
                    nonRetrospectiveRetryAnchorEnvelopeSha256: hashE,
                    nonRetrospectiveRetryAnchorAuthoritySha256: hashF,
                    retryableSemanticRejections:
                        Qwen3VlGroundedMetadataSynthesisRecoveryPoolPolicy
                            .RetryableSemanticRejectionSet,
                    recoveryCandidateSelectionModuleSupported:
                        recoveryCandidateSelectionSupported,
                    synthesisSanitizationModuleSupported:
                        synthesisSanitizationSupported,
                    editorialResponsibilityModulesSupported: editorialResponsibilityModulesSupported);
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

        Qwen3VlGroundedMetadataVisualEventAssessment[] tiedAssessments =
        [
            assessments[0],
            assessments[0] with { Ordinal = 2 },
        ];
        Qwen3VlGroundedMetadataVisualDraft[] qualityDrafts =
        [
            new(
                1,
                0,
                5,
                "Interior",
                false,
                ["A doorway"],
                ["A figure crossed the doorway."],
                [],
                []),
            new(
                2,
                5,
                10,
                "Interior",
                false,
                ["A doorway"],
                ["A figure stood beside a枪,"],
                [],
                []),
        ];
        Qwen3VlGroundedMetadataVisualEventSelectionOutcome qualitySelected =
            Qwen3VlGroundedMetadataSelection.SelectPrimaryVisualDraft(
                tiedAssessments,
                qualityDrafts);
        TestAssert.Equal(
            1,
            qualitySelected.PrimaryVisualDraftOrdinal,
            "A later truncated or non-Latin action must not win an otherwise " +
            "equal primary-selection tie.");

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

        Qwen3VlGroundedMetadataVisualEventAssessment[] groundedAssessments =
        [
            assessments[1] with { Ordinal = 1, Uncertain = true },
            assessments[1] with { Ordinal = 2 },
            assessments[1] with { Ordinal = 3, RoutineOnly = true },
            assessments[1] with { Ordinal = 4 },
        ];
        Qwen3VlGroundedMetadataVisualDraft[] groundedDrafts =
        [
            new(1, 0, 5, "Exterior", true, ["A wall"],
                ["A figure walked beside a wall."], [], ["The setting is unclear."]),
            new(2, 5, 10, "Corridor", false, ["A corridor"],
                ["A figure walked toward the"], [], []),
            new(3, 10, 15, "Corridor", false, ["A corridor"],
                ["A figure continued along the corridor."], [], []),
            new(4, 15, 20, "Corridor", false, ["A doorway"],
                ["A figure turned beside a doorway."], [], []),
        ];
        Qwen3VlGroundedMetadataVisualEventSelectionOutcome grounded =
            Qwen3VlGroundedMetadataSelection.SelectPrimaryVisualDraft(
                groundedAssessments,
                groundedDrafts,
                allowGroundedPrimaryWithoutDistinctSupport: true);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataVisualEventSelectionOutcomeCode
                .SelectedGroundedPrimaryEvent,
            grounded.Code,
            "Current recovery must be typed as grounded evidence, not a " +
            "distinct event.");
        TestAssert.Equal(
            4,
            grounded.PrimaryVisualDraftOrdinal,
            "Recovery should reject uncertain, malformed, and routine-only " +
            "alternatives before the final ordinal tie-break.");
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
        string providerVersion = "1.0",
        DateTimeOffset? retrievedAtUtc = null)
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
        DateTimeOffset retrieved = retrievedAtUtc ?? DateTimeOffset.UtcNow;
        return new GameKnowledgeSnapshot(
            ConfirmedIdentity(),
            new GameKnowledgeProviderIdentity(
                providerName,
                providerVersion),
            retrieved,
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
            ],
            [
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.WikipediaPrimary,
                    GameKnowledgeComponentCompleteness.Complete,
                    retrieved,
                    revisionId: source.RevisionId,
                    licenseIdentifier: source.LicenseIdentifier,
                    attribution: source.Attribution,
                    contentSha256: source.ContentSha256),
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.StrategyWiki,
                    GameKnowledgeComponentCompleteness.Disabled,
                    retrieved),
            ]);
    }

    private static GameKnowledgeSnapshot CreateReceiptSnapshot(
        DateTimeOffset retrievedAtUtc)
    {
        const string sourceId = "gks-wikidata-receipt";
        const string identity =
            "Example Quest is a 2024 video game developed by Example Studio.";
        const string developer = "Developer: Example Studio.";
        string retained = identity + "\n" + developer;
        var source = new GameKnowledgeSource(
            sourceId,
            GameKnowledgeSourceKind.Wikidata,
            "Example Quest",
            new Uri("https://www.wikidata.org/wiki/Q123?oldid=314"),
            "314",
            DateTimeOffset.UnixEpoch,
            "CC0-1.0",
            new Uri(
                "https://creativecommons.org/publicdomain/zero/1.0/"),
            "Wikidata contributors, Q123, revision 314.",
            GameKnowledgePassage.ComputeSha256(retained),
            GameKnowledgeSourceRole.StructuredIdentity);
        return new GameKnowledgeSnapshot(
            ConfirmedIdentity(),
            new GameKnowledgeProviderIdentity("Test knowledge", "1.0"),
            retrievedAtUtc,
            [source],
            [
                new GameKnowledgePassage(
                    "gkp-receipt-identity",
                    sourceId,
                    "Identity",
                    identity,
                    GameKnowledgePassage.ComputeSha256(identity)),
                new GameKnowledgePassage(
                    "gkp-receipt-developer",
                    sourceId,
                    "Developer",
                    developer,
                    GameKnowledgePassage.ComputeSha256(developer)),
            ],
            [
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.WikidataIdentity,
                    GameKnowledgeComponentCompleteness.Complete,
                    retrievedAtUtc,
                    revisionId: "314",
                    licenseIdentifier: "CC0-1.0",
                    attribution: source.Attribution,
                    contentSha256:
                        GameKnowledgePassage.ComputeSha256(identity)),
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.WikidataClaims,
                    GameKnowledgeComponentCompleteness.Complete,
                    retrievedAtUtc,
                    revisionId: "314",
                    licenseIdentifier: "CC0-1.0",
                    attribution: source.Attribution,
                    contentSha256:
                        GameKnowledgePassage.ComputeSha256(developer)),
                new GameKnowledgeComponentState(
                    GameKnowledgeComponentKind.StrategyWiki,
                    GameKnowledgeComponentCompleteness.Disabled,
                    retrievedAtUtc),
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
                useOpenKnowledge,
                ConfirmedIdentity()));
    }

    private static ConfirmedGameIdentity ConfirmedIdentity(
        string title = "Example Quest") =>
        new(
            "Q123",
            title,
            edition: null,
            releaseYear: 2024,
            developer: "Example Studio",
            series: "Example Quest",
            GameIdentityAuthority.Wikidata,
            "Wikidata",
            "en",
            userConfirmed: true,
            DateTimeOffset.UnixEpoch,
            GameKnowledgeSourcePermissions.WikimediaDefault);

    private static GameKnowledgeCacheKey CacheKey(
        GameKnowledgeSnapshot snapshot) =>
        new(
            snapshot.ConfirmedIdentity!,
            snapshot.Provider,
            GameKnowledgeSnapshot.SchemaVersion,
            WikimediaGameKnowledgeProvider.RetrievalPolicyVersion);

    private sealed class PassThroughMetadataService :
        IClipEditorialMetadataGenerationService
    {
        private static readonly ClipEditorialMetadataGeneratorIdentity
            Generator = new("Depth boundary fixture", "1.0");
        private static readonly ClipEditorialAiProvenance AiProvenance = new(
            Generator.Name,
            Generator.Version,
            "test-runtime-1.0",
            "replayfoundry/depth-boundary-fixture",
            "test-revision",
            new string('a', 64),
            "depth-boundary-prompt",
            "1.0",
            new string('b', 64),
            TimeSpan.Zero,
            peakAllocatedGpuBytes: null);

        public bool IsAiAvailable => true;

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool useAi = request.Preference !=
                ClipEditorialGenerationPreference.HeuristicOnly;
            return Task.FromResult(new ClipEditorialMetadataDraft(
                $"Depth boundary {request.Context.GameContext.GameHashtag}",
                "A deterministic metadata fixture preserves the supplied context.",
                [request.Context.GameContext.GameName],
                useAi
                    ? ClipEditorialMetadataOrigin.AiAssisted
                    : ClipEditorialMetadataOrigin.Heuristic,
                Generator,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: useAi ? AiProvenance : null,
                readiness: useAi
                    ? ClipEditorialMetadataReadiness.GroundedDraft
                    : ClipEditorialMetadataReadiness.WorkingLabel));
        }
    }

    private sealed class RecordingNarrativeKnowledgeService :
        IGenerationGameKnowledgeService
    {
        public int EnrichCalls { get; private set; }

        public Task<ClipEditorialContext> EnrichAsync(
            ClipEditorialContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnrichCalls++;
            return Task.FromResult(context.WithGameKnowledge(
                new ClipGameKnowledgeContext(
                    context.GameContext.GameName,
                    CreateSnapshot())));
        }

        public Task<ClipEditorialContext> RefreshAsync(
            ClipEditorialContext context,
            CancellationToken cancellationToken) =>
            EnrichAsync(context, cancellationToken);

        public GameKnowledgeContextReceipt Inspect(
            ClipEditorialContext context) =>
            new(
                context.GameContext.GameName,
                context.GameContext.ConfirmedIdentity?.WikidataEntityId,
                GameKnowledgeContextFreshness.NotCached,
                retrievedAtUtc: null,
                nextRefreshAtUtc: null,
                canRefresh: false,
                canRemove: false,
                sources: [],
                claims: [],
                components: []);

        public ClipEditorialContext RemoveCachedContext(
            ClipEditorialContext context) =>
            context.WithGameKnowledge(gameKnowledge: null);

        public void RemoveCachedContext(ConfirmedGameIdentity identity)
        {
        }
    }

    private sealed class RecordingHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeCandidateProvider(GameIdentityCandidate candidate) :
        IGameIdentityCandidateProvider
    {
        public GameIdentityCandidate Candidate { get; set; } = candidate;

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public Task<GameIdentityCandidateSet> DiscoverAsync(
            GameIdentityDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<GameIdentityCandidateSet>(Failure);
            }
            return Task.FromResult(new GameIdentityCandidateSet(
                request,
                [Candidate]));
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

        public GameKnowledgeRefreshRequest? LastRequest { get; private set; }

        public GameKnowledgeProviderIdentity Identity =>
            _snapshot?.Provider ?? new("Fake knowledge", "1.0");

        public Task<GameKnowledgeSnapshot> AcquireAsync(
            GameKnowledgeRefreshRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            return _exception is null
                ? Task.FromResult(_snapshot!)
                : Task.FromException<GameKnowledgeSnapshot>(_exception);
        }
    }

    private sealed class MemoryStore : IGameKnowledgeSnapshotStore
    {
        private GameKnowledgeSnapshot? _snapshot;

        public bool HasSnapshot => _snapshot is not null;

        public GameKnowledgeSnapshot? Find(GameKnowledgeCacheKey key) =>
            _snapshot is not null &&
                _snapshot.ConfirmedIdentity?.WikidataEntityId.Equals(
                    key.Identity.WikidataEntityId,
                    StringComparison.Ordinal) == true
                    ? _snapshot
                    : null;

        public void Remember(
            GameKnowledgeSnapshot snapshot,
            string policyVersion) =>
            _snapshot = snapshot;

        public void Remove(GameKnowledgeCacheKey key) => _snapshot = null;
    }
}
