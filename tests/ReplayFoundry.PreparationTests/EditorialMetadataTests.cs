using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Editorial.GameKnowledge;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.GameContext;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Composition;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.GameKnowledge;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Studio typing updates only draft-dependent surfaces", StudioTypingKeepsSavedSurfacesStable),
        new("Editorial profile snapshots reusable tags", ProfileIsImmutable),
        new("Editorial copy objectives remain typed and independent of custom guidance", CopyObjectiveIsTypedAndIndependentOfGuidance),
        new("Editorial copy objectives survive session and request clones", CopyObjectiveSurvivesSessionAndRequestClones),
        new("Studio rerolls preserve the typed editorial copy objective", StudioRerollPreservesTypedCopyObjective),
        new("Grounded Qwen serializes the typed editorial copy objective", GroundedQwenSerializesTypedCopyObjective),
        new("Balance review retains its source code and user-facing explanation", BalanceReviewRetainsItsTypedSourceAndExplanation),
        new("Provider balance findings cross the production metadata-review parser", BalanceReviewCrossesProviderParser),
        new("Balanced schema audits preserve current and historical contracts", BalancedSchemaAuditPreservesHistoricalContracts),
        new("Isolated audience fields bind both component witnesses to the mechanical merge", IsolatedFieldsBindBothComponentsToTheirMechanicalMerge),
        new("Isolated audience fields reject witness and merge tampering", IsolatedFieldsRejectWitnessAndMergeTampering),
        new("Isolated authoring counts only actual component and grounding passes", IsolatedFieldsCountOnlyActualComponentPasses),
        new("Actual isolated generation retains a verified rephrase non-attempt", ActualIsolatedGenerationRetainsVerifiedRephraseNonAttempt),
        new("Studio shows actionable copy review without blocking edits or queue readiness", StudioCopyReviewIsVisibleAndNonBlocking),
        new("Studio explains safe local AI startup failures at the rewrite action", StudioShowsSafeAiUnavailableReason),
        new("Balance review alone retains AI copy without repeating inference", BalanceReviewAloneDoesNotRepeatProviderInference),
        new("Editorial variant intent never treats unreviewed ASR as commentary authority", VariantIntentRequiresReviewedTranscript),
        new("AI rerolls preserve four distinct provider-authored metadata packages", AiRerollsKeepDistinctProviderPackages),
        new("Historical Qwen metadata retains its exact prompt identity", HistoricalQwenPromptIdentityIsPreserved),
        new("A stale grounded prompt reports safe AI readiness without blocking startup", StaleGroundedPromptFailsSoft),
        new("Grounded metadata uses creator and game transcript context", UsesBothTranscriptRoles),
        new("Manual game notes retain provider-neutral draft provenance", ManualGameNotesRetainDraftProvenance),
        new("Heuristic metadata never promotes raw automatic transcript wording", RawTranscriptIsNotAudienceMetadata),
        new("Heuristic metadata rerolls are deterministic and distinct", RerollsAreDeterministic),
        new("Heuristic metadata uses safe per-clip visual evidence", HeuristicMetadataUsesPerClipEvidence),
        new("Grounded document fallbacks vary without generic story framing", GroundedDocumentFallbackVaries),
        new("Objective instructions stay objective-grounded", ObjectiveInstructionsStayGrounded),
        new("Attempt-zero heuristic batches use candidate-scoped presentation styles", AttemptZeroHeuristicBatchDoesNotCollapse),
        new("Transcript-free metadata records limited grounding", MissingTranscriptWarns),
        new("Optional AI metadata requires an available provider", OptionalAiRequiresProvider),
        new("Explicit heuristic metadata preserves the generator draft", HeuristicOnlyPreservesDraftState),
        new("Optional AI metadata provider failures propagate", OptionalAiFailurePropagates),
        new("Optional AI metadata batch failures propagate without hidden reruns", OptionalAiBatchFailurePropagatesOnce),
        new("Grounded metadata rejects a batch with one typed case failure", FailSoftBatchRejectsFailedCase),
        new("AI batches retry only abstract or colliding titles", AiBatchRetriesOnlyNoveltyFailures),
        new("Neural wording overrides phrase rules while exact duplicates still retry", NeuralWordingKeepsDuplicateProtection),
        new("Editorial retry diagnostics match submitted cases and attempts", RetryDiagnosticsMatchProviderCalls),
        new("Editorial retry captures restore scopes and cannot change generation", RetryDiagnosticCapturesAreScoped),
        new("Grounded executor pass timing is opt-in and preserves provider failures", GroundedExecutorPassDiagnosticsAreOptIn),
        new("Grounding reuse requires identical verified prior packet witnesses", PacketReceiptsRequireVerifiedIdenticalPriorFacts),
        new("Grounding handoff promotes only bounded parsed packets", PacketHandoffPromotesOnlyParsedBoundedPackets),
        new("Grounding handoff cleans child files after parse failure and cancellation", GroundedExecutorCleansHandoffAfterParseFailureAndCancellation),
        new("Editorial automatic retries retain only operation-scoped provider state", EditorialRetrySessionsAreOperationScoped),
        new("Grounded sessions reject concurrent use and clean cancelled workspaces", GroundedSessionRejectsConcurrentUseAndCleansCancellation),
        new("Reconciled reroll provenance alone does not repeat inference", ReconciledProvenanceDoesNotTriggerRetry),
        new("Reconciled provenance does not suppress substantive retries", ReconciledProvenancePreservesRealRetries),
        new("AI batches retry literal and redundant metadata packages", AiBatchRetriesCurrentMetadataRegressions),
        new("Single AI requests use the shared audience-copy review", SingleAiRequestsUseSharedQualityReview),
        new("Description-only AI retries may retain their grounded title", DescriptionOnlyRetryRetainsTitle),
        new("AI rewrite exhaustion retains the best grounded draft", AiBatchRetryExhaustionRetainsBestGroundedDraft),
        new("AI rewrite exhaustion rejects a later incomplete title", AiBatchRetryExhaustionRejectsLaterIncompleteTitle),
        new("AI retention prefers a complete draft over an unflagged unfinished modifier", IncompleteModifierCannotOutrankCompleteDraft),
        new("AI rewrite exhaustion retains readable-text review findings", AiBatchRetryExhaustionRetainsReadableTextReview),
        new("AI rewrite exhaustion retains incomplete-title review findings", AiBatchRetryExhaustionRetainsIncompleteTitleReview),
        new("AI rewrite exhaustion ranks typed review risk", AiBatchRetryExhaustionRanksTypedReviewRisk),
        new("AI corrective rewrite failure retains the prior AI draft", AiCorrectiveRewriteFailureRetainsPriorDraft),
        new("Retired generic AI filler remains an advisory retry", RetiredGenericAiFillerRemainsAdvisory),
        new("AI rewrite exhaustion retains wait and automatic-transcript review findings", AiBatchRetryExhaustionRetainsWaitAndAsrReview),
        new("Reusable description signatures do not trigger editorial retries", ReusableDescriptionSignatureIsExcludedFromEditorialReview),
        new("AI internal-process wording remains a hard failure", InternalProcessAiCopyRemainsHardFailure),
        new("AI drafts require complete provider-authored state", AiDraftPostconditionIsRequired),
        new("Grounded per-case failure rows are typed bounded and versioned", GroundedCaseFailureRowsAreBounded),
        new("Failed AI batches drop transient visual reviews without fallback", AiBatchFailureDropsTransientReviews),
        new("Qwen structured failure details remain actionable", StructuredQwenFailureIsActionable),
        new("Grounded Qwen failure diagnostics remain bounded and durable", GroundedFailureArchiveIsBounded),
        new("Grounded Qwen executor attaches its typed failure envelope", GroundedQwenExecutorAttachesFailureEnvelope),
        new("Grounded Qwen serializes only wire-authorized visual text", GroundedQwenSerializesOnlyWireAuthorizedVisualText),
        new("Grounded Qwen prioritizes game-linked OCR inside its bounded evidence wire contract", GroundedQwenPrioritizesGameLinkedOcrEvidence),
        new("Grounded Qwen withholds unconfirmed path identity", GroundedQwenWithholdsUnconfirmedPathIdentity),
        new("Confirmed local Wikidata identity survives context retrieval failure", ConfirmedIdentitySurvivesMissingKnowledge),
        new("Stable local OCR authorizes literal facts without public identity", StableLocalOcrAuthorizesLiteralFacts),
        new("Automatic creator reactions shape attributed angles without factual authority", AutomaticCreatorReactionSuppliesSafeAngle),
        new("Recorded cuts nominate complete creator thoughts without asserting game facts", RecordedCutsNominateCompleteCreatorThoughts),
        new("Automatic commentary joins the recorded incomplete thought without adding authority", CommentaryNominationCompletesRecordedThought),
        new("Automatic commentary declines uncertain and disconnected fragments", CommentaryNominationDeclinesUncertainFragments),
        new("Automatic commentary retains negation and ignores filler", CommentaryNominationPreservesNegationAndFiltersFiller),
        new("New commentary nominations preserve older authored context", CommentaryNominationRefreshPreservesHistoricalContext),
        new("Balanced defaults preserve custom guidance and reviewed speech", BalancedGuidancePreservesCustomProfileAndReviewedSpeech),
        new("Title punctuation review preserves quotations and apostrophes", DanglingTitlePunctuationNeedsReview),
        new("Observed dangling title punctuation triggers a bounded rewrite", DanglingTitlePunctuationRetries),
        new("Automatic commentary authority stays bounded and explicitly opted in", AutomaticCommentaryAuthorityIsBoundedAndOptIn),
        new("Exact mission claims require licensed guide authority and local corroboration", GroundedMissionRequiresLicensedGuideAuthority),
        new("Grounded CUDA OOM telemetry propagates without rerolls or isolation", GroundedCudaOomTelemetryStopsRetryAndIsolation),
        new("Typed Qwen resource failures propagate without retry or isolation", TypedQwenResourceFailuresDoNotRetry),
        new("Untyped Qwen technical failures propagate without GPU reruns", UntypedQwenTechnicalFailuresFailClosed),
        new("Legacy semantic host failures propagate without hidden reruns", LegacySemanticHostFailuresRunOnce),
        new("Qwen retries reuse one immutable verified model lease", QwenRetriesReuseVerifiedModelLease),
        new("Qualified Qwen observation and metadata share one owned model lease", QualifiedQwenRuntimeSharesAndOwnsModelLease),
        new("Unavailable required AI fails closed", RequiredAiRequiresProvider),
        new("Required AI provider failures are typed", RequiredAiFailurePropagates),
        new("Editorial AI batches load one provider for every candidate", AiBatchPreservesOrder),
        new("Visual AI metadata reviews the complete selected cut and cleans it", VisualAiMaterializesAndCleansReview),
        new("Visual AI metadata preserves a selected long cut", VisualAiPreservesLongSelectedCut),
        new("Visual AI metadata reuses an existing bounded review", VisualAiReusesExistingReview),
        new("Grounded heuristic titles always retain the game hashtag", GameHashtagSurvivesTitleLimit),
        new("Separated Qwen title bodies restore one canonical game hashtag", SeparatedQwenTitlesRestoreCanonicalHashtag),
        new("Capture paths stay unconfirmed and never become game hashtags", CapturePathsStayUnconfirmed),
        new("Game context memory is private local and source-reusable", GameContextMemoryIsPrivate),
        new("Game context memory quarantines corrupt supported documents", GameContextMemoryQuarantinesCorruptDocuments),
        new("Game context memory preserves future-schema documents", GameContextMemoryPreservesFutureSchema),
        new("Game context memory fails soft when its file is unavailable", GameContextMemoryFailsSoftWhenUnavailable),
        new("Game context memory preserves original v1.0 documents", GameContextMemoryPreservesOriginalSchema),
        new("Game context memory normalizes inherited v1.1 flags", GameContextMemoryNormalizesInheritedFlags),
        new("User metadata edits preserve provenance", UserEditsPreserveProvenance),
        new("Studio asset edits retain editorial metadata", AssetEditsRetainMetadata),
        new("Studio metadata editor saves through its focused MVVM boundary", StudioEditorSavesMetadata),
        new("Studio wording preferences preserve explicit default tags", WordingPreferencesPreserveDefaultTags),
        new("Studio rewrites save valid edits before generating", StudioRerollSavesPendingDraft),
        new("Failed Studio rewrites keep the saved user edits", FailedStudioRewriteKeepsEdits),
        new("Studio metadata changes preserve newer clip edits", StudioMetadataPreservesNewerEdits),
        new("Studio AI rerolls use the shared audience-copy review", StudioAiRerollUsesSharedQualityReview),
        new("Caption edits stale grounded copy until a fresh Studio reroll", CaptionEditsStaleGroundedCopyUntilReroll),
        new("Caption edits during a rewrite reject obsolete generated copy", CaptionEditsDuringRerollArePreserved),
        new("Late transcript corrections and timing edits invalidate a pending rewrite", CompleteCaptionContextGuardsReroll),
        new("Complete editorial revision projects OCR context without serializing frame buffers", CompleteRevisionProjectsRetainedVisualText),
        new("Earlier copy retains its original context when reroll enrichment changes the brief", RerollHistoryRetainsPreEnrichmentContext),
        new("Earlier copy restoration requires the complete current context revision", CopyRestorationRequiresCompleteRevision),
        new("Saving after a caption edit preserves the earlier authored context", CaptionEditBeforeSaveKeepsAuthoredHistory),
        new("Rerolling after an unbounded-brief caption edit preserves authored context", CaptionEditBeforeRerollKeepsAuthoredHistory),
        new("Manual metadata saves clear stale grounding without staying stale", ManualMetadataSaveClearsStaleGrounding),
        new("Studio metadata rerolls use the current saved cut", StudioMetadataRerollUsesCurrentCut),
        new("Studio rejects metadata completed for a superseded cut", StudioMetadataRejectsSupersededCut),
        new("Creator voice Settings updates the shared editorial profile", CreatorVoiceSettingsUpdatesSharedProfile),
        new("Accepted Thorough Hidden Moments refresh metadata through AI", AcceptedHiddenMomentRefreshesEditorialMetadata),
        new("Grounded heuristic metadata never copies style instructions or source timing", HeuristicMetadataDoesNotLeakInstructionsOrTiming),
        new("Qwen metadata rejects generic titles and internal timing", QwenMetadataRejectsUngroundedContent),
        new("Grounded metadata prefers supported creator commentary over literal reports", CreatorCommentaryShapesAudienceCopy),
        new("Qwen metadata enforces grounded creator voice", QwenMetadataEnforcesCreatorVoice),
        new("Qwen metadata enforces typed primary actor authority", QwenMetadataEnforcesActorAuthority),
        new("Grounded action strength treats bounded inflections consistently", ActionStrengthMatchesBoundedInflections),
        new("Review-flagged grounded copy remains usable", ReviewFlaggedGroundedCopyRemainsUsable),
        new("Structurally valid heuristic labels complete without approval", WorkingLabelsCompleteWorkflow),
        new("Finalized editorial metadata reaches Publish", FinalizedMetadataReachesPublish),
    ];

    private static Task QwenRetriesReuseVerifiedModelLease()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry.PreparationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string modelPath = Path.Combine(root, "weights.bin");
        Qwen3VlVerifiedModelLease? lease = null;
        try
        {
            File.WriteAllBytes(modelPath, [1, 2, 3, 4, 5, 6]);
            var info = new FileInfo(modelPath);
            var modelFile = new VisualSemanticModelFile(
                "weights.bin",
                ModelArtifactManifest.ComputeSha256(modelPath),
                info.Length);
            const string repository = "Qwen/Qwen3-VL-4B-Instruct";
            const string revision = "test-revision";
            const string license = "Apache-2.0";
            const string source = "https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct";
            var model = new VisualSemanticModelManifest(
                VisualSemanticModelManifest.SupportedSchemaVersion,
                repository,
                revision,
                root,
                license,
                source,
                [modelFile],
                VisualSemanticModelManifest.ComputeManifestSha256(
                    VisualSemanticModelManifest.SupportedSchemaVersion,
                    repository,
                    revision,
                    license,
                    source,
                    [modelFile]));

            lease = new Qwen3VlVerifiedModelLease(model);
            lease.Verify(CancellationToken.None);
            lease.Verify(CancellationToken.None);

            TestAssert.Equal(
                1,
                lease.FullVerificationCount,
                "Retries must reuse one full model verification.");
            TestAssert.Throws<IOException>(
                () => File.WriteAllBytes(modelPath, [9, 9, 9, 9, 9, 9]),
                "A verified model file must remain immutable while retries use it.");

            lease.Dispose();
            lease = null;
            File.WriteAllBytes(modelPath, [9, 9, 9, 9, 9, 9]);
        }
        finally
        {
            lease?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private static Task QualifiedQwenRuntimeSharesAndOwnsModelLease()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry.PreparationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Qwen3VlQualifiedEditorialRuntime? runtime = null;
        try
        {
            VisualSemanticModelManifest model = CreateLeaseTestModel(root);
            runtime = CreateLeaseTestRuntime(root, model);
            var provider = (Qwen3VlQualifiedEditorialProvider)runtime.Provider;
            Qwen3VlVerifiedModelLease providerLease =
                provider.GetOrCreateModelIntegrity(model);

            TestAssert.True(
                ReferenceEquals(providerLease, runtime.ModelIntegrity),
                "Observation and metadata must use the same verified model lease.");
            providerLease.Verify(CancellationToken.None);
            runtime.ModelIntegrity.Verify(CancellationToken.None);
            TestAssert.Equal(
                1,
                providerLease.FullVerificationCount,
                "The shared runtime must hash the model only once.");

            runtime.Dispose();
            runtime = null;
            TestAssert.Throws<ObjectDisposedException>(
                () => providerLease.Verify(CancellationToken.None),
                "Disposing the runtime must release its provider-owned lease.");
            provider.Dispose();
        }
        finally
        {
            runtime?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private static VisualSemanticModelManifest CreateLeaseTestModel(string root)
    {
        string modelPath = Path.Combine(root, "weights.bin");
        File.WriteAllBytes(modelPath, [1, 2, 3, 4, 5, 6]);
        var modelFile = new VisualSemanticModelFile(
            "weights.bin",
            ModelArtifactManifest.ComputeSha256(modelPath),
            new FileInfo(modelPath).Length);
        const string repository = "Qwen/Qwen3-VL-4B-Instruct";
        const string revision = "lease-sharing-test";
        const string license = "Apache-2.0";
        const string source = "https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct";
        return new VisualSemanticModelManifest(
            VisualSemanticModelManifest.SupportedSchemaVersion,
            repository,
            revision,
            root,
            license,
            source,
            [modelFile],
            VisualSemanticModelManifest.ComputeManifestSha256(
                VisualSemanticModelManifest.SupportedSchemaVersion,
                repository,
                revision,
                license,
                source,
                [modelFile]));
    }

    private static Qwen3VlQualifiedEditorialRuntime CreateLeaseTestRuntime(
        string root,
        VisualSemanticModelManifest model)
    {
        string lockPath = Path.Combine(root, "qualification-lock.json");
        File.WriteAllText(lockPath, "{}");
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("The test process path is unavailable.");
        var host = new Qwen3VlBatchHostSettings(
            processPath,
            processPath,
            root,
            Qwen3VlBatchHostSettings.SupportedVideoBackend,
            root,
            TimeSpan.FromSeconds(1));
        var settings = new Qwen3VlQualifiedEditorialSettings(
            host,
            lockPath,
            new string('a', 64));
        const string promptText = "Qualified model lease sharing test prompt.";
        var prompt = new VisualSemanticPromptManifest(
            VisualSemanticPromptManifest.QualifiedEditorialSchemaVersion,
            VisualSemanticPromptManifest.QualifiedEditorialName,
            VisualSemanticPromptManifest.QualifiedEditorialVersion,
            promptText,
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(promptText))),
            DateTimeOffset.UtcNow);
        return new Qwen3VlQualifiedEditorialRuntime(
            new Qwen3VlQualifiedEditorialProvider(settings),
            prompt,
            model,
            VisualSemanticVideoInputPolicy.CreateV05A1(),
            host,
            lockPath,
            new string('a', 64));
    }

    private static Task CreatorVoiceSettingsUpdatesSharedProfile()
    {
        var session = new ClipEditorialProfileSession();
        using var settings = new SettingsViewModel(session);
        settings.SelectedSection = SettingsSection.CreatorVoice;
        settings.CreatorVoice.AudienceAddress = "Friends";
        settings.CreatorVoice.NamingGuidance =
            "Prefer concise past-tense action titles.";
        settings.CreatorVoice.DescriptionSignature = "Follow for more.";
        settings.CreatorVoice.DefaultTags =
            "#gaming, ReplayFoundry, gaming";

        settings.CreatorVoice.SaveCommand.Execute(null);

        TestAssert.Equal(
            "Friends",
            session.Current.AudienceAddress,
            "Settings should update the shared profile session.");
        TestAssert.Equal(
            2,
            session.Current.DefaultTags.Count,
            "Settings should normalize and deduplicate reusable tags.");
        TestAssert.True(
            settings.CreatorVoice.Status.Contains(
                "app session",
                StringComparison.OrdinalIgnoreCase),
            "Settings should state the current session lifetime.");

        settings.CreatorVoice.AudienceAddress = string.Empty;
        settings.CreatorVoice.SaveCommand.Execute(null);
        TestAssert.Equal(
            "Friends",
            session.Current.AudienceAddress,
            "Invalid settings must not replace the shared profile.");
        TestAssert.True(
            settings.CreatorVoice.Status.Contains(
                "could not be saved",
                StringComparison.OrdinalIgnoreCase),
            "Validation failures should be actionable.");

        using var studio = new StudioEditorialMetadataViewModel(
            outputEditor: null,
            generator: null,
            profileEditor: session);
        session.UpdateCreatorVoice(
            "Viewers",
            "Prefer direct factual wording.",
            string.Empty,
            ["clips"]);
        studio.Bind(project: null, asset: null);
        TestAssert.Equal(
            "Viewers",
            studio.AudienceAddress,
            "Studio should reload the shared defaults whenever a clip is bound.");
        return Task.CompletedTask;
    }

    private static Task StaleGroundedPromptFailsSoft()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture(
            corruptGroundedPrompt: true);
        Qwen3VlGroundedMetadataGenerator? provider =
            EditorialComposition.TryCreateAiProvider(
                fixture.Runtime,
                out string? unavailableReason);
        TestAssert.True(
            provider is null,
            "A stale Advanced AI prompt must disable only grounded metadata instead of terminating application startup.");
        TestAssert.Equal(
            EditorialComposition.AiProviderStartupFailureReason,
            unavailableReason,
            "Composition should expose bounded repair guidance instead of raw runtime details.");
        TestAssert.False(
            unavailableReason!.Contains(
                fixture.Runtime.Host.HostScriptPath,
                StringComparison.OrdinalIgnoreCase),
            "The user-facing readiness reason must not expose local runtime paths.");
        return Task.CompletedTask;
    }

    private static Task ProfileIsImmutable()
    {
        var tags = new List<string> { "ReplayFoundry", "gaming" };
        var profile = new ClipEditorialProfile(
            "Chat",
            "Short factual titles",
            "Follow for more.",
            tags);
        tags.Clear();

        TestAssert.Equal(2, profile.DefaultTags.Count, "Tag snapshot.");
        TestAssert.Equal("Chat", profile.AudienceAddress, "Audience.");
        TestAssert.Equal(
            ClipEditorialVoicePerspective.CreatorFirstPerson,
            profile.VoicePerspective,
            "Creator-first-person must be the default metadata voice.");
        TestAssert.Equal(
            ClipEditorialProfile.DefaultNamingGuidance,
            ClipEditorialProfile.Default.NamingGuidance,
            "The default profile must carry only the shared creator-ready style policy.");
        TestAssert.False(
            (ClipEditorialProfile.Default.NamingGuidance ?? string.Empty)
                .Contains('#'),
            "Default style guidance must not hard-code any game or output hashtag.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<string>)profile.DefaultTags).Add("mutate"),
            "Profile tags must be read-only.");
        return Task.CompletedTask;
    }

    private static Task VariantIntentRequiresReviewedTranscript()
    {
        ClipEditorialContext automatic = CreateContext(
        [
            new ClipEditorialTranscriptContext(
                1,
                new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech,
                    AudioContentRoleSource.UserConfirmed),
                "automatic transcript words remain unreviewed"),
        ]);
        ClipEditorialVariantIntent[] automaticIntents = Enumerable
            .Range(0, 4)
            .Select(attempt => new ClipEditorialMetadataRequest(
                automatic,
                ClipEditorialProfile.Default,
                attempt).VariantIntent)
            .ToArray();
        TestAssert.True(
            automaticIntents.SequenceEqual(
            [
                ClipEditorialVariantIntent.DirectAction,
                ClipEditorialVariantIntent.SpecificCuriosity,
                ClipEditorialVariantIntent.OutcomeFocused,
                ClipEditorialVariantIntent.ConcreteDetail,
            ]),
            "Initial generation plus three transcript-free rerolls must expose four distinct non-commentary intents. Actual: " +
                string.Join(",", automaticIntents));

        ClipEditorialContext corrected = CreateContext(
        [
            new ClipEditorialTranscriptContext(
                1,
                new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech,
                    AudioContentRoleSource.UserConfirmed),
                "I found the switch behind the panel",
                ClipEditorialTranscriptAuthority.UserCorrected),
        ]);
        var correctedAttempt = new ClipEditorialMetadataRequest(
            corrected,
            ClipEditorialProfile.Default,
            attempt: 4);
        TestAssert.Equal(
            ClipEditorialVariantIntent.CommentaryLed,
            correctedAttempt.VariantIntent,
            "Corrected creator speech can authorize a commentary-led variant.");
        return Task.CompletedTask;
    }

    private static async Task AiRerollsKeepDistinctProviderPackages()
    {
        var provider = new VariantPackageMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            provider);
        var packages = new List<ClipEditorialMetadataDraft>();
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            packages.Add(await service.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(transcripts: []),
                    ClipEditorialProfile.Default,
                    attempt,
                    ClipEditorialGenerationPreference.AiRequired),
                CancellationToken.None));
        }

        TestAssert.Equal(
            3,
            packages.Select(static package => package.Title)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "Three Qwen reroll intents must yield three distinct title bodies.");
        TestAssert.Equal(
            3,
            packages.Select(static package => package.Description)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "The intent must guide the provider-authored descriptions too.");
        TestAssert.Equal(
            3,
            packages.Select(static package => package.TagsText)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "The intent must guide the provider-authored tag arrays too.");
        TestAssert.True(
            packages.All(static package =>
                package.Origin == ClipEditorialMetadataOrigin.AiAssisted),
            "The reroll gate cannot be satisfied by deterministic fallback copy.");
        TestAssert.Equal(
            3,
            provider.Calls,
            "A provider without batch support must remain compatible with direct one-clip generation.");
    }

    private static Task HistoricalQwenPromptIdentityIsPreserved()
    {
        (string currentVersion, string currentHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.OutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PromptVersion,
            currentVersion,
            "Current prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PromptSha256,
            currentHash,
            "Current prompt hash.");
        TestAssert.Equal(
            ("1.46", "61ad677ba7cb97a250df90bf77aa0fcaeb27dcd226af871b7226d89b1cf6b2d0"),
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousResponsibilitySplitOutputSchema),
            "The 1.61 responsibility split must retain the exact 1.60 prompt identity.");

        (string previousCompactIsolatedVersion, string previousCompactIsolatedHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousCompactIsolatedFieldAuthoringOutputSchema);
        TestAssert.Equal("1.45", previousCompactIsolatedVersion, "Output 1.59 retains its original isolated authoring prompt.");
        TestAssert.Equal("6fa6e96a7a33d28e4c1fdd8ee4a806f57d23cb267aab04aedc3ba591f9f1249d",
            previousCompactIsolatedHash, "The compact authoring revision cannot relabel a saved 1.59 prompt identity.");

        (string previousIsolatedVersion, string previousIsolatedHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousIsolatedFieldAuthoringOutputSchema);
        TestAssert.Equal("1.44", previousIsolatedVersion, "Output 1.58 retains its original full-object prompt.");
        TestAssert.Equal("b09ae948487a8cb447076eb72ae22e7736eb43e249ca46f82e2d33a727a75c28",
            previousIsolatedHash, "A saved full-object generation cannot be relabeled as isolated authoring.");

        (string previousSchemaEnforcedVersion, string previousSchemaEnforcedHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousSchemaEnforcedBalancedCopyOutputSchema);
        TestAssert.Equal("1.43", previousSchemaEnforcedVersion,
            "Output 1.57 retains the exact prompt used for saved generation.");
        TestAssert.Equal(
            "defb5c76573252699c548200ad86a73d1c1d62bb74fbdc8b9980f5e225de1ec6",
            previousSchemaEnforcedHash,
            "Output 1.57 must not be relabeled with the schema-enforced prompt hash.");

        (string previousCompactVersion, string previousCompactHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousCompactBalancedCopyOutputSchema);
        TestAssert.Equal("1.42", previousCompactVersion,
            "Output 1.56 retains the exact prompt used for saved generation.");
        TestAssert.Equal(
            "688ed574ae46fb8155070b6d6cc340d9e42847db15eb0fd9935228ecaaae2e58",
            previousCompactHash,
            "Output 1.56 must not be relabeled with the compact-brief prompt hash.");

        (string previousBalancedVersion, string previousBalancedHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousBalancedCopyOutputSchema);
        TestAssert.Equal("1.41", previousBalancedVersion,
            "Output 1.55 retains the exact prompt used for saved generation.");
        TestAssert.Equal(
            "3cad54f5a7aa47b59e1979aa2d41ac85ac6fe0338d18574cff35527102276b53",
            previousBalancedHash,
            "Output 1.55 must not be relabeled with the balanced-copy prompt hash.");

        (string previousTimingVersion, string previousTimingHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousCommentaryTimingOutputSchema);
        TestAssert.Equal("1.40", previousTimingVersion,
            "Output 1.54 retains the exact prompt used for saved generation.");
        TestAssert.Equal(
            "241086ff61f2e10108fc5c09c3e0a00381af102ee5b97534d2a1314dd892b72a",
            previousTimingHash,
            "Output 1.54 must not be relabeled with the new prompt hash.");

        (string previousCreatorVoiceVersion,
            string previousCreatorVoiceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCreatorVoiceOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousCreatorVoicePromptVersion,
            previousCreatorVoiceVersion,
            "Pre-creator-voice prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousCreatorVoicePromptSha256,
            previousCreatorVoiceHash,
            "Pre-creator-voice prompt hash.");

        (string previousFrameAdherenceVersion,
            string previousFrameAdherenceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousEditorialFrameAdherenceOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousCreatorVoicePromptVersion,
            previousFrameAdherenceVersion,
            "Pre-editorial-frame-adherence prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousCreatorVoicePromptSha256,
            previousFrameAdherenceHash,
            "Pre-editorial-frame-adherence prompt hash.");

        (string previousWholeBatchVersion, string previousWholeBatchHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousWholeBatchOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptVersion,
            previousWholeBatchVersion,
            "Pre-fail-soft prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptSha256,
            previousWholeBatchHash,
            "Pre-fail-soft prompt hash.");

        (string previousVisualDraftVersion,
            string previousVisualDraftHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousVisualDraftPromptOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousVisualDraftVersion,
            "Pre-literal-action visual-draft prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousVisualDraftHash,
            "Pre-literal-action visual-draft prompt hash.");

        (string previousInterfaceVersion,
            string previousInterfaceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousInterfaceAttributionOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousInterfaceVersion,
            "Pre-interface-attribution prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousInterfaceHash,
            "Pre-interface-attribution prompt hash.");

        (string previousEffectiveVoiceVersion,
            string previousEffectiveVoiceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousEffectiveVoiceOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousEffectiveVoiceVersion,
            "Pre-effective-voice prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousEffectiveVoiceHash,
            "Pre-effective-voice prompt hash.");

        (string previousWhitespaceVersion, string previousWhitespaceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousGroundedJsonWhitespaceOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousWhitespaceVersion,
            "Pre-canonical-whitespace prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousWhitespaceHash,
            "Pre-canonical-whitespace prompt hash.");

        (string previousCreatorAuthorityVersion,
            string previousCreatorAuthorityHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCreatorAuthorityOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousCreatorAuthorityVersion,
            "Previous creator-authority prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousCreatorAuthorityHash,
            "Previous creator-authority prompt hash.");

        (string previousAudienceVersion, string previousAudienceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousAudienceCopyWithholdingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousAudienceVersion,
            "Previous audience-copy-withholding prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousAudienceHash,
            "Previous audience-copy-withholding prompt hash.");

        (string previousCrossDraftVersion, string previousCrossDraftHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCrossDraftRetryOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousCrossDraftVersion,
            "Previous cross-draft retry prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousCrossDraftHash,
            "Previous cross-draft retry prompt hash.");

        (string previousRootPreloadVersion, string previousRootPreloadHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousRootPreloadOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousRootPreloadVersion,
            "Previous root-preload prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousRootPreloadHash,
            "Previous root-preload prompt hash.");

        (string previousAttentionVersion, string previousAttentionHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCudnnAttentionOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousAttentionVersion,
            "Previous attention-backend prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousAttentionHash,
            "Previous attention-backend prompt hash.");

        (string previousAccelerateVersion, string previousAccelerateHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousAccelerateOffloadOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousAccelerateVersion,
            "Previous Accelerate-offload prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousAccelerateHash,
            "Previous Accelerate-offload prompt hash.");

        (string previousPositionVersion, string previousPositionHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousPositionEmbeddingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousPositionVersion,
            "Previous position-embedding prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousPositionHash,
            "Previous position-embedding prompt hash.");

        (string previousVisionVersion, string previousVisionHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousVisionOffloadOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousVisionVersion,
            "Previous all-CUDA prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousVisionHash,
            "Previous all-CUDA prompt hash.");

        (string previousLowPeakVersion, string previousLowPeakHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousLowPeakSamplingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousLowPeakVersion,
            "Previous low-peak prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousLowPeakHash,
            "Previous low-peak prompt hash.");

        (string previousPeakVersion, string previousPeakHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousPeakBoundedSamplingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousPeakVersion,
            "Previous peak-bounded prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousPeakHash,
            "Previous peak-bounded prompt hash.");

        (string previousSamplingVersion, string previousSamplingHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousSamplingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousSamplingVersion,
            "Previous-sampling prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousSamplingHash,
            "Previous-sampling prompt hash.");

        (string preWatchdogVersion, string preWatchdogHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreWatchdogOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            preWatchdogVersion,
            "Pre-watchdog prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            preWatchdogHash,
            "Pre-watchdog prompt hash.");

        (string previousVersion, string previousHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            previousVersion,
            "Previous prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            previousHash,
            "Previous prompt hash.");

        (string priorVersion, string priorHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PriorOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            priorVersion,
            "Prior prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            priorHash,
            "Prior prompt hash.");

        (string legacyVersion, string legacyHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.LegacyOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            legacyVersion,
            "Legacy prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            legacyHash,
            "Legacy prompt hash.");

        (string historicalVersion, string historicalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.HistoricalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            historicalVersion,
            "Historical prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            historicalHash,
            "Historical prompt hash.");

        (string priorHistoricalVersion, string priorHistoricalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PriorHistoricalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            priorHistoricalVersion,
            "Prior historical prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            priorHistoricalHash,
            "Prior historical prompt hash.");

        (string earlierHistoricalVersion, string earlierHistoricalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.EarlierHistoricalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            earlierHistoricalVersion,
            "Earlier historical prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            earlierHistoricalHash,
            "Earlier historical prompt hash.");

        (string initialVersion, string initialHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.InitialOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptVersion,
            initialVersion,
            "Initial prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptSha256,
            initialHash,
            "Initial prompt hash.");

        (string oldestVersion, string oldestHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.OldestOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptVersion,
            oldestVersion,
            "Oldest prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptSha256,
            oldestHash,
            "Oldest prompt hash.");
        (string earliestVersion, string earliestHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.EarliestOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptVersion,
            earliestVersion,
            "Earliest prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptSha256,
            earliestHash,
            "Earliest prompt hash.");
        (string foundationalVersion, string foundationalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.FoundationalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptVersion,
            foundationalVersion,
            "Foundational prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptSha256,
            foundationalHash,
            "Foundational prompt hash.");
        (string originalVersion, string originalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.OriginalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptVersion,
            originalVersion,
            "Original prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptSha256,
            originalHash,
            "Original prompt hash.");
        (string baselineVersion, string baselineHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.BaselineOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.BaselinePromptVersion,
            baselineVersion,
            "Baseline prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.BaselinePromptSha256,
            baselineHash,
            "Baseline prompt hash.");
        return Task.CompletedTask;
    }

    private static async Task UsesBothTranscriptRoles()
    {
        ClipEditorialContext context = CreateContext(
        [
            new ClipEditorialTranscriptContext(
                1,
                new AudioContentRoleAssignment(
                    AudioContentRole.CreatorSpeech,
                    AudioContentRoleSource.UserConfirmed),
                "Chat, I cannot believe that actually worked."),
            new ClipEditorialTranscriptContext(
                2,
                new AudioContentRoleAssignment(
                    AudioContentRole.GameDialogue,
                    AudioContentRoleSource.UserConfirmed),
                "The gate is open. Move now."),
        ]);
        var generator = new HeuristicClipEditorialMetadataGenerator();

        ClipEditorialMetadataDraft result =
            await generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    context,
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);

        TestAssert.False(
            result.Description.Contains(
                "The gate is open",
                StringComparison.Ordinal),
            "Unreviewed ASR wording must remain captions rather than audience metadata.");
        TestAssert.False(
            result.Description.Contains(
                "retained as an editable caption track",
                StringComparison.OrdinalIgnoreCase) ||
            result.Description.Contains(
                "Creator-supplied game context:",
                StringComparison.OrdinalIgnoreCase),
            "Transcript and game-context provenance belongs in typed evidence, not audience-facing copy.");
        TestAssert.True(
            result.Tags.Contains(
                "commentary",
                StringComparer.OrdinalIgnoreCase),
            "Creator tag.");
        TestAssert.True(
            result.Tags.Contains(
                "game dialogue",
                StringComparer.OrdinalIgnoreCase),
            "Game-dialogue tag.");
        TestAssert.Equal(2, result.Evidence.Count(
            item => item.Kind is
                ClipEditorialEvidenceKind.CreatorTranscript or
                ClipEditorialEvidenceKind.GameDialogueTranscript),
            "Both transcript roles need provenance.");
    }

    private static async Task ManualGameNotesRetainDraftProvenance()
    {
        const string notes =
            "A confirmed masked visitor met the protagonist beside a glowing chain.";
        const string editorialPremise =
            "An in-world briefing introduced an unusual object.";
        string sourcePath = Path.GetFullPath(
            "ExampleGame/Vertical/manual-context-source.mkv");
        var context = new ClipEditorialContext(
            "candidate-manual-context",
            sourcePath,
            "ExampleGame",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromMinutes(5),
            84,
            "A bounded visible event was selected.",
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visible-event",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "A masked person stood beside a glowing chain."),
            ],
            gameContext: new ClipEditorialGameContext(
                "ExampleGame",
                "#ExampleGame",
                notes,
                ClipEditorialGameContextSource.UserConfirmed,
                useOpenGameKnowledge: true));
        TestAssert.False(
            context.GameContext.UseOpenGameKnowledge,
            "A typed game name and notes cannot silently authorize public lookup without an explicitly selected QID.");

        ClipEditorialEvidenceReference retained = context.Evidence.Single(
            value => value.Id.Equals(
                ClipEditorialGameContext.ContextNotesEvidenceId,
                StringComparison.Ordinal));
        TestAssert.Equal(
            ClipEditorialEvidenceKind.UserGameContext,
            retained.Kind,
            "Manual note evidence kind.");
        TestAssert.True(
            retained.Description.Contains(
                ClipEditorialGameContextSource.UserConfirmed.ToString(),
                StringComparison.Ordinal) &&
            retained.Description.Contains(notes, StringComparison.Ordinal),
            "The exact bounded local note and its typed origin must remain reconcilable.");

        var pathHintContext = new ClipEditorialContext(
            "candidate-path-hint",
            sourcePath,
            "ExampleGame",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromMinutes(5),
            84,
            "A bounded visible event was selected.",
            gameContext: new ClipEditorialGameContext(
                "Example Game",
                "#ExampleGame",
                notes,
                ClipEditorialGameContextSource.SourcePathHint));
        TestAssert.False(
            pathHintContext.Evidence.Any(value => value.Id.Equals(
                ClipEditorialGameContext.ContextNotesEvidenceId,
                StringComparison.Ordinal)),
            "A source-path hint must not be promoted to user-confirmed note authority.");

        ClipEditorialMetadataDraft heuristic =
            await new HeuristicClipEditorialMetadataGenerator().GenerateAsync(
                new ClipEditorialMetadataRequest(
                    context,
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);
        TestAssert.True(
            heuristic.Evidence.Any(value => value.Id.Equals(
                ClipEditorialGameContext.ContextNotesEvidenceId,
                StringComparison.Ordinal)),
            "The provider-neutral draft must retain manual-note evidence.");

        string reviewPath = Path.GetFullPath("ReplayFoundry.slnx");
        var reviewInfo = new FileInfo(reviewPath);
        var review = new VisualSemanticInputManifest(
            reviewPath,
            ModelArtifactManifest.ComputeSha256(reviewPath),
            reviewInfo.Length,
            context.Duration,
            new DateTimeOffset(reviewInfo.LastWriteTimeUtc));
        var request = new ClipEditorialMetadataRequest(
            context,
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired,
            TestMediaFactory.Create(sourcePath, context.SourceDuration),
            review);
        var validation = new Qwen3VlGroundedMetadataGenerationValidation(
            GenerationPassCount: 3,
            GroundingPassCount: null,
            SynthesisPassCount: null,
            DuplicateSynthesisRecoveryApplied: false,
            DuplicateSynthesisRecoverySourcePassOrdinal: null,
            DuplicateSynthesisRecoveryRepeatedPassOrdinal: null,
            DuplicateSynthesisRecoverySourceRejectedJsonSha256: null,
            DuplicateSynthesisRecoveryRepeatedRejectedJsonSha256: null,
            SampledSynthesisApplied: false,
            SampledSynthesisPassOrdinal: null,
            SampledSynthesisTrigger: null,
            SampledSynthesisSourceRejectedJsonSha256: null,
            NonRetrospectiveRetryAnchorApplied: false,
            NonRetrospectiveRetryAnchorSourcePassOrdinal: null,
            NonRetrospectiveRetryAnchorSourceRule: null,
            NonRetrospectiveRetryAnchorEnvelopeSha256: null,
            NonRetrospectiveRetryAnchorAuthoritySha256: null,
            SynthesisRecoveryPoolApplied: false,
            SynthesisRecoveryPoolSourcePassOrdinal: null,
            SynthesisRecoveryPoolSourceRejectedJsonSha256: null,
            SynthesisRecoveryPoolSourceSelectionReason: null,
            SynthesisRecoveryPoolAttemptedCandidateCount: 0,
            SynthesisRecoveryPoolSelectedCandidateOrdinal: null,
            SynthesisRecoveryPoolRetryableSemanticRejections: [],
            SynthesisRecoveryPoolRetryableSemanticRejectionsSha256: null,
            GroundedMetadataModuleIdentities: [],
            SynthesisPassAttestations: [],
            GroundingPacketRequestSha256: null,
            GroundingPacketFactSha256: null,
            GroundingPacketSourceAttempt: null,
            GroundingPacketReused: null,
            PrimaryOnlySynthesisEvidenceApplied: null,
            VisualDrafts:
            [
                new Qwen3VlGroundedMetadataVisualDraft(
                    1,
                    0,
                    20,
                    "A fictional archive",
                    false,
                    ["A projected briefing", "An unusual object"],
                    ["A recorded briefing introduced an unusual object"],
                    [],
                    []),
            ],
            StableReadableText: [],
            ActorAuthorityAssessmentApplied: false,
            PrimaryVisualDraftOrdinal: 1,
            PrimaryActorAuthority:
                Qwen3VlGroundedMetadataActorAuthority.Unknown,
            PrimaryCreatorExperienceRelation:
                Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
            EditorialFrame: new Qwen3VlGroundedMetadataEditorialFrame(
                "grounded-editorial-frame-1.0",
                "StoryShapeOnly",
                Qwen3VlGroundedMetadataMomentKind.Exposition,
                editorialPremise,
                [1],
                false),
            VisualEventSelectionAssessments:
            [
                new Qwen3VlGroundedMetadataVisualEventAssessment(
                    1,
                    true,
                    false,
                    false,
                    false,
                    false,
                    false,
                    Qwen3VlGroundedMetadataActorAuthority.OtherPerson,
                    Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
                    Qwen3VlGroundedMetadataPresentationKind.InWorldRecording),
            ],
            KnowledgeSelectionApplied: true,
            SelectedCurrentPassageId: "None",
            KnowledgeSelectionAssessments: [],
            GroundingReviewApplied: true,
            RejectedRules: [],
            PriorAcceptedTitleCount: null,
            RerollTitleDiversityCode: null,
            RerollTitleTokenJaccardNumerator: null,
            RerollTitleTokenJaccardDenominator: null,
            MetadataReviewRequired: false,
            MetadataReviewIssues: []);
        var diversityScope =
            new Qwen3VlGroundedMetadataRerollTitleScope(
                context.CandidateId,
                context.SourceStart,
                context.SourceEnd);
        Qwen3VlGroundedMetadataRerollTitleDiversityResult actualDiversity =
            Qwen3VlGroundedMetadataRerollDiversityPolicy.Evaluate(
                new Qwen3VlGroundedMetadataRerollTitleReference(
                    diversityScope,
                    "The briefing introduced a different subject #ExampleGame",
                    "#ExampleGame"),
                [
                    new Qwen3VlGroundedMetadataRerollTitleReference(
                        diversityScope,
                        "The archive opened with an old subject #ExampleGame",
                        "#ExampleGame"),
                ]);
        Qwen3VlGroundedMetadataGenerationValidation reconciled =
            Qwen3VlGroundedMetadataRerollDiversityPolicy
                .ReconcileReportedProvenance(
                    actualDiversity,
                    validation,
                    retainReviewableMismatch: true);
        TestAssert.Equal(
            actualDiversity.Code,
            reconciled.RerollTitleDiversityCode,
            "Current output must retain the locally recomputed diversity result when redundant host telemetry disagrees.");
        TestAssert.True(
            reconciled.MetadataReviewRequired &&
            reconciled.MetadataReviewIssues.Contains(
                "RerollDiversityProvenanceRecomputed",
                StringComparer.Ordinal),
            "A retained diversity-provenance mismatch must remain visible for review.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataRerollDiversityPolicy
                .ReconcileReportedProvenance(
                    actualDiversity,
                    validation,
                    retainReviewableMismatch: false),
            "Historical output schemas must continue to reject conflicting reroll provenance.");
        IReadOnlyList<ClipEditorialEvidenceReference> qwenEvidence =
            Qwen3VlGroundedMetadataEvidenceBuilder.Build(
                request,
                grounding: [],
                validation);
        TestAssert.True(
            qwenEvidence.Any(value => value.Id.Equals(
                ClipEditorialGameContext.ContextNotesEvidenceId,
                StringComparison.Ordinal)),
            "The grounded provider evidence builder must carry manual-note provenance unchanged.");
        GameKnowledgeInfluenceAudit audit =
            Qwen3VlGroundedMetadataEvidenceBuilder.BuildAudit(
                request,
                grounding: [],
                validation);
        TestAssert.Equal(
            editorialPremise,
            audit.ResolvedBrief?.PrimaryGameplayBeat,
            "The persisted editorial brief must prefer the bounded story-shape premise over joined literal visual actions.");
        TestAssert.Equal(
            GroundedEditorialPresentationKind.InWorldRecording,
            audit.ResolvedBrief?.PresentationKind,
            "The persisted editorial brief must retain its typed presentation kind.");
        TestAssert.Equal(
            GroundedEditorialMomentKind.Exposition,
            audit.ResolvedBrief?.MomentKind,
            "The persisted editorial brief must retain its typed narrative role.");
        TestAssert.True(
            audit.UsedEvidenceIds.Contains(
                "qwen-editorial-frame",
                StringComparer.Ordinal),
            "The influence audit must record that the bounded editorial frame shaped the copy.");
        Qwen3VlGroundedMetadataGenerationValidation unresolvedValidation =
            validation with
            {
                EditorialFrame = new Qwen3VlGroundedMetadataEditorialFrame(
                    "grounded-editorial-frame-1.0",
                    "StoryShapeOnly",
                    Qwen3VlGroundedMetadataMomentKind.Exposition,
                    null,
                    [1],
                    false),
            };
        GameKnowledgeInfluenceAudit unresolvedAudit =
            Qwen3VlGroundedMetadataEvidenceBuilder.BuildAudit(
                request,
                grounding: [],
                unresolvedValidation);
        TestAssert.True(
            unresolvedAudit.ResolvedBrief?.PrimaryGameplayBeat is null,
            "An unresolved editorial premise must remain null instead of relabeling joined visual actions as an editorial beat.");
        TestAssert.Equal(
            GroundedEditorialMomentKind.Exposition,
            unresolvedAudit.ResolvedBrief?.MomentKind,
            "A safe typed narrative role must survive even when its free-text premise is withheld.");
        TestAssert.True(
            unresolvedAudit.ResolvedBrief?.QualityFlags.Contains(
                "EditorialPremiseUnresolved",
                StringComparer.Ordinal) == true &&
            unresolvedAudit.ResolvedBrief.QualityFlags.Contains(
                "PrimaryBeatUnresolved",
                StringComparer.Ordinal),
            "Unresolved framing must remain reviewable diagnostics rather than becoming generated copy.");
        IReadOnlyList<ClipEditorialWarning> warnings =
            Qwen3VlGroundedMetadataEvidenceBuilder.BuildWarnings(
                request,
                validation,
                grounding: []);
        TestAssert.False(
            warnings.Any(value =>
                value.Code == ClipEditorialWarningCode.GameKnowledgeNotSelected),
            "Local notes without a selected QID should remain provider-neutral rather than implying that an online knowledge request occurred.");
    }

    private static async Task RawTranscriptIsNotAudienceMetadata()
    {
        const string rawTranscript =
            "a noisy automatic transcript fragment that should not become a title";
        ClipEditorialMetadataDraft result =
            await new HeuristicClipEditorialMetadataGenerator().GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(
                    [
                        new ClipEditorialTranscriptContext(
                            1,
                            new AudioContentRoleAssignment(
                                AudioContentRole.CreatorSpeech,
                                AudioContentRoleSource.UserConfirmed),
                            rawTranscript),
                    ]),
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);

        TestAssert.False(
            result.Title.Contains(rawTranscript, StringComparison.Ordinal),
            "Raw ASR text must not become deterministic title authority.");
        TestAssert.False(
            result.Description.Contains(rawTranscript, StringComparison.Ordinal),
            "Raw ASR text must not become deterministic description authority.");
        TestAssert.True(
            result.Warnings.Any(static warning =>
                warning.Code == ClipEditorialWarningCode.LimitedGrounding),
            "The safe fallback must disclose its limited semantic grounding.");
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.WorkingLabel,
            result.Readiness,
            "Heuristic-only copy must be retained as a working label.");
        TestAssert.True(
            result.IsPublishReady,
            "Structurally valid heuristic metadata must not be blocked by optional review state.");
    }

    private static async Task RerollsAreDeterministic()
    {
        ClipEditorialContext context = CreateContext();
        var generator = new HeuristicClipEditorialMetadataGenerator();
        var request0 = new ClipEditorialMetadataRequest(
            context,
            ClipEditorialProfile.Default,
            0);
        var request1 = new ClipEditorialMetadataRequest(
            context,
            ClipEditorialProfile.Default,
            1);

        ClipEditorialMetadataDraft first =
            await generator.GenerateAsync(
                request0,
                CancellationToken.None);
        ClipEditorialMetadataDraft repeated =
            await generator.GenerateAsync(
                request0,
                CancellationToken.None);
        ClipEditorialMetadataDraft rerolled =
            await generator.GenerateAsync(
                request1,
                CancellationToken.None);

        TestAssert.Equal(first.Title, repeated.Title, "Repeat title.");
        TestAssert.Equal(
            first.Description,
            repeated.Description,
            "Repeat description.");
        TestAssert.False(
            first.Title.Equals(
                rerolled.Title,
                StringComparison.Ordinal),
            "A new attempt should choose another versioned template.");
    }

    private static async Task HeuristicMetadataUsesPerClipEvidence()
    {
        const string firstBeat =
            "A sealed doorway opened beside the control panel.";
        const string secondBeat =
            "A supply crate broke apart near the stairwell.";
        var generator = new HeuristicClipEditorialMetadataGenerator();
        ClipEditorialMetadataDraft first = await generator.GenerateAsync(
            new ClipEditorialMetadataRequest(
                CreateVisualHeuristicContext(
                    "candidate-doorway",
                    "visual-doorway",
                    firstBeat),
                ClipEditorialProfile.Default,
                0),
            CancellationToken.None);
        ClipEditorialMetadataDraft second = await generator.GenerateAsync(
            new ClipEditorialMetadataRequest(
                CreateVisualHeuristicContext(
                    "candidate-crate",
                    "visual-crate",
                    secondBeat),
                ClipEditorialProfile.Default,
                0),
            CancellationToken.None);

        TestAssert.False(
            first.Title.Equals(second.Title, StringComparison.Ordinal) &&
            first.Description.Equals(
                second.Description,
                StringComparison.Ordinal),
            "Different safe visual evidence must not collapse into one metadata package.");
        TestAssert.True(
            first.Title.StartsWith(
                "The Playthrough Continued Along the Route",
                StringComparison.Ordinal),
            "Doorway progress should select the bounded progress framing.");
        TestAssert.True(
            second.Title.StartsWith(
                "A Discovery Changed the Route",
                StringComparison.Ordinal),
            "A discovered crate should select the bounded discovery framing.");
        string audienceCopy = string.Join(
            '\n',
            first.Title,
            first.Description,
            second.Title,
            second.Description);
        TestAssert.False(
            audienceCopy.Contains(firstBeat, StringComparison.Ordinal) ||
            audienceCopy.Contains(secondBeat, StringComparison.Ordinal),
            "Raw visual observations must select framing without becoming audience copy.");
    }

    private static async Task GroundedDocumentFallbackVaries()
    {
        const int documentCount = 24;
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator());
        ClipEditorialMetadataRequest[] requests = Enumerable.Range(
                0,
                documentCount)
            .Select(index => new ClipEditorialMetadataRequest(
                CreateVisualHeuristicContext(
                    $"document-{index:D2}",
                    $"visual-document-{index:D2}",
                    "An in-game document was reviewed."),
                ClipEditorialProfile.Default,
                attempt: 0,
                preference:
                    ClipEditorialGenerationPreference.HeuristicOnly))
            .ToArray();

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await service.GenerateBatchAsync(
                requests,
                CancellationToken.None);
        int distinctPackages = drafts.Select(static draft =>
                $"{draft.Title}\n{draft.Description}")
            .Distinct(StringComparer.Ordinal)
            .Count();
        string audienceCopy = string.Join(
            '\n',
            drafts.SelectMany(static draft =>
                new[] { draft.Title, draft.Description }));

        TestAssert.True(
            distinctPackages >= 3,
            "Same-class document clips should rotate among grounded, candidate-scoped packages instead of sharing one attempt-zero phrase.");
        TestAssert.True(
            drafts.All(static draft =>
                draft.Title.Contains(
                    "Document",
                    StringComparison.Ordinal) &&
                draft.Description.Contains(
                    "document",
                    StringComparison.OrdinalIgnoreCase)),
            "Document evidence should remain document-grounded across every wording variant.");
        foreach (string retired in new[]
                 {
                     "A New Piece of the Story Emerged",
                     "another piece of the story",
                 })
        {
            TestAssert.False(
                audienceCopy.Contains(retired, StringComparison.OrdinalIgnoreCase),
                $"Retired abstract document wording must not return: {retired}");
        }
    }

    private static async Task ObjectiveInstructionsStayGrounded()
    {
        ClipEditorialMetadataDraft draft =
            await new HeuristicClipEditorialMetadataGenerator().GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateVisualHeuristicContext(
                        "candidate-objective",
                        "visual-objective",
                        "Proceed further into the Bureau."),
                    ClipEditorialProfile.Default,
                    attempt: 0),
                CancellationToken.None);

        TestAssert.True(
            draft.Title.Contains("Objective", StringComparison.Ordinal) &&
            draft.Description.Contains(
                "objective",
                StringComparison.OrdinalIgnoreCase),
            "A proceed-further instruction should select the bounded objective family instead of a generic gameplay package.");
        TestAssert.False(
            draft.Title.Contains(
                "The Next Beat Landed",
                StringComparison.OrdinalIgnoreCase) ||
            draft.Description.Contains(
                "new beat",
                StringComparison.OrdinalIgnoreCase),
            "Objective evidence must never fall back to the retired beat metaphor.");
    }

    private static async Task AttemptZeroHeuristicBatchDoesNotCollapse()
    {
        const int hiddenMomentCount = 78;
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator());
        ClipEditorialMetadataRequest[] requests = Enumerable.Range(
                0,
                hiddenMomentCount)
            .Select(index => new ClipEditorialMetadataRequest(
                CreateContext(
                    transcripts: [],
                    candidateId: $"hidden-{index:D2}"),
                ClipEditorialProfile.Default,
                attempt: 0,
                preference:
                    ClipEditorialGenerationPreference.HeuristicOnly))
            .ToArray();

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await service.GenerateBatchAsync(
                requests,
                CancellationToken.None);
        int distinctPackages = drafts.Select(static draft =>
                $"{draft.Title}\n{draft.Description}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        TestAssert.Equal(
            hiddenMomentCount,
            drafts.Count,
            "Every hidden-moment request must retain one working draft.");
        TestAssert.True(
            distinctPackages >= 8,
            "A large all-attempt-zero batch must distribute across the bounded presentation styles instead of repeating one package 78 times.");
        TestAssert.True(
            drafts.All(static draft =>
                draft.Attempt == 0 &&
                draft.Title.EndsWith(
                    "#ExampleGame",
                    StringComparison.Ordinal) &&
                !draft.Title.Contains(
                    "hidden-",
                    StringComparison.OrdinalIgnoreCase) &&
                !draft.Description.Contains(
                    "hidden-",
                    StringComparison.OrdinalIgnoreCase)),
            "Candidate identity may select presentation style but must never become audience copy.");
        string audienceCopy = string.Join(
            '\n',
            drafts.SelectMany(static draft =>
                new[] { draft.Title, draft.Description }));
        foreach (string retired in new[]
                 {
                     "The Next Beat Landed",
                     "new beat",
                     "moment took shape",
                     "story emerged",
                 })
        {
            TestAssert.False(
                audienceCopy.Contains(retired, StringComparison.OrdinalIgnoreCase),
                $"Broad fallback copy must avoid retired abstract framing: {retired}");
        }
    }

    private static async Task MissingTranscriptWarns()
    {
        var generator = new HeuristicClipEditorialMetadataGenerator();
        ClipEditorialMetadataDraft result =
            await generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(transcripts: []),
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);

        TestAssert.True(
            result.Warnings.Any(
                static warning =>
                    warning.Code ==
                    ClipEditorialWarningCode.TranscriptUnavailable),
            "Missing transcript warning.");
        TestAssert.False(
            result.Description.Contains('“'),
            "No transcript means no invented quotation.");
    }

    private static async Task OptionalAiRequiresProvider()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic);
        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiWhenAvailable),
                CancellationToken.None),
                "AI-when-available must fail closed when the provider is unavailable.");

        TestAssert.Equal(
            ClipEditorialAiFailureKind.ProviderUnavailable,
            exception.FailureKind,
            "Unavailable optional AI failure kind.");
        TestAssert.Equal("candidate-01", exception.CandidateId,
            "Unavailable provider failures retain candidate identity.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Optional AI must not silently invoke the heuristic generator.");
    }

    private static async Task HeuristicOnlyPreservesDraftState()
    {
        var heuristic = new RichDraftMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(heuristic);

        ClipEditorialMetadataDraft result = await service.GenerateAsync(
            new ClipEditorialMetadataRequest(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.HeuristicOnly),
            CancellationToken.None);

        TestAssert.Same(heuristic.Provenance, result.AiProvenance!,
            "Explicit heuristic mode returns the generator draft unchanged.");
        TestAssert.Equal(ClipEditorialMetadataReadiness.UserApproved,
            result.Readiness,
            "Explicit heuristic mode must not rewrite readiness.");
        TestAssert.True(result.QualityIssues.Any(static issue =>
                issue.Code ==
                    ClipEditorialMetadataQualityIssueCode.GenericOpening),
            "Explicit heuristic mode preserves existing quality diagnostics.");
        TestAssert.Equal("Earlier accepted title #ExampleGame",
            result.PriorAcceptedTitles.Single(),
            "Explicit heuristic mode preserves reroll history.");
    }

    private static async Task OptionalAiFailurePropagates()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            new FailingBatchMetadataGenerator());
        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        CreateContext(),
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.AiWhenAvailable),
                    CancellationToken.None),
                "Optional AI provider failures must propagate.");

        TestAssert.Equal(
            ClipEditorialAiFailureKind.ProviderFailed,
            exception.FailureKind,
            "Optional AI provider failure kind.");
        TestAssert.True(exception.InnerException is InvalidDataException,
            "The typed failure must preserve the provider exception.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Provider failure must not invoke the heuristic generator.");
    }

    private static async Task OptionalAiBatchFailurePropagatesOnce()
    {
        var ai = new PartiallyFailingBatchMetadataGenerator();
        var heuristic = new RecordingFallbackMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
            new(
                CreateContext(candidateId: "candidate-02"),
                ClipEditorialProfile.Default,
                1,
                ClipEditorialGenerationPreference.AiWhenAvailable),
        ];

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateBatchAsync(
                    requests,
                    CancellationToken.None),
                "Optional AI batch failures must propagate.");

        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            exception.FailureKind,
            "Failed AI batch failure kind.");
        TestAssert.Equal(1, ai.BatchCalls, "The normal fast batch path is attempted once.");
        TestAssert.Equal(0, ai.SingleCalls,
            "A failed batch must not fan out into hidden per-clip model runs.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "A failed AI batch must not synthesize heuristic rows.");
    }

    private static async Task FailSoftBatchRejectsFailedCase()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new FailSoftBatchMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                CreateContext(candidateId: "candidate-01"),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
            new(
                CreateContext(candidateId: "candidate-02"),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
            new(
                CreateContext(candidateId: "candidate-03"),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
        ];

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateBatchAsync(
                    requests,
                    CancellationToken.None),
                "A typed per-case rejection must fail the AI batch.");

        TestAssert.Equal(ClipEditorialAiFailureKind.CaseRejected,
            exception.FailureKind,
            "Per-case failure kind.");
        TestAssert.Equal("candidate-02", exception.CandidateId,
            "Per-case failure retains the rejected candidate identity.");
        TestAssert.True(exception.Message.Contains(
                "NoDistinctPrimaryVisualEvent",
                StringComparison.Ordinal),
            "The bounded typed provider state must remain actionable.");
        TestAssert.Equal(1, ai.OutcomeBatchCalls,
            "The GPU-backed fail-soft batch must run exactly once.");
        TestAssert.Equal(0, ai.LegacyBatchCalls,
            "The service must consume typed outcomes without rerunning the batch.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "A failed AI row must not invoke deterministic fallback.");
    }

    private static async Task AiBatchRetriesOnlyNoveltyFailures()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new NoveltyRetryMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialMetadataRequest[] requests =
        [
            new ClipEditorialMetadataRequest(
                    CreateContext(candidateId: "candidate-01"),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired)
                .WithVariantIntent(ClipEditorialVariantIntent.DirectAction),
            new ClipEditorialMetadataRequest(
                    CreateContext(candidateId: "candidate-02"),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired)
                .WithVariantIntent(
                    ClipEditorialVariantIntent.SpecificCuriosity),
            new ClipEditorialMetadataRequest(
                    CreateContext(candidateId: "candidate-03"),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired)
                .WithVariantIntent(
                    ClipEditorialVariantIntent.OutcomeFocused),
        ];

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await service.GenerateBatchAsync(requests, CancellationToken.None);

        TestAssert.Equal(3, drafts.Count,
            "Novelty retries preserve the full batch.");
        TestAssert.Equal(
            "Door Fight Turned Around #ExampleGame",
            drafts[0].Title,
            "The initially accepted title must not be regenerated.");
        TestAssert.Equal(
            "The Reactor Opened a New Route #ExampleGame",
            drafts[1].Title,
            "The colliding row must use its accepted retry.");
        TestAssert.Equal(
            "A Hidden Door Finally Opened #ExampleGame",
            drafts[2].Title,
            "The abstract row may use the second bounded AI retry.");
        TestAssert.True(
            ai.BatchRequests.Select(static batch => batch.Length)
                .SequenceEqual([3, 2, 1]),
            "Only rejected rows may be resubmitted to AI.");
        TestAssert.Equal(0, ai.SingleCalls,
            "Batch novelty retries must stay on the batch provider path.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Novelty retries must never invoke heuristic metadata.");

        ClipEditorialMetadataRequest[] firstRetry = ai.BatchRequests[1];
        TestAssert.True(firstRetry.All(static request =>
                request.Attempt == 1 &&
                request.RevisionKind ==
                    ClipEditorialRevisionKind.StructuralReroll),
            "The first retry must be identified as a structural reroll.");
        TestAssert.Equal(ClipEditorialVariantIntent.OutcomeFocused,
            firstRetry[0].VariantIntent,
            "The colliding SpecificCuriosity row rotates to OutcomeFocused.");
        TestAssert.Equal(ClipEditorialVariantIntent.DirectAction,
            firstRetry[1].VariantIntent,
            "The abstract OutcomeFocused row rotates to DirectAction.");
        TestAssert.True(firstRetry.All(request =>
                request.PriorAcceptedTitleExclusions.Any(exclusion =>
                    exclusion.Title.Equals(
                        "Door Fight Turned Around #ExampleGame",
                        StringComparison.OrdinalIgnoreCase)) &&
                request.PriorAcceptedTitleExclusions.All(exclusion =>
                    exclusion.CandidateId.Equals(
                        request.Context.CandidateId,
                        StringComparison.Ordinal))),
            "Accepted batch titles must be rescaled as exact-cut exclusions for each retry.");
        ClipEditorialMetadataRequest secondRetry = ai.BatchRequests[2].Single();
        TestAssert.Equal(2, secondRetry.Attempt,
            "The second retry must advance from the original attempt.");
        TestAssert.Equal(ClipEditorialRevisionKind.StructuralReroll,
            secondRetry.RevisionKind,
            "The second retry must remain a structural reroll.");
        TestAssert.Equal(ClipEditorialVariantIntent.SpecificCuriosity,
            secondRetry.VariantIntent,
            "The final retry rotates to the last remaining initial intent.");
        TestAssert.True(secondRetry.PriorAcceptedTitleExclusions.Any(
                exclusion => exclusion.Title.Equals(
                    "The Reactor Opened a New Route #ExampleGame",
                    StringComparison.OrdinalIgnoreCase)),
            "Newly accepted retry titles constrain later colliding rows.");
    }

    private static async Task AiBatchRetriesCurrentMetadataRegressions()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new CurrentRegressionMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialMetadataRequest[] requests = Enumerable.Range(1, 5)
            .Select(index => new ClipEditorialMetadataRequest(
                CreateContext(candidateId: $"candidate-{index:00}"),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired))
            .ToArray();

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await service.GenerateBatchAsync(requests, CancellationToken.None);

        TestAssert.True(
            ai.BatchRequests.Select(static batch => batch.Length)
                .SequenceEqual([5, 5]),
            "Literal reports, title-paraphrase descriptions, and provider review flags must resubmit only through AI.");
        TestAssert.True(
            drafts.Select(static draft => draft.Title).SequenceEqual(
                [
                    "The office decor survived another FBC disaster #ExampleGame",
                    "Rubber duckies violated FBC policy #ExampleGame",
                    "The shark experiment raised one obvious question #ExampleGame",
                    "The clearance lock kept those collectibles secret #ExampleGame",
                    "That intro gave me immediate X-Files vibes #ExampleGame",
                ]),
            "The accepted five-clip batch should retain distinct creator-shaped theses.");
        TestAssert.True(
            Enumerable.Range(0, drafts.Count).All(index =>
                ClipEditorialMetadataQuality.Evaluate(
                    drafts[index].Title,
                    drafts[index].Description,
                    requests[index].Context).Count == 0),
            "Every accepted package must clear provider-neutral audience-copy quality.");
        TestAssert.Equal(
            0,
            heuristic.Requests.Count,
            "Quality recovery must not fall back to heuristic copy while AI is enabled.");
    }

    private static async Task SingleAiRequestsUseSharedQualityReview()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new CurrentRegressionMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(candidateId: "candidate-02"),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft draft = await service.GenerateAsync(
            request,
            CancellationToken.None);

        TestAssert.Equal(
            "Rubber duckies violated FBC policy #ExampleGame",
            draft.Title,
            "A one-clip application request must return the corrected audience-shaped package, not the literal first draft.");
        TestAssert.True(
            ai.BatchRequests.Select(static batch => batch.Length)
                .SequenceEqual([1, 1]),
            "A batch-capable provider must receive the same bounded singleton retry sequence used by Generate.");
        TestAssert.Equal(
            0,
            ai.BatchRequests[0].Single().Attempt,
            "The first singleton batch preserves the caller's attempt.");
        TestAssert.Equal(
            1,
            ai.BatchRequests[1].Single().Attempt,
            "The shared review advances the corrective retry exactly once.");
        TestAssert.Equal(
            ClipEditorialRevisionKind.StructuralReroll,
            ai.BatchRequests[1].Single().RevisionKind,
            "The singleton correction must retain the same structural-reroll contract as a normal batch.");
        TestAssert.Equal(
            0,
            heuristic.Requests.Count,
            "A one-clip AI correction must not cross into heuristic metadata.");
    }

    private static async Task DescriptionOnlyRetryRetainsTitle()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new DescriptionOnlyRetryMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft draft = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.Equal(
            "The Clearance Lock Kept Those Collectibles Secret #ExampleGame",
            draft.Title,
            "A description-only correction may retain a grounded title.");
        TestAssert.Equal(2, ai.BatchRequests.Count,
            "The description receives one bounded AI retry.");
        TestAssert.Equal(1, ai.BatchRequests[1].Single().Attempt,
            "A description retry must advance the provider attempt state.");
        TestAssert.Equal(ClipEditorialRevisionKind.StructuralReroll,
            ai.BatchRequests[1].Single().RevisionKind,
            "A description retry must request a structural rewrite.");
        TestAssert.True(
            ai.BatchRequests[1].Single().PriorAcceptedTitleExclusions.All(
                exclusion => !exclusion.Title.Equals(
                    draft.Title,
                    StringComparison.OrdinalIgnoreCase)),
            "Description-only quality must not mislabel the title as rejected.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Description correction must stay on the AI path.");
    }

    private static async Task AiBatchRetryExhaustionRetainsBestGroundedDraft()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new ExhaustedReviewableBatchMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
        ];

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await service.GenerateBatchAsync(requests, CancellationToken.None);

        ClipEditorialMetadataDraft retained = drafts.Single();
        TestAssert.Equal(
            "The Locked Door Interrupted the Search #ExampleGame",
            retained.Title,
            "The cleanest safe AI package survives bounded rewrite exhaustion.");
        TestAssert.True(retained.QualityIssues.Any(static issue =>
                issue.Code ==
                    ClipEditorialMetadataQualityIssueCode.AudienceCopyReview &&
                issue.Message.Contains(
                    "stayed too literal",
                    StringComparison.OrdinalIgnoreCase)),
            "The retained AI package keeps its provider advisory issue.");
        TestAssert.True(retained.Warnings.Any(static warning =>
                warning.Code ==
                    ClipEditorialWarningCode.MetadataReviewRequired),
            "Exhaustion explicitly marks the retained AI package for review.");
        TestAssert.True(retained.Warnings.Any(static warning =>
                warning.Code == ClipEditorialWarningCode.AiDraftRegenerated),
            "The retained package records that bounded AI rewrites ran.");
        TestAssert.Equal(ClipEditorialMetadataOrigin.AiAssisted,
            retained.Origin,
            "Exhaustion must retain AI-authored copy, never heuristics.");
        TestAssert.True(retained.AiProvenance is not null,
            "The retained package preserves AI provenance.");
        TestAssert.Equal(3, ai.BatchRequests.Count,
            "Editorial recovery uses one initial batch and at most two AI retries.");
        TestAssert.True(
            ai.BatchRequests.Select(static batch => batch.Single().Attempt)
                .SequenceEqual([0, 1, 2]),
            "Bounded AI recovery must expose each retry attempt to the provider.");
        TestAssert.Equal(2, retained.Attempt,
            "The retained best draft records the targeted retry that authored it.");
        TestAssert.True(ai.BatchRequests.All(static batch => batch.Length == 1),
            "Only the unresolved package is retried.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "AI rewrite exhaustion must not invoke heuristic metadata.");
    }

    private static async Task AiBatchRetryExhaustionRetainsReadableTextReview()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RepeatedReviewBatchMetadataGenerator(
            "The Red Hallway Led to a Locked Door #ExampleGame",
            "I followed the red hallway until a locked door stopped the route.",
            providerRuleCode: "UnstableReadableTextReuse");
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
        ];

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            requests,
            CancellationToken.None)).Single();

        TestAssert.Equal(ClipEditorialMetadataOrigin.AiAssisted,
            retained.Origin,
            "Review exhaustion must retain AI-authored copy.");
        TestAssert.True(retained.AiProvenance is not null,
            "Review exhaustion must preserve exact AI provenance.");
        TestAssert.True(retained.QualityIssues.Any(static issue =>
                issue.SourceRuleCode == "UnstableReadableTextReuse"),
            "The retained draft keeps its typed readable-text review finding.");
        TestAssert.True(retained.Warnings.Any(static warning =>
                warning.Code ==
                    ClipEditorialWarningCode.MetadataReviewRequired),
            "The retained draft clearly asks for optional review.");
        TestAssert.Equal(3, ai.BatchRequests.Count,
            "The provider receives exactly three bounded AI attempts.");
        TestAssert.True(
            ai.BatchRequests.Select(static batch => batch.Single().Attempt)
                .SequenceEqual([0, 1, 2]),
            "Review recovery advances every bounded AI attempt.");
        TestAssert.Equal(2, retained.Attempt,
            "Tie-breaking retains the latest provenance-valid AI attempt.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Review exhaustion must never invoke heuristic metadata.");
    }

    private static async Task AiBatchRetryExhaustionRetainsWaitAndAsrReview()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RepeatedReviewBatchMetadataGenerator(
            "The Red Door Held Up the Route #ExampleGame",
            "I waited for the red door to open while the alarm kept sounding.");
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialContext context = CreateContext(
            transcripts:
            [
                new ClipEditorialTranscriptContext(
                    1,
                    new AudioContentRoleAssignment(
                        AudioContentRole.CreatorSpeech,
                        AudioContentRoleSource.UserConfirmed),
                    "I waited for the red door to open while the alarm kept sounding.",
                    ClipEditorialTranscriptAuthority.AutomaticUnreviewed),
            ]);
        var request = new ClipEditorialMetadataRequest(
            context,
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.True(retained.QualityIssues.Any(static issue =>
                issue.Code ==
                    ClipEditorialMetadataQualityIssueCode.UnsupportedMentalState),
            "A lexical wait-for finding remains visible for review.");
        TestAssert.True(retained.QualityIssues.Any(static issue =>
                issue.Code == ClipEditorialMetadataQualityIssueCode
                    .UnreviewedTranscriptReuse),
            "Automatic-transcript overlap remains visible for review.");
        TestAssert.Equal(3, ai.BatchRequests.Count,
            "Local review findings receive only the bounded AI attempts.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Local lexical review findings never invoke heuristic metadata.");
    }

    private static async Task ReusableDescriptionSignatureIsExcludedFromEditorialReview()
    {
        const string signature =
            "This video shows a person waiting for something unseen.";
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RepeatedReviewBatchMetadataGenerator(
            "The Red Door Changed the Route #ExampleGame",
            "The hallway opened into a different route.");
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        ClipEditorialContext context = CreateContext(
            transcripts:
            [
                new ClipEditorialTranscriptContext(
                    1,
                    new AudioContentRoleAssignment(
                        AudioContentRole.CreatorSpeech,
                        AudioContentRoleSource.UserConfirmed),
                    signature,
                    ClipEditorialTranscriptAuthority.AutomaticUnreviewed),
            ]);
        var profile = new ClipEditorialProfile(
            reusableDescriptionSignature: signature);
        var request = new ClipEditorialMetadataRequest(
            context,
            profile,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.Equal(
            "The hallway opened into a different route." +
                Environment.NewLine + Environment.NewLine + signature,
            retained.Description,
            "The full reusable signature remains stored on the selected draft.");
        TestAssert.Equal(1, ai.BatchRequests.Count,
            "Signature-only lexical matches must not trigger an AI retry.");
        TestAssert.Equal(0, retained.QualityIssues.Count,
            "Signature text is outside generated-copy quality evaluation.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Signature handling never invokes heuristic metadata.");
    }

    private static async Task InternalProcessAiCopyRemainsHardFailure()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RepeatedReviewBatchMetadataGenerator(
            "Visual Evidence Supports the Observation #ExampleGame",
            "The red hallway ended at a locked door.");
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateBatchAsync(
                    [request],
                    CancellationToken.None),
                "Internal process wording must remain a hard AI postcondition.");

        TestAssert.Equal(ClipEditorialAiFailureKind.UnsafeOutput,
            exception.FailureKind,
            "Internal process wording retains its typed hard failure.");
        TestAssert.Equal(1, ai.BatchRequests.Count,
            "A hard internal-process violation stops before editorial retries.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "A hard AI failure must not silently invoke heuristics.");
    }

    private static async Task
        AiBatchRetryExhaustionRejectsLaterIncompleteTitle()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RecoveredHallwayRetryMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.Equal(
            "I opened menu, entered room with red-lit hallway ahead #ExampleGame",
            retained.Title,
            "A complete earlier AI draft must beat a later title that ends mid-word.");
        TestAssert.Equal(
            "I opened menu, viewed collectibles, entered room with red-lit hallway ahead.",
            retained.Description,
            "The retained package must keep the matching complete description.");
        TestAssert.Equal(1, retained.Attempt,
            "Retention must preserve the provider attempt that authored the safe package.");
        TestAssert.Equal(3, ai.BatchRequests.Count,
            "The fixture must exercise initial generation and both bounded rewrites.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Incomplete AI copy must never cause a hidden heuristic fallback.");
    }

    private static async Task
        AiBatchRetryExhaustionRetainsIncompleteTitleReview()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RepeatedReviewBatchMetadataGenerator(
            "A Red Hallway Ends at the Locked Door #ExampleGame",
            "The red hallway ended at a locked door.",
            "IncompleteTitle");
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.Equal(ClipEditorialMetadataOrigin.AiAssisted,
            retained.Origin,
            "A provider editorial finding must not erase validated AI provenance.");
        TestAssert.True(retained.QualityIssues.Any(static issue =>
                issue.SourceRuleCode == "IncompleteTitle"),
            "The retained draft must expose the typed review finding.");
        TestAssert.True(retained.Warnings.Any(static warning => warning.Code ==
                ClipEditorialWarningCode.MetadataReviewRequired),
            "Retry exhaustion must make the retained copy's review state explicit.");
        TestAssert.Equal(3, ai.BatchRequests.Count,
            "All three bounded AI attempts must be exercised by the regression.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Advisory exhaustion must never invoke heuristic metadata.");
    }

    private static async Task AiCorrectiveRewriteFailureRetainsPriorDraft()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new ReviewThenFailBatchMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.Equal(
            "The Red Hallway Led to a Locked Door #ExampleGame",
            retained.Title,
            "A failed corrective rewrite must preserve the prior validated AI copy.");
        TestAssert.Equal(ClipEditorialMetadataOrigin.AiAssisted,
            retained.Origin,
            "Corrective failure continuity must retain AI authorship.");
        TestAssert.True(retained.AiProvenance is not null,
            "Corrective failure continuity must preserve exact AI provenance.");
        TestAssert.Equal(0, retained.Attempt,
            "The retained attempt remains the one that authored the draft.");
        TestAssert.True(retained.QualityIssues.Any(static issue =>
                issue.SourceRuleCode == "UnstableReadableTextReuse"),
            "The prior AI draft keeps its typed advisory finding.");
        TestAssert.True(retained.Warnings.Any(static warning => warning.Code ==
                ClipEditorialWarningCode.MetadataReviewRequired),
            "A failed corrective rewrite leaves an explicit review warning.");
        TestAssert.Equal(2, ai.BatchCalls,
            "The fixture must fail only after one validated AI batch exists.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Corrective AI failure must never invoke heuristic metadata.");
    }

    private static async Task AiBatchRetryExhaustionRanksTypedReviewRisk()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RiskRankedReviewBatchMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.Equal(0, retained.Attempt,
            "Two ordinary style advisories must outrank one serious grounding or text-integrity risk.");
        TestAssert.True(retained.QualityIssues.Count(static issue =>
                issue.SourceRuleCode is "EditorialFrameDrift" or
                    "ThirdPersonCreatorFraming") == 2,
            "The selected lower-risk draft keeps both typed style advisories.");
        TestAssert.Equal(3, ai.BatchCalls,
            "Risk ranking runs only after all three bounded AI attempts.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Risk-ranked retention must never invoke heuristic metadata.");
    }

    private static async Task RetiredGenericAiFillerRemainsAdvisory()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new RepeatedReviewBatchMetadataGenerator(
            "The Red Hallway Changed the Route #ExampleGame",
            "A new piece of the story emerged.");
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai);
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        ClipEditorialMetadataDraft retained = (await service.GenerateBatchAsync(
            [request],
            CancellationToken.None)).Single();

        TestAssert.True(retained.QualityIssues.Any(static issue =>
                issue.SourceRuleCode == "LocalAudienceCopyBoundary"),
            "Retired generic filler must remain a typed local review finding.");
        TestAssert.Equal(3, ai.BatchRequests.Count,
            "Generic filler receives every bounded AI rewrite before retention.");
        TestAssert.Equal(ClipEditorialMetadataOrigin.AiAssisted,
            retained.Origin,
            "Generic filler exhaustion retains AI authorship for user review.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Generic filler review must never invoke heuristic metadata.");
    }

    private static async Task AiDraftPostconditionIsRequired()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        foreach (InvalidAiDraftKind invalidKind in
                 Enum.GetValues<InvalidAiDraftKind>())
        {
            var ai = new InvalidAiDraftMetadataGenerator(invalidKind);
            var service = new ClipEditorialMetadataGenerationService(
                heuristic,
                ai);
            ClipEditorialAiGenerationException exception =
                await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                    () => service.GenerateAsync(
                        new ClipEditorialMetadataRequest(
                            CreateContext(),
                            ClipEditorialProfile.Default,
                            0,
                            ClipEditorialGenerationPreference.AiRequired),
                        CancellationToken.None),
                    $"Invalid AI draft state must fail closed: {invalidKind}.");

            TestAssert.Equal(ClipEditorialAiFailureKind.IncompleteResult,
                exception.FailureKind,
                $"Invalid AI postcondition kind: {invalidKind}.");
            TestAssert.Equal(0, ai.Calls,
                $"A batch-capable provider must not use its stale direct path: {invalidKind}.");
            TestAssert.Equal(1, ai.BatchCalls,
                $"The direct application request runs one reviewed singleton batch: {invalidKind}.");
            ClipEditorialAiGenerationException batchException =
                await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                    () => service.GenerateBatchAsync(
                        [
                            new ClipEditorialMetadataRequest(
                                CreateContext(),
                                ClipEditorialProfile.Default,
                                0,
                                ClipEditorialGenerationPreference.AiRequired),
                        ],
                        CancellationToken.None),
                    $"Invalid AI batch draft state must fail closed: {invalidKind}.");
            TestAssert.Equal(ClipEditorialAiFailureKind.IncompleteResult,
                batchException.FailureKind,
                $"Invalid batch AI postcondition kind: {invalidKind}.");
            TestAssert.Equal(2, ai.BatchCalls,
                $"Each invalid singleton request runs exactly one batch attempt: {invalidKind}.");
        }

        TestAssert.Equal(0, heuristic.Requests.Count,
            "Invalid AI draft state must never invoke heuristic metadata.");
    }

    private static Task GroundedCaseFailureRowsAreBounded()
    {
        ClipEditorialMetadataRequest request = new(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiWhenAvailable);
        const string row = """
            {
              "candidateId": "candidate-01",
              "attempt": 0,
              "caseFailure": {
                "errorCode": "NoDistinctPrimaryVisualEvent"
              }
            }
            """;
        using JsonDocument document = JsonDocument.Parse(row);
        ClipEditorialMetadataCaseFailure failure =
            Qwen3VlGroundedMetadataResultParser.ParseCaseFailure(
                document.RootElement,
                request,
                Qwen3VlGroundedMetadataGenerator.OutputSchema);
        TestAssert.Equal(
            ClipEditorialMetadataCaseFailureCode.NoDistinctPrimaryVisualEvent,
            failure.Code,
            "The fixed failure code must retain its typed meaning.");

        _ = TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataResultParser.ParseCaseFailure(
                document.RootElement,
                request,
                Qwen3VlGroundedMetadataGenerator
                    .PreviousWholeBatchOutputSchema),
            "Older whole-batch schemas must reject partial result rows.");
        using JsonDocument unbounded = JsonDocument.Parse(
            row.Replace(
                "\"errorCode\": \"NoDistinctPrimaryVisualEvent\"",
                "\"errorCode\": \"NoDistinctPrimaryVisualEvent\", " +
                "\"message\": \"raw provider detail\"",
                StringComparison.Ordinal));
        _ = TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataResultParser.ParseCaseFailure(
                unbounded.RootElement,
                request,
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "Per-case rows must reject arbitrary diagnostic text.");
        return Task.CompletedTask;
    }

    private static async Task AiBatchFailureDropsTransientReviews()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new FailingVisualBatchMetadataGenerator();
        var materializer = new RecordingReviewVideoMaterializer();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            ai,
            materializer);
        ClipEditorialContext firstContext = CreateContext();
        ClipEditorialContext secondContext = CreateContext(
            candidateId: "candidate-02");
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                firstContext,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable,
                TestMediaFactory.Create(
                    firstContext.SourceFullPath,
                    firstContext.SourceDuration)),
            new(
                secondContext,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable,
                TestMediaFactory.Create(
                    secondContext.SourceFullPath,
                    secondContext.SourceDuration)),
        ];

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateBatchAsync(
                    requests,
                    CancellationToken.None),
                "A failed visual AI batch must propagate after cleanup.");

        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            exception.FailureKind,
            "Visual AI batch failure kind.");
        TestAssert.Equal(1, ai.BatchCalls, "The visual batch runs once.");
        TestAssert.True(ai.SawVerifiedReviews,
            "The visual provider receives both bounded review videos.");
        TestAssert.Equal(2, materializer.Calls,
            "Each request gets one bounded visual review.");
        TestAssert.Equal(2, materializer.Cleanups,
            "Every transient visual review is cleaned after the batch.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "A failed visual AI batch must not invoke deterministic fallback.");
    }

    private static Task StructuredQwenFailureIsActionable()
    {
        var process = new ProcessRunResult(
            4,
            string.Empty,
            "Loading checkpoint shards...\n" +
            "{\"errorCode\":\"InferenceError\",\"message\":\"Grounded metadata assigned an unsupported mental state.\"}\n",
            TimeSpan.FromSeconds(5));

        TestAssert.Equal(
            "InferenceError: Grounded metadata assigned an unsupported mental state.",
            Qwen3VlProcessOutputReader.FailureSummary(process),
            "The structured host failure should be separated from noisy model progress.");
        return Task.CompletedTask;
    }

    private static Task GroundedFailureArchiveIsBounded()
    {
        using var directory = new EditorialTestDirectory();
        string source = Path.Combine(directory.Path, "failure.json");
        string archiveRoot = Path.Combine(directory.Path, "archive");
        var archive = new SystemQwen3VlGroundedFailureArchive(archiveRoot);
        for (int ordinal = 0;
             ordinal < SystemQwen3VlGroundedFailureArchive.MaximumRetainedFiles + 2;
             ordinal++)
        {
            File.WriteAllText(source, $"{{\"ordinal\":{ordinal}}}");
            Qwen3VlGroundedFailureArchiveResult result =
                archive.Archive(source, maximumBytes: 1024);
            TestAssert.True(
                result.ArchivedPath is not null &&
                File.Exists(result.ArchivedPath) &&
                result.Warning is null,
                "Each bounded failure envelope must be retained exactly once.");
        }
        TestAssert.Equal(
            SystemQwen3VlGroundedFailureArchive.MaximumRetainedFiles,
            Directory.EnumerateFiles(archiveRoot, "*.json").Count(),
            "The local failure archive must prune beyond its fixed bound.");
        Qwen3VlGroundedFailureArchiveResult oversized =
            archive.Archive(source, maximumBytes: 1);
        TestAssert.True(
            oversized.ArchivedPath is null && oversized.Warning is not null,
            "An oversized failure payload must not enter diagnostics storage.");
        return Task.CompletedTask;
    }

    private static async Task GroundedQwenExecutorAttachesFailureEnvelope()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        using var archiveDirectory = new EditorialTestDirectory();
        string? capturedFailurePath = null;
        var runner = new FailureArtifactProcessRunner(processRequest =>
        {
            string[] arguments = processRequest.Arguments.ToArray();
            int pathIndex = Array.IndexOf(arguments, "--failure-output");
            TestAssert.True(pathIndex >= 0 && pathIndex + 1 < arguments.Length,
                "Grounded execution must request one failure artifact.");
            capturedFailurePath = arguments[pathIndex + 1];
            TestAssert.Equal(
                processRequest.WorkingDirectory,
                Path.GetDirectoryName(capturedFailurePath),
                "The default failure artifact must be owned by the bounded workspace.");
            File.WriteAllText(
                capturedFailurePath,
                CreateStartupMemoryFailureJson(
                    fixture.Runtime.Model.ManifestSha256));
            return new ProcessRunResult(
                3,
                string.Empty,
                "{\"errorCode\":\"InitializationError\",\"message\":\"Grounded CUDA startup admission was rejected.\"}",
                TimeSpan.FromMilliseconds(20));
        });
        using var generator = new Qwen3VlGroundedMetadataGenerator(
            fixture.Runtime,
            runner,
            new SystemQwen3VlBatchWorkspaceFactory(),
            new SystemQwen3VlGroundedFailureArchive(
                archiveDirectory.Path));

        Qwen3VlInferenceException exception =
            await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
                () => generator.GenerateAsync(
                    fixture.CreateRequest(),
                    CancellationToken.None),
                "The fake failed process must surface its typed envelope.");

        TestAssert.True(exception.HostFailure is
        {
            Failure.ErrorCode: Qwen3VlHostErrorCode.InitializationError,
            GroundedMemoryPolicy.RuntimeOutcome:
                    "StartupAdmissionRejected",
        },
            "The executor must attach the validated 1.4 memory-policy failure.");
        TestAssert.True(
            exception.FailureEnvelopeParseException is null,
            "A valid failure envelope must not retain a parse warning.");
        TestAssert.True(capturedFailurePath is not null &&
            !File.Exists(capturedFailurePath) &&
            !Directory.Exists(Path.GetDirectoryName(capturedFailurePath)),
            "Owned failure telemetry must be cleaned with its batch workspace.");
        string[] retained = Directory.GetFiles(
            archiveDirectory.Path,
            "*.json",
            SearchOption.TopDirectoryOnly);
        TestAssert.Equal(1, retained.Length,
            "The exact failure envelope must survive workspace cleanup.");
        TestAssert.True(
            exception.DiagnosticDetails?.Contains(
                retained[0],
                StringComparison.OrdinalIgnoreCase) == true,
            "Technical details must identify the retained local envelope.");
    }

    private static async Task GroundedQwenSerializesOnlyWireAuthorizedVisualText()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        string? inputJson = null;
        var runner = new FailureArtifactProcessRunner(processRequest =>
        {
            inputJson = File.ReadAllText(Path.Combine(
                processRequest.WorkingDirectory!,
                "input-batch.json"));
            return new ProcessRunResult(
                2,
                string.Empty,
                "{\"errorCode\":\"UsageOrInputError\",\"message\":\"model-free stop\"}",
                TimeSpan.FromMilliseconds(20));
        });
        using var generator = new Qwen3VlGroundedMetadataGenerator(
            fixture.Runtime,
            runner,
            new SystemQwen3VlBatchWorkspaceFactory());

        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => generator.GenerateAsync(
                fixture.CreateRequestWithVisualText(),
                CancellationToken.None),
            "The model-free process intentionally stops after capturing input.");

        TestAssert.True(inputJson is not null, "The grounded request was captured.");
        using JsonDocument document = JsonDocument.Parse(inputJson!);
        JsonElement visualText = document.RootElement
            .GetProperty("requests")[0]
            .GetProperty("visualText");
        JsonElement grounding = visualText.GetProperty("groundingAnchors");
        JsonElement diagnostics = visualText.GetProperty("diagnosticAnchors");
        TestAssert.Equal(1, grounding.GetArrayLength(),
            "Punctuation-separated display text must not enter grounding anchors.");
        TestAssert.Equal(
            "Objective Updated",
            grounding[0].GetProperty("text").GetString(),
            "Whitespace-separated repeated OCR retains grounding authority.");
        TestAssert.True(
            diagnostics.EnumerateArray().Any(anchor =>
                anchor.GetProperty("text").GetString() == "MISSION:UPDATED"),
            "Rejected grounding text remains serialized as a diagnostic anchor.");
        JsonElement transcript = document.RootElement
            .GetProperty("requests")[0]
            .GetProperty("transcripts")[0];
        JsonElement spans = transcript.GetProperty("spans");
        TestAssert.Equal(2, spans.GetArrayLength(),
            "Qwen receives the bounded transcript timing rather than only a flattened paragraph.");
        TestAssert.Equal(
            961d,
            spans[0].GetProperty("startSeconds").GetDouble(),
            "Transcript spans retain absolute source time for clip-local grounding.");
        TestAssert.Equal(
            "I found the route.",
            spans[0].GetProperty("text").GetString(),
            "Timed creator commentary remains attached to its exact words.");
    }

    private static async Task GroundedQwenWithholdsUnconfirmedPathIdentity()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        string? inputJson = null;
        var runner = new FailureArtifactProcessRunner(processRequest =>
        {
            inputJson = File.ReadAllText(Path.Combine(
                processRequest.WorkingDirectory!,
                "input-batch.json"));
            return new ProcessRunResult(
                2,
                string.Empty,
                "{\"errorCode\":\"UsageOrInputError\",\"message\":\"model-free stop\"}",
                TimeSpan.FromMilliseconds(20));
        });
        using var generator = new Qwen3VlGroundedMetadataGenerator(
            fixture.Runtime,
            runner,
            new SystemQwen3VlBatchWorkspaceFactory());

        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => generator.GenerateAsync(
                fixture.CreateUnconfirmedPathRequest(),
                CancellationToken.None),
            "The model-free process intentionally stops after capturing input.");

        TestAssert.True(inputJson is not null, "The grounded request was captured.");
        TestAssert.False(
            inputJson!.Contains(
                "Recording Video Files",
                StringComparison.OrdinalIgnoreCase) ||
            inputJson.Contains(
                "RecordingVideoFiles",
                StringComparison.OrdinalIgnoreCase) ||
            inputJson.Contains(
                "path-derived note",
                StringComparison.OrdinalIgnoreCase),
            "The grounded wire request must not expose unconfirmed folder identity or notes.");
        using JsonDocument document = JsonDocument.Parse(inputJson);
        JsonElement game = document.RootElement
            .GetProperty("requests")[0]
            .GetProperty("game");
        TestAssert.Equal(
            ClipEditorialGameContext.UnconfirmedGameName,
            game.GetProperty("name").GetString(),
            "The grounded host receives only the neutral internal identity.");
        TestAssert.Equal(
            ClipEditorialGameContext.UnconfirmedGameHashtag,
            game.GetProperty("hashtag").GetString(),
            "The grounded host receives only the neutral internal hashtag.");
        TestAssert.Equal(
            ClipEditorialGameContextSource.SourcePathHint.ToString(),
            game.GetProperty("source").GetString(),
            "The neutral identity must remain explicitly unconfirmed.");
        TestAssert.True(
            game.GetProperty("notes").ValueKind == JsonValueKind.Null,
            "Unconfirmed notes must remain outside the model request.");
    }

    private static async Task GroundedQwenPrioritizesGameLinkedOcrEvidence()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        ClipEditorialContext context = CreateContext();
        TimeSpan[] timestamps =
        [
            context.SourceStart + TimeSpan.FromSeconds(1),
            context.SourceStart + TimeSpan.FromSeconds(2),
        ];
        VisualTextAnchor[] anchors = Enumerable.Range(1, 24)
            .Select(index => new VisualTextAnchor(
                $"evidence line {index:D2}",
                $"Evidence line {index:D2}",
                VisualTextAnchorAuthority.RepeatedAcrossFrames,
                timestamps))
            .ToArray();
        var visualText = new ClipVisualTextContext(
            context.CandidateId,
            context.SourceFullPath,
            NormalizedRectangle.FullFrame,
            frames: [],
            anchors);
        VisualTextAnchor linkedAnchor = visualText.GroundingAnchors[^1];
        const string passageText =
            "The licensed context shares the final stable interface phrase.";
        var source = new GameKnowledgeSource(
            "source-linked-ocr",
            GameKnowledgeSourceKind.Wikipedia,
            "Example Game",
            new Uri("https://example.invalid/example-game"),
            "revision-1",
            DateTimeOffset.UnixEpoch,
            "CC-BY-SA-4.0",
            new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
            "Example contributors",
            new string('a', 64));
        var passage = new GameKnowledgePassage(
            "passage-linked-ocr",
            source.Id,
            "Overview",
            passageText,
            GameKnowledgePassage.ComputeSha256(passageText));
        var snapshot = new GameKnowledgeSnapshot(
            context.GameContext.GameName,
            new GameKnowledgeProviderIdentity("fixture", "1.0"),
            DateTimeOffset.UnixEpoch,
            [source],
            [passage]);
        var match = new GameKnowledgeMatch(
            passage,
            GameKnowledgeMatchStrength.ClipLinked,
            0.9,
            ["evidence", "line"],
            [linkedAnchor.EvidenceId],
            GameKnowledgeTemporalRelation.CurrentEventCandidate);
        context = context
            .WithVisualText(visualText)
            .WithGameKnowledge(new ClipGameKnowledgeContext(
                context.GameContext.GameName,
                snapshot,
                [match]));

        string? inputJson = null;
        var runner = new FailureArtifactProcessRunner(processRequest =>
        {
            inputJson = File.ReadAllText(Path.Combine(
                processRequest.WorkingDirectory!,
                "input-batch.json"));
            return new ProcessRunResult(
                2,
                string.Empty,
                "{\"errorCode\":\"UsageOrInputError\",\"message\":\"model-free stop\"}",
                TimeSpan.FromMilliseconds(20));
        });
        using var generator = new Qwen3VlGroundedMetadataGenerator(
            fixture.Runtime,
            runner,
            new SystemQwen3VlBatchWorkspaceFactory());

        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => generator.GenerateAsync(
                fixture.CreateRequest(context),
                CancellationToken.None),
            "The model-free process intentionally stops after capturing input.");

        TestAssert.True(inputJson is not null, "The grounded request was captured.");
        using JsonDocument document = JsonDocument.Parse(inputJson!);
        JsonElement request = document.RootElement.GetProperty("requests")[0];
        JsonElement evidence = request.GetProperty("evidence");
        string[] evidenceIds = evidence.EnumerateArray()
            .Select(static item => item.GetProperty("id").GetString()!)
            .ToArray();
        string[] matchEvidenceIds = request.GetProperty("gameKnowledge")
            .GetProperty("matches")[0]
            .GetProperty("clipEvidenceIds")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        TestAssert.True(evidenceIds.Length <= 24,
            "The host evidence collection must retain its fixed wire bound.");
        TestAssert.True(evidenceIds.Contains(
                linkedAnchor.EvidenceId,
                StringComparer.Ordinal),
            "An OCR anchor referenced by licensed game context must be prioritized over unrelated overflow anchors.");
        TestAssert.True(matchEvidenceIds.SequenceEqual(
                [linkedAnchor.EvidenceId],
                StringComparer.Ordinal),
            "Every transmitted clip-linked knowledge ID must resolve to transmitted clip evidence.");
    }

    private static Task GroundedMissionRequiresLicensedGuideAuthority()
    {
        GroundedEditorialBrief encyclopedia = CreateMissionBrief(
            GameKnowledgeSourceKind.Wikipedia);
        GroundedGameContextClaim encyclopediaMission =
            encyclopedia.Claims.Single(static claim =>
                claim.Kind ==
                    GroundedGameContextClaimKind.MissionOrChapter);
        TestAssert.Equal(
            GroundedGameContextClaimState.Ambiguous,
            encyclopediaMission.State,
            "Wikipedia wording plus local OCR and visual evidence may nominate an exact mission, but cannot authorize it.");
        TestAssert.Equal(0, encyclopediaMission.FieldAuthorizations.Count,
            "An ambiguous exact mission must authorize no audience-copy field.");

        GroundedEditorialBrief guide = CreateMissionBrief(
            GameKnowledgeSourceKind.StrategyWiki);
        GroundedGameContextClaim supportedMission =
            guide.Claims.Single(static claim =>
                claim.Kind ==
                    GroundedGameContextClaimKind.MissionOrChapter);
        TestAssert.Equal(
            GroundedGameContextClaimState.Supported,
            supportedMission.State,
            "A licensed guide passage still requires compatible qualified visual evidence plus stable OCR before exact mission wording is supported.");
        TestAssert.Equal(1, supportedMission.PublicSourceIds.Count,
            "Public source authority must remain separate from local corroboration.");
        TestAssert.Equal(2, supportedMission.LocalEvidenceIds.Count,
            "The supported claim must retain both independent local evidence IDs.");
        return Task.CompletedTask;
    }

    private static Task StableLocalOcrAuthorizesLiteralFacts()
    {
        string sourcePath = Path.GetFullPath(
            "UnconfirmedCapture/Vertical/source.mkv");
        var anchor = new VisualTextAnchor(
            "federal bureau of control",
            "FEDERAL BUREAU OF CONTROL",
            VisualTextAnchorAuthority.RepeatedAcrossFrames,
            [TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(13)]);
        var visualText = new ClipVisualTextContext(
            "candidate-stable-ocr",
            sourcePath,
            NormalizedRectangle.FullFrame,
            frames: [],
            anchors: [anchor]);
        var transcript = new ClipEditorialTranscriptContext(
            1,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            "Rubber ducks and ketchup bottles are prohibited items.",
            ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
            [new ClipEditorialTranscriptSpan(
                TimeSpan.FromSeconds(11),
                TimeSpan.FromSeconds(14),
                "Rubber ducks and ketchup bottles are prohibited items.")]);
        var context = new ClipEditorialContext(
            "candidate-stable-ocr",
            sourcePath,
            "Unconfirmed capture",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromMinutes(5),
            80,
            "A stable notice and aligned commentary were retained.",
            transcripts: [transcript],
            gameContext: new ClipEditorialGameContext(
                ClipEditorialGameContext.UnconfirmedGameName,
                ClipEditorialGameContext.UnconfirmedGameHashtag,
                contextNotes: null,
                ClipEditorialGameContextSource.SourcePathHint),
            visualText: visualText);

        GroundedGameContextClaim ocr = context.EditorialBrief.Claims.Single(
            static claim =>
                claim.Kind == GroundedGameContextClaimKind.StableReadableText);
        TestAssert.Equal(
            GroundedGameContextClaimState.Supported,
            ocr.State,
            "Repeated line-level OCR is safe local authority for the literal notice text.");
        TestAssert.True(
            ocr.FieldAuthorizations.SequenceEqual(
                [GroundedEditorialField.Title, GroundedEditorialField.Description]),
            "Stable OCR may shape title and description but cannot invent tags or identity.");
        TestAssert.Equal(0, ocr.PublicSourceIds.Count,
            "Literal local OCR does not require or impersonate public game context.");
        TestAssert.True(
            ocr.LocalEvidenceIds.SequenceEqual([anchor.EvidenceId]),
            "The audience-copy claim remains bound to the repeated OCR evidence.");
        TestAssert.True(
            context.EditorialBrief.CanonicalIdentity is null,
            "Local OCR and automatic commentary must not establish game identity.");
        GroundedGameContextClaim commentary = context.EditorialBrief.Claims.Single(
            static claim =>
                claim.Kind == GroundedGameContextClaimKind.CreatorCommentaryCue);
        TestAssert.Equal(
            GroundedGameContextClaimState.Ambiguous,
            commentary.State,
            "Automatic aligned commentary may suggest the visible angle but cannot authorize exact copy.");
        return Task.CompletedTask;
    }

    private static Task AutomaticCreatorReactionSuppliesSafeAngle()
    {
        var transcript = new ClipEditorialTranscriptContext(
            1,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            "So as you can see, this is a very conspiratorial type of game. " +
            "Very heavy influences. Like SCP, MKUltra, and The X-Files; " +
            "a very nice intro.",
            ClipEditorialTranscriptAuthority.AutomaticUnreviewed,
            [
                new ClipEditorialTranscriptSpan(
                    TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(1),
                    TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(3),
                    "So as you can see, this is a very conspiratorial type of game."),
                new ClipEditorialTranscriptSpan(
                    TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(3),
                    TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(4),
                    "Very heavy influences."),
                new ClipEditorialTranscriptSpan(
                    TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(4),
                    TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(7),
                    "Like SCP, MKUltra, and The X-Files; a very nice intro."),
            ]);
        ClipEditorialContext context = CreateContext(transcripts: [transcript]);

        TestAssert.True(
            context.EditorialBrief.SafeCommentaryAngle?.Contains(
                "SCP",
                StringComparison.Ordinal) == true &&
            context.EditorialBrief.SafeCommentaryAngle.Contains(
                "X-Files",
                StringComparison.Ordinal),
            "The host brief should retain the creator's strongest reaction-shaped comparison angle.");
        GroundedGameContextClaim[] automaticClaims = context.EditorialBrief.Claims
            .Where(static claim =>
                claim.Kind == GroundedGameContextClaimKind.CreatorCommentaryCue)
            .ToArray();
        TestAssert.True(
            automaticClaims.Length > 0 &&
            automaticClaims.All(static claim =>
                claim.State == GroundedGameContextClaimState.Ambiguous &&
                claim.Authority ==
                    GroundedGameContextClaimAuthority.AutomaticTranscriptCue &&
                claim.FieldAuthorizations.Count == 0),
            "Automatic ASR must remain ambiguous and authorize no objective title or description fact.");
        TestAssert.True(
            context.EditorialBrief.QualityFlags.Contains(
                "AutomaticCreatorReactionAngleAvailable",
                StringComparer.Ordinal),
            "The synthesis host needs a typed flag before treating the safe angle as attributed commentary.");
        var retainedLegacyBrief = new GroundedEditorialBrief(
            context.CandidateId,
            context.SourceStart,
            context.SourceEnd,
            context.EditorialBrief.Claims,
            context.EditorialBrief.CanonicalIdentity,
            context.EditorialBrief.PrimaryGameplayBeat,
            context.EditorialBrief.LeadIn,
            context.EditorialBrief.VisibleFollowThrough,
            safeCommentaryAngle: null,
            context.EditorialBrief.CreatorControlRelation,
            context.EditorialBrief.QualityFlags.Where(static flag =>
                !flag.Equals(
                    "AutomaticCreatorReactionAngleAvailable",
                    StringComparison.Ordinal)).ToArray(),
            context.EditorialBrief.PresentationKind,
            context.EditorialBrief.MomentKind);
        GroundedEditorialBrief enriched = GroundedEditorialBriefBuilder
            .EnrichCreatorReactionAngle(retainedLegacyBrief, [transcript]);
        TestAssert.True(
            enriched.SafeCommentaryAngle?.Contains(
                "SCP",
                StringComparison.Ordinal) == true &&
            enriched.QualityFlags.Contains(
                "AutomaticCreatorReactionAngleAvailable",
                StringComparer.Ordinal),
            "Retained pre-fix contexts must gain the attributed reaction angle when they are reopened for an AI reroll.");
        return Task.CompletedTask;
    }

    private static Task ConfirmedIdentitySurvivesMissingKnowledge()
    {
        var identity = new ConfirmedGameIdentity(
            "Q123",
            "Example Quest",
            edition: "Definitive Edition",
            releaseYear: 2024,
            developer: "Example Studio",
            series: "Example Series",
            GameIdentityAuthority.Wikidata,
            "Wikidata",
            "en",
            userConfirmed: true,
            DateTimeOffset.UnixEpoch,
            GameKnowledgeSourcePermissions.WikimediaDefault);
        var game = new ClipEditorialGameContext(
            identity.CanonicalTitle,
            "#ExampleQuest",
            contextNotes: null,
            ClipEditorialGameContextSource.UserConfirmed,
            useOpenGameKnowledge: true,
            identity);
        var context = new ClipEditorialContext(
            "candidate-offline-identity",
            Path.GetFullPath("ExampleQuest/Vertical/source.mkv"),
            "ExampleQuest",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(5),
            80,
            "A deterministic moment was retained.",
            gameContext: game,
            gameKnowledge: null);

        TestAssert.Equal(
            identity.CanonicalTitle,
            context.EditorialBrief.CanonicalIdentity!,
            "The user-selected canonical title remains available when narrative retrieval is offline or unavailable.");
        GroundedGameContextClaim gameClaim = context.EditorialBrief.Claims.Single(
            static claim => claim.Kind == GroundedGameContextClaimKind.GameIdentity);
        TestAssert.Equal(
            GroundedGameContextClaimAuthority.ConfirmedWikidataIdentity,
            gameClaim.Authority,
            "The retained QID identity keeps its exact authority without a downloaded snapshot.");
        TestAssert.True(
            gameClaim.PublicSourceIds.SequenceEqual([identity.WikidataEntityId]),
            "The canonical claim remains bound to the explicitly selected Wikidata entity.");
        return Task.CompletedTask;
    }

    private static GroundedEditorialBrief CreateMissionBrief(
        GameKnowledgeSourceKind sourceKind)
    {
        string sourcePath = Path.GetFullPath(
            $"ExampleGame/{sourceKind}/mission-source.mkv");
        var anchor = new VisualTextAnchor(
            "chapter complete",
            "Chapter Complete",
            VisualTextAnchorAuthority.RepeatedAcrossFrames,
            [TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(13)]);
        const string passageText =
            "The licensed mission section identifies Chapter Complete as the end of the opening objective.";
        var source = new GameKnowledgeSource(
            "mission-guide",
            sourceKind,
            "Example Game guide",
            new Uri("https://example.invalid/game-guide"),
            "revision-7",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            "CC-BY-SA-4.0",
            new Uri("https://creativecommons.org/licenses/by-sa/4.0/"),
            "Example guide contributors",
            new string('a', 64));
        var passage = new GameKnowledgePassage(
            "opening-mission",
            source.Id,
            "Mission 1",
            passageText,
            GameKnowledgePassage.ComputeSha256(passageText));
        var snapshot = new GameKnowledgeSnapshot(
            "ExampleGame",
            new GameKnowledgeProviderIdentity("fixture", "1.0"),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            [source],
            [passage]);
        var match = new GameKnowledgeMatch(
            passage,
            GameKnowledgeMatchStrength.ClipLinked,
            0.9,
            ["chapter", "complete"],
            ["qualified-visual", anchor.EvidenceId],
            GameKnowledgeTemporalRelation.CurrentEventCandidate);
        var visualText = new ClipVisualTextContext(
            "candidate-mission-authority",
            sourcePath,
            NormalizedRectangle.FullFrame,
            frames: [],
            anchors: [anchor]);
        var context = new ClipEditorialContext(
            "candidate-mission-authority",
            sourcePath,
            "ExampleGame",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromMinutes(5),
            80,
            "A local event was selected.",
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "qualified-visual",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "The reviewed clip visibly completed the opening objective."),
            ],
            gameContext: new ClipEditorialGameContext(
                "ExampleGame",
                "#ExampleGame",
                contextNotes: null,
                ClipEditorialGameContextSource.UserConfirmed),
            gameKnowledge: new ClipGameKnowledgeContext(
                "ExampleGame",
                snapshot,
                [match]),
            visualText: visualText);
        return context.EditorialBrief;
    }

    private static async Task GroundedCudaOomTelemetryStopsRetryAndIsolation()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        ClipEditorialMetadataRequest[] requests =
        [
            fixture.CreateRequest(),
            fixture.CreateRequest("candidate-02"),
        ];
        int processCalls = 0;
        var runner = new FailureArtifactProcessRunner(processRequest =>
        {
            processCalls++;
            string[] arguments = processRequest.Arguments.ToArray();
            int pathIndex = Array.IndexOf(arguments, "--failure-output");
            TestAssert.True(pathIndex >= 0 && pathIndex + 1 < arguments.Length,
                "Grounded batch execution must request failure telemetry.");
            File.WriteAllText(
                arguments[pathIndex + 1],
                CreateCudaOomFailureJson(
                    fixture.Runtime.Model.ManifestSha256,
                    requests[0]));
            return new ProcessRunResult(
                4,
                string.Empty,
                "{\"errorCode\":\"InferenceError\",\"message\":\"CUDA allocator out of memory.\"}",
                TimeSpan.FromMilliseconds(20));
        });
        using var generator = new Qwen3VlGroundedMetadataGenerator(
            fixture.Runtime,
            runner,
            new SystemQwen3VlBatchWorkspaceFactory());
        var materializer = new RecordingReviewVideoMaterializer();
        var heuristic = new RecordingFallbackMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            generator,
            materializer);

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateBatchAsync(
                    requests,
                    CancellationToken.None),
                "Typed CUDA OOM telemetry must propagate.");

        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            exception.FailureKind,
            "Typed CUDA failure kind.");
        TestAssert.True(exception.Message.Contains(
                "InferenceError/Inference",
                StringComparison.Ordinal),
            "Typed CUDA failure state remains actionable.");
        TestAssert.True(exception.InnerException is Qwen3VlInferenceException,
            "The service must preserve the Qwen provider exception.");
        TestAssert.Equal(1, processCalls,
            "A CUDA OOM batch must run one host process with no reroll or isolation.");
        TestAssert.Equal(0, materializer.Calls,
            "Pre-materialized bounded reviews must be reused during failure handling.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "A CUDA OOM must not invoke heuristic metadata.");
    }

    private static string CreateStartupMemoryFailureJson(
        string modelManifestSha256)
    {
        const long gibibyte = 1024L * 1024 * 1024;
        long total = 16 * gibibyte;
        long startupFree = 12 * gibibyte;
        long reserve =
            Qwen3VlGroundedMemoryPolicy.ReservedAllocatorHeadroomBytes;
        long allocatorLimit = startupFree - reserve;
        long minimum =
            Qwen3VlGroundedMemoryPolicy.MinimumViableAllocatorLimitBytes;
        object payload = new
        {
            schemaVersion =
                Qwen3VlHostFailureEnvelope.SupportedSchemaVersion,
            hostVersion = Qwen3VlHostFailureEnvelope.SupportedHostVersion,
            command = "run-grounded-editorial-metadata-batch",
            stage = Qwen3VlHostFailureStage.RuntimeInitialization.ToString(),
            @case = (object?)null,
            videoArtifact = (object?)null,
            timing = (object?)null,
            sampling = new
            {
                backend = Qwen3VlBatchHostSettings.SupportedVideoBackend,
                sourceAverageFramesPerSecond = (double?)null,
                frameIndices = (int[]?)null,
                inferredTimestampsSeconds = (double[]?)null,
                actualPtsSeconds = (double[]?)null,
                actualFrameDurationsSeconds = (double[]?)null,
                frameCount = (int?)null,
                candidateIntersectingFrameCount = (int?)null,
            },
            generation = (object?)null,
            generationWatchdog = (object?)null,
            groundedMemoryPolicy = new
            {
                policyVersion = Qwen3VlGroundedMemoryPolicy.Version,
                policySha256 = Qwen3VlGroundedMemoryPolicy.Sha256,
                cudaDeviceIndex = 0,
                cacheImplementation = "bounded-dynamic",
                attentionImplementation = "sdpa",
                sdpaBackend = "CudnnAttention",
                sdpaBackendForced = true,
                attentionFallbackPermitted = false,
                allocatorScope = "PyTorchNativeCudaCachingAllocator",
                startupGate =
                    "FreeMemoryMinusReserveExceedsQualificationPeak",
                preGenerationGate =
                    "CurrentFreeMemoryAtLeastFixedReserve",
                totalDeviceMemoryBytes = total,
                startupFreeMemoryBytes = startupFree,
                startupExternallyOccupiedMemoryBytes = total - startupFree,
                requiredStartupFreeMemoryBytes = reserve + minimum,
                reservedAllocatorHeadroomBytes = reserve,
                allocatorLimitBytes = allocatorLimit,
                minimumViableAllocatorLimitBytes = minimum,
                allocatorFraction = (double)allocatorLimit / total,
                observedAllocatorFraction = (double?)null,
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
                preGenerationAdmissionCount = 0,
                minimumPreGenerationFreeDeviceMemoryBytes = (long?)null,
                lastPreGenerationFreeDeviceMemoryBytes = (long?)null,
                peakAllocatedGpuBytes = (long?)null,
                peakReservedGpuBytes = (long?)null,
                endAllocatedGpuBytes = (long?)null,
                endReservedGpuBytes = (long?)null,
                endFreeDeviceMemoryBytes = (long?)null,
                runtimeOutcome = "StartupAdmissionRejected",
                failureReason = "InsufficientStartupFreeMemory",
                globalFreeMemoryGuaranteed = false,
                cpuModelOffloadPermitted = true,
                quantizationPermitted = false,
                automaticFallbackPermitted = false,
            },
            recoveryPoolLedger = Array.Empty<object>(),
            identity = new
            {
                inputBatchSha256 = new string('a', 64),
                inputCaseSha256 = (string?)null,
                modelManifestSha256,
                environmentSha256 = (string?)null,
                promptSha256 =
                    Qwen3VlGroundedMetadataGenerator.PromptSha256,
            },
            failure = new
            {
                errorCode =
                    Qwen3VlHostErrorCode.InitializationError.ToString(),
                exitCode = 3,
                message = "Grounded CUDA startup admission was rejected.",
            },
            createdAtUtc = "2026-08-10T12:00:00.000Z",
            diagnostics = new[] { "InitializationError: startup rejected" },
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private static string CreateCudaOomFailureJson(
        string modelManifestSha256,
        ClipEditorialMetadataRequest request)
    {
        VisualSemanticInputManifest review = request.ReviewVideo ??
            throw new InvalidOperationException(
                "CUDA OOM telemetry requires the submitted bounded review.");
        const long gibibyte = 1024L * 1024 * 1024;
        long total = 16 * gibibyte;
        long startupFree = 15 * gibibyte;
        long reserve =
            Qwen3VlGroundedMemoryPolicy.ReservedAllocatorHeadroomBytes;
        long allocatorLimit = startupFree - reserve;
        long minimum =
            Qwen3VlGroundedMemoryPolicy.MinimumViableAllocatorLimitBytes;
        double reviewEnd = review.ReviewVideoDuration.TotalSeconds;
        object payload = new
        {
            schemaVersion =
                Qwen3VlHostFailureEnvelope.SupportedSchemaVersion,
            hostVersion = Qwen3VlHostFailureEnvelope.SupportedHostVersion,
            command = "run-grounded-editorial-metadata-batch",
            stage = Qwen3VlHostFailureStage.Inference.ToString(),
            @case = new
            {
                caseId = request.Context.CandidateId,
                candidateId = request.Context.CandidateId,
                caseOrdinal = 1,
            },
            videoArtifact = new
            {
                sha256 = review.ReviewVideoSha256.ToLowerInvariant(),
                byteLength = review.ReviewVideoByteLength,
                reviewDurationSeconds = reviewEnd,
            },
            timing = new
            {
                sourceAbsoluteOffsetSeconds = 0.0,
                reviewStartSeconds = 0.0,
                reviewEndSeconds = reviewEnd,
                candidateRelativeStartSeconds = 0.0,
                candidateRelativeEndSeconds = reviewEnd,
                candidateAbsoluteStartSeconds = 0.0,
                candidateAbsoluteEndSeconds = reviewEnd,
            },
            sampling = new
            {
                backend = Qwen3VlBatchHostSettings.SupportedVideoBackend,
                sourceAverageFramesPerSecond = (double?)null,
                frameIndices = (int[]?)null,
                inferredTimestampsSeconds = (double[]?)null,
                actualPtsSeconds = (double[]?)null,
                actualFrameDurationsSeconds = (double[]?)null,
                frameCount = (int?)null,
                candidateIntersectingFrameCount = (int?)null,
            },
            generation = (object?)null,
            generationWatchdog = (object?)null,
            groundedMemoryPolicy = new
            {
                policyVersion = Qwen3VlGroundedMemoryPolicy.Version,
                policySha256 = Qwen3VlGroundedMemoryPolicy.Sha256,
                cudaDeviceIndex = 0,
                cacheImplementation = "bounded-dynamic",
                attentionImplementation = "sdpa",
                sdpaBackend = "CudnnAttention",
                sdpaBackendForced = true,
                attentionFallbackPermitted = false,
                allocatorScope = "PyTorchNativeCudaCachingAllocator",
                startupGate =
                    "FreeMemoryMinusReserveExceedsQualificationPeak",
                preGenerationGate =
                    "CurrentFreeMemoryAtLeastFixedReserve",
                totalDeviceMemoryBytes = total,
                startupFreeMemoryBytes = startupFree,
                startupExternallyOccupiedMemoryBytes = total - startupFree,
                requiredStartupFreeMemoryBytes = reserve + minimum,
                reservedAllocatorHeadroomBytes = reserve,
                allocatorLimitBytes = allocatorLimit,
                minimumViableAllocatorLimitBytes = minimum,
                allocatorFraction = (double)allocatorLimit / total,
                observedAllocatorFraction =
                    (double?)allocatorLimit / total,
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
                minimumPreGenerationFreeDeviceMemoryBytes = reserve,
                lastPreGenerationFreeDeviceMemoryBytes = reserve,
                peakAllocatedGpuBytes = 11 * gibibyte,
                peakReservedGpuBytes = allocatorLimit,
                endAllocatedGpuBytes = 10 * gibibyte,
                endReservedGpuBytes = 11 * gibibyte,
                endFreeDeviceMemoryBytes = gibibyte,
                runtimeOutcome = "CudaAllocatorOutOfMemory",
                failureReason = "CudaAllocatorOutOfMemory",
                globalFreeMemoryGuaranteed = false,
                cpuModelOffloadPermitted = true,
                quantizationPermitted = false,
                automaticFallbackPermitted = false,
            },
            recoveryPoolLedger = Array.Empty<object>(),
            identity = new
            {
                inputBatchSha256 = new string('a', 64),
                inputCaseSha256 = new string('b', 64),
                modelManifestSha256,
                environmentSha256 = new string('c', 64),
                promptSha256 =
                    Qwen3VlGroundedMetadataGenerator.PromptSha256,
            },
            failure = new
            {
                errorCode = Qwen3VlHostErrorCode.InferenceError.ToString(),
                exitCode = 4,
                message = "CUDA allocator out of memory.",
            },
            createdAtUtc = "2026-08-10T12:00:00.000Z",
            diagnostics = new[] { "InferenceError: CUDA allocator OOM" },
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private static async Task TypedQwenResourceFailuresDoNotRetry()
    {
        Qwen3VlHostFailureEnvelope resourceFailure = CreateHostFailure(
            Qwen3VlHostErrorCode.InferenceError,
            Qwen3VlHostFailureStage.Inference,
            memoryOutcome: "CudaAllocatorOutOfMemory");
        var heuristic = new RecordingFallbackMetadataGenerator();
        var directAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "The fixed CUDA allocator limit was reached.",
                hostFailure: resourceFailure));
        var directService = new ClipEditorialMetadataGenerationService(
            heuristic,
            directAi);

        ClipEditorialAiGenerationException directException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => directService.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        CreateContext(),
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.AiRequired),
                    CancellationToken.None),
                "A proven allocator failure must propagate.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            directException.FailureKind,
            "Direct allocator failure kind.");
        TestAssert.True(
            directException.InnerException is Qwen3VlInferenceException,
            "The direct allocator failure retains provider telemetry.");
        TestAssert.Equal(1, directAi.BatchCalls,
            "A deterministic resource failure must run one reviewed singleton batch attempt.");
        TestAssert.Equal(0, directAi.SingleCalls,
            "A batch-capable provider must not use its stale direct path.");

        var requiredBatchAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "The fixed CUDA allocator limit was reached.",
                hostFailure: resourceFailure));
        var requiredBatchService = new ClipEditorialMetadataGenerationService(
            heuristic,
            requiredBatchAi);
        ClipEditorialMetadataRequest[] requiredRequests =
        [
            new(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
            new(
                CreateContext(candidateId: "candidate-02"),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
        ];
        ClipEditorialAiGenerationException requiredException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => requiredBatchService.GenerateBatchAsync(
                    requiredRequests,
                    CancellationToken.None),
                "A required-AI allocator batch failure must propagate.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            requiredException.FailureKind,
            "Required batch allocator failure kind.");
        TestAssert.Equal(1, requiredBatchAi.BatchCalls,
            "The failing batch is submitted once.");
        TestAssert.Equal(0, requiredBatchAi.SingleCalls,
            "Resource failure must not fan out into isolated model loads.");

        var optionalBatchAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "The fixed CUDA allocator limit was reached.",
                hostFailure: resourceFailure));
        var optionalBatchService = new ClipEditorialMetadataGenerationService(
            heuristic,
            optionalBatchAi);
        ClipEditorialMetadataRequest[] optionalRequests =
            requiredRequests.Select(request =>
                new ClipEditorialMetadataRequest(
                    request.Context,
                    request.Profile,
                    request.Attempt,
                    ClipEditorialGenerationPreference.AiWhenAvailable))
                .ToArray();
        ClipEditorialAiGenerationException optionalException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => optionalBatchService.GenerateBatchAsync(
                    optionalRequests,
                    CancellationToken.None),
                "AI-when-available allocator batch failure must propagate.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            optionalException.FailureKind,
            "Optional batch allocator failure kind.");
        TestAssert.Equal(1, optionalBatchAi.BatchCalls,
            "Optional AI also submits the failing batch once.");
        TestAssert.Equal(0, optionalBatchAi.SingleCalls,
            "Optional AI must not isolate a deterministic resource failure.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "No AI resource failure may invoke heuristic metadata.");
    }

    private static async Task LegacySemanticHostFailuresRunOnce()
    {
        Qwen3VlHostFailureEnvelope semanticFailure = CreateHostFailure(
            Qwen3VlHostErrorCode.InferenceError,
            Qwen3VlHostFailureStage.Inference);
        var heuristic = new RecordingFallbackMetadataGenerator();
        var directAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "Grounded metadata assigned an unsupported mental state.",
                hostFailure: semanticFailure));
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            directAi);

        ClipEditorialAiGenerationException directException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        CreateContext(),
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.AiRequired),
                    CancellationToken.None),
                "A semantic copy rejection must propagate.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            directException.FailureKind,
            "Direct semantic failure kind.");
        TestAssert.Equal(1, directAi.BatchCalls,
            "Current copy review happens inside one singleton batch host result.");
        TestAssert.Equal(0, directAi.SingleCalls,
            "A batch-capable provider must not use its stale direct path.");

        var batchAi = new TypedSemanticBatchMetadataGenerator(
            new Qwen3VlInferenceException(
                "Grounded metadata assigned an unsupported mental state.",
                hostFailure: semanticFailure));
        var batchService = new ClipEditorialMetadataGenerationService(
            heuristic,
            batchAi);
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
            new(
                CreateContext(candidateId: "candidate-02"),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
        ];
        ClipEditorialAiGenerationException batchException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => batchService.GenerateBatchAsync(
                    requests,
                    CancellationToken.None),
                "A semantic batch rejection must propagate.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            batchException.FailureKind,
            "Batch semantic failure kind.");
        TestAssert.Equal(1, batchAi.BatchCalls,
            "Semantic batch rejection uses one batch attempt.");
        TestAssert.Equal(0, batchAi.SingleCalls,
            "A semantic batch failure no longer enters case isolation.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Semantic AI failures must not invoke heuristic metadata.");
    }

    private static async Task UntypedQwenTechnicalFailuresFailClosed()
    {
        var untypedFailure = new Qwen3VlInferenceException(
            "The grounded host failed without a valid typed failure artifact.");
        var heuristic = new RecordingFallbackMetadataGenerator();
        var directAi = new TypedFailureMetadataGenerator(untypedFailure);
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            directAi);

        ClipEditorialAiGenerationException directException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        CreateContext(),
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.AiRequired),
                    CancellationToken.None),
                "Missing failure telemetry must propagate without another GPU attempt.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            directException.FailureKind,
            "Untyped direct failure kind.");
        TestAssert.Equal(1, directAi.BatchCalls,
            "An untyped grounded inference failure runs only one singleton batch.");
        TestAssert.Equal(0, directAi.SingleCalls,
            "An untyped failure must not fall back to the provider's stale direct path.");

        var batchAi = new TypedFailureMetadataGenerator(untypedFailure);
        var batchService = new ClipEditorialMetadataGenerationService(
            heuristic,
            batchAi);
        ClipEditorialMetadataRequest[] batchRequests =
        [
            new(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
            new(
                CreateContext(candidateId: "candidate-02"),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired),
        ];
        ClipEditorialAiGenerationException batchException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => batchService.GenerateBatchAsync(
                    batchRequests,
                    CancellationToken.None),
                "Missing batch failure telemetry must propagate.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            batchException.FailureKind,
            "Untyped batch failure kind.");
        TestAssert.Equal(1, batchAi.BatchCalls,
            "An untyped failing batch is submitted once.");
        TestAssert.Equal(0, batchAi.SingleCalls,
            "An untyped technical failure does not enter per-clip isolation.");

        var parseFailure = new Qwen3VlOutputParseException(
            "The provider response was structurally valid JSON but failed editorial validation.");
        var semanticAi = new TypedFailureMetadataGenerator(parseFailure);
        var semanticService = new ClipEditorialMetadataGenerationService(
            heuristic,
            semanticAi);
        ClipEditorialAiGenerationException parseException =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => semanticService.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        CreateContext(),
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.AiRequired),
                    CancellationToken.None),
                "Malformed provider output must propagate without hidden reruns.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            parseException.FailureKind,
            "Structured-output failure kind.");
        TestAssert.Equal(1, semanticAi.BatchCalls,
            "Output parsing runs once in the singleton batch; schema-valid copy quality is retained by the host.");
        TestAssert.Equal(0, semanticAi.SingleCalls,
            "Structured-output failure must not fall back to the provider's stale direct path.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Technical and parsing AI failures must not invoke heuristic metadata.");
    }

    private static Qwen3VlHostFailureEnvelope CreateHostFailure(
        Qwen3VlHostErrorCode errorCode,
        Qwen3VlHostFailureStage stage,
        string? memoryOutcome = null,
        bool watchdogTriggered = false)
    {
        int exitCode = errorCode switch
        {
            Qwen3VlHostErrorCode.UnexpectedHostFailure => 1,
            Qwen3VlHostErrorCode.UsageOrInputError => 2,
            Qwen3VlHostErrorCode.InitializationError or
                Qwen3VlHostErrorCode.NetworkProhibitedError => 3,
            Qwen3VlHostErrorCode.InferenceError => 4,
            Qwen3VlHostErrorCode.OutputError => 5,
            Qwen3VlHostErrorCode.RawAuditCaptured => 6,
            Qwen3VlHostErrorCode.GenerationTokenBudgetExceededError => 7,
            Qwen3VlHostErrorCode.UnexpectedGenerationTerminationError => 8,
            Qwen3VlHostErrorCode.ProviderCaseFailuresDetected => 9,
            Qwen3VlHostErrorCode.GenerationWallClockBudgetExceededError => 10,
            Qwen3VlHostErrorCode.Cancelled => 130,
            _ => throw new ArgumentOutOfRangeException(nameof(errorCode)),
        };
        Qwen3VlGroundedMemoryPolicyAudit? memory = memoryOutcome is null
            ? null
            : new Qwen3VlGroundedMemoryPolicyAudit(
                16L * 1024 * 1024 * 1024,
                13L * 1024 * 1024 * 1024,
                10L * 1024 * 1024 * 1024,
                0.625,
                0.625,
                1,
                3L * 1024 * 1024 * 1024,
                3L * 1024 * 1024 * 1024,
                null,
                null,
                null,
                null,
                null,
                memoryOutcome,
                "CudaAllocatorOutOfMemory");
        Qwen3VlHostFailureGenerationWatchdog? watchdog =
            watchdogTriggered
                ? new Qwen3VlHostFailureGenerationWatchdog(
                    Qwen3VlGenerationWatchdogPolicy.Version,
                    Qwen3VlGenerationWatchdogPolicy.Sha256,
                    240,
                    720,
                    "CooperativeCancellation",
                    "candidate-01",
                    "candidate-01",
                    1,
                    1,
                    240,
                    240,
                    240,
                    true,
                    "GenerationWallClockBudgetExceeded")
                : null;
        return new Qwen3VlHostFailureEnvelope(
            Qwen3VlHostFailureEnvelope.SupportedSchemaVersion,
            Qwen3VlHostCommand.Run,
            stage,
            null,
            null,
            null,
            new Qwen3VlHostFailureSampling(
                Qwen3VlBatchHostSettings.SupportedVideoBackend,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            null,
            watchdog,
            memory,
            new Qwen3VlHostFailureIdentity(
                null,
                null,
                null,
                null,
                null),
            new Qwen3VlHostFailureDetails(
                errorCode,
                exitCode,
                errorCode.ToString()),
            DateTimeOffset.UtcNow,
            [],
            []);
    }

    private static async Task RequiredAiRequiresProvider()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic);

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        CreateContext(),
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.AiRequired),
                    CancellationToken.None),
                "Unavailable required AI must fail closed.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderUnavailable,
            exception.FailureKind,
            "Unavailable required AI failure kind.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Unavailable required AI must not invoke heuristic metadata.");
    }

    private static async Task RequiredAiFailurePropagates()
    {
        var heuristic = new RecordingFallbackMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            heuristic,
            new FailingBatchMetadataGenerator());

        ClipEditorialAiGenerationException exception =
            await TestAssert.ThrowsAsync<ClipEditorialAiGenerationException>(
                () => service.GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        CreateContext(),
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.AiRequired),
                    CancellationToken.None),
                "Required AI provider failure must propagate.");
        TestAssert.Equal(ClipEditorialAiFailureKind.ProviderFailed,
            exception.FailureKind,
            "Required AI provider failure kind.");
        TestAssert.True(exception.InnerException is InvalidDataException,
            "Required AI failure must preserve the provider exception.");
        TestAssert.Equal(0, heuristic.Requests.Count,
            "Required AI failure must not invoke heuristic metadata.");
    }

    private static async Task AiBatchPreservesOrder()
    {
        var ai = new RecordingBatchMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            ai);
        ClipEditorialMetadataRequest[] requests =
        [
            new(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
            new(
                new ClipEditorialContext(
                    "candidate-02",
                    Path.GetFullPath("ExampleGame/Vertical/source.mkv"),
                    "ExampleGame",
                    TimeSpan.FromMinutes(20),
                    TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(90),
                    78,
                    "A distinct gameplay change occurred.",
                    gameContext: new ClipEditorialGameContext(
                        "Example Game",
                        "#ExampleGame",
                        null,
                        ClipEditorialGameContextSource.UserConfirmed)),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
        ];

        IReadOnlyList<ClipEditorialMetadataDraft> drafts =
            await service.GenerateBatchAsync(
                requests,
                CancellationToken.None);

        TestAssert.Equal(1, ai.BatchCalls, "One AI batch call.");
        TestAssert.Equal(0, ai.SingleCalls, "No per-candidate AI calls.");
        TestAssert.Equal(2, drafts.Count, "Draft count.");
        TestAssert.True(
            drafts[0].Title.StartsWith("candidate-01", StringComparison.Ordinal),
            "First candidate ordering.");
        TestAssert.True(
            drafts[1].Title.StartsWith("candidate-02", StringComparison.Ordinal),
            "Second candidate ordering.");
    }

    private static async Task VisualAiMaterializesAndCleansReview()
    {
        ClipEditorialContext context = CreateContext();
        var media = TestMediaFactory.Create(
            context.SourceFullPath,
            context.SourceDuration);
        var provider = new RecordingVisualMetadataGenerator();
        var materializer = new RecordingReviewVideoMaterializer();
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            provider,
            materializer);

        ClipEditorialMetadataDraft result = await service.GenerateAsync(
            new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired,
                media),
            CancellationToken.None);

        TestAssert.Equal(
            ClipEditorialMetadataOrigin.AiAssisted,
            result.Origin,
            "Visual provider origin.");
        TestAssert.Equal(1, materializer.Calls, "One bounded materialization.");
        TestAssert.Equal(1, materializer.Cleanups, "Transient review cleanup.");
        TestAssert.Equal(
            context.Duration,
            materializer.LastRequest!.Duration,
            "Grounded metadata must preserve the complete selected cut for adaptive chronological sampling.");
        TestAssert.Equal(
            context.SourceStart,
            materializer.LastRequest.SourceStart,
            "The grounded review must begin at the exact selected-cut boundary.");
        TestAssert.True(provider.SawVerifiedReview, "Provider review input.");
    }

    private static async Task VisualAiPreservesLongSelectedCut()
    {
        TimeSpan selectedDuration = TimeSpan.FromSeconds(114.5);
        var context = new ClipEditorialContext(
            "candidate-long-cut",
            Path.GetFullPath("ExampleGame/Vertical/long-source.mkv"),
            "ExampleGame",
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(2) + selectedDuration,
            TimeSpan.FromMinutes(10),
            88,
            "A complete longer gameplay beat remained above the quality threshold.",
            gameContext: new ClipEditorialGameContext(
                "Example Game",
                "#ExampleGame",
                contextNotes: null,
                ClipEditorialGameContextSource.UserConfirmed));
        var media = TestMediaFactory.Create(
            context.SourceFullPath,
            context.SourceDuration);
        var provider = new RecordingVisualMetadataGenerator();
        var materializer = new RecordingReviewVideoMaterializer();
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            provider,
            materializer);

        await service.GenerateAsync(
            new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired,
                media),
            CancellationToken.None);

        TestAssert.Equal(
            selectedDuration,
            materializer.LastRequest!.Duration,
            "A selected cut above the general 70-second shortlist-review limit must reach grounded metadata intact.");
        TestAssert.Equal(
            context.SourceEnd,
            materializer.LastRequest.SourceEnd,
            "The grounded review must retain the selected cut's exact follow-through boundary.");
        TestAssert.Equal(1, materializer.Cleanups,
            "The longer transient review must still be released after metadata generation.");
    }

    private static async Task VisualAiReusesExistingReview()
    {
        ClipEditorialContext context = CreateContext();
        var media = TestMediaFactory.Create(
            context.SourceFullPath,
            context.SourceDuration);
        var provider = new RecordingVisualMetadataGenerator();
        var materializer = new RecordingReviewVideoMaterializer();
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            provider,
            materializer);
        using MaterializedVisualSemanticReviewVideo review =
            await materializer.MaterializeAsync(
                new VisualSemanticReviewVideoMaterializationRequest(
                    context.CandidateId,
                    media,
                    context.SourceStart,
                    context.SourceEnd),
                CancellationToken.None);

        await service.GenerateAsync(
            new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired,
                media,
                review.Input),
            CancellationToken.None);

        TestAssert.Equal(
            1,
            materializer.Calls,
            "The editorial provider must reuse the retained visual-review artifact instead of decoding the same candidate twice.");
        TestAssert.True(
            provider.SawVerifiedReview,
            "The reused review remains integrity-verified for the visual provider.");
    }

    private static async Task GameHashtagSurvivesTitleLimit()
    {
        var context = new ClipEditorialContext(
            "candidate-hashtag",
            Path.GetFullPath("ExampleGame/Vertical/source.mkv"),
            "ExampleGame",
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(20),
            TimeSpan.FromMinutes(90),
            80,
            "A long deterministic reason that remains grounded in the selected gameplay interval.",
            gameContext: new ClipEditorialGameContext(
                "Example Game",
                "#ExampleGame",
                null,
                ClipEditorialGameContextSource.UserConfirmed));
        var generator = new HeuristicClipEditorialMetadataGenerator();
        ClipEditorialMetadataDraft draft = await generator.GenerateAsync(
            new ClipEditorialMetadataRequest(
                context,
                new ClipEditorialProfile(
                    "Chat",
                    new string('x', 260),
                    null),
                0),
            CancellationToken.None);

        TestAssert.True(
            draft.Title.EndsWith(
                "#ExampleGame",
                StringComparison.Ordinal),
            "The exact game hashtag must survive title trimming.");
        TestAssert.True(
            draft.Title.Length <=
                ClipEditorialMetadataDraft.MaximumTitleLength,
            "Bounded title length.");
    }

    private static Task SeparatedQwenTitlesRestoreCanonicalHashtag()
    {
        var request = new ClipEditorialMetadataRequest(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);
        string title = Qwen3VlGroundedMetadataResultParser
            .FinalizeAudienceTitle(
                "Rubber duckies were not allowed inside the FBC",
                request,
                Qwen3VlGroundedMetadataGenerator.OutputSchema);

        TestAssert.Equal(
            "Rubber duckies were not allowed inside the FBC #ExampleGame",
            title,
            "The hashtag-free wire title must receive the confirmed canonical suffix at the app boundary.");
        TestAssert.Equal(
            1,
            title.Count(static character => character == '#'),
            "Final title packaging must contain exactly one hashtag.");
        string schemaMaximumBody = new('A', 67);
        string schemaMaximumTitle = Qwen3VlGroundedMetadataResultParser
            .FinalizeAudienceTitle(
                schemaMaximumBody,
                request,
                Qwen3VlGroundedMetadataGenerator.OutputSchema);
        TestAssert.Equal(
            ClipEditorialMetadataQuality.PreferredMaximumTitleLength,
            schemaMaximumTitle.Length,
            "The separated schema maximum must finalize at the effective preferred title limit.");
        TestAssert.True(
            ClipEditorialMetadataQuality.Evaluate(
                    schemaMaximumTitle,
                    "A distinct grounded outcome added useful setting and action context.",
                    request.Context)
                .All(static issue => issue.Code !=
                    ClipEditorialMetadataQualityIssueCode.OverlongAudienceCopy),
            "A schema-maximum title must never trigger a post-generation overlong retry.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataResultParser.FinalizeAudienceTitle(
                "Rubber duckies #ExampleGame",
                request,
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "A provider-supplied hashtag must not survive separated title packaging.");
        TestAssert.Equal(
            "Historical title #ExampleGame",
            Qwen3VlGroundedMetadataResultParser.FinalizeAudienceTitle(
                "Historical title #ExampleGame",
                request,
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCreatorVoiceOutputSchema),
            "Historical schemas that already own title packaging must remain byte-compatible.");

        string longHashtag = "#" + new string('G', 89);
        var longContext = new ClipEditorialContext(
            "candidate-long-hashtag",
            Path.GetFullPath("LongGame/Vertical/source.mkv"),
            "LongGame",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMinutes(5),
            80,
            "A bounded candidate retained a long canonical hashtag.",
            gameContext: new ClipEditorialGameContext(
                "Long Game",
                longHashtag,
                contextNotes: null,
                ClipEditorialGameContextSource.UserConfirmed));
        var longRequest = new ClipEditorialMetadataRequest(
            longContext,
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);
        string boundaryTitle = Qwen3VlGroundedMetadataResultParser
            .FinalizeAudienceTitle(
                "123456789",
                longRequest,
                Qwen3VlGroundedMetadataGenerator.OutputSchema);
        TestAssert.Equal(
            ClipEditorialMetadataDraft.MaximumTitleLength,
            boundaryTitle.Length,
            "The exact long hashtag and schema-sized body must fit the publishing boundary without truncation.");
        TestAssert.True(
            boundaryTitle.EndsWith(longHashtag, StringComparison.Ordinal),
            "Long canonical hashtags must remain exact.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataResultParser.FinalizeAudienceTitle(
                "1234567890",
                longRequest,
                Qwen3VlGroundedMetadataGenerator.OutputSchema),
            "The parser must reject a body that violates the schema-reserved boundary.");
        return Task.CompletedTask;
    }

    private static async Task CapturePathsStayUnconfirmed()
    {
        foreach (string folder in new[]
                 {
                     "Recording Video Files",
                     "Videos",
                     "Captures",
                     "OBS",
                     "vertical",
                     "Desktop",
                     "Documents",
                     "Downloads",
                     "OneDrive",
                     "Dropbox",
                     "Google Drive",
                 })
        {
            GenerationSourceGameContext hint =
                GenerationSourceGameContext.CreatePathHint(
                    $@"D:\{folder}\2026-08-24 14-36-10-vertical.mkv");
            TestAssert.Equal(
                GenerationGamePathHintPolicy.UnconfirmedGameName,
                hint.GameName,
                $"The storage folder '{folder}' must not become game identity.");
            TestAssert.Equal(
                "#Gameplay",
                hint.GameHashtag,
                $"The storage folder '{folder}' must not become a hashtag.");
        }

        GenerationSourceGameContext profileStorage =
            GenerationSourceGameContext.CreatePathHint(
                @"C:\Users\Creator\OneDrive\Videos\source.mkv");
        TestAssert.Equal(
            GenerationGamePathHintPolicy.UnconfirmedGameName,
            profileStorage.GameName,
            "Walking above storage folders must stop before a user-profile name can become game identity.");

        GenerationSourceGameContext nestedGame =
            GenerationSourceGameContext.CreatePathHint(
                @"D:\Recording Video Files\Example Quest\vertical\source.mkv");
        TestAssert.Equal(
            "Example Quest",
            nestedGame.GameName,
            "A meaningful game folder below a storage root remains a useful suggestion.");

        GenerationSourceGameContext capturesBelowGame =
            GenerationSourceGameContext.CreatePathHint(
                @"D:\Example Quest\Captures\source.mkv");
        TestAssert.Equal(
            "Example Quest",
            capturesBelowGame.GameName,
            "A generic capture subfolder must be skipped without discarding its game parent.");

        var sourceViewModel = new GameContextSourceViewModel(
            nestedGame,
            static () => { });
        TestAssert.True(
            sourceViewModel.IsValid,
            "A meaningful path hint remains optional and must not block local-only generation.");
        TestAssert.False(
            sourceViewModel.IsConfirmed,
            "A meaningful folder suggestion is not confirmation.");
        TestAssert.True(
            sourceViewModel.ConfirmGameCommand.CanExecute(null),
            "A meaningful suggestion can be explicitly confirmed.");
        sourceViewModel.ConfirmGameCommand.Execute(null);
        TestAssert.True(
            sourceViewModel.IsConfirmed,
            "The explicit action establishes user-confirmed identity.");
        TestAssert.True(
            sourceViewModel.IsValid,
            "The explicitly confirmed meaningful local name satisfies Setup without requiring public context.");

        var fallbackViewModel = new GameContextSourceViewModel(
            GenerationSourceGameContext.CreatePathHint(
                @"D:\Recording Video Files\source.mkv"),
            static () => { });
        TestAssert.False(
            fallbackViewModel.ConfirmGameCommand.CanExecute(null),
            "The generic Gameplay fallback cannot certify itself as a game.");

        var unconfirmedEditorial = new ClipEditorialGameContext(
            "Recording Video Files",
            "#RecordingVideoFiles",
            "A path-derived note",
            ClipEditorialGameContextSource.SourcePathHint,
            useOpenGameKnowledge: true);
        TestAssert.Equal(
            "Recording Video Files",
            unconfirmedEditorial.GameName,
            "The bounded internal context retains its auditable path hint.");
        TestAssert.Equal(
            ClipEditorialGameContext.UnconfirmedGameHashtag,
            unconfirmedEditorial.AudienceGameHashtag,
            "Editorial generation must use only the neutral fail-soft hashtag.");
        TestAssert.Equal(
            ClipEditorialGameContext.UnconfirmedGameName,
            unconfirmedEditorial.AudienceGameName,
            "Editorial generation must withhold an unconfirmed folder label.");
        TestAssert.True(
            unconfirmedEditorial.ContextNotes is not null &&
            !unconfirmedEditorial.UseOpenGameKnowledge &&
            !unconfirmedEditorial.IsUserGrounded,
            "The internal hint may remain auditable but cannot authorize notes or online game knowledge.");

        ClipEditorialContext context = new(
            "capture-path-candidate",
            @"D:\Recording Video Files\source.mkv",
            "Recording Video Files",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(1),
            75,
            "A deterministic moment was selected.",
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "visual",
                    ClipEditorialEvidenceKind.VisualObservation,
                    "Crossed the narrow walkway and reached the doorway."),
            ],
            gameContext: unconfirmedEditorial);
        ClipEditorialMetadataDraft draft =
            await new HeuristicClipEditorialMetadataGenerator().GenerateAsync(
                new ClipEditorialMetadataRequest(
                    context,
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);
        TestAssert.False(
            draft.Title.Contains(
                "RecordingVideoFiles",
                StringComparison.OrdinalIgnoreCase),
            "Fail-soft audience copy must never emit a capture-folder hashtag.");
        TestAssert.True(
            draft.Title.EndsWith("#Gameplay", StringComparison.Ordinal),
            "Fail-soft audience copy retains a neutral non-identity hashtag.");
        ClipEditorialPriorTitleExclusion migratedPrior =
            ClipEditorialPriorTitleExclusion.ForContext(
                context,
                "Crossed the narrow walkway #RecordingVideoFiles");
        TestAssert.Equal(
            "Crossed the narrow walkway #Gameplay",
            migratedPrior.Title,
            "A retained title from an older path-hint run must not carry its storage hashtag into a reroll.");
    }

    private static Task GameContextMemoryIsPrivate()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameContextTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string memoryPath = Path.Combine(root, "game-context.json");
        string source = Path.Combine(root, "ExampleGame", "Vertical", "source.mkv");
        string sibling = Path.Combine(root, "ExampleGame", "other.mkv");
        try
        {
            var memory = new JsonGenerationGameContextMemory(memoryPath);
            memory.Remember(
            [
                new GenerationSourceGameContext(
                    source,
                    "Example Game",
                    "Prefer concise gameplay wording.",
                    GenerationGameContextOrigin.UserConfirmed),
            ]);

            string json = File.ReadAllText(memoryPath);
            TestAssert.False(
                json.Contains(root, StringComparison.OrdinalIgnoreCase),
                "Persistent game memory must not expose personal paths.");
            GenerationSourceGameContext? recalled = memory.Find(sibling);
            TestAssert.True(recalled is not null, "Folder-level reuse.");
            TestAssert.Equal(
                "Example Game",
                recalled!.GameName,
                "Recalled game name.");
            TestAssert.Equal(
                GenerationGameContextOrigin.RememberedSuggestion,
                recalled.Origin,
                "Recalled provenance.");
            TestAssert.False(recalled.IsUserGrounded, "Sharing a recording folder does not confirm the same game.");
            var suggestion = new ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.GameContext.GameContextSourceViewModel(recalled, () => { });
            TestAssert.True(suggestion.CanConfirmGame && !suggestion.IsConfirmed,
                "Remembered games must remain easy to confirm, without silently adding the wrong game hashtag.");
            suggestion.ConfirmGameCommand.Execute(null);
            TestAssert.True(suggestion.IsConfirmed, "Explicit confirmation promotes the remembered suggestion.");
            TestAssert.True(
                Directory.GetFiles(root, "*.tmp").Length == 0,
                "Atomic memory writes must clean staging files.");
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

    private static Task GameContextMemoryQuarantinesCorruptDocuments()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameContextCorruptionTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string memoryPath = Path.Combine(root, "game-context.json");
        string source = Path.Combine(root, "ExampleGame", "source.mkv");
        const string corrupt = """
            {
              "SchemaVersion": "replayfoundry-game-context-memory-1.2",
              "Entries": [
                {
                  "SourceDirectoryKey": null,
                  "GameName": "Example Game",
                  "ContextNotes": null,
                  "UseOpenGameKnowledge": false,
                  "ConfirmedIdentity": null,
                  "UpdatedAtUtc": "2026-08-31T12:00:00.0000000+00:00"
                }
              ]
            }
            """;
        try
        {
            File.WriteAllText(memoryPath, corrupt);
            var memory = new JsonGenerationGameContextMemory(memoryPath);

            TestAssert.True(
                memory.Find(source) is null,
                "An invalid optional memory document must not block Generate setup.");
            TestAssert.False(
                File.Exists(memoryPath),
                "A corrupt supported document must leave the active memory path.");
            string quarantineDirectory = Path.Combine(
                root,
                "Diagnostics",
                "GameContextMemory");
            string[] quarantined = Directory.GetFiles(
                quarantineDirectory,
                "game-context.json.*.invalid.json");
            TestAssert.Equal(
                1,
                quarantined.Length,
                "The corrupt document must be retained as one local recovery artifact.");
            TestAssert.Equal(
                corrupt,
                File.ReadAllText(quarantined[0]),
                "Quarantine must preserve the invalid document byte-for-byte.");

            memory.Remember(
            [
                new GenerationSourceGameContext(
                    source,
                    "Example Game",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed),
            ]);
            TestAssert.True(
                memory.Find(source) is not null,
                "A successful quarantine must allow a fresh memory document to be saved.");

            string pendingRecovery = Path.Combine(
                quarantineDirectory,
                "game-context.json.crash.recovery-pending.json");
            File.Move(memoryPath, pendingRecovery);
            var restarted = new JsonGenerationGameContextMemory(memoryPath);
            TestAssert.True(
                restarted.Find(source) is not null,
                "A restart must restore a file stranded during quarantine validation.");
            TestAssert.True(
                File.Exists(memoryPath),
                "Pending recovery must return the preserved file to its active path.");
            TestAssert.False(
                File.Exists(pendingRecovery),
                "A restored pending recovery file must not remain duplicated.");
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

    private static Task GameContextMemoryPreservesFutureSchema()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameContextFutureSchemaTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string memoryPath = Path.Combine(root, "game-context.json");
        string source = Path.Combine(root, "ExampleGame", "source.mkv");
        const string future = """
            {"SchemaVersion":"replayfoundry-game-context-memory-2.0-preview","Entries":{"ownedBy":"a newer Replay Foundry"}}
            """;
        try
        {
            File.WriteAllText(memoryPath, future);
            var memory = new JsonGenerationGameContextMemory(memoryPath);
            int changes = 0;
            memory.Changed += (_, _) => changes++;

            TestAssert.True(
                memory.Find(source) is null,
                "A newer optional memory schema must be ignored without blocking setup.");
            memory.Remember(
            [
                new GenerationSourceGameContext(
                    source,
                    "Example Game",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed),
            ]);

            TestAssert.Equal(
                future,
                File.ReadAllText(memoryPath),
                "An older reader must preserve a newer memory document byte-for-byte.");
            TestAssert.Equal(
                0,
                changes,
                "A refused forward-schema write must not announce a saved change.");
            TestAssert.False(
                Directory.Exists(Path.Combine(
                    root,
                    "Diagnostics",
                    "GameContextMemory")),
                "A valid future schema must not be mislabeled as corrupt.");
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

    private static Task GameContextMemoryFailsSoftWhenUnavailable()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameContextUnavailableTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string memoryPath = Path.Combine(root, "game-context.json");
        string source = Path.Combine(root, "ExampleGame", "source.mkv");
        string sibling = Path.Combine(root, "ExampleGame", "other.mkv");
        try
        {
            var memory = new JsonGenerationGameContextMemory(memoryPath);
            memory.Remember(
            [
                new GenerationSourceGameContext(
                    source,
                    "Original Game",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed),
            ]);
            int changes = 0;
            memory.Changed += (_, _) => changes++;

            using (FileStream locked = File.Open(
                       memoryPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                TestAssert.True(
                    memory.Find(sibling) is null,
                    "A temporarily locked memory file must not block Generate setup.");
                memory.Remember(
                [
                    new GenerationSourceGameContext(
                        source,
                        "Replacement Game",
                        contextNotes: null,
                        GenerationGameContextOrigin.UserConfirmed),
                ]);
                TestAssert.Equal(
                    0,
                    changes,
                    "A locked memory file must not report an unsaved replacement.");
            }

            using var transactionAcquired = new ManualResetEventSlim();
            using var releaseTransaction = new ManualResetEventSlim();
            Task transactionHolder = Task.Run(() =>
            {
                using var mutex = new Mutex(
                    initiallyOwned: false,
                    JsonGenerationGameContextMemory.CreateTransactionMutexName(
                        memoryPath));
                bool ownsMutex = false;
                try
                {
                    ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(5));
                    if (!ownsMutex)
                    {
                        throw new InvalidOperationException(
                            "The test could not acquire the game-context transaction.");
                    }
                    transactionAcquired.Set();
                    releaseTransaction.Wait(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    transactionAcquired.Set();
                    if (ownsMutex)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            });
            TestAssert.True(
                transactionAcquired.Wait(TimeSpan.FromSeconds(5)),
                "The competing game-context transaction must start.");
            try
            {
                if (transactionHolder.IsCompleted)
                {
                    transactionHolder.GetAwaiter().GetResult();
                }
                TestAssert.True(
                    memory.Find(sibling) is null,
                    "A busy second app instance must not block Generate setup.");
                memory.Remember(
                [
                    new GenerationSourceGameContext(
                        source,
                        "Concurrent Replacement",
                        contextNotes: null,
                        GenerationGameContextOrigin.UserConfirmed),
                ]);
                TestAssert.Equal(
                    0,
                    changes,
                    "A refused concurrent write must not announce a saved change.");
            }
            finally
            {
                releaseTransaction.Set();
                transactionHolder.GetAwaiter().GetResult();
            }

            GenerationSourceGameContext recalled = memory.Find(sibling) ??
                throw new InvalidOperationException(
                    "The retained memory was not readable after its lock cleared.");
            TestAssert.Equal(
                "Original Game",
                recalled.GameName,
                "A transient read failure must not overwrite retained memory.");
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

    private static Task GameContextMemoryPreservesOriginalSchema()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameContextOriginalSchemaTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string memoryPath = Path.Combine(root, "game-context.json");
        string source = Path.Combine(root, "ExampleGame", "source.mkv");
        string sibling = Path.Combine(root, "ExampleGame", "other.mkv");
        try
        {
            var memory = new JsonGenerationGameContextMemory(memoryPath);
            memory.Remember(
            [
                new GenerationSourceGameContext(
                    source,
                    "Example Game",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed),
            ]);
            string originalSchema = File.ReadAllText(memoryPath).Replace(
                "replayfoundry-game-context-memory-1.2",
                "replayfoundry-game-context-memory-1.0",
                StringComparison.Ordinal);
            File.WriteAllText(memoryPath, originalSchema);

            GenerationSourceGameContext recalled = memory.Find(sibling) ??
                throw new InvalidOperationException(
                    "The original game-context schema was not recalled.");
            TestAssert.Equal(
                "Example Game",
                recalled.GameName,
                "The original supported schema must retain its memory entry.");
            memory.Remember([recalled]);
            TestAssert.True(
                File.ReadAllText(memoryPath).Contains(
                    "replayfoundry-game-context-memory-1.2",
                    StringComparison.Ordinal),
                "The next legitimate save must normalize the original schema.");
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

    private static Task GameContextMemoryNormalizesInheritedFlags()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-GameContextCompatibilityTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string memoryPath = Path.Combine(root, "game-context.json");
        string source = Path.Combine(root, "ExampleGame", "Vertical", "source.mkv");
        string sibling = Path.Combine(root, "ExampleGame", "other.mkv");
        try
        {
            var memory = new JsonGenerationGameContextMemory(memoryPath);
            memory.Remember(
            [
                new GenerationSourceGameContext(
                    source,
                    "Example Game",
                    contextNotes: null,
                    GenerationGameContextOrigin.UserConfirmed),
            ]);

            string brokenMigration = File.ReadAllText(memoryPath).Replace(
                "\"UseOpenGameKnowledge\": false",
                "\"UseOpenGameKnowledge\": null",
                StringComparison.Ordinal);
            TestAssert.True(
                brokenMigration.Contains(
                    "\"UseOpenGameKnowledge\": null",
                    StringComparison.Ordinal),
                "The compatibility fixture must reproduce the interim v1.1 output.");
            File.WriteAllText(memoryPath, brokenMigration);

            GenerationSourceGameContext recalled =
                memory.Find(sibling) ??
                throw new InvalidOperationException(
                    "The inherited game-context entry was not recalled.");
            TestAssert.False(
                recalled.UseOpenGameKnowledge,
                "A missing historical flag must retain the disabled default.");

            memory.Remember([recalled]);
            string normalized = File.ReadAllText(memoryPath);
            TestAssert.False(
                normalized.Contains(
                    "\"UseOpenGameKnowledge\": null",
                    StringComparison.Ordinal),
                "The next legitimate save must normalize inherited flags.");
            TestAssert.True(
                normalized.Contains(
                    "\"UseOpenGameKnowledge\": false",
                    StringComparison.Ordinal),
                "The normalized document must preserve the explicit disabled value.");
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

    private static async Task UserEditsPreserveProvenance()
    {
        var generator = new HeuristicClipEditorialMetadataGenerator();
        ClipEditorialMetadataDraft original =
            await generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    2),
                CancellationToken.None);
        ClipEditorialMetadataDraft edited = original.WithUserEdits(
            "My final title",
            "My final description",
            ["one", "two"]);

        TestAssert.Equal(
            ClipEditorialMetadataOrigin.UserEdited,
            edited.Origin,
            "Edited origin.");
        TestAssert.Equal(original.Generator, edited.Generator, "Generator.");
        TestAssert.Equal(original.Attempt, edited.Attempt, "Attempt.");
        TestAssert.Equal(original.Evidence.Count, edited.Evidence.Count, "Evidence.");
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserEditedDraft,
            edited.Readiness,
            "Ordinary user edits remain a draft until review is marked explicitly.");
        ClipEditorialMetadataDraft reviewed = edited.MarkReviewed();
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserApproved,
            reviewed.Readiness,
            "Review must be a distinct optional transition.");
        ClipEditorialMetadataDraft revised = reviewed.WithUserEdits(
            "My revised title",
            "My revised description",
            ["one", "two"]);
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserEditedDraft,
            revised.Readiness,
            "Editing reviewed copy must reset it to an unreviewed draft.");
    }

    private static async Task AssetEditsRetainMetadata()
    {
        (GenerationOutputAsset asset, ClipEditorialMetadataDraft metadata) =
            await CreateAssetAsync();
        GenerationOutputAsset edited = asset.WithStudioEdits(
            asset.SourceStart + TimeSpan.FromSeconds(1),
            asset.SourceEnd + TimeSpan.FromSeconds(1),
            StudioClipAppearance.CreateDefault(
                GenerationCaptionStylePreset.Clean));

        TestAssert.Same(
            metadata,
            edited.EditorialMetadata!,
            "Studio appearance edits must retain metadata identity.");
        TestAssert.Same(
            asset.EditorialContext!,
            edited.EditorialContext!,
            "Studio appearance edits must retain grounding context.");
    }

    private static async Task FinalizedMetadataReachesPublish()
    {
        (GenerationOutputAsset asset, ClipEditorialMetadataDraft metadata) =
            await CreateAssetAsync();
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ReplayFoundry-PublishMetadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var project = new GenerationOutputProject(
            "project-editorial",
            GenerationMode.IndividualClips,
            outputDirectory,
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var catalog = new GenerationLibraryCatalog(
            session,
            new InMemoryLibraryCatalogStore());
        using var publish = new PublishViewModel(
            catalog,
            youtube: null,
            new InMemoryYouTubePublishPreferencesStore(),
            thumbnailPicker: new TestThumbnailFilePicker());
        string renderedPath = Path.Combine(outputDirectory, "clip.mp4");
        File.WriteAllBytes(renderedPath, [0]);
        GenerationOutputAsset rendered = asset.WithRenderedOutput(renderedPath);

        session.FinalizeProject(
            project.Finalize(
                [rendered],
                DateTimeOffset.UtcNow));

        TestAssert.Equal(metadata.Title, publish.Title, "Publish title.");
        TestAssert.Equal(
            metadata.Description,
            publish.Description,
            "Publish description.");
        TestAssert.Equal(metadata.TagsText, publish.Tags, "Publish tags.");
        Directory.Delete(outputDirectory, recursive: true);
    }

    private static async Task StudioEditorSavesMetadata()
    {
        (GenerationOutputAsset asset, _) = await CreateAssetAsync();
        ClipEditorialMetadataDraft reviewable =
            await new ClipEditorialMetadataGenerationService(
                    new HeuristicClipEditorialMetadataGenerator())
                .GenerateAsync(
                    new ClipEditorialMetadataRequest(
                        asset.EditorialContext!,
                        ClipEditorialProfile.Default,
                        0,
                        ClipEditorialGenerationPreference.HeuristicOnly),
                    CancellationToken.None);
        asset = asset.WithCurrentCutEditorialMetadata(
            asset.EditorialContext!,
            reviewable);
        var project = new GenerationOutputProject(
            "project-editorial-edit",
            GenerationMode.IndividualClips,
            Path.GetFullPath("studio-editorial-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var publicContext = new RecordingGameKnowledgeContextService();
        using var editor = new StudioEditorialMetadataViewModel(
            session,
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator()),
            new ClipEditorialProfileSession(),
            gameKnowledge: publicContext);
        editor.Bind(project, asset);
        TestAssert.True(editor.HasContextReceipt,
            "Studio must expose one privacy-safe context-use receipt for the selected clip.");
        TestAssert.True(
            editor.ContextUsedSummary.Contains(
                "broad local draft",
                StringComparison.OrdinalIgnoreCase) &&
            editor.ContextNeedsReview &&
            !editor.ContextUsedSummary.Contains(
                asset.SourceFullPath,
                StringComparison.OrdinalIgnoreCase),
            "A fail-soft receipt must explain the retained local draft without leaking source paths or raw evidence.");
        TestAssert.True(
            editor.CanonicalGameContextText.Equals(
                "Example Game",
                StringComparison.Ordinal) &&
            !editor.CanonicalGameContextText.Contains(
                "Q123",
                StringComparison.Ordinal) &&
            editor.GameContextSourcesText.Contains(
                "revision fixture-9",
                StringComparison.Ordinal) &&
            editor.SupportedGameContextClaimsText.Contains(
                "Developer",
                StringComparison.Ordinal) &&
            editor.GameContextComponentsText.Contains(
                "WikidataClaims: Complete",
                StringComparison.Ordinal),
            "Studio must present the game name, revision attribution, supported claims, and cache components without exposing support IDs or internal evidence bodies.");
        TestAssert.True(editor.RefreshGameContextCommand.CanExecute(null),
            "A user-confirmed online context must expose independent refresh.");
        await ((AsyncDelegateCommand)editor.RefreshGameContextCommand)
            .ExecuteAsync();
        TestAssert.Equal(1, publicContext.RefreshCalls,
            "Refreshing context must not invoke metadata or visual analysis.");
        TestAssert.True(editor.RemoveCachedGameContextCommand.CanExecute(null),
            "Retained public context must expose one-click removal.");
        editor.RemoveCachedGameContextCommand.Execute(null);
        TestAssert.Equal(1, publicContext.RemoveCalls,
            "One-click removal must strip only cached public context.");
        TestAssert.False(editor.HasCachedPublicGameContext,
            "The receipt must immediately reflect removal.");
        editor.Title = "Chat, this is the saved title";
        editor.Description = "A grounded edited description.";
        editor.Tags = "examplegame, gameplay";

        TestAssert.True(
            editor.SaveCommand.CanExecute(null),
            "A valid draft should be saveable.");
        editor.SaveCommand.Execute(null);

        ClipEditorialMetadataDraft saved = session.Current!.Assets[0]
            .EditorialMetadata!;
        TestAssert.Equal(editor.Title, saved.Title, "Saved title.");
        TestAssert.Equal(
            ClipEditorialMetadataOrigin.UserEdited,
            saved.Origin,
            "Saved origin.");
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserEditedDraft,
            saved.Readiness,
            "Saving ordinary edits must not silently mark them reviewed.");
        TestAssert.True(
            editor.MarkReviewedCommand.CanExecute(null),
            "Review should remain an optional explicit action after saving.");
        editor.MarkReviewedCommand.Execute(null);
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserApproved,
            session.Current!.Assets[0].EditorialMetadata!.Readiness,
            "The explicit review action should be the only Studio transition to reviewed.");
    }

    private static Task WordingPreferencesPreserveDefaultTags()
    {
        string[] expectedTags = ["channel default", "short form"];
        var profile = new ClipEditorialProfileSession();
        profile.Update(new ClipEditorialProfile(
            "Chat",
            ClipEditorialProfile.DefaultNamingGuidance,
            "Original ending.",
            expectedTags));
        using var studio = new StudioEditorialMetadataViewModel(
            outputEditor: null,
            generator: null,
            profile);
        studio.Tags = "Example Game, gameplay, one-off boss";
        studio.AudienceAddress = "Viewers";

        studio.SaveProfileCommand.Execute(null);

        TestAssert.True(
            profile.Current.DefaultTags.SequenceEqual(
                expectedTags,
                StringComparer.Ordinal),
            "Saving Studio wording preferences must not promote the current clip's generated or game-specific tags into global defaults.");
        return Task.CompletedTask;
    }

    private static async Task StudioRerollSavesPendingDraft()
    {
        (GenerationOutputAsset asset, ClipEditorialMetadataDraft original) =
            await CreateAssetAsync();
        var project = new GenerationOutputProject(
            "project-editorial-clean-reroll",
            GenerationMode.IndividualClips,
            Path.GetFullPath("studio-editorial-clean-reroll-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var generator = new RecordingRequestMetadataGenerator();
        using var studio = new StudioViewModel(
            session,
            session,
            new UnusedProjectRenderer(),
            generator,
            new ClipEditorialProfileSession());
        StudioEditorialMetadataViewModel editor = studio.Inspector.Editorial;
        editor.SelectedVariantChoice = editor.VariantChoices.Last();
        TestAssert.True(editor.RerollCommand.CanExecute(null), "Style and rewrite work before any manual edit.");
        TestAssert.False(editor.HasUnsavedChanges, "Choosing a style does not change the current wording.");
        const string unsavedTitle = "My unsaved title must not mask a reroll";
        editor.Title = unsavedTitle;

        TestAssert.True(editor.RerollCommand.CanExecute(null), "Valid pending edits can be saved and rewritten in one action.");
        TestAssert.Equal("Save & rewrite", editor.RerollButtonText, "The action must disclose that it saves first.");
        TestAssert.True(editor.RerollProviderText.Contains("History"), "The editor explains where the saved copy remains.");
        string validDescription = editor.Description;
        editor.Description = "";
        TestAssert.False(editor.RerollCommand.CanExecute(null), "Invalid pending edits cannot be overwritten by a rewrite.");
        editor.Description = validDescription;

        await ((AsyncDelegateCommand)editor.RerollCommand).ExecuteAsync();

        TestAssert.Equal(
            2,
            generator.LastRequest!.PriorAcceptedTitleExclusions.Count,
            "Studio must exclude the generated title and the saved user edit from the next exact-cut reroll.");
        ClipEditorialMetadataDraft rerolled = session.Current!.PrimaryAsset
            .EditorialMetadata!;
        TestAssert.True(rerolled.CopyVersions.Any(copy => copy.Title == unsavedTitle),
            "Save and rewrite must keep the user's edited wording available in History.");
        TestAssert.Equal(
            original.Attempt + 1,
            rerolled.Attempt,
            "The clean Studio reroll must advance the retained attempt.");
        TestAssert.Equal(
            rerolled.Title,
            studio.Inspector.Editorial.Title,
            "The actual session rebind must show the completed reroll instead of restoring stale editor text over it.");
        TestAssert.False(
            studio.Inspector.Editorial.Title.Equals(
                unsavedTitle,
                StringComparison.Ordinal),
            "The saved pre-reroll title must not mask the newly generated draft after session rebinding.");
        TestAssert.False(
            studio.Inspector.Editorial.HasUnsavedChanges,
            "A completed reroll must remain the clean saved session value.");
    }

    private static async Task StudioMetadataPreservesNewerEdits()
    {
        (GenerationOutputAsset original, _) = await CreateAssetAsync();
        var project = new GenerationOutputProject(
            "project-editorial-current",
            GenerationMode.IndividualClips,
            Path.GetFullPath("studio-editorial-current-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [original],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        GenerationOutputAsset trimmed = original.WithStudioEdits(
            original.SourceStart + TimeSpan.FromSeconds(3),
            original.SourceEnd - TimeSpan.FromSeconds(2),
            StudioClipAppearance.CreateDefault(
                GenerationCaptionStylePreset.Clean));
        session.ReplaceAsset(project.Id, trimmed);
        var service = new StudioEditorialMetadataService(
            session,
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator()),
            new ClipEditorialProfileSession());

        service.Save(
            project,
            original,
            "A concrete saved action #ExampleGame",
            "A concrete saved description.",
            "examplegame");
        GenerationOutputAsset saved = session.Current!.Assets[0];
        TestAssert.Equal(trimmed.SourceStart, saved.SourceStart, "Saved metadata must retain the newer start.");
        TestAssert.Equal(trimmed.SourceEnd, saved.SourceEnd, "Saved metadata must retain the newer end.");

        GenerationOutputAsset restyled = saved.WithStudioEdits(
            saved.SourceStart,
            saved.SourceEnd,
            new StudioClipAppearance(
                GenerationCaptionStylePreset.Clean,
                64,
                StudioVideoEffectPreset.Noir,
                35));
        session.ReplaceAsset(project.Id, restyled);
        await service.RerollAsync(
            project,
            saved,
            "Chat",
            string.Empty,
            string.Empty,
            requireAi: false,
            CancellationToken.None);
        GenerationOutputAsset rerolled = session.Current!.Assets[0];
        TestAssert.Equal(restyled.SourceStart, rerolled.SourceStart, "Reroll must retain the latest start.");
        TestAssert.Equal(restyled.SourceEnd, rerolled.SourceEnd, "Reroll must retain the latest end.");
        TestAssert.Equal(StudioVideoEffectPreset.Noir, rerolled.Appearance.VideoEffect, "Reroll must retain the latest effect.");
        TestAssert.Equal(35d, rerolled.Appearance.VideoEffectIntensityPercent, "Reroll must retain effect intensity.");
    }

    private static async Task StudioAiRerollUsesSharedQualityReview()
    {
        (GenerationOutputAsset asset, ClipEditorialMetadataDraft original) =
            await CreateAssetAsync();
        var project = new GenerationOutputProject(
            "project-editorial-shared-ai-review",
            GenerationMode.IndividualClips,
            Path.GetFullPath("studio-editorial-shared-ai-review-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new CurrentRegressionMetadataGenerator();
        var service = new StudioEditorialMetadataService(
            session,
            new ClipEditorialMetadataGenerationService(heuristic, ai),
            new ClipEditorialProfileSession());

        StudioEditorialRerollResult result = await service.RerollAsync(
            project,
            asset,
            "Chat",
            string.Empty,
            string.Empty,
            requireAi: true,
            CancellationToken.None);

        ClipEditorialMetadataDraft rerolled = session.Current!.Assets[0]
            .EditorialMetadata!;
        TestAssert.True(
            result.IsAiAssisted,
            "The completed Studio reroll must retain AI authorship.");
        TestAssert.Equal(
            "The office decor survived another FBC disaster #ExampleGame",
            rerolled.Title,
            "Studio must receive the corrected package after the shared audience-copy review rejects the literal first draft.");
        TestAssert.True(
            ai.BatchRequests.Select(static batch => batch.Length)
                .SequenceEqual([1, 1]),
            "Studio's one-clip reroll must use singleton batch review and its bounded correction.");
        TestAssert.Equal(
            original.Attempt + 1,
            ai.BatchRequests[0].Single().Attempt,
            "Studio's first provider attempt must continue the retained reroll cadence.");
        TestAssert.Equal(
            original.Attempt + 2,
            rerolled.Attempt,
            "The accepted correction must retain its advanced provider attempt.");
        TestAssert.Equal(
            0,
            heuristic.Requests.Count,
            "Studio's AI reroll must never fall back to heuristic copy during quality correction.");
    }

    private static async Task CaptionEditsStaleGroundedCopyUntilReroll()
    {
        GenerationOutputAsset original = await CreateGroundedAiAssetAsync();
        string originalBriefFingerprint =
            original.EditorialContext!.EditorialBrief.Fingerprint;
        GenerationOutputAsset captionEdited = original.WithCaptionTrack(
            CreateCaptionTrack(
                original,
                "Rubber duckies and ketchup bottles are not allowed in the FBC."));
        TestAssert.False(
            captionEdited.EditorialContext!.EditorialBrief.Fingerprint.Equals(
                originalBriefFingerprint,
                StringComparison.Ordinal),
            "Saving different caption words must rebuild the grounded editorial brief.");
        TestAssert.False(
            captionEdited.IsEditorialMetadataCurrentForCut,
            "AI wording bound to the earlier brief must become stale even when the clip boundaries did not move.");
        TestAssert.True(
            captionEdited.EditorialMetadata!.IsPublishReady,
            "Metadata freshness must stay separate from Studio's structural local-render requirement.");

        var project = new GenerationOutputProject(
            "project-caption-metadata-freshness",
            GenerationMode.IndividualClips,
            Path.GetFullPath("caption-metadata-freshness-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [captionEdited],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var heuristic = new RecordingFallbackMetadataGenerator();
        var ai = new CurrentRegressionMetadataGenerator();
        var service = new StudioEditorialMetadataService(
            session,
            new ClipEditorialMetadataGenerationService(heuristic, ai),
            new ClipEditorialProfileSession());
        StudioEditorialDraftSnapshot stale = service.LoadDraft(captionEdited);

        TestAssert.True(
            stale.NeedsCurrentCutRefresh,
            "Studio must surface caption-driven metadata staleness.");
        TestAssert.True(
            stale.CurrentCutStatus.Contains(
                "captions",
                StringComparison.OrdinalIgnoreCase),
            "The refresh explanation must name captions instead of describing only changed boundaries.");
        TestAssert.False(
            project.HasPublishReadyEditorialMetadata,
            "Publish readiness must reject metadata grounded against the earlier captions.");

        await service.RerollAsync(
            project,
            captionEdited,
            "Chat",
            string.Empty,
            string.Empty,
            requireAi: true,
            CancellationToken.None);

        GenerationOutputAsset refreshed = session.Current!.PrimaryAsset;
        TestAssert.Equal(
            "Rubber duckies and ketchup bottles are not allowed in the FBC.",
            ai.BatchRequests[0].Single().Context.Transcripts.Single().Text,
            "The fresh AI request must use the saved caption correction as its current transcript context.");
        TestAssert.True(
            refreshed.IsEditorialMetadataCurrentForCut,
            "A successful reroll must bind its grounding audit to the refreshed caption-aware brief.");
        TestAssert.True(
            session.Current.HasPublishReadyEditorialMetadata,
            "Fresh caption-aware AI metadata must restore Publish readiness.");
        TestAssert.False(
            service.LoadDraft(refreshed).NeedsCurrentCutRefresh,
            "Studio must clear the refresh state after the caption-aware reroll.");
        TestAssert.Equal(
            0,
            heuristic.Requests.Count,
            "Caption freshness recovery must stay on the requested AI path.");
    }

    private static async Task ManualMetadataSaveClearsStaleGrounding()
    {
        GenerationOutputAsset original = await CreateGroundedAiAssetAsync();
        GenerationOutputAsset captionEdited = original.WithCaptionTrack(
            CreateCaptionTrack(
                original,
                "I found the hidden route behind the FBC checkpoint."));
        var project = new GenerationOutputProject(
            "project-caption-manual-metadata",
            GenerationMode.IndividualClips,
            Path.GetFullPath("caption-manual-metadata-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [captionEdited],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var service = new StudioEditorialMetadataService(
            session,
            generator: null,
            new ClipEditorialProfileSession());

        service.Save(
            project,
            captionEdited,
            "That checkpoint hid the next route #ExampleGame",
            "I found the hidden route behind the checkpoint after updating the captions.",
            "ExampleGame, checkpoint");

        GenerationOutputAsset saved = session.Current!.PrimaryAsset;
        TestAssert.True(
            saved.IsEditorialMetadataCurrentForCut,
            "A user save against the current caption-aware context must not remain permanently stale.");
        TestAssert.Null(
            saved.EditorialMetadata!.GroundingAudit,
            "A manual edit must remove, rather than falsely rebind, an obsolete AI grounding audit.");
        TestAssert.Equal(
            ClipEditorialMetadataOrigin.UserEdited,
            saved.EditorialMetadata.Origin,
            "The manually reconciled wording must retain user authorship.");

        service.MarkReviewed(session.Current, saved);

        GenerationOutputAsset reviewed = session.Current!.PrimaryAsset;
        TestAssert.True(
            reviewed.IsEditorialMetadataCurrentForCut,
            "Reviewing the manually reconciled wording must preserve current-context freshness.");
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserApproved,
            reviewed.EditorialMetadata!.Readiness,
            "The explicit review must still advance the manual draft state.");
        TestAssert.Null(
            reviewed.EditorialMetadata.GroundingAudit,
            "Review must not resurrect the obsolete AI-to-brief binding.");
        TestAssert.True(
            session.Current.HasPublishReadyEditorialMetadata,
            "A current user-authored review must restore Publish readiness without requiring another AI run.");
    }

    private static async Task StudioMetadataRerollUsesCurrentCut()
    {
        (GenerationOutputAsset original, _) = await CreateAssetAsync();
        GenerationOutputAsset trimmed = original.WithStudioEdits(
            original.SourceStart + TimeSpan.FromSeconds(5),
            original.SourceEnd - TimeSpan.FromSeconds(4),
            original.Appearance);
        TestAssert.False(
            trimmed.IsEditorialMetadataCurrentForCut,
            "A changed Studio window must identify Generate-time copy as stale.");

        var project = new GenerationOutputProject(
            "project-editorial-current-cut",
            GenerationMode.IndividualClips,
            Path.GetFullPath("studio-editorial-current-cut-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [trimmed],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        TestAssert.False(
            session.Current!.HasPublishReadyEditorialMetadata,
            "A trimmed clip must not render with metadata made for an older cut.");
        var generator = new RecordingRequestMetadataGenerator();
        var service = new StudioEditorialMetadataService(
            session,
            generator,
            new ClipEditorialProfileSession());

        await service.RerollAsync(
            project,
            trimmed,
            "Chat",
            string.Empty,
            string.Empty,
            requireAi: false,
            CancellationToken.None);

        ClipEditorialContext requestContext = generator.LastRequest?.Context ??
            throw new InvalidOperationException(
                "The Studio metadata generator was not invoked.");
        TestAssert.Equal(
            trimmed.SourceStart,
            requestContext.SourceStart,
            "Reroll request start must use the current cut.");
        TestAssert.Equal(
            trimmed.SourceEnd,
            requestContext.SourceEnd,
            "Reroll request end must use the current cut.");
        TestAssert.Equal(
            0,
            requestContext.Transcripts.Count,
            "An untimed Generate-time transcript must not leak into a changed cut.");
        TestAssert.Equal(
            0,
            generator.LastRequest!.PriorAcceptedTitleExclusions.Count,
            "Title history from the prior cut must not cross a changed Studio window.");
        TestAssert.True(
            session.Current!.Assets[0].IsEditorialMetadataCurrentForCut,
            "The refreshed metadata context must be rebound to the current cut.");
    }

    private static async Task StudioMetadataRejectsSupersededCut()
    {
        (GenerationOutputAsset original, _) = await CreateAssetAsync();
        var project = new GenerationOutputProject(
            "project-editorial-superseded-cut",
            GenerationMode.IndividualClips,
            Path.GetFullPath("studio-editorial-superseded-cut-output"),
            1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [original],
            DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var generator = new DeferredMetadataGenerator();
        var service = new StudioEditorialMetadataService(
            session,
            generator,
            new ClipEditorialProfileSession());

        Task<StudioEditorialRerollResult> reroll = service.RerollAsync(
            project,
            original,
            "Chat",
            string.Empty,
            string.Empty,
            requireAi: false,
            CancellationToken.None);
        await generator.Started.WaitAsync(TimeSpan.FromSeconds(5));
        GenerationOutputAsset newerCut = original.WithStudioEdits(
            original.SourceStart + TimeSpan.FromSeconds(3),
            original.SourceEnd,
            original.Appearance);
        session.ReplaceAsset(project.Id, newerCut);
        generator.Complete();

        InvalidOperationException error =
            await TestAssert.ThrowsAsync<InvalidOperationException>(
                async () => await reroll,
                "Metadata for an older cut must not overwrite a newer Studio edit.");
        TestAssert.True(
            error.Message.Contains(
                "start or end changed",
                StringComparison.OrdinalIgnoreCase),
            "The superseded-cut failure must be actionable.");
        TestAssert.Same(
            newerCut,
            session.Current!.Assets[0],
            "The latest cut must survive the rejected metadata result.");
    }

    private static async Task CaptionEditsDuringRerollArePreserved()
    {
        GenerationOutputAsset original = await CreateGroundedAiAssetAsync();
        var project = new GenerationOutputProject("caption-reroll-race", GenerationMode.IndividualClips,
            Path.GetFullPath("caption-reroll-race-output"), 1, ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [original], DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession(); session.Publish(project);
        var generator = new DeferredMetadataGenerator();
        var service = new StudioEditorialMetadataService(session, generator, new ClipEditorialProfileSession());
        var pending = service.RerollAsync(project, original, "Chat", "", "", false, CancellationToken.None);
        await generator.Started.WaitAsync(TimeSpan.FromSeconds(5));
        var corrected = original.WithCaptionTrack(CreateCaptionTrack(original, "We reached a different bridge."));
        session.ReplaceAsset(project.Id, corrected);
        generator.Complete();
        await TestAssert.ThrowsAsync<InvalidOperationException>(async () => await pending,
            "Copy generated from obsolete captions must not be applied to a corrected clip.");
        TestAssert.Same(corrected, session.Current!.PrimaryAsset, "The saved correction and its current context must survive.");
    }

    private static async Task CompleteCaptionContextGuardsReroll()
    {
        foreach (bool timingOnly in new[] { false, true })
        {
            (GenerationOutputAsset original, _) = await CreateAssetAsync(hasAudio: true);
            GenerationCandidateCaptionTrack Track(bool changed)
            {
                const string neighborhood = "complete-caption-revision";
                var segments = Enumerable.Range(0, 13).Select(index =>
                {
                    TimeSpan start = TimeSpan.FromSeconds(1 + index * 2 + (changed && timingOnly && index == 0 ? 0.1 : 0));
                    TimeSpan end = TimeSpan.FromSeconds(2 + index * 2);
                    string text = changed && !timingOnly && index == 12 ? "The final door was closed." : $"Sentence {index + 1} stays here.";
                    return new AudioTranscriptionSegment($"segment-{index}", neighborhood, text, start, end,
                        original.SourceStart + start, original.SourceStart + end);
                }).ToArray();
                return GenerationCandidateCaptionTrack.RestoreStudioHandoff(original.Id, neighborhood,
                    new GenerationCaptionSourceSelection(original.SourceFullPath, original.SourceMedia.AudioStreams.Single().Index,
                        CaptionAudioContentRole.CreatorCommentary, GenerationCaptionLanguagePolicy.English),
                    original.Appearance.CaptionStyle, original.SourceStart, original.Duration, original.SourceDuration,
                    segments, isUserEdited: true, GenerationCaptionSuppressionReason.None);
            }
            original = original.WithCaptionTrack(Track(false));
            var project = new GenerationOutputProject("complete-caption-revision", GenerationMode.IndividualClips,
                Path.GetFullPath("complete-caption-revision-output"), 1, ClipFulfillmentPreference.FillRequestedCount,
                GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [original], DateTimeOffset.UtcNow);
            var session = new GenerationOutputSession(); session.Publish(project);
            var generator = new DeferredMetadataGenerator();
            var service = new StudioEditorialMetadataService(session, generator, new ClipEditorialProfileSession());
            var pending = service.RerollAsync(project, original, "Chat", "", "", false, CancellationToken.None);
            await generator.Started.WaitAsync(TimeSpan.FromSeconds(5));
            var corrected = original.WithCaptionTrack(Track(true));
            TestAssert.Equal(original.EditorialContext!.EditorialBrief.Fingerprint, corrected.EditorialContext!.EditorialBrief.Fingerprint,
                "This regression must exercise an edit omitted by the bounded brief identity.");
            session.ReplaceAsset(project.Id, corrected);
            generator.Complete();
            await TestAssert.ThrowsAsync<InvalidOperationException>(async () => await pending,
                "All retained transcript content and timing must participate in pending rewrite concurrency checks.");
            TestAssert.Same(corrected, session.Current!.PrimaryAsset, "Rejected copy must preserve the corrected transcript and captions.");
        }
    }

    private static Task CompleteRevisionProjectsRetainedVisualText()
    {
        using var fixture = new ModelFreeGroundedExecutorFixture();
        ClipEditorialContext context = fixture.CreateRequestWithVisualText().Context;
        string revision = StudioEditorialContextRevision.Create(context);
        TestAssert.Equal(64, revision.Length, "A complete retained context must create a bounded revision identity.");
        TestAssert.Equal(revision, StudioEditorialContextRevision.Create(context), "The explicit value projection must be deterministic.");
        TestAssert.True(revision != StudioEditorialContextRevision.Create(context.WithVisualText(null)),
            "Removing retained OCR facts must invalidate pending copy independently of its bounded brief.");
        return Task.CompletedTask;
    }

    private static async Task RerollHistoryRetainsPreEnrichmentContext()
    {
        (GenerationOutputAsset original, _) = await CreateAssetAsync();
        var source = new GameKnowledgeSource("history-source", GameKnowledgeSourceKind.Wikipedia, "Example Game",
            new Uri("https://example.invalid/example-game"), "revision-1", DateTimeOffset.UnixEpoch,
            "CC-BY-SA-4.0", new Uri("https://creativecommons.org/licenses/by-sa/4.0/"), "Example contributors", new string('a', 64));
        const string knowledgeText = "The game includes a bridge and an arch.";
        var passage = new GameKnowledgePassage("history-passage", source.Id, "Overview", knowledgeText,
            GameKnowledgePassage.ComputeSha256(knowledgeText));
        var snapshot = new GameKnowledgeSnapshot(original.EditorialContext!.GameContext.GameName,
            new GameKnowledgeProviderIdentity("fixture", "1.0"), DateTimeOffset.UnixEpoch, [source], [passage]);
        var match = new GameKnowledgeMatch(passage, GameKnowledgeMatchStrength.GeneralContext, 0.8,
            ["bridge"], [], GameKnowledgeTemporalRelation.CurrentEventCandidate);
        var knowledge = new RecordingGameKnowledgeContextService
        {
            Enrichment = context => context.WithGameKnowledge(new ClipGameKnowledgeContext(context.GameContext.GameName, snapshot, [match])),
        };
        var project = new GenerationOutputProject("copy-history-enrichment", GenerationMode.IndividualClips,
            Path.GetFullPath("copy-history-enrichment-output"), 1, ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [original], DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession(); session.Publish(project);
        var service = new StudioEditorialMetadataService(session, new RecordingRequestMetadataGenerator(), new ClipEditorialProfileSession(), knowledge);
        await service.RerollAsync(project, original, "Chat", "", "", true, CancellationToken.None);
        GenerationOutputAsset result = session.Current!.PrimaryAsset;
        TestAssert.True(original.EditorialContext.EditorialBrief.Fingerprint != result.EditorialContext!.EditorialBrief.Fingerprint,
            "The fixture must add context during enrichment.");
        TestAssert.Equal(StudioEditorialContextRevision.CreateDurable(original.EditorialContext), result.EditorialMetadata!.CopyVersions.Single().ContextFingerprint,
            "Old wording must retain the identity under which it was authored, rather than acquiring new grounding through reroll enrichment.");
    }

    private static async Task CopyRestorationRequiresCompleteRevision()
    {
        (GenerationOutputAsset asset, _) = await CreateAssetAsync();
        ClipEditorialContext Context(bool corrected)
        {
            var spans = Enumerable.Range(0, 13).Select(index => new ClipEditorialTranscriptSpan(
                asset.SourceStart + TimeSpan.FromSeconds(index * 2), asset.SourceStart + TimeSpan.FromSeconds(index * 2 + 1),
                corrected && index == 12 ? "The last door was closed." : $"Sentence {index + 1} stays here.")).ToArray();
            return asset.EditorialContext!.WithTranscripts([new ClipEditorialTranscriptContext(1,
                new AudioContentRoleAssignment(AudioContentRole.CreatorSpeech, AudioContentRoleSource.UserConfirmed),
                string.Join(" ", spans.Select(static span => span.Text)), ClipEditorialTranscriptAuthority.UserCorrected, spans)]);
        }
        ClipEditorialContext original = Context(false), corrected = Context(true);
        TestAssert.Equal(original.EditorialBrief.Fingerprint, corrected.EditorialBrief.Fingerprint,
            "The restore regression must exercise content outside the brief's bounded claims.");
        var currentMetadata = await new HeuristicClipEditorialMetadataGenerator().GenerateAsync(
            new ClipEditorialMetadataRequest(original, ClipEditorialProfile.Default, 0), CancellationToken.None);
        var draft = currentMetadata.WithUserEdits("A different title", currentMetadata.Description, currentMetadata.Tags)
            .RememberPreviousCopy(currentMetadata, StudioEditorialContextRevision.CreateDurable(original));
        asset = asset.WithCurrentCutEditorialMetadata(original, draft);
        var project = new GenerationOutputProject("copy-restore-context", GenerationMode.IndividualClips,
            Path.GetFullPath("copy-restore-context-output"), 1, ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [asset], DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioEditorialMetadataViewModel(session, new RecordingRequestMetadataGenerator(), new ClipEditorialProfileSession());
        editor.Bind(project, asset);
        TestAssert.True(editor.RestoreCopyCommand.CanExecute(null), "Exact current-context wording must be restorable.");
        editor.Bind(project, asset.WithCurrentCutEditorialMetadata(corrected, draft));
        TestAssert.False(editor.RestoreCopyCommand.CanExecute(null), "A correction beyond the first twelve claims must invalidate restoration.");
        var legacy = draft.RememberPreviousCopy(asset.EditorialMetadata!, original.EditorialBrief.Fingerprint);
        editor.Bind(project, asset.WithCurrentCutEditorialMetadata(original, legacy));
        TestAssert.True(editor.HasCopyVersions, "Earlier brief-only versions remain available for comparison.");
        TestAssert.False(editor.RestoreCopyCommand.CanExecute(null), "A legacy brief-only identity cannot claim complete current-context validity.");
    }

    private static async Task AcceptedHiddenMomentRefreshesEditorialMetadata()
    {
        GenerationMomentFindingRequest request =
            GenerationMomentFindingTests.CreateRequest(
                sourceCount: 1,
                desiredCount: 1,
                analysisDepth: GenerationAnalysisDepth.Thorough);
        GenerationMomentFindingResult moments =
            new GenerationMomentFindingService(
                new GenerationMomentFindingTests.RecordingMomentFinder(
                    [[90, 80, 70]]))
            .Find(request);
        GenerationHiddenMomentDeck deck =
            GenerationHiddenMomentPlanner.Create(moments);
        var ai = new HiddenMomentRetryMetadataGenerator();
        var deferredText = new RecordingHiddenVisualText();
        var service = new GenerationEditorialMetadataService(
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator(),
                ai),
            new ClipEditorialProfileSession(), visualText: deferredText);
        GenerationHiddenMomentDeck hydrated =
            await service.GenerateHiddenAsync(
                deck,
                candidateIntelligence: null,
                CancellationToken.None);

        GenerationHiddenMoment provisional = hydrated.Moments[1];
        TestAssert.Equal(0, deferredText.Calls, "Unused AI alternatives must not decode any OCR frames during generation.");
        GenerationHiddenMoment persisted = provisional.ToStudioHandoff();
        TestAssert.False(
            persisted.HasGenerationProvenance,
            "The regression fixture must exercise the persisted Studio handoff rather than the live Generate graph.");
        TestAssert.True(
            persisted.EditorialContext is not null,
            "Persisted alternates must retain their bounded editorial context for later metadata preparation.");

        GenerationMomentCandidate selected = moments.SelectedCandidates[0];
        string selectedReason = selected.Candidate.Score.Components
            .OrderByDescending(static component =>
                component.SignedContribution)
            .Select(static component => component.Explanation)
            .FirstOrDefault() ?? "Selected deterministic candidate.";
        var selectedContext = new ClipEditorialContext(
            selected.Id,
            selected.AnalyzedSource.PreparedSource.Media.FullPath,
            "Gameplay",
            selected.Candidate.Window.Start,
            selected.Candidate.Window.End,
            selected.AnalyzedSource.PreparedSource.Media.Duration,
            selected.FinalScore,
            selectedReason,
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    "selected-moment",
                    ClipEditorialEvidenceKind.DeterministicMoment,
                    selectedReason),
            ],
            gameContext: persisted.EditorialContext!.GameContext,
            gameplayRegion: persisted.EditorialContext.GameplayRegion);
        string existingBrowserTitle =
            "The Existing Route Changed the Search " +
            selectedContext.GameContext.AudienceGameHashtag;
        var selectedMetadata = new ClipEditorialMetadataDraft(
            existingBrowserTitle,
            "I followed the existing route until the search moved into another section.",
            [selectedContext.GameContext.AudienceGameName],
            ClipEditorialMetadataOrigin.Heuristic,
            new ClipEditorialMetadataGeneratorIdentity(
                "Existing Browser metadata",
                "1.0.0"),
            attempt: 0,
            selectedContext.Evidence);
        var selectedAsset = new GenerationOutputAsset(
            selected.Id,
            1,
            selected.AnalyzedSource.PreparedSource.Media,
            outputFullPath: null,
            selected.Candidate.Window.Start,
            selected.Candidate.Window.End,
            selected.FinalScore,
            request.Setup.QualityThreshold,
            selected.SelectionReason,
            selectedReason,
            editorialContext: selectedContext,
            editorialMetadata: selectedMetadata,
            preferenceFeatures:
                GenerationClipPreferenceFeatureExtractor.Create(selected));
        var project = new GenerationOutputProject(
            "project-persisted-hidden-metadata",
            GenerationMode.IndividualClips,
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundryPersistedHiddenMetadataTest"),
            requestedCount: 1,
            ClipFulfillmentPreference.QualityFirst,
            moments.FulfillmentOutcome,
            [selectedAsset],
            DateTimeOffset.UtcNow,
            hiddenMoments: [persisted]);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var studioHiddenMoments = new StudioHiddenMomentsViewModel(
            session,
            previewMediaService: null,
            decisionStore: null,
            editorialMetadata: service);
        studioHiddenMoments.Bind(project);
        studioHiddenMoments.OpenCommand.Execute(null);
        studioHiddenMoments.AcceptCommand.Execute(null);
        using (var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5)))
        {
            await studioHiddenMoments.WaitForQueueIdleAsync(timeout.Token);
        }

        GenerationOutputAsset accepted = session.Current!.Assets.Single(asset =>
            asset.Id.Equals(persisted.Id, StringComparison.Ordinal));
        TestAssert.Equal(1, deferredText.Calls, "Promoting one saved alternative must enrich exactly that clip before writing its title.");

        TestAssert.Equal(
            0,
            ai.SingleCalls,
            "Accepted review must not bypass the shared batch editorial review policy.");
        TestAssert.Equal(
            3,
            ai.BatchRequests.Count,
            "Abstract and Browser-colliding drafts must receive the same bounded AI corrections used by the main Generate pipeline.");
        TestAssert.True(
            ai.BatchRequests.All(static batch => batch.Length == 1),
            "Review Moments should keep one queued clip isolated while using the shared batch policy.");
        TestAssert.Equal(
            ClipEditorialVariantIntent.SpecificCuriosity,
            ai.BatchRequests[0].Single().VariantIntent,
            "The retained review order must choose a stable initial editorial angle.");
        TestAssert.Equal(
            ClipEditorialVariantIntent.OutcomeFocused,
            ai.BatchRequests[1].Single().VariantIntent,
            "A corrective rewrite must rotate to the next shared editorial angle.");
        TestAssert.Equal(
            ClipEditorialVariantIntent.DirectAction,
            ai.BatchRequests[2].Single().VariantIntent,
            "The final bounded rewrite must complete the shared angle rotation.");
        TestAssert.True(
            ai.BatchRequests[0].Single().PriorAcceptedTitleExclusions.Any(
                exclusion => exclusion.Title.Equals(
                    existingBrowserTitle,
                    StringComparison.OrdinalIgnoreCase)),
            "Accepted Review Moments must avoid titles already visible in the Studio browser.");
        TestAssert.Equal(
            ClipEditorialMetadataOrigin.AiAssisted,
            accepted.EditorialMetadata!.Origin,
            "The accepted alternate replaces its deck placeholder with grounded AI metadata in Thorough mode.");
        TestAssert.Null(
            provisional.EditorialMetadata,
            "An AI-required hidden moment must carry context without pre-authoring audience copy before acceptance.");
        TestAssert.True(
            !string.IsNullOrWhiteSpace(
                accepted.EditorialMetadata.Title) &&
            !string.IsNullOrWhiteSpace(
                accepted.EditorialMetadata.Description),
            "Studio acceptance must author complete audience copy through AI.");
        TestAssert.False(
            accepted.EditorialMetadata.Title.Contains(
                "piece of the story",
                StringComparison.OrdinalIgnoreCase),
            "Review Moments must not retain the retired abstract title family.");
        TestAssert.Equal(
            accepted.Id,
            accepted.EditorialContext!.CandidateId,
            "Refreshed metadata remains bound to the accepted hidden candidate.");
    }

    private static async Task HeuristicMetadataDoesNotLeakInstructionsOrTiming()
    {
        var generator = new HeuristicClipEditorialMetadataGenerator();
        const string instruction = "COPY THIS STYLE INSTRUCTION";
        ClipEditorialMetadataDraft draft = await generator.GenerateAsync(
            new ClipEditorialMetadataRequest(
                CreateContext(),
                new ClipEditorialProfile(
                    "Chat",
                    instruction,
                    null),
                3),
            CancellationToken.None);

        TestAssert.False(draft.Title.Contains(instruction, StringComparison.Ordinal), "Style guidance is not title content.");
        TestAssert.False(draft.Description.Contains(instruction, StringComparison.Ordinal), "Style guidance is not description content.");
        TestAssert.False(draft.Title.Contains("16:00", StringComparison.Ordinal), "Source time must not enter the title.");
        TestAssert.False(draft.Description.Contains("16:00", StringComparison.Ordinal), "Source time must not enter the description.");
        TestAssert.False(
            draft.Description.Contains(
                "how would you have handled it",
                StringComparison.OrdinalIgnoreCase),
            "Heuristic descriptions must not invent audience calls to action.");
        TestAssert.False(
            draft.Description.Contains(
                "same play",
                StringComparison.OrdinalIgnoreCase),
            "Heuristic descriptions must not invent engagement prompts.");
        TestAssert.False(
            draft.Description.Contains(
                "stood out to you",
                StringComparison.OrdinalIgnoreCase),
            "Heuristic descriptions must remain grounded rather than canned.");
        TestAssert.False(
            draft.Title.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase) ||
            draft.Title.StartsWith("Review this", StringComparison.OrdinalIgnoreCase) ||
            draft.Title.StartsWith("Add a title", StringComparison.OrdinalIgnoreCase),
            "A heuristic-only run must still hand Studio a real neutral working title rather than an empty-title instruction.");
    }

    private static Task QwenMetadataRejectsUngroundedContent()
    {
        ClipEditorialMetadataRequest request = new(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);
        string[] tags = ["ExampleGame", "gaming"];

        Qwen3VlOutputParseException generic =
            TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Gameplay peak moment #ExampleGame",
                "A generic gameplay highlight.",
                tags,
                request),
            "Generic-only titles must be rejected.");
        TestAssert.True(
            generic.Message.Contains(
                "no concrete supported content words",
                StringComparison.Ordinal),
            "Generic-title rejection must identify its exact failed rule.");
        Qwen3VlOutputParseException timing =
            TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Opening the gate #ExampleGame",
                "The gate opens, starting at 16:00 in the source.",
                tags,
                request),
            "Internal source timing must be rejected.");
        TestAssert.True(
            timing.Message.Contains(
                "description exposes internal source timing",
                StringComparison.Ordinal),
            "Timing rejection must identify its exact failed rule.");
        Qwen3VlOutputParseException bookkeeping =
            TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Visual evidence observed #ExampleGame",
                "An observation supports this clip.",
                tags,
                request),
            "Analysis bookkeeping must not reach audience metadata.");
        TestAssert.True(
            bookkeeping.Message.Contains(
                "analysis bookkeeping",
                StringComparison.Ordinal),
            "Bookkeeping rejection must identify its exact failed rule.");
        Qwen3VlOutputParseException languageDrift =
            TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "我认为这个时刻会很有趣 #ExampleGame",
                "这个说明完全切换成了与应用界面不同的语言。",
                tags,
                request),
            "Predominantly non-Latin audience copy must reject under the English product-language policy.");
        TestAssert.True(
            languageDrift.Message.Contains(
                "English output-language policy",
                StringComparison.Ordinal),
            "Language drift rejection must identify the failed policy.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Found a note on the 地板 #ExampleGame",
                "I found the note on the floor.",
                tags,
                request),
            "A stray mixed-script token outside the confirmed game identity must reject.");
        Qwen3VlOutputParseException concatenatedHashtag =
            TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Opening the gate#ExampleGame",
                "The gate opens while the selected dialogue calls for movement.",
                tags,
                request),
            "Audience metadata must separate the content title from its hashtag.");
        TestAssert.True(
            concatenatedHashtag.Message.Contains(
                "embedded hashtag",
                StringComparison.Ordinal),
            "Hashtag-spacing rejection must identify the failed policy.");
        foreach (string unsupportedTag in new[]
                 {
                     "new release",
                     "best game 2026",
                     "PC gaming",
                     "PlayStation 5 gameplay",
                 })
        {
            Qwen3VlOutputParseException unsupported =
                TestAssert.Throws<Qwen3VlOutputParseException>(
                    () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                        "Opened the sealed gate #ExampleGame",
                        "I crossed the threshold and reached the courtyard.",
                        ["ExampleGame", unsupportedTag],
                        request),
                    "A generated release, year, or platform tag requires typed grounding that this request does not carry.");
            TestAssert.True(
                unsupported.Message.Contains(
                    "unsupported release, year, or platform claim",
                    StringComparison.Ordinal),
                "The high-risk tag rejection should identify the unsupported claim class.");
        }
        TestAssert.False(
            ClipEditorialGeneratedTags.ContainsUnsupportedGeneratedClaim(
                "NBA2K2026",
                "NBA 2K 2026",
                "#NBA2K2026"),
            "A year that is literally part of the confirmed game identity remains grounded.");
        TestAssert.False(
            ClipEditorialGeneratedTags.ContainsUnsupportedGeneratedClaim(
                "switch",
                "Example Game",
                "#ExampleGame"),
            "An ordinary supported object tag must not be mistaken for a Nintendo platform claim.");
        var explicitPlatformRequest = new ClipEditorialMetadataRequest(
            request.Context,
            new ClipEditorialProfile(defaultTags: ["PC gaming"]),
            0,
            ClipEditorialGenerationPreference.AiRequired);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Opened the sealed gate #ExampleGame",
            "I crossed the threshold and reached the courtyard.",
            ["ExampleGame", "PC gaming"],
            explicitPlatformRequest);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Released the prisoner #ExampleGame",
            "I opened the cell and watched the prisoner cross the threshold.",
            ["ExampleGame", "release"],
            request);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Opened the gate #ExampleGame",
            "I opened the gate while the selected dialogue called for movement.",
            tags,
            request);

        TimeSpan[] textTimes =
        [
            request.Context.SourceStart + TimeSpan.FromSeconds(1),
            request.Context.SourceStart + TimeSpan.FromSeconds(2),
        ];
        ClipEditorialContext interfaceContext = request.Context.WithVisualText(
            new ClipVisualTextContext(
                request.Context.CandidateId,
                request.Context.SourceFullPath,
                NormalizedRectangle.FullFrame,
                frames: [],
                anchors:
                [
                    new VisualTextAnchor(
                        "voidling bound",
                        "VOIDLING BOUND",
                        VisualTextAnchorAuthority.RepeatedAcrossFrames,
                        textTimes),
                ]));
        var interfaceRequest = new ClipEditorialMetadataRequest(
            interfaceContext,
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);
        Qwen3VlOutputParseException inventedPlatform =
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                    "Opened the Steam Client #ExampleGame",
                    "I opened the Steam Client before returning to the run.",
                    ["ExampleGame", "menu"],
                    interfaceRequest,
                    requireInterfaceAttributionAuthority: true),
                "A familiar launcher layout must not become an unsupported platform brand.");
        TestAssert.True(
            inventedPlatform.Message.Contains(
                "unsupported interface platform identity",
                StringComparison.Ordinal),
            "Platform-attribution rejection must name its exact authority failure.");
        Qwen3VlOutputParseException inventedPhysicalDisplay =
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                    "VOIDLING BOUND appeared on the display #ExampleGame",
                    "VOIDLING BOUND blinked on the display after I crossed the room.",
                    ["ExampleGame", "objective"],
                    interfaceRequest,
                    requireInterfaceAttributionAuthority: true),
                "Stable HUD text must not be reassigned to an imagined physical display.");
        TestAssert.True(
            inventedPhysicalDisplay.Message.Contains(
                "attribution to a physical display source",
                StringComparison.Ordinal),
            "Display-attribution rejection must identify the spatial authority failure.");
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Saw VOIDLING BOUND in the HUD #ExampleGame",
            "The HUD showed VOIDLING BOUND after I crossed the room.",
            ["ExampleGame", "objective"],
            interfaceRequest,
            requireInterfaceAttributionAuthority: true);
        return Task.CompletedTask;
    }

    private static Task CreatorCommentaryShapesAudienceCopy()
    {
        ClipEditorialTranscriptContext reviewedCreatorCommentary = new(
            1,
            new AudioContentRoleAssignment(
                AudioContentRole.CreatorSpeech,
                AudioContentRoleSource.UserConfirmed),
            "Rubber duckies are not allowed here",
            ClipEditorialTranscriptAuthority.HumanReviewed);
        ClipEditorialMetadataRequest request = new(
            CreateContext([reviewedCreatorCommentary]),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);

        IReadOnlyList<ClipEditorialMetadataQualityIssue> literalIssues =
            ClipEditorialMetadataQuality.Evaluate(
                "Document revealed restricted items",
                "A document listed rubber ducks beside ketchup bottles.",
                request.Context);
        TestAssert.True(
            literalIssues.Any(static issue =>
                issue.Code ==
                    ClipEditorialMetadataQualityIssueCode.LiteralSceneReport),
            "Reviewed creator commentary should penalize a literal document inventory.");

        IReadOnlyList<ClipEditorialMetadataQualityIssue> naturalIssues =
            ClipEditorialMetadataQuality.Evaluate(
                "Rubber duckies are not allowed here",
                "The restricted-items notice grouped rubber ducks with ketchup bottles.",
                request.Context);
        TestAssert.True(
            naturalIssues.All(static issue =>
                issue.Code !=
                    ClipEditorialMetadataQualityIssueCode.LiteralSceneReport),
            "Grounded creator wording should not be treated as a literal scene report.");
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Rubber duckies are not allowed here",
            "The restricted-items notice grouped rubber ducks with ketchup bottles.",
            ["ExampleGame", "rubber ducks", "restricted items"],
            request);

        ClipEditorialMetadataRequest reviewedDialogueRequest = new(
            CreateContext(
            [
                new ClipEditorialTranscriptContext(
                    1,
                    new AudioContentRoleAssignment(
                        AudioContentRole.GameDialogue,
                        AudioContentRoleSource.UserConfirmed),
                    "Rubber duckies are not allowed here",
                    ClipEditorialTranscriptAuthority.HumanReviewed),
            ]),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);
        Qwen3VlOutputParseException dialogueDidNotAuthorizeCommentary =
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                    "Rubber duckies are not allowed here",
                    "The restricted-items notice grouped rubber ducks with ketchup bottles.",
                    ["ExampleGame", "rubber ducks"],
                    reviewedDialogueRequest),
                "Reviewed game dialogue must not unlock creator-commentary voice.");
        TestAssert.True(
            dialogueDidNotAuthorizeCommentary.Message.Contains(
                "present-tense",
                StringComparison.OrdinalIgnoreCase),
            "Only reviewed CreatorSpeech may preserve a grounded present-tense commentary premise.");
        return Task.CompletedTask;
    }

    private static Task QwenMetadataEnforcesCreatorVoice()
    {
        ClipEditorialMetadataRequest request = new(
            CreateContext(
            [
                new ClipEditorialTranscriptContext(
                    1,
                    new AudioContentRoleAssignment(
                        AudioContentRole.CreatorSpeech,
                        AudioContentRoleSource.UserConfirmed),
                    "automatic words should never become audience copy"),
            ]),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);
        string[] tags = ["ExampleGame", "skill menu"];

        AssertQualityRejects(
            request,
            "Choosing the next skill #ExampleGame",
            "The player opens the skill menu and confirms an upgrade.",
            tags,
            ClipEditorialMetadataQualityIssueCode.ThirdPersonCreatorFraming);
        AssertQualityRejects(
            request,
            "A man in a green shirt says something #ExampleGame",
            "I heard a man in a green shirt beside the doorway.",
            tags,
            ClipEditorialMetadataQualityIssueCode.ThirdPersonCreatorFraming);
        Qwen3VlOutputParseException longPresentTitle =
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                    "A man in a green shirt says something #ExampleGame",
                    "I crossed the room and reached the doorway.",
                    tags,
                    request),
                "The first finite title action must be inspected beyond the sixth word.");
        TestAssert.True(
            longPresentTitle.Message.Contains(
                "present-tense",
                StringComparison.OrdinalIgnoreCase) &&
            longPresentTitle.Message.Contains(
                ClipEditorialMetadataQualityIssueCode.ThirdPersonCreatorFraming.ToString(),
                StringComparison.Ordinal),
            "The real observer-style title must fail both tense and audience-voice authority.");
        Qwen3VlOutputParseException raisedPresentTitle =
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                    "Lyra raises both arms beside the doorway #ExampleGame",
                    "Lyra crossed the room and reached the doorway.",
                    ["ExampleGame", "doorway"],
                    request),
                "A named third-party action using raises must not escape the retrospective gate.");
        TestAssert.True(
            raisedPresentTitle.Message.Contains(
                "present-tense",
                StringComparison.OrdinalIgnoreCase),
            "Python and C# must reject the observed missing simple-present form equally.");
        foreach ((string Title, string Description, string Form) tenseCase in new[]
                 {
                     (
                         "The scene shifts into a foggy area #ExampleGame",
                         "The blue chain tightened around the masked figure.",
                         "shifts"),
                     (
                         "The blue chain tightened around the masked figure #ExampleGame",
                         "A blue chain hangs beside the doorway.",
                         "hangs"),
                     (
                         "Explosions erupt beside the wooden ruin #ExampleGame",
                         "Colored light covered the grassy clearing.",
                         "erupt"),
                     (
                         "Explosions erupted beside the wooden ruin #ExampleGame",
                         "A purple, green, yellow, and red glowing explosion erupts beside the ruin.",
                         "erupts"),
                     (
                         "Purple explosion occurs in the jungle #ExampleGame",
                         "A yellow projectile crossed the jungle near wooden structures.",
                         "occurs"),
                      (
                          "Purple creature hovered in the cavern #ExampleGame",
                          "A purple creature floats above the water beneath yellow energy arcs.",
                          "floats"),
                      (
                          "The sequence reveals a small object #ExampleGame",
                          "The sequence introduced the small object.",
                          "reveals"),
                      (
                          "The cutscene presents a small object #ExampleGame",
                          "In this cutscene, a small object is presented.",
                          "presents"),
                  })
        {
            Qwen3VlOutputParseException present =
                TestAssert.Throws<Qwen3VlOutputParseException>(
                    () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                        tenseCase.Title,
                        tenseCase.Description,
                        ["ExampleGame", "blue chain"],
                        request),
                    $"Trace-proven present form '{tenseCase.Form}' must fail closed.");
            TestAssert.True(
                present.Message.Contains(
                    "non-retrospective",
                    StringComparison.OrdinalIgnoreCase) ||
                present.Message.Contains(
                    "present-tense",
                    StringComparison.OrdinalIgnoreCase),
                $"Trace-proven present form '{tenseCase.Form}' must remain a Python/C# parity target.");
        }
        AssertQualityRejects(
            request,
            "Heard someone beside the doorway #ExampleGame",
            "I heard a man in a green shirt beside the doorway.",
            tags,
            ClipEditorialMetadataQualityIssueCode.ThirdPersonCreatorFraming);
        AssertQualityRejects(
            request,
            "A tense encounter #ExampleGame",
            "I appear nervous as the figure moves closer.",
            tags,
            ClipEditorialMetadataQualityIssueCode.UnsupportedMentalState);
        AssertQualityRejects(
            request,
            "A gesture beside the van #ExampleGame",
            "A person turns toward the camera with a tense expression.",
            tags,
            ClipEditorialMetadataQualityIssueCode.UnsupportedMentalState);
        AssertQualityRejects(
            request,
            "A blue figure pulsed beneath the sign #ExampleGame",
            "A blue figure waited for an event beneath the sign.",
            tags,
            ClipEditorialMetadataQualityIssueCode.UnsupportedMentalState);
        AssertQualityRejects(
            request,
            "Choosing the next skill #ExampleGame",
            "I watch the skill menu open before choosing an upgrade.",
            tags,
            ClipEditorialMetadataQualityIssueCode.GenericOpening);
        AssertQualityRejects(
            request,
            "Choosing a skill in ExampleGame #ExampleGame",
            "I open the skill menu and compare the available upgrades.",
            tags,
            ClipEditorialMetadataQualityIssueCode.RedundantGameIdentity);
        AssertQualityRejects(
            request,
            "Opening the sealed gate #ExampleGame",
            "Opening the sealed gate again.",
            tags,
            ClipEditorialMetadataQualityIssueCode.TitleDescriptionRepetition);
        AssertQualityRejects(
            request,
            "Choosing the next skill #ExampleGame",
            "Automatic words should never become the description.",
            tags,
            ClipEditorialMetadataQualityIssueCode.UnreviewedTranscriptReuse);

        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Chose the next skill #ExampleGame",
            "I opened the skill menu, compared the available upgrades, and confirmed one.",
            tags,
            request);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Opened the sealed gate #ExampleGame",
            "I opened the sealed gate and revealed a flooded courtyard beyond it.",
            tags,
            request);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Ellie crossed the flooded courtyard #ExampleGame",
            "I followed Ellie through the flooded courtyard and reached the stairwell.",
            tags,
            request);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Ellie at the end of the flooded courtyard crossed safely #ExampleGame",
            "I followed Ellie through the flooded courtyard and reached the stairwell.",
            tags,
            request);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "Helped a man escape the room #ExampleGame",
            "I helped a man escape the room before the door closed.",
            tags,
            request);
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "The Dark Hospital Corridor #ExampleGame",
            "I crossed the dark corridor and reached the stairwell.",
            tags,
            request);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Spacecraft enters orbit #ExampleGame",
                "A spacecraft descends through cloud cover. A checkpoint appears.",
                tags,
                request),
            "Neutral present-tense narration must reject even when the model declares retrospective voice.");
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "I opened the sealed gate #ExampleGame",
            "I opened the sealed gate and revealed a flooded courtyard beyond it.",
            tags,
            request);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Hand holds the note #ExampleGame",
                "A hand held the note near the door.",
                tags,
                request),
            "A retrospective declaration requires an actual past-tense finite title action.");
        Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
            "A blue figure pulsed beneath the sign #ExampleGame",
            "A blue figure pulsed beneath the illuminated sign while a doorway opened behind it.",
            tags,
            request);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                "Opened the hidden door then a #ExampleGame",
                "I opened the hidden door.",
                tags,
                request),
            "A bounded title must not end in an incomplete connective or article.");
        return Task.CompletedTask;
    }

    private static Task ActionStrengthMatchesBoundedInflections()
    {
        var draft = new Qwen3VlGroundedMetadataVisualDraft(
            1,
            0,
            10,
            "A visible gameplay area",
            false,
            ["A visible object"],
            [],
            [],
            []);
        (string Audience, string Support)[] supported =
        [
            ("Defeated the enemy", "The enemy defeats the target."),
            ("Defeated the enemy", "The enemy is defeating the target."),
            ("Defeated the enemy", "The enemy collapses."),
            ("Defeated the enemy", "The enemy is collapsing."),
            ("Defeated the enemy", "The health bar empty."),
            ("Defeated the enemy", "The health bar empties."),
            ("Defeated the enemy", "The health bar emptied."),
            ("Defeated the enemy", "The health bar is emptying."),
            ("Defeated the enemy", "The health bar reach zero."),
            ("Defeated the enemy", "The health bar reaches zero."),
            ("Defeated the enemy", "The health bar reached zero."),
            ("Defeated the enemy", "The health bar is reaching zero."),
            ("Entered the room", "The viewpoint enters the room."),
            ("Entered the room", "The viewpoint is entering the room."),
            ("Entered the room", "The viewpoint passes through the doorway."),
            ("Entered the room", "The viewpoint is passing through the doorway."),
            ("Entered the room", "The viewpoint moves into the room."),
            ("Entered the room", "The viewpoint is moving through the doorway."),
            ("Entered the room", "The viewpoint walks into the room."),
            ("Entered the room", "The viewpoint is walking through the doorway."),
            ("Entered the room", "The viewpoint runs into the room."),
            ("Entered the room", "The viewpoint is running through the doorway."),
            ("The device exploded", "The device explodes."),
            ("The device exploded", "The device is exploding."),
            ("The device exploded", "The device detonates."),
            ("The device exploded", "The device is detonating."),
            ("The device exploded", "The device blows up."),
            ("The device exploded", "The device is blowing up."),
            ("The device exploded", "The device bursts apart."),
            ("The device exploded", "The device is bursting apart."),
            ("The figure disappeared", "The figure disappears."),
            ("The figure disappeared", "The figure is disappearing."),
            ("The figure disappeared", "The figure vanishes."),
            ("The figure disappeared", "The figure is vanishing."),
            ("The figure disappeared", "The figure reappears."),
            ("The figure disappeared", "The figure is reappearing."),
            ("The figure disappeared", "The figure rematerializes."),
            ("The figure disappeared", "The figure is rematerializing."),
            ("Completed the objective", "The objective completes."),
            ("Completed the objective", "The objective is completing."),
            ("Completed the objective", "The objective finishes."),
            ("Completed the objective", "The objective is finishing."),
            ("Completed the objective", "The objective clears."),
            ("Completed the objective", "The objective is clearing."),
        ];
        foreach ((string audience, string support) in supported)
        {
            var failures = new List<string>();
            Qwen3VlGroundedMetadataActionStrengthPolicy.Validate(
                audience,
                "A separate supported detail remained visible.",
                draft with { Actions = [support] },
                failures);
            TestAssert.Equal(
                0,
                failures.Count,
                $"Bounded support inflection must be accepted: {support}");
        }

        foreach (string unownedAudienceForm in new[]
                 {
                     "Defeat the enemy",
                     "Enter the room",
                     "The device explodes",
                     "The figure disappears",
                     "Complete the objective",
                 })
        {
            var failures = new List<string>();
            Qwen3VlGroundedMetadataActionStrengthPolicy.Validate(
                unownedAudienceForm,
                "A separate supported detail remained visible.",
                draft with { Actions = ["An unrelated object remained still."] },
                failures);
            TestAssert.Equal(
                0,
                failures.Count,
                "Action-strength validation must not absorb general tense validation.");
        }

        foreach (string unsupportedPastOutcome in new[]
                 {
                     "Defeated the enemy",
                     "Entered the room",
                     "The device exploded",
                     "The figure disappeared",
                     "Completed the objective",
                 })
        {
            var failures = new List<string>();
            Qwen3VlGroundedMetadataActionStrengthPolicy.Validate(
                unsupportedPastOutcome,
                "A separate supported detail remained visible.",
                draft with { Actions = ["An unrelated object remained still."] },
                failures);
            TestAssert.True(
                failures.Any(failure => failure.Contains(
                    ClipEditorialMetadataQualityIssueCode
                        .UnsupportedMentalState.ToString(),
                    StringComparison.Ordinal)),
                "Unsupported retrospective outcomes must retain their typed failure.");
        }

        return Task.CompletedTask;
    }

    private static Task QwenMetadataEnforcesActorAuthority()
    {
        ClipEditorialMetadataRequest request = new(
            CreateContext(),
            ClipEditorialProfile.Default,
            0,
            ClipEditorialGenerationPreference.AiRequired);
        var otherPerson = new Qwen3VlGroundedMetadataVisualDraft(
            1,
            0,
            10,
            "Interior",
            false,
            ["A masked figure", "A blue chain"],
            ["A masked figure screamed while a blue chain tightened."],
            [],
            []);

        Qwen3VlOutputParseException embodiment =
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                    "I screamed as the blue chain tightened #ExampleGame",
                    "The masked figure transformed as the chain wrapped around my neck.",
                    ["transformation", "blue chain"],
                    request,
                    otherPerson,
                    Qwen3VlGroundedMetadataActorAuthority.OtherPerson,
                    Qwen3VlGroundedMetadataCreatorExperienceRelation
                        .CreatorEncountered),
                "Another person's primary body and action must not become creator embodiment.");
        TestAssert.True(
            embodiment.Message.Contains(
                "unsupported creator embodiment",
                StringComparison.Ordinal),
            "Actor-authority rejection must remain typed and actionable.");

        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            "A blue chain tightened during the transformation #ExampleGame",
            "The masked figure transformed as the chain tightened.",
            ["transformation", "blue chain"],
            request,
            otherPerson,
            Qwen3VlGroundedMetadataActorAuthority.OtherPerson,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorEncountered);
        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            "Confronted the masked figure during the transformation #ExampleGame",
            "I confronted the masked figure as the chain tightened.",
            ["confrontation", "transformation"],
            request,
            otherPerson,
            Qwen3VlGroundedMetadataActorAuthority.OtherPerson,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorEncountered);

        var ongoingCombat = otherPerson with
        {
            Environment = "A visible gameplay area",
            SubjectsAndObjects =
                ["A purple creature", "A visible enemy health bar"],
            Actions =
                ["A purple creature attacked while the enemy health bar remained visible."],
        };
        foreach ((string Title, string Description) unsupported in new[]
                 {
                     (
                         "Greater Festering Hives were defeated #ExampleGame",
                         "The enemy health bar remained visible during the attack."),
                     (
                         "Entered the tropical landscape #ExampleGame",
                         "NEW GAME opened a save-slot menu against a tropical backdrop."),
                     (
                         "Passed through the yellow archway #ExampleGame",
                         "A purple creature remained beneath the archway during combat."),
                     (
                         "The creature detonated then reappeared #ExampleGame",
                         "Red particles surrounded the creature while it remained visible."),
                 })
        {
            Qwen3VlOutputParseException actionStrength =
                TestAssert.Throws<Qwen3VlOutputParseException>(
                    () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                        unsupported.Title,
                        unsupported.Description,
                        ["combat"],
                        request,
                        ongoingCombat,
                        Qwen3VlGroundedMetadataActorAuthority.Unknown,
                        Qwen3VlGroundedMetadataCreatorExperienceRelation
                            .Unestablished,
                        requireLiteralActionEntailment: true),
                    "Audience copy must not strengthen an ongoing primary action into a completed outcome or transition.");
            TestAssert.True(
                actionStrength.Message.Contains(
                    ClipEditorialMetadataQualityIssueCode
                        .UnsupportedMentalState.ToString(),
                    StringComparison.Ordinal),
                "Strengthened action rejection must retain the typed quality code.");
        }

        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            "The enemy was defeated after its health bar emptied #ExampleGame",
            "The enemy collapsed after its health bar reached zero.",
            ["combat"],
            request,
            ongoingCombat with
            {
                Actions =
                    ["The enemy collapsed after its health bar reached zero."],
            },
            Qwen3VlGroundedMetadataActorAuthority.Unknown,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
            requireLiteralActionEntailment: true);

        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            "Entered the room through the doorway #ExampleGame",
            "The viewpoint entered the room and walked through the doorway.",
            ["room"],
            request,
            ongoingCombat with
            {
                Environment = "A room",
                SubjectsAndObjects = ["A doorway", "A room"],
                Actions =
                    ["The viewpoint enters the room and walks through a doorway into it."],
            },
            Qwen3VlGroundedMetadataActorAuthority.Unknown,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
            requireLiteralActionEntailment: true);

        var controlled = otherPerson with
        {
            SubjectsAndObjects = ["The controlled avatar", "A workbench"],
            Actions = ["The controlled avatar upgraded equipment."],
        };
        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            "Upgraded my equipment at the workbench #ExampleGame",
            "I upgraded the equipment before leaving the room.",
            ["equipment", "workbench"],
            request,
            controlled,
            Qwen3VlGroundedMetadataActorAuthority.CreatorControlled,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorActed);

        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                "I opened the sealed doorway #ExampleGame",
                "I crossed the threshold after the doorway opened.",
                ["doorway"],
                request,
                otherPerson,
                Qwen3VlGroundedMetadataActorAuthority.Unknown,
                Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished),
            "Unknown actor authority must not authorize first-person embodiment.");

        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            "A person walked along the dirt path #ExampleGame",
            "A person carried a rifle and backpack past grass and rocks.",
            ["The Last of Us", "dirt path"],
            request,
            otherPerson with
            {
                Environment = "Dirt path",
                SubjectsAndObjects = ["A person", "A rifle", "A backpack"],
                Actions =
                    ["A person walked along a dirt path with a rifle and backpack."],
            },
            Qwen3VlGroundedMetadataActorAuthority.Unknown,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
            allowNeutralPersonSubject: true,
            creatorAuthorityUsesAudienceFieldsOnly: true);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                "A person walks along the dirt path #ExampleGame",
                "A person carries a rifle and backpack past grass and rocks.",
                ["The Last of Us", "dirt path"],
                request,
                otherPerson,
                Qwen3VlGroundedMetadataActorAuthority.Unknown,
                Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
                allowNeutralPersonSubject: true,
                creatorAuthorityUsesAudienceFieldsOnly: true),
            "Neutral unknown-person narration remains subject to retrospective grammar.");
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                "The player walked along the dirt path #ExampleGame",
                "The player carried a rifle and backpack past grass and rocks.",
                ["The Last of Us", "dirt path"],
                request,
                otherPerson,
                Qwen3VlGroundedMetadataActorAuthority.Unknown,
                Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
                allowNeutralPersonSubject: true,
                creatorAuthorityUsesAudienceFieldsOnly: true),
            "Neutral-person permission must not permit detached player framing.");

        Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
            "I got attacked beside the doorway #ExampleGame",
            "A masked figure attacked near the doorway before I reached the threshold.",
            ["attack", "doorway"],
            request,
            otherPerson with
            {
                Actions = ["A masked figure attacked near a doorway."],
            },
            Qwen3VlGroundedMetadataActorAuthority.OtherPerson,
            Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorAffected);
        TestAssert.Throws<Qwen3VlOutputParseException>(
            () => Qwen3VlGroundedMetadataAudienceValidator.ValidateMetadata(
                "I screamed beside the doorway #ExampleGame",
                "I screamed before the masked figure moved.",
                ["doorway", "masked figure"],
                request,
                otherPerson,
                Qwen3VlGroundedMetadataActorAuthority.OtherPerson,
                Qwen3VlGroundedMetadataCreatorExperienceRelation.CreatorAffected),
            "CreatorAffected must not transfer another person's direct bodily action.");
        return Task.CompletedTask;
    }

    private static async Task WorkingLabelsCompleteWorkflow()
    {
        ClipEditorialMetadataDraft working =
            await new HeuristicClipEditorialMetadataGenerator().GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);
        TestAssert.True(
            working.IsPublishReady,
            "A structurally valid working label should not be blocked by its provenance state.");
        ClipEditorialMetadataDraft edited = working.WithUserEdits(
            "Choosing my next skill #ExampleGame",
            "I compare the available upgrades and confirm the one I want.",
            ["ExampleGame", "skill menu"]);
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserEditedDraft,
            edited.Readiness,
            "Saving a Studio edit should create an unreviewed user draft.");
        TestAssert.True(edited.IsPublishReady, "User-edited draft readiness.");
        TestAssert.True(
            edited.MarkReviewed().IsPublishReady,
            "Optional review must not change structural workflow eligibility.");
    }

    private static Task ReviewFlaggedGroundedCopyRemainsUsable()
    {
        IReadOnlyList<ClipEditorialMetadataQualityIssue> issues =
            ClipEditorialMetadataReview.BuildIssues(
            ["ThirdPersonCreatorFraming", "ThirdPersonCreatorFraming"]);
        var draft = new ClipEditorialMetadataDraft(
            "A person crossed the ruined street #ExampleGame",
            "A person crossed the ruined street and reached the next building.",
            ["Example Game", "ruined street"],
            ClipEditorialMetadataOrigin.AiAssisted,
            new ClipEditorialMetadataGeneratorIdentity("Reviewable AI", "1.0"),
            attempt: 0,
            warnings:
            [
                new ClipEditorialWarning(
                    ClipEditorialWarningCode.MetadataReviewRequired,
                    "Review this audience copy or reroll it."),
            ],
            qualityIssues: issues);

        TestAssert.Equal(
            ClipEditorialMetadataReadiness.GroundedDraft,
            draft.Readiness,
            "A structurally complete AI draft remains grounded.");
        TestAssert.True(
            draft.IsPublishReady,
            "A copy-review flag must not discard a completed grounded draft.");
        TestAssert.Equal(
            1,
            draft.QualityIssues.Count,
            "Repeated provider diagnostics should map to one public review issue.");
        TestAssert.Equal(
            ClipEditorialMetadataQualityIssueCode.AudienceCopyReview,
            draft.QualityIssues[0].Code,
            "Provider-specific copy rules should share one stable review contract.");
        TestAssert.Equal(
            "ThirdPersonCreatorFraming",
            draft.QualityIssues[0].SourceRuleCode,
            "Provider rule identity should remain typed provenance, not message text.");
        return Task.CompletedTask;
    }

    private static void AssertQualityRejects(
        ClipEditorialMetadataRequest request,
        string title,
        string description,
        IReadOnlyList<string> tags,
        ClipEditorialMetadataQualityIssueCode issue)
    {
        Qwen3VlOutputParseException exception =
            TestAssert.Throws<Qwen3VlOutputParseException>(
                () => Qwen3VlGroundedMetadataGenerator.ValidateMetadata(
                    title,
                    description,
                    tags,
                    request),
                $"Qwen metadata must reject {issue}.");
        TestAssert.True(
            exception.Message.Contains(issue.ToString(), StringComparison.Ordinal),
            $"The rejection must identify {issue}.");
    }

    private static async Task<(
        GenerationOutputAsset Asset,
        ClipEditorialMetadataDraft Metadata)> CreateAssetAsync(
            bool hasAudio = false)
    {
        ClipEditorialContext context = CreateContext();
        var generator = new HeuristicClipEditorialMetadataGenerator();
        ClipEditorialMetadataDraft metadata =
            await generator.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    context,
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);
        var asset = new GenerationOutputAsset(
            context.CandidateId,
            1,
            TestMediaFactory.Create(
                context.SourceFullPath,
                context.SourceDuration,
                hasAudio: hasAudio),
            outputFullPath: null,
            context.SourceStart,
            context.SourceEnd,
            context.DeterministicScore,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            context.DeterministicReason,
            editorialContext: context,
            editorialMetadata: metadata);
        return (asset.WithCurrentCutEditorialMetadata(context, metadata), metadata);
    }

    private static async Task<GenerationOutputAsset>
        CreateGroundedAiAssetAsync()
    {
        (GenerationOutputAsset asset, _) = await CreateAssetAsync(
            hasAudio: true);
        ClipEditorialContext context = asset.EditorialContext!;
        var identity = new ClipEditorialMetadataGeneratorIdentity(
            "Grounded AI fixture",
            "1.0.0");
        var audit = new GameKnowledgeInfluenceAudit(
            context.EditorialBrief.Fingerprint,
            ClipEditorialRevisionKind.InitialDraft,
            resolvedBrief: context.EditorialBrief);
        var metadata = new ClipEditorialMetadataDraft(
            "The checkpoint hid one more route #ExampleGame",
            "I followed the checkpoint notice until another route opened.",
            [context.GameContext.AudienceGameName],
            ClipEditorialMetadataOrigin.AiAssisted,
            identity,
            attempt: 0,
            context.Evidence,
            aiProvenance: CreateTestAiProvenance(identity),
            groundingAudit: audit);
        return asset.WithCurrentCutEditorialMetadata(context, metadata);
    }

    private static GenerationCandidateCaptionTrack CreateCaptionTrack(
        GenerationOutputAsset asset,
        string text)
    {
        const string neighborhoodId = "caption-metadata-freshness";
        TimeSpan relativeStart = TimeSpan.FromSeconds(1);
        TimeSpan relativeEnd = TimeSpan.FromSeconds(4);
        var segment = new AudioTranscriptionSegment(
            "caption-metadata-freshness-segment",
            neighborhoodId,
            text,
            relativeStart,
            relativeEnd,
            asset.SourceStart + relativeStart,
            asset.SourceStart + relativeEnd);
        var selection = new GenerationCaptionSourceSelection(
            asset.SourceFullPath,
            asset.SourceMedia.AudioStreams.Single().Index,
            CaptionAudioContentRole.CreatorCommentary,
            GenerationCaptionLanguagePolicy.English);
        return GenerationCandidateCaptionTrack.RestoreStudioHandoff(
            asset.Id,
            neighborhoodId,
            selection,
            asset.Appearance.CaptionStyle,
            asset.SourceStart,
            asset.Duration,
            asset.SourceDuration,
            [segment],
            isUserEdited: true,
            GenerationCaptionSuppressionReason.None);
    }

    private static ClipEditorialContext CreateContext(
        IEnumerable<ClipEditorialTranscriptContext>? transcripts = null,
        string candidateId = "candidate-01",
        IEnumerable<ClipEditorialEvidenceReference>? evidence = null) =>
        new(
            candidateId,
            Path.GetFullPath("ExampleGame/Vertical/source.mkv"),
            "ExampleGame",
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(42),
            TimeSpan.FromMinutes(90),
            82.5,
            "Gameplay onset and audio novelty aligned.",
            transcripts ??
            [
                new ClipEditorialTranscriptContext(
                    1,
                    new AudioContentRoleAssignment(
                        AudioContentRole.CreatorSpeech,
                        AudioContentRoleSource.UserConfirmed),
                    "Chat, I cannot believe that actually worked."),
            ],
            evidence ??
            [
                new ClipEditorialEvidenceReference(
                    "gameplay-onset",
                    ClipEditorialEvidenceKind.DeterministicMoment,
                    "Gameplay onset was locally prominent."),
                new ClipEditorialEvidenceReference(
                    "audio-novelty",
                    ClipEditorialEvidenceKind.DeterministicMoment,
                    "Audio novelty aligned with the gameplay change."),
            ],
            gameContext: new ClipEditorialGameContext(
                "ExampleGame",
                "#ExampleGame",
                contextNotes: null,
                ClipEditorialGameContextSource.UserConfirmed));

    private static ClipEditorialContext CreateVisualHeuristicContext(
        string candidateId,
        string evidenceId,
        string description) =>
        CreateContext(
            transcripts: [],
            candidateId: candidateId,
            evidence:
            [
                new ClipEditorialEvidenceReference(
                    evidenceId,
                    ClipEditorialEvidenceKind.VisualObservation,
                description),
            ]);

    private static ClipEditorialAiProvenance CreateTestAiProvenance(
        ClipEditorialMetadataGeneratorIdentity identity) =>
        new(
            identity.Name,
            identity.Version,
            "test-runtime-1.0",
            "replayfoundry/test-editorial-model",
            "test-revision",
            new string('a', 64),
            "test-editorial-prompt",
            "1.0",
            new string('b', 64),
            TimeSpan.Zero,
            peakAllocatedGpuBytes: null);

    private static ClipEditorialMetadataDraft CreateTestAiDraft(
        ClipEditorialMetadataRequest request,
        ClipEditorialMetadataGeneratorIdentity identity,
        string title) =>
        new(
            title,
            "I completed one concrete visible action in the selected clip.",
            [request.Context.GameContext.GameName],
            ClipEditorialMetadataOrigin.AiAssisted,
            identity,
            request.Attempt,
            request.Context.Evidence,
            aiProvenance: CreateTestAiProvenance(identity));

    private sealed class HiddenMomentRetryMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Hidden Moment retry AI", "1.0.0");

        public bool IsAvailable => true;

        public int SingleCalls { get; private set; }

        public List<ClipEditorialMetadataRequest[]> BatchRequests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            SingleCalls++;
            throw new InvalidOperationException(
                "Accepted Hidden Moments must use the shared batch editorial policy.");
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataRequest[] snapshot = requests.ToArray();
            BatchRequests.Add(snapshot);
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = snapshot
                .Select(request => CreateTestAiDraft(
                    request,
                    Identity,
                    BatchRequests.Count switch
                    {
                        1 => "A New Piece of the Story Emerged " +
                            request.Context.GameContext.AudienceGameHashtag,
                        2 => "The Existing Route Changed the Search " +
                            request.Context.GameContext.AudienceGameHashtag,
                        _ => "The Locked Route Changed the Next Move " +
                            request.Context.GameContext.AudienceGameHashtag,
                    }))
                .ToArray();
            return Task.FromResult(drafts);
        }
    }

    private sealed class RecordingBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Recording AI", "1.0.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public int SingleCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            SingleCalls++;
            return Task.FromResult(CreateDraft(request));
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            BatchCalls++;
            IReadOnlyList<ClipEditorialMetadataDraft> result = requests
                .Select(CreateDraft)
                .ToArray();
            return Task.FromResult(result);
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request) =>
            new(
                $"{request.Context.CandidateId} {request.Context.GameContext.GameHashtag}",
                "Grounded AI metadata fixture.",
                [request.Context.GameContext.GameHashtag[1..]],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity));
    }

    private sealed class NoveltyRetryMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Novelty retry AI", "1.0.0");

        public bool IsAvailable => true;

        public int SingleCalls { get; private set; }

        public List<ClipEditorialMetadataRequest[]> BatchRequests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            SingleCalls++;
            throw new InvalidOperationException(
                "The novelty retry fixture must stay on the batch path.");
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataRequest[] snapshot = requests.ToArray();
            BatchRequests.Add(snapshot);
            int call = BatchRequests.Count;
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = snapshot
                .Select(request => CreateTestAiDraft(
                    request,
                    Identity,
                    TitleFor(call, request.Context.CandidateId)))
                .ToArray();
            return Task.FromResult(drafts);
        }

        private static string TitleFor(int call, string candidateId) =>
            (call, candidateId) switch
            {
                (1, "candidate-01") =>
                    "Door Fight Turned Around #ExampleGame",
                (1, "candidate-02") =>
                    "Door Fight Changed Course #ExampleGame",
                (1, "candidate-03") =>
                    "A New Piece of the Story Emerged #ExampleGame",
                (2, "candidate-02") =>
                    "The Reactor Opened a New Route #ExampleGame",
                (2, "candidate-03") =>
                    "The Next Beat Landed #ExampleGame",
                (3, "candidate-03") =>
                    "A Hidden Door Finally Opened #ExampleGame",
                _ => throw new InvalidOperationException(
                    "The novelty retry fixture received an unexpected batch."),
            };
    }

    private sealed class CurrentRegressionMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Current regression recovery AI", "1.0.0");

        public bool IsAvailable => true;

        public List<ClipEditorialMetadataRequest[]> BatchRequests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The current-regression fixture must stay on the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataRequest[] snapshot = requests.ToArray();
            BatchRequests.Add(snapshot);
            bool corrected = BatchRequests.Count > 1;
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = snapshot
                .Select(request => CreateDraft(request, corrected))
                .ToArray();
            return Task.FromResult(drafts);
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request,
            bool corrected)
        {
            int ordinal = int.Parse(
                request.Context.CandidateId.AsSpan(
                    request.Context.CandidateId.Length - 2),
                System.Globalization.CultureInfo.InvariantCulture);
            (string Title, string Description) package = corrected
                ? CorrectedPackage(ordinal)
                : RejectedPackage(ordinal);
            ClipEditorialMetadataQualityIssue[] qualityIssues =
                !corrected && ordinal == 3
                    ?
                    [
                        new ClipEditorialMetadataQualityIssue(
                            ClipEditorialMetadataQualityIssueCode
                                .AudienceCopyReview,
                            "The provider marked unstable readable text for review."),
                    ]
                    : [];
            return new ClipEditorialMetadataDraft(
                package.Title,
                package.Description,
                [request.Context.GameContext.AudienceGameName],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity),
                qualityIssues: qualityIssues,
                groundingAudit: new GameKnowledgeInfluenceAudit(
                    request.Context.EditorialBrief.Fingerprint,
                    request.RevisionKind,
                    resolvedBrief: request.Context.EditorialBrief));
        }

        private static (string Title, string Description) RejectedPackage(
            int ordinal) => ordinal switch
            {
                1 => (
                    "Framed pictures and plant stay in room #ExampleGame",
                    "Framed pictures and a potted plant remain in the room."),
                2 => (
                    "Woman stands at FBC checkpoint #ExampleGame",
                    "A woman stands at a security checkpoint as FBC rules display on screen."),
                3 => (
                    "FBC R4 deadline looms #ExampleGame",
                    "The R4 reporting deadline looms while a digital notice flashes."),
                4 => (
                    "I opened emergency storage door, accessed collectibles menu, viewed materialstab #ExampleGame",
                    "I opened emergency storage door, accessed collectibles menu, viewed materials tab."),
                5 => (
                    "COURTNEY HOPE appears on screen #ExampleGame",
                    "A black screen displays COURTNEY HOPE before a red hallway appeared."),
                _ => throw new InvalidOperationException(
                    "The current-regression fixture received an unknown candidate."),
            };

        private static (string Title, string Description) CorrectedPackage(
            int ordinal) => ordinal switch
            {
                1 => (
                    "The office decor survived another FBC disaster #ExampleGame",
                    "I doubled back through the red hallway without disturbing the framed photos or potted plant."),
                2 => (
                    "Rubber duckies violated FBC policy #ExampleGame",
                    "The restricted-items notice grouped them with ketchup bottles at the security checkpoint."),
                3 => (
                    "The shark experiment raised one obvious question #ExampleGame",
                    "The R4 notice interrupted my search before the hallway opened into the next office."),
                4 => (
                    "The clearance lock kept those collectibles secret #ExampleGame",
                    "I opened emergency storage, but the materials tab still hid entries above my access level."),
                5 => (
                    "That intro gave me immediate X-Files vibes #ExampleGame",
                    "The red-lit hallway and stark opening credits made the conspiracy tone land before gameplay began."),
                _ => throw new InvalidOperationException(
                    "The current-regression fixture received an unknown candidate."),
            };
    }

    private sealed class DescriptionOnlyRetryMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Description retry AI", "1.0.0");

        public bool IsAvailable => true;

        public List<ClipEditorialMetadataRequest[]> BatchRequests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The description retry fixture must stay on the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataRequest[] snapshot = requests.ToArray();
            BatchRequests.Add(snapshot);
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = snapshot
                .Select(request => new ClipEditorialMetadataDraft(
                    "The Clearance Lock Kept Those Collectibles Secret #ExampleGame",
                    BatchRequests.Count == 1
                        ? "The clearance lock kept those collectibles secret."
                        : "I opened emergency storage, but the materials tab still hid entries above my access level.",
                    [request.Context.GameContext.GameName],
                    ClipEditorialMetadataOrigin.AiAssisted,
                    Identity,
                    request.Attempt,
                    request.Context.Evidence,
                    aiProvenance: CreateTestAiProvenance(Identity)))
                .ToArray();
            return Task.FromResult(drafts);
        }
    }

    private sealed class ExhaustedReviewableBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Reviewable exhaustion AI", "1.0.0");

        public bool IsAvailable => true;

        public List<ClipEditorialMetadataRequest[]> BatchRequests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The reviewable exhaustion fixture must stay on the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataRequest[] snapshot = requests.ToArray();
            BatchRequests.Add(snapshot);
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = snapshot
                .Select(CreateDraft)
                .ToArray();
            return Task.FromResult(drafts);
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request)
        {
            (string Title, string Description) package = BatchRequests.Count switch
            {
                1 => (
                    "A Person Appeared on Screen #ExampleGame",
                    "A person appeared on screen beside a closed door."),
                2 => (
                    "The Hallway Stayed Empty #ExampleGame",
                    "The hallway stayed empty."),
                3 => (
                    "The Locked Door Interrupted the Search #ExampleGame",
                    "I doubled back through the red hallway before trying another route."),
                _ => throw new InvalidOperationException(
                    "Reviewable retries exceeded the bounded fixture."),
            };
            IReadOnlyList<ClipEditorialMetadataQualityIssue> issues =
                BatchRequests.Count == 3
                    ? ClipEditorialMetadataReview.BuildIssues(
                        ["EditorialFrameDrift"])
                    : [];
            return new ClipEditorialMetadataDraft(
                package.Title,
                package.Description,
                [request.Context.GameContext.GameName],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity),
                qualityIssues: issues);
        }
    }

    private sealed class RepeatedReviewBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        private readonly string _title;
        private readonly string _description;
        private readonly string? _providerRuleCode;

        public RepeatedReviewBatchMetadataGenerator(
            string title,
            string description,
            string? providerRuleCode = null)
        {
            _title = title;
            _description = description;
            _providerRuleCode = providerRuleCode;
        }

        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Repeated review AI", "1.0.0");

        public bool IsAvailable => true;

        public List<ClipEditorialMetadataRequest[]> BatchRequests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The repeated-review fixture must stay on the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataRequest[] snapshot = requests.ToArray();
            BatchRequests.Add(snapshot);
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = snapshot
                .Select(CreateDraft)
                .ToArray();
            return Task.FromResult(drafts);
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request)
        {
            string description = _description;
            if (!string.IsNullOrWhiteSpace(
                    request.Profile.ReusableDescriptionSignature))
            {
                description += Environment.NewLine + Environment.NewLine +
                    request.Profile.ReusableDescriptionSignature;
            }
            IReadOnlyList<ClipEditorialMetadataQualityIssue> issues =
                _providerRuleCode is null
                    ? []
                    : ClipEditorialMetadataReview.BuildIssues(
                        [_providerRuleCode]);
            return new ClipEditorialMetadataDraft(
                _title,
                description,
                [request.Context.GameContext.GameName],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity),
                qualityIssues: issues);
        }
    }

    private sealed class RecoveredHallwayRetryMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Recovered hallway retry AI", "1.0.0");

        public bool IsAvailable => true;

        public List<ClipEditorialMetadataRequest[]> BatchRequests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The recovered hallway fixture must stay on the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipEditorialMetadataRequest[] snapshot = requests.ToArray();
            BatchRequests.Add(snapshot);
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = snapshot
                .Select(CreateDraft)
                .ToArray();
            return Task.FromResult(drafts);
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request)
        {
            (string Title, string Description) package = BatchRequests.Count switch
            {
                1 => (
                    "I opened menu, moved through door, walked down hallway with red light #ExampleGame",
                    "I opened menu, moved through door, walked down red-lit hallway."),
                2 => (
                    "I opened menu, entered room with red-lit hallway ahead #ExampleGame",
                    "I opened menu, viewed collectibles, entered room with red-lit hallway ahead."),
                3 => (
                    "White word flashes on black background, chilling opener for Control's l #ExampleGame",
                    "A stark white word flashes on black — a chilling opener for Control’s conspiracy-laden world."),
                _ => throw new InvalidOperationException(
                    "Recovered hallway retries exceeded the bounded fixture."),
            };
            IReadOnlyList<ClipEditorialMetadataQualityIssue> issues =
                BatchRequests.Count switch
                {
                    2 => ClipEditorialMetadataReview.BuildIssues(
                        ["EditorialFrameDrift"]),
                    3 => ClipEditorialMetadataReview.BuildIssues(
                        ["IncompleteTitle"]),
                    _ => [],
                };
            return new ClipEditorialMetadataDraft(
                package.Title,
                package.Description,
                [request.Context.GameContext.GameName],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity),
                qualityIssues: issues);
        }
    }

    private sealed class ReviewThenFailBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Review then fail AI", "1.0.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The corrective failure fixture must stay on the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            if (BatchCalls > 1)
            {
                throw new InvalidDataException(
                    "The corrective AI rewrite failed after the initial draft.");
            }

            IReadOnlyList<ClipEditorialMetadataDraft> drafts = requests
                .Select(request => new ClipEditorialMetadataDraft(
                    "The Red Hallway Led to a Locked Door #ExampleGame",
                    "I followed the red hallway until a locked door stopped the route.",
                    [request.Context.GameContext.GameName],
                    ClipEditorialMetadataOrigin.AiAssisted,
                    Identity,
                    request.Attempt,
                    request.Context.Evidence,
                    aiProvenance: CreateTestAiProvenance(Identity),
                    qualityIssues: ClipEditorialMetadataReview.BuildIssues(
                        ["UnstableReadableTextReuse"])))
                .ToArray();
            return Task.FromResult(drafts);
        }
    }

    private sealed class RiskRankedReviewBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Risk-ranked review AI", "1.0.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The risk-ranked fixture must stay on the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            (string Title, string Description, string[] Rules) package =
                BatchCalls switch
                {
                    1 => (
                        "The Locked Door Interrupted the Search #ExampleGame",
                        "I doubled back through the red hallway and tried another route.",
                        ["EditorialFrameDrift", "ThirdPersonCreatorFraming"]),
                    2 => (
                        "The Red Hallway Forced Another Route #ExampleGame",
                        "The locked door sent me back through the red hallway.",
                        ["UnstableReadableTextReuse"]),
                    3 => (
                        "The Alarm Changed the Plan #ExampleGame",
                        "I left the locked door when the alarm changed the route.",
                        ["OutputLanguage"]),
                    _ => throw new InvalidOperationException(
                        "Risk-ranked retries exceeded the bounded fixture."),
                };
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = requests
                .Select(request => new ClipEditorialMetadataDraft(
                    package.Title,
                    package.Description,
                    [request.Context.GameContext.GameName],
                    ClipEditorialMetadataOrigin.AiAssisted,
                    Identity,
                    request.Attempt,
                    request.Context.Evidence,
                    aiProvenance: CreateTestAiProvenance(Identity),
                    qualityIssues: ClipEditorialMetadataReview.BuildIssues(
                        package.Rules)))
                .ToArray();
            return Task.FromResult(drafts);
        }
    }

    private enum InvalidAiDraftKind
    {
        WrongOrigin,
        MissingProvenance,
        UserApprovedReadiness,
        MismatchedProviderIdentity,
    }

    private sealed class InvalidAiDraftMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        private readonly InvalidAiDraftKind _invalidKind;

        public InvalidAiDraftMetadataGenerator(InvalidAiDraftKind invalidKind)
        {
            _invalidKind = invalidKind;
        }

        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Invalid postcondition AI", "1.0.0");

        public bool IsAvailable => true;

        public int Calls { get; private set; }

        public int BatchCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(CreateDraft(request));
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            IReadOnlyList<ClipEditorialMetadataDraft> drafts = requests
                .Select(CreateDraft)
                .ToArray();
            return Task.FromResult(drafts);
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request)
        {
            ClipEditorialMetadataGeneratorIdentity draftIdentity =
                _invalidKind ==
                    InvalidAiDraftKind.MismatchedProviderIdentity
                    ? new ClipEditorialMetadataGeneratorIdentity(
                        "Different provider",
                        "1.0.0")
                    : Identity;
            var draft = new ClipEditorialMetadataDraft(
                "A Concrete Provider Result #ExampleGame",
                "I completed one concrete visible action in the selected clip.",
                [request.Context.GameContext.GameName],
                _invalidKind == InvalidAiDraftKind.WrongOrigin
                    ? ClipEditorialMetadataOrigin.Heuristic
                    : ClipEditorialMetadataOrigin.AiAssisted,
                draftIdentity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance:
                    _invalidKind == InvalidAiDraftKind.MissingProvenance
                        ? null
                        : CreateTestAiProvenance(Identity),
                readiness:
                    _invalidKind == InvalidAiDraftKind.UserApprovedReadiness
                        ? ClipEditorialMetadataReadiness.UserApproved
                        : ClipEditorialMetadataReadiness.GroundedDraft);
            return draft;
        }
    }

    private sealed class ModelFreeGroundedExecutorFixture : IDisposable
    {
        private readonly string _root;
        private readonly VisualSemanticInputManifest _review;

        public ModelFreeGroundedExecutorFixture(
            bool corruptGroundedPrompt = false)
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundry.PreparationTests",
                Guid.NewGuid().ToString("N"));
            string hostDirectory = Path.Combine(_root, "host");
            string modelDirectory = Path.Combine(_root, "model");
            string ffmpegDirectory = Path.Combine(_root, "ffmpeg");
            Directory.CreateDirectory(hostDirectory);
            Directory.CreateDirectory(modelDirectory);
            Directory.CreateDirectory(ffmpegDirectory);

            string hostPath = Path.Combine(hostDirectory, "host.py");
            File.WriteAllText(hostPath, "# model-free test host");
            string promptFileName =
                "replayfoundry-editorial-metadata-prompt-" +
                Qwen3VlGroundedMetadataGenerator.PromptVersion +
                ".txt";
            File.Copy(
                RepositoryLayout.VisualSemanticHostPath(promptFileName),
                Path.Combine(hostDirectory, promptFileName));
            if (corruptGroundedPrompt)
            {
                File.AppendAllText(
                    Path.Combine(hostDirectory, promptFileName),
                    " stale-runtime");
            }
            string lockPath = Path.Combine(
                _root,
                "qualification-lock.json");
            File.WriteAllText(lockPath, "{}");

            string modelPath = Path.Combine(modelDirectory, "weights.bin");
            File.WriteAllBytes(modelPath, [1, 2, 3, 4]);
            var modelInfo = new FileInfo(modelPath);
            var modelFile = new VisualSemanticModelFile(
                "weights.bin",
                ModelArtifactManifest.ComputeSha256(modelPath),
                modelInfo.Length);
            const string repository = "Qwen/Qwen3-VL-4B-Instruct";
            const string revision = "model-free-test";
            const string license = "Apache-2.0";
            const string source =
                "https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct";
            var model = new VisualSemanticModelManifest(
                VisualSemanticModelManifest.SupportedSchemaVersion,
                repository,
                revision,
                modelDirectory,
                license,
                source,
                [modelFile],
                VisualSemanticModelManifest.ComputeManifestSha256(
                    VisualSemanticModelManifest.SupportedSchemaVersion,
                    repository,
                    revision,
                    license,
                    source,
                    [modelFile]));
            const string qualificationPromptText =
                "Model-free qualified editorial prompt fixture.";
            string qualificationPromptHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        qualificationPromptText)));
            var prompt = new VisualSemanticPromptManifest(
                VisualSemanticPromptManifest.QualifiedEditorialSchemaVersion,
                VisualSemanticPromptManifest.QualifiedEditorialName,
                VisualSemanticPromptManifest.QualifiedEditorialVersion,
                qualificationPromptText,
                qualificationPromptHash,
                DateTimeOffset.UtcNow);
            string processPath = Environment.ProcessPath ??
                throw new InvalidOperationException(
                    "The model-free test process path is unavailable.");
            var host = new Qwen3VlBatchHostSettings(
                processPath,
                hostPath,
                modelDirectory,
                Qwen3VlBatchHostSettings.SupportedVideoBackend,
                ffmpegDirectory,
                TimeSpan.FromSeconds(5));
            Runtime = new Qwen3VlQualifiedEditorialRuntime(
                new UnusedVisualSemanticEditorialProvider(),
                prompt,
                model,
                VisualSemanticVideoInputPolicy.CreateV05A1(),
                host,
                lockPath,
                new string('d', 64));

            ClipEditorialContext context = CreateContext();
            string reviewPath = Path.Combine(_root, "review.mp4");
            File.WriteAllBytes(reviewPath, [5, 6, 7, 8]);
            var reviewInfo = new FileInfo(reviewPath);
            _review = new VisualSemanticInputManifest(
                reviewPath,
                ModelArtifactManifest.ComputeSha256(reviewPath),
                reviewInfo.Length,
                context.Duration,
                new DateTimeOffset(
                    DateTime.SpecifyKind(
                        reviewInfo.LastWriteTimeUtc,
                        DateTimeKind.Utc)));
        }

        public Qwen3VlQualifiedEditorialRuntime Runtime { get; }

        public ClipEditorialMetadataRequest CreateRequest(
            string candidateId = "candidate-01")
        {
            ClipEditorialContext context = CreateContext(
                candidateId: candidateId);
            return new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired,
                sourceMedia: TestMediaFactory.Create(
                    context.SourceFullPath,
                    context.SourceDuration),
                reviewVideo: _review);
        }

        public ClipEditorialMetadataRequest CreateRequest(
            ClipEditorialContext context) =>
            new(
                context,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired,
                sourceMedia: TestMediaFactory.Create(
                    context.SourceFullPath,
                    context.SourceDuration),
                reviewVideo: _review);

        public ClipEditorialMetadataRequest CreateRequestWithVisualText()
        {
            TimeSpan clipStart = TimeSpan.FromMinutes(16);
            ClipEditorialContext context = CreateContext(
                transcripts:
                [
                    new ClipEditorialTranscriptContext(
                        1,
                        new AudioContentRoleAssignment(
                            AudioContentRole.CreatorSpeech,
                            AudioContentRoleSource.UserConfirmed),
                        "I found the route. That changed everything.",
                        ClipEditorialTranscriptAuthority.UserCorrected,
                        [
                            new ClipEditorialTranscriptSpan(
                                clipStart + TimeSpan.FromSeconds(1),
                                clipStart + TimeSpan.FromSeconds(2),
                                "I found the route."),
                            new ClipEditorialTranscriptSpan(
                                clipStart + TimeSpan.FromSeconds(3),
                                clipStart + TimeSpan.FromSeconds(4),
                                "That changed everything."),
                        ]),
                ]);
            TimeSpan[] timestamps =
            [
                context.SourceStart + TimeSpan.FromSeconds(1),
                context.SourceStart + TimeSpan.FromSeconds(2),
            ];
            context = context.WithVisualText(new ClipVisualTextContext(
                context.CandidateId,
                context.SourceFullPath,
                NormalizedRectangle.FullFrame,
                frames: [],
                anchors:
                [
                    new VisualTextAnchor(
                        "objective updated",
                        "Objective Updated",
                        VisualTextAnchorAuthority.RepeatedAcrossFrames,
                        timestamps),
                    new VisualTextAnchor(
                        "mission updated",
                        "MISSION:UPDATED",
                        VisualTextAnchorAuthority.RepeatedAcrossFrames,
                        timestamps),
                ]));
            return new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired,
                sourceMedia: TestMediaFactory.Create(
                    context.SourceFullPath,
                    context.SourceDuration),
                reviewVideo: _review);
        }

        public ClipEditorialMetadataRequest CreateUnconfirmedPathRequest()
        {
            ClipEditorialContext confirmed = CreateContext();
            var context = new ClipEditorialContext(
                confirmed.CandidateId,
                confirmed.SourceFullPath,
                confirmed.SourceLabel,
                confirmed.SourceStart,
                confirmed.SourceEnd,
                confirmed.SourceDuration,
                confirmed.DeterministicScore,
                confirmed.DeterministicReason,
                confirmed.Transcripts,
                confirmed.Evidence,
                new ClipEditorialGameContext(
                    "Recording Video Files",
                    "#RecordingVideoFiles",
                    "path-derived note",
                    ClipEditorialGameContextSource.SourcePathHint,
                    useOpenGameKnowledge: true));
            return new ClipEditorialMetadataRequest(
                context,
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiRequired,
                sourceMedia: TestMediaFactory.Create(
                    context.SourceFullPath,
                    context.SourceDuration),
                reviewVideo: _review);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class FailureArtifactProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _run;

        public FailureArtifactProcessRunner(
            Func<ProcessRunRequest, ProcessRunResult> run)
        {
            _run = run;
        }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_run(request));
        }
    }

    private sealed class UnusedVisualSemanticEditorialProvider :
        IVisualSemanticEditorialProvider
    {
        public InferenceProviderIdentity Identity { get; } = new(
            "Unused model-free provider",
            "1.0",
            "1.0");

        public Task<VisualSemanticEditorialBatchResult> ObserveAsync(
            VisualSemanticBatchRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The grounded metadata executor test must not call the observation provider.");
    }

    private sealed class VariantPackageMetadataGenerator :
        IClipEditorialMetadataGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Qwen package fixture", "1.0.0");

        public bool IsAvailable => true;

        public int Calls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            string intent = request.VariantIntent.ToString();
            return Task.FromResult(new ClipEditorialMetadataDraft(
                $"{intent} grounded title {request.Context.GameContext.GameHashtag}",
                $"I completed the {intent} grounded event in this bounded clip.",
                [request.Context.GameContext.GameName, intent],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity)));
        }
    }

    private sealed class RecordingRequestMetadataGenerator :
        IClipEditorialMetadataGenerationService
    {
        private readonly HeuristicClipEditorialMetadataGenerator _inner = new();

        private static readonly ClipEditorialMetadataGeneratorIdentity Identity =
            new("Recording request AI", "1.0.0");

        public bool IsAiAvailable => true;

        public ClipEditorialMetadataRequest? LastRequest { get; private set; }

        public async Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            ClipEditorialMetadataDraft draft = await _inner.GenerateAsync(
                request,
                cancellationToken);
            if (request.Preference is
                ClipEditorialGenerationPreference.HeuristicOnly)
            {
                return draft;
            }

            return new ClipEditorialMetadataDraft(
                draft.Title,
                draft.Description,
                draft.Tags,
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                draft.Evidence,
                draft.Warnings,
                CreateTestAiProvenance(Identity),
                ClipEditorialMetadataReadiness.GroundedDraft,
                draft.QualityIssues,
                draft.PriorAcceptedTitles,
                draft.GroundingAudit);
        }
    }

    private sealed class RecordingGameKnowledgeContextService :
        IGenerationGameKnowledgeService
    {
        private bool _removed;

        public Func<ClipEditorialContext, ClipEditorialContext>? Enrichment { get; init; }

        public int RefreshCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public Task<ClipEditorialContext> EnrichAsync(
            ClipEditorialContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Enrichment?.Invoke(context) ?? context);
        }

        public Task<ClipEditorialContext> RefreshAsync(
            ClipEditorialContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCalls++;
            return Task.FromResult(context);
        }

        public GameKnowledgeContextReceipt Inspect(
            ClipEditorialContext context) =>
            new(
                "Example Game",
                "Q123",
                _removed
                    ? GameKnowledgeContextFreshness.NotCached
                    : GameKnowledgeContextFreshness.Fresh,
                _removed
                    ? null
                    : new DateTimeOffset(
                        2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
                _removed
                    ? null
                    : new DateTimeOffset(
                        2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
                canRefresh: true,
                canRemove: !_removed,
                sources: _removed
                    ? []
                    :
                    [
                        new GameKnowledgeSourceReceipt(
                            "Example Game",
                            GameKnowledgeSourceKind.Wikidata,
                            GameKnowledgeSourceRole.StructuredIdentity,
                            new Uri("https://www.wikidata.org/wiki/Q123"),
                            "fixture-9",
                            new DateTimeOffset(
                                2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
                            "CC0-1.0",
                            new Uri(
                                "https://creativecommons.org/publicdomain/zero/1.0/"),
                            "Wikidata contributors"),
                    ],
                claims: _removed
                    ? []
                    :
                    [
                        new GameKnowledgeClaimReceipt(
                            "developer",
                            "Developer",
                            "Example Studio",
                            "Wikidata"),
                    ],
                components: _removed
                    ? []
                    :
                    [
                        new GameKnowledgeComponentReceipt(
                            GameKnowledgeComponentKind.WikidataClaims,
                            GameKnowledgeComponentCompleteness.Complete,
                            new DateTimeOffset(
                                2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
                            RetryAfterUtc: null,
                            RevisionId: "fixture-9",
                            LicenseIdentifier: "CC0-1.0",
                            Attribution: "Wikidata contributors"),
                    ]);

        public ClipEditorialContext RemoveCachedContext(
            ClipEditorialContext context)
        {
            RemoveCalls++;
            _removed = true;
            return context.WithGameKnowledge(gameKnowledge: null);
        }

        public void RemoveCachedContext(ConfirmedGameIdentity identity)
        {
            RemoveCalls++;
            _removed = true;
        }
    }

    private sealed class RecordingHiddenVisualText : ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText.IGenerationVisualTextAnalysisService
    {
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public Task<ClipEditorialContext> EnrichAsync(
            ReplayFoundry.Desktop.Features.Generate.Editorial.VisualText.GenerationVisualTextAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(request.Context.WithVisualText(new(request.Context.CandidateId,
                request.Context.SourceFullPath, request.Context.GameplayRegion!, [], [])));
        }
    }

    private sealed class DeferredMetadataGenerator :
        IClipEditorialMetadataGenerationService
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _complete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAiAvailable => false;

        public Task Started => _started.Task;

        public void Complete() => _complete.TrySetResult();

        public async Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return await new HeuristicClipEditorialMetadataGenerator()
                .GenerateAsync(request, cancellationToken);
        }
    }

    private sealed class UnusedProjectRenderer :
        IStudioProjectRenderingService
    {
        public Task<StudioProjectRenderResult> FinalizeAsync(
            GenerationOutputProject draft,
            IProgress<StudioProjectRenderProgress> progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The editorial metadata test must not render media.");

        public void AcceptCompletedRender(StudioProjectRenderResult result) =>
            throw new InvalidOperationException(
                "The editorial metadata test must not accept rendered media.");

        public void DiscardCompletedRender(StudioProjectRenderResult result) =>
            throw new InvalidOperationException(
                "The editorial metadata test must not discard rendered media.");
    }

    private sealed class FailingBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Failing AI", "1.0.0");

        public bool IsAvailable => true;

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<ClipEditorialMetadataDraft>(
                new InvalidDataException("Invalid provider output."));

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<ClipEditorialMetadataDraft>>(
                new InvalidDataException("Invalid provider batch output."));
    }

    private sealed class TypedFailureMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        private readonly Exception _failure;

        public TypedFailureMetadataGenerator(Exception failure)
        {
            _failure = failure;
        }

        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Typed failing AI", "1.0.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public int SingleCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SingleCalls++;
            return Task.FromException<ClipEditorialMetadataDraft>(_failure);
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            return Task.FromException<
                IReadOnlyList<ClipEditorialMetadataDraft>>(_failure);
        }
    }

    private sealed class TypedSemanticBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        private readonly Exception _batchFailure;

        public TypedSemanticBatchMetadataGenerator(Exception batchFailure)
        {
            _batchFailure = batchFailure;
        }

        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Typed semantic AI", "1.0.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public int SingleCalls { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SingleCalls++;
            return Task.FromResult(new ClipEditorialMetadataDraft(
                $"Isolated grounded action {request.Context.GameContext.GameHashtag}",
                "I completed one visible action in the bounded clip.",
                [request.Context.GameContext.GameName],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity)));
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            return Task.FromException<
                IReadOnlyList<ClipEditorialMetadataDraft>>(_batchFailure);
        }
    }

    private sealed class RichDraftMetadataGenerator :
        IClipEditorialMetadataGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Rich deterministic test generator", "1.0");

        public ClipEditorialAiProvenance Provenance { get; } = new(
            "retained-provider",
            "1.0",
            "retained-runtime",
            "example/model",
            "revision",
            new string('a', 64),
            "retained-prompt",
            "1.0",
            new string('b', 64),
            TimeSpan.FromSeconds(2),
            42);

        public bool IsAvailable => true;

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ClipEditorialMetadataDraft(
                "Retained approved title #ExampleGame",
                "I retained every draft field.",
                ["Example Game"],
                ClipEditorialMetadataOrigin.UserEdited,
                Identity,
                request.Attempt,
                evidence:
                [
                    new ClipEditorialEvidenceReference(
                        "retained-evidence",
                        ClipEditorialEvidenceKind.VisualObservation,
                        "One retained evidence record."),
                ],
                warnings:
                [
                    new ClipEditorialWarning(
                        ClipEditorialWarningCode.MetadataReviewRequired,
                        "Retained warning."),
                ],
                aiProvenance: Provenance,
                readiness: ClipEditorialMetadataReadiness.UserApproved,
                qualityIssues:
                [
                    new ClipEditorialMetadataQualityIssue(
                        ClipEditorialMetadataQualityIssueCode.GenericOpening,
                        "Retained quality issue."),
                ],
                priorAcceptedTitles:
                [
                    "Earlier accepted title #ExampleGame",
                ]));
        }
    }

    private sealed class PartiallyFailingBatchMetadataGenerator :
        IClipEditorialMetadataBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Partially failing AI", "1.0.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public int SingleCalls { get; private set; }

        public List<int> Attempts { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SingleCalls++;
            Attempts.Add(request.Attempt);
            if (request.Context.CandidateId.Equals(
                    "candidate-02",
                    StringComparison.Ordinal))
            {
                return Task.FromException<ClipEditorialMetadataDraft>(
                    new InvalidDataException(
                        "Grounded metadata assigned an unsupported mental state."));
            }

            return Task.FromResult(new ClipEditorialMetadataDraft(
                $"Qualified isolated action {request.Context.GameContext.GameHashtag}",
                "I completed one concrete action in the bounded clip.",
                [request.Context.GameContext.GameHashtag[1..]],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity)));
        }

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            return Task.FromException<IReadOnlyList<ClipEditorialMetadataDraft>>(
                new InvalidDataException(
                    "One strict case invalidated the provider batch."));
        }
    }

    private sealed class RecordingFallbackMetadataGenerator :
        IClipEditorialMetadataGenerator
    {
        private readonly HeuristicClipEditorialMetadataGenerator _inner = new();

        public ClipEditorialMetadataGeneratorIdentity Identity =>
            _inner.Identity;

        public bool IsAvailable => true;

        public List<ClipEditorialMetadataRequest> Requests { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _inner.GenerateAsync(request, cancellationToken);
        }
    }

    private sealed class FailSoftBatchMetadataGenerator :
        IClipEditorialMetadataFailSoftBatchGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Fail-soft AI", "1.0.0");

        public bool IsAvailable => true;

        public int OutcomeBatchCalls { get; private set; }

        public int LegacyBatchCalls { get; private set; }

        public List<ClipEditorialMetadataDraft> AcceptedDrafts { get; } = [];

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The fail-soft batch test must not run one clip at a time.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            LegacyBatchCalls++;
            throw new InvalidOperationException(
                "The fail-soft batch test must use typed outcomes.");
        }

        public Task<IReadOnlyList<ClipEditorialMetadataBatchOutcome>>
            GenerateBatchOutcomesAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutcomeBatchCalls++;
            ClipEditorialMetadataDraft first = CreateDraft(requests[0]);
            ClipEditorialMetadataDraft third = CreateDraft(requests[2]);
            AcceptedDrafts.Add(first);
            AcceptedDrafts.Add(third);
            IReadOnlyList<ClipEditorialMetadataBatchOutcome> outcomes =
            [
                new(first, Failure: null),
                new(
                    Draft: null,
                    Failure: new ClipEditorialMetadataCaseFailure(
                        ClipEditorialMetadataCaseFailureCode
                            .NoDistinctPrimaryVisualEvent)),
                new(third, Failure: null),
            ];
            return Task.FromResult(outcomes);
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request) =>
            new(
                $"Retained model title {request.Context.GameContext.GameHashtag}",
                "I retained the accepted model and rephraser description.",
                [request.Context.GameContext.GameName],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity));
    }

    private sealed class FailingVisualBatchMetadataGenerator :
        IClipEditorialVisualMetadataGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Failing visual AI", "1.0.0");

        public bool IsAvailable => true;

        public int BatchCalls { get; private set; }

        public bool SawVerifiedReviews { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "This fixture only supports the batch path.");

        public Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            SawVerifiedReviews = requests.All(static request =>
                request.ReviewVideo is not null &&
                File.Exists(request.ReviewVideo.ReviewVideoPath));
            return Task.FromException<
                IReadOnlyList<ClipEditorialMetadataDraft>>(
                    new InvalidDataException(
                        "The visual batch failed after review preparation."));
        }
    }

    private sealed class RecordingVisualMetadataGenerator :
        IClipEditorialVisualMetadataGenerator
    {
        public ClipEditorialMetadataGeneratorIdentity Identity { get; } =
            new("Recording visual AI", "1.0.0");

        public bool IsAvailable => true;

        public bool SawVerifiedReview { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            SawVerifiedReview = request.ReviewVideo is not null &&
                File.Exists(request.ReviewVideo.ReviewVideoPath);
            return Task.FromResult(CreateDraft(request));
        }

        public async Task<IReadOnlyList<ClipEditorialMetadataDraft>>
            GenerateBatchAsync(
                IReadOnlyList<ClipEditorialMetadataRequest> requests,
                CancellationToken cancellationToken)
        {
            var drafts = new List<ClipEditorialMetadataDraft>(requests.Count);
            foreach (ClipEditorialMetadataRequest request in requests)
            {
                drafts.Add(await GenerateAsync(request, cancellationToken));
            }
            return drafts.AsReadOnly();
        }

        private ClipEditorialMetadataDraft CreateDraft(
            ClipEditorialMetadataRequest request) =>
            new(
                $"Concrete visible action {request.Context.GameContext.GameHashtag}",
                "A concrete visible action occurs in the bounded source clip.",
                [request.Context.GameContext.GameHashtag[1..]],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence,
                aiProvenance: CreateTestAiProvenance(Identity));
    }

    private sealed class RecordingReviewVideoMaterializer :
        IVisualSemanticReviewVideoMaterializer
    {
        public int Calls { get; private set; }

        public int Cleanups { get; private set; }

        public VisualSemanticReviewVideoMaterializationRequest? LastRequest
        {
            get;
            private set;
        }

        public Task<MaterializedVisualSemanticReviewVideo> MaterializeAsync(
            VisualSemanticReviewVideoMaterializationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            string directory = Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundry-EditorialReviewTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "review.mp4");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var info = new FileInfo(path);
            DateTimeOffset written = new(
                DateTime.SpecifyKind(
                    info.LastWriteTimeUtc,
                    DateTimeKind.Utc));
            var input = new VisualSemanticInputManifest(
                path,
                Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        File.ReadAllBytes(path))),
                info.Length,
                request.Duration,
                written);
            return Task.FromResult(
                new MaterializedVisualSemanticReviewVideo(
                    request,
                    input,
                    () =>
                    {
                        Cleanups++;
                        Directory.Delete(directory, recursive: true);
                    }));
        }
    }

    private sealed class EditorialTestDirectory : IDisposable
    {
        public EditorialTestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ReplayFoundry-EditorialTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
