using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;
using ReplayFoundry.Desktop.Media.Intelligence.VisualText;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.VisualSemantic;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static class EditorialMetadataTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Editorial profile snapshots reusable tags", ProfileIsImmutable),
        new("Editorial variant intent never treats unreviewed ASR as commentary authority", VariantIntentRequiresReviewedTranscript),
        new("AI rerolls preserve four distinct provider-authored metadata packages", AiRerollsKeepDistinctProviderPackages),
        new("Historical Qwen metadata retains its exact prompt identity", HistoricalQwenPromptIdentityIsPreserved),
        new("Grounded metadata uses creator and game transcript context", UsesBothTranscriptRoles),
        new("Manual game notes retain provider-neutral draft provenance", ManualGameNotesRetainDraftProvenance),
        new("Heuristic metadata never promotes raw automatic transcript wording", RawTranscriptIsNotAudienceMetadata),
        new("Heuristic metadata rerolls are deterministic and distinct", RerollsAreDeterministic),
        new("Transcript-free metadata records limited grounding", MissingTranscriptWarns),
        new("Optional AI metadata falls back explicitly", OptionalAiFallbackWarns),
        new("Editorial warning decoration preserves every draft field", WarningDecorationPreservesDraftState),
        new("Optional AI metadata failure degrades explicitly", OptionalAiFailureFallsBack),
        new("Optional AI metadata batch failure falls back without hidden reruns", OptionalAiBatchFailureFallsBackOnce),
        new("Optional AI batch fallback drops transient visual reviews", OptionalAiBatchFallbackDropsTransientReviews),
        new("Qwen structured failure details remain actionable", StructuredQwenFailureIsActionable),
        new("Grounded Qwen failure diagnostics remain bounded and durable", GroundedFailureArchiveIsBounded),
        new("Grounded Qwen executor attaches its typed failure envelope", GroundedQwenExecutorAttachesFailureEnvelope),
        new("Grounded Qwen serializes only wire-authorized visual text", GroundedQwenSerializesOnlyWireAuthorizedVisualText),
        new("Grounded CUDA OOM telemetry stops rerolls and batch isolation", GroundedCudaOomTelemetryStopsRetryAndIsolation),
        new("Typed Qwen resource failures do not reroll or isolate", TypedQwenResourceFailuresDoNotRetry),
        new("Untyped Qwen technical failures fail closed without GPU rerolls", UntypedQwenTechnicalFailuresFailClosed),
        new("Legacy semantic host failures do not trigger hidden reruns", LegacySemanticHostFailuresRunOnce),
        new("Qwen retries reuse one immutable verified model lease", QwenRetriesReuseVerifiedModelLease),
        new("Required AI metadata never silently substitutes heuristics", RequiredAiRejectsFallback),
        new("Required AI metadata propagates provider failure", RequiredAiFailurePropagates),
        new("Editorial AI batches load one provider for every candidate", AiBatchPreservesOrder),
        new("Visual AI metadata uses one bounded verified review and cleans it", VisualAiMaterializesAndCleansReview),
        new("Visual AI metadata reuses an existing bounded review", VisualAiReusesExistingReview),
        new("Grounded heuristic titles always retain the game hashtag", GameHashtagSurvivesTitleLimit),
        new("Game context memory is private local and source-reusable", GameContextMemoryIsPrivate),
        new("Game context memory normalizes inherited v1.1 flags", GameContextMemoryNormalizesInheritedFlags),
        new("User metadata edits preserve provenance", UserEditsPreserveProvenance),
        new("Studio asset edits retain editorial metadata", AssetEditsRetainMetadata),
        new("Studio metadata editor saves through its focused MVVM boundary", StudioEditorSavesMetadata),
        new("Studio wording preferences preserve explicit default tags", WordingPreferencesPreserveDefaultTags),
        new("Studio rerolls require a clean saved metadata draft", StudioRerollRequiresCleanDraft),
        new("Studio metadata changes preserve newer clip edits", StudioMetadataPreservesNewerEdits),
        new("Studio metadata rerolls use the current saved cut", StudioMetadataRerollUsesCurrentCut),
        new("Studio rejects metadata completed for a superseded cut", StudioMetadataRejectsSupersededCut),
        new("Creator voice Settings updates the shared editorial profile", CreatorVoiceSettingsUpdatesSharedProfile),
        new("Accepted Thorough Hidden Moments refresh metadata through AI", AcceptedHiddenMomentRefreshesEditorialMetadata),
        new("Grounded heuristic metadata never copies style instructions or source timing", HeuristicMetadataDoesNotLeakInstructionsOrTiming),
        new("Qwen metadata rejects generic titles and internal timing", QwenMetadataRejectsUngroundedContent),
        new("Qwen metadata enforces grounded creator voice", QwenMetadataEnforcesCreatorVoice),
        new("Qwen metadata enforces typed primary actor authority", QwenMetadataEnforcesActorAuthority),
        new("Review-flagged grounded copy remains usable", ReviewFlaggedGroundedCopyRemainsUsable),
        new("Heuristic labels require Studio approval before rendering", WorkingLabelsRequireApproval),
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
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            new VariantPackageMetadataGenerator());
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

        (string previousVisualDraftVersion,
            string previousVisualDraftHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousVisualDraftPromptOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptVersion,
            previousVisualDraftVersion,
            "Pre-literal-action visual-draft prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptSha256,
            previousVisualDraftHash,
            "Pre-literal-action visual-draft prompt hash.");

        (string previousInterfaceVersion,
            string previousInterfaceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousInterfaceAttributionOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptVersion,
            previousInterfaceVersion,
            "Pre-interface-attribution prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptSha256,
            previousInterfaceHash,
            "Pre-interface-attribution prompt hash.");

        (string previousEffectiveVoiceVersion,
            string previousEffectiveVoiceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousEffectiveVoiceOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptVersion,
            previousEffectiveVoiceVersion,
            "Pre-effective-voice prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptSha256,
            previousEffectiveVoiceHash,
            "Pre-effective-voice prompt hash.");

        (string previousWhitespaceVersion, string previousWhitespaceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousGroundedJsonWhitespaceOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptVersion,
            previousWhitespaceVersion,
            "Pre-canonical-whitespace prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PreviousPromptSha256,
            previousWhitespaceHash,
            "Pre-canonical-whitespace prompt hash.");

        (string previousCreatorAuthorityVersion,
            string previousCreatorAuthorityHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCreatorAuthorityOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousCreatorAuthorityVersion,
            "Previous creator-authority prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousCreatorAuthorityHash,
            "Previous creator-authority prompt hash.");

        (string previousAudienceVersion, string previousAudienceHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousAudienceCopyWithholdingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousAudienceVersion,
            "Previous audience-copy-withholding prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousAudienceHash,
            "Previous audience-copy-withholding prompt hash.");

        (string previousCrossDraftVersion, string previousCrossDraftHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCrossDraftRetryOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousCrossDraftVersion,
            "Previous cross-draft retry prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousCrossDraftHash,
            "Previous cross-draft retry prompt hash.");

        (string previousRootPreloadVersion, string previousRootPreloadHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousRootPreloadOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousRootPreloadVersion,
            "Previous root-preload prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousRootPreloadHash,
            "Previous root-preload prompt hash.");

        (string previousAttentionVersion, string previousAttentionHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousCudnnAttentionOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousAttentionVersion,
            "Previous attention-backend prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousAttentionHash,
            "Previous attention-backend prompt hash.");

        (string previousAccelerateVersion, string previousAccelerateHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousAccelerateOffloadOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousAccelerateVersion,
            "Previous Accelerate-offload prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousAccelerateHash,
            "Previous Accelerate-offload prompt hash.");

        (string previousPositionVersion, string previousPositionHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousPositionEmbeddingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousPositionVersion,
            "Previous position-embedding prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousPositionHash,
            "Previous position-embedding prompt hash.");

        (string previousVisionVersion, string previousVisionHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousVisionOffloadOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousVisionVersion,
            "Previous all-CUDA prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousVisionHash,
            "Previous all-CUDA prompt hash.");

        (string previousLowPeakVersion, string previousLowPeakHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousLowPeakSamplingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousLowPeakVersion,
            "Previous low-peak prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousLowPeakHash,
            "Previous low-peak prompt hash.");

        (string previousPeakVersion, string previousPeakHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousPeakBoundedSamplingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousPeakVersion,
            "Previous peak-bounded prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousPeakHash,
            "Previous peak-bounded prompt hash.");

        (string previousSamplingVersion, string previousSamplingHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator
                    .PreviousSamplingOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousSamplingVersion,
            "Previous-sampling prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousSamplingHash,
            "Previous-sampling prompt hash.");

        (string preWatchdogVersion, string preWatchdogHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreWatchdogOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            preWatchdogVersion,
            "Pre-watchdog prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            preWatchdogHash,
            "Pre-watchdog prompt hash.");

        (string previousVersion, string previousHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PreviousOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            previousVersion,
            "Previous prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            previousHash,
            "Previous prompt hash.");

        (string priorVersion, string priorHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PriorOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            priorVersion,
            "Prior prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            priorHash,
            "Prior prompt hash.");

        (string legacyVersion, string legacyHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.LegacyOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            legacyVersion,
            "Legacy prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            legacyHash,
            "Legacy prompt hash.");

        (string historicalVersion, string historicalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.HistoricalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            historicalVersion,
            "Historical prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            historicalHash,
            "Historical prompt hash.");

        (string priorHistoricalVersion, string priorHistoricalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.PriorHistoricalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            priorHistoricalVersion,
            "Prior historical prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            priorHistoricalHash,
            "Prior historical prompt hash.");

        (string earlierHistoricalVersion, string earlierHistoricalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.EarlierHistoricalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptVersion,
            earlierHistoricalVersion,
            "Earlier historical prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.EarlierPromptSha256,
            earlierHistoricalHash,
            "Earlier historical prompt hash.");

        (string initialVersion, string initialHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.InitialOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            initialVersion,
            "Initial prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            initialHash,
            "Initial prompt hash.");

        (string oldestVersion, string oldestHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.OldestOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            oldestVersion,
            "Oldest prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            oldestHash,
            "Oldest prompt hash.");
        (string earliestVersion, string earliestHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.EarliestOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            earliestVersion,
            "Earliest prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            earliestHash,
            "Earliest prompt hash.");
        (string foundationalVersion, string foundationalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.FoundationalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            foundationalVersion,
            "Foundational prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            foundationalHash,
            "Foundational prompt hash.");
        (string originalVersion, string originalHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.OriginalOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptVersion,
            originalVersion,
            "Original prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.PriorPromptSha256,
            originalHash,
            "Original prompt hash.");
        (string baselineVersion, string baselineHash) =
            Qwen3VlGroundedMetadataResultParser.PromptIdentityFor(
                Qwen3VlGroundedMetadataGenerator.BaselineOutputSchema);
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptVersion,
            baselineVersion,
            "Baseline prompt version.");
        TestAssert.Equal(
            Qwen3VlGroundedMetadataGenerator.InitialPromptSha256,
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
                "Example Game",
                "#ExampleGame",
                notes,
                ClipEditorialGameContextSource.UserConfirmed,
                useOpenGameKnowledge: true));

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
            VisualDrafts: [],
            StableReadableText: [],
            ActorAuthorityAssessmentApplied: false,
            PrimaryVisualDraftOrdinal: 1,
            PrimaryActorAuthority:
                Qwen3VlGroundedMetadataActorAuthority.Unknown,
            PrimaryCreatorExperienceRelation:
                Qwen3VlGroundedMetadataCreatorExperienceRelation.Unestablished,
            VisualEventSelectionAssessments: [],
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
        IReadOnlyList<ClipEditorialWarning> warnings =
            Qwen3VlGroundedMetadataEvidenceBuilder.BuildWarnings(
                request,
                validation,
                grounding: []);
        ClipEditorialWarning notSelected = warnings.Single(value =>
            value.Code == ClipEditorialWarningCode.GameKnowledgeNotSelected);
        TestAssert.True(
            notSelected.Message.Contains(
                "No licensed game-knowledge passage was visually confirmed",
                StringComparison.Ordinal) &&
            notSelected.Message.Contains(
                "user notes",
                StringComparison.OrdinalIgnoreCase),
            "The typed warning must distinguish local notes from unselected licensed knowledge.");
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
        TestAssert.False(
            result.IsPublishReady,
            "Unreviewed heuristic metadata must not flow to final output.");
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

    private static async Task OptionalAiFallbackWarns()
    {
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator());
        ClipEditorialMetadataDraft result =
            await service.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiWhenAvailable),
                CancellationToken.None);

        TestAssert.Equal(
            ClipEditorialMetadataOrigin.Heuristic,
            result.Origin,
            "Fallback origin.");
        TestAssert.True(
            result.Warnings.Any(
                static warning =>
                    warning.Code ==
                    ClipEditorialWarningCode.AiProviderUnavailable),
            "Fallback must be visible.");
    }

    private static async Task WarningDecorationPreservesDraftState()
    {
        var heuristic = new RichDraftMetadataGenerator();
        var service = new ClipEditorialMetadataGenerationService(heuristic);

        ClipEditorialMetadataDraft result = await service.GenerateAsync(
            new ClipEditorialMetadataRequest(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
            CancellationToken.None);

        TestAssert.Same(heuristic.Provenance, result.AiProvenance!,
            "Adding a provider-unavailable warning must preserve AI provenance identity.");
        TestAssert.Equal(ClipEditorialMetadataReadiness.UserApproved,
            result.Readiness,
            "Adding a warning must preserve readiness.");
        TestAssert.Equal(1, result.QualityIssues.Count,
            "Adding a warning must preserve quality diagnostics.");
        TestAssert.Equal("Earlier accepted title #ExampleGame",
            result.PriorAcceptedTitles.Single(),
            "Adding a warning must preserve reroll history.");
    }

    private static async Task OptionalAiFailureFallsBack()
    {
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            new FailingBatchMetadataGenerator());
        ClipEditorialMetadataDraft result = await service.GenerateAsync(
            new ClipEditorialMetadataRequest(
                CreateContext(),
                ClipEditorialProfile.Default,
                0,
                ClipEditorialGenerationPreference.AiWhenAvailable),
            CancellationToken.None);

        TestAssert.Equal(
            ClipEditorialMetadataOrigin.Heuristic,
            result.Origin,
            "Provider failure fallback origin.");
        TestAssert.True(
            result.Warnings.Any(static warning =>
                warning.Code == ClipEditorialWarningCode.AiProviderFailed),
            "Provider failure must remain visible.");
    }

    private static async Task OptionalAiBatchFailureFallsBackOnce()
    {
        var ai = new PartiallyFailingBatchMetadataGenerator();
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
                CreateContext(candidateId: "candidate-02"),
                ClipEditorialProfile.Default,
                1,
                ClipEditorialGenerationPreference.AiWhenAvailable),
        ];

        IReadOnlyList<ClipEditorialMetadataDraft> results =
            await service.GenerateBatchAsync(
                requests,
                CancellationToken.None);

        TestAssert.Equal(2, results.Count, "Isolated result count.");
        TestAssert.Equal(1, ai.BatchCalls, "The normal fast batch path is attempted once.");
        TestAssert.Equal(0, ai.SingleCalls,
            "A failed batch must not fan out into hidden per-clip model runs.");
        TestAssert.True(results.All(static result =>
                result.Origin == ClipEditorialMetadataOrigin.Heuristic &&
                result.Warnings.Any(static warning =>
                    warning.Code == ClipEditorialWarningCode.AiProviderFailed)),
            "Optional mode keeps one explicit working label per request.");

        ClipEditorialContext rejectedContext = requests[1].Context;
        var rejectedAsset = new GenerationOutputAsset(
            rejectedContext.CandidateId,
            2,
            TestMediaFactory.Create(
                rejectedContext.SourceFullPath,
                rejectedContext.SourceDuration),
            outputFullPath: null,
            rejectedContext.SourceStart,
            rejectedContext.SourceEnd,
            rejectedContext.DeterministicScore,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            rejectedContext.DeterministicReason,
            editorialContext: rejectedContext,
            editorialMetadata: results[0]);
        StudioEditorialDraftSnapshot studio =
            new StudioEditorialMetadataService(null, null, null)
                .LoadDraft(rejectedAsset);
        TestAssert.True(
            studio.Status.Contains(
                "qualified local AI could not return a complete metadata batch",
                StringComparison.OrdinalIgnoreCase),
            "Studio must explain why optional AI used a working label.");
    }

    private static async Task OptionalAiBatchFallbackDropsTransientReviews()
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

        IReadOnlyList<ClipEditorialMetadataDraft> results =
            await service.GenerateBatchAsync(
                requests,
                CancellationToken.None);

        TestAssert.Equal(2, results.Count, "Fallback result count.");
        TestAssert.Equal(1, ai.BatchCalls, "The visual batch runs once.");
        TestAssert.True(ai.SawVerifiedReviews,
            "The visual provider receives both bounded review videos.");
        TestAssert.Equal(2, materializer.Calls,
            "Each request gets one bounded visual review.");
        TestAssert.Equal(2, materializer.Cleanups,
            "Every transient visual review is cleaned after the batch.");
        TestAssert.Equal(2, heuristic.Requests.Count,
            "The deterministic fallback runs once per original request.");
        TestAssert.True(heuristic.Requests.All(static request =>
                request.ReviewVideo is null),
            "Fallback requests must not retain temporary AI review-video paths.");
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
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            generator,
            materializer);

        Qwen3VlInferenceException exception =
            await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
                () => service.GenerateBatchAsync(
                    requests,
                    CancellationToken.None),
                "Typed CUDA OOM telemetry must propagate without case isolation.");

        TestAssert.True(exception.HostFailure is
        {
            Stage: Qwen3VlHostFailureStage.Inference,
            Failure.ErrorCode: Qwen3VlHostErrorCode.InferenceError,
            GroundedMemoryPolicy.RuntimeOutcome:
                    "CudaAllocatorOutOfMemory",
            GroundedMemoryPolicy.FailureReason:
                    "CudaAllocatorOutOfMemory",
        },
            "The real failure-1.4 parser must attach the exact CUDA OOM pair.");
        TestAssert.True(
            exception.FailureEnvelopeParseException is null,
            "Valid CUDA OOM telemetry must not degrade to untyped diagnostics.");
        TestAssert.True(
            exception.Message.Contains(
                "allocatorLimitBytes=12884901888",
                StringComparison.Ordinal),
            "A pasted CUDA failure must retain its allocator limit.");
        TestAssert.True(
            exception.Message.Contains(
                "peakReservedGpuBytes=12884901888",
                StringComparison.Ordinal),
            "A pasted CUDA failure must retain its peak reserved allocation.");
        TestAssert.Equal(1, processCalls,
            "A CUDA OOM batch must run one host process with no reroll or isolation.");
        TestAssert.Equal(0, materializer.Calls,
            "Pre-materialized bounded reviews must be reused during failure handling.");
    }

    private static string CreateStartupMemoryFailureJson(
        string modelManifestSha256)
    {
        const long gibibyte = 1024L * 1024 * 1024;
        long total = 16 * gibibyte;
        long startupFree = 13 * gibibyte;
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
                cacheImplementation = "offloaded",
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
                cacheImplementation = "offloaded",
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
        var directAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "The fixed CUDA allocator limit was reached.",
                hostFailure: resourceFailure));
        var directService = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            directAi);

        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => directService.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired),
                CancellationToken.None),
            "A proven allocator failure must propagate without creative rerolls.");
        TestAssert.Equal(1, directAi.SingleCalls,
            "A deterministic resource failure must run one direct attempt.");

        var requiredBatchAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "The fixed CUDA allocator limit was reached.",
                hostFailure: resourceFailure));
        var requiredBatchService = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
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
        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => requiredBatchService.GenerateBatchAsync(
                requiredRequests,
                CancellationToken.None),
            "A proven batch resource failure must not enter per-clip isolation.");
        TestAssert.Equal(1, requiredBatchAi.BatchCalls,
            "The failing batch is submitted once.");
        TestAssert.Equal(0, requiredBatchAi.SingleCalls,
            "Resource failure must not fan out into isolated model loads.");

        var optionalBatchAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "The fixed CUDA allocator limit was reached.",
                hostFailure: resourceFailure));
        var optionalBatchService = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            optionalBatchAi);
        IReadOnlyList<ClipEditorialMetadataDraft> optional =
            await optionalBatchService.GenerateBatchAsync(
                requiredRequests.Select(request =>
                    new ClipEditorialMetadataRequest(
                        request.Context,
                        request.Profile,
                        request.Attempt,
                        ClipEditorialGenerationPreference.AiWhenAvailable))
                    .ToArray(),
                CancellationToken.None);
        TestAssert.Equal(1, optionalBatchAi.BatchCalls,
            "Optional AI also submits the failing batch once.");
        TestAssert.Equal(0, optionalBatchAi.SingleCalls,
            "Optional fallback must not isolate a deterministic resource failure.");
        TestAssert.True(optional.All(static draft =>
                draft.Origin == ClipEditorialMetadataOrigin.Heuristic &&
                draft.Warnings.Any(static warning =>
                    warning.Code == ClipEditorialWarningCode.AiProviderFailed)),
            "Optional mode must keep explicit deterministic working labels.");

    }

    private static async Task LegacySemanticHostFailuresRunOnce()
    {
        Qwen3VlHostFailureEnvelope semanticFailure = CreateHostFailure(
            Qwen3VlHostErrorCode.InferenceError,
            Qwen3VlHostFailureStage.Inference);
        var directAi = new TypedFailureMetadataGenerator(
            new Qwen3VlInferenceException(
                "Grounded metadata assigned an unsupported mental state.",
                hostFailure: semanticFailure));
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            directAi);

        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => service.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired),
                CancellationToken.None),
            "A legacy semantic host rejection is surfaced without hidden model work.");
        TestAssert.Equal(1, directAi.SingleCalls,
            "Current copy review happens inside one host result.");

        var batchAi = new TypedSemanticBatchMetadataGenerator(
            new Qwen3VlInferenceException(
                "Grounded metadata assigned an unsupported mental state.",
                hostFailure: semanticFailure));
        var batchService = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
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
        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => batchService.GenerateBatchAsync(
                requests,
                CancellationToken.None),
            "A legacy semantic batch failure is surfaced once.");
        TestAssert.Equal(1, batchAi.BatchCalls,
            "Semantic batch rejection uses one batch attempt.");
        TestAssert.Equal(0, batchAi.SingleCalls,
            "A semantic batch failure no longer enters case isolation.");
    }

    private static async Task UntypedQwenTechnicalFailuresFailClosed()
    {
        var untypedFailure = new Qwen3VlInferenceException(
            "The grounded host failed without a valid typed failure artifact.");
        var directAi = new TypedFailureMetadataGenerator(untypedFailure);
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            directAi);

        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => service.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired),
                CancellationToken.None),
            "Missing failure telemetry must fail closed without another GPU attempt.");
        TestAssert.Equal(1, directAi.SingleCalls,
            "An untyped grounded inference failure runs only once.");

        var batchAi = new TypedFailureMetadataGenerator(untypedFailure);
        var batchService = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
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
        await TestAssert.ThrowsAsync<Qwen3VlInferenceException>(
            () => batchService.GenerateBatchAsync(
                batchRequests,
                CancellationToken.None),
            "Missing failure telemetry must not fan out through batch isolation.");
        TestAssert.Equal(1, batchAi.BatchCalls,
            "An untyped failing batch is submitted once.");
        TestAssert.Equal(0, batchAi.SingleCalls,
            "An untyped technical failure does not enter per-clip isolation.");

        var parseFailure = new Qwen3VlOutputParseException(
            "The provider response was structurally valid JSON but failed editorial validation.");
        var semanticAi = new TypedFailureMetadataGenerator(parseFailure);
        var semanticService = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            semanticAi);
        await TestAssert.ThrowsAsync<Qwen3VlOutputParseException>(
            () => semanticService.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired),
                CancellationToken.None),
            "Malformed output remains a technical failure without hidden reruns.");
        TestAssert.Equal(1, semanticAi.SingleCalls,
            "Output parsing runs once; schema-valid copy quality is retained by the host.");
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

    private static async Task RequiredAiRejectsFallback()
    {
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator());

        await TestAssert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired),
                CancellationToken.None),
            "Required AI must not use heuristics silently.");
    }

    private static async Task RequiredAiFailurePropagates()
    {
        var service = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(),
            new FailingBatchMetadataGenerator());

        await TestAssert.ThrowsAsync<InvalidDataException>(
            () => service.GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0,
                    ClipEditorialGenerationPreference.AiRequired),
                CancellationToken.None),
            "Required AI must preserve provider failure.");
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
                media,
                reviewFocusSourceTimestamp:
                    context.SourceStart + TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        TestAssert.Equal(
            ClipEditorialMetadataOrigin.AiAssisted,
            result.Origin,
            "Visual provider origin.");
        TestAssert.Equal(1, materializer.Calls, "One bounded materialization.");
        TestAssert.Equal(1, materializer.Cleanups, "Transient review cleanup.");
        TestAssert.Equal(
            TimeSpan.FromSeconds(16),
            materializer.LastRequest!.Duration,
            "Grounded metadata must use exactly one bounded sampling window.");
        TestAssert.Equal(
            context.SourceStart + TimeSpan.FromSeconds(22),
            materializer.LastRequest.SourceStart,
            "The bounded metadata review must center the qualified event focus.");
        TestAssert.True(provider.SawVerifiedReview, "Provider review input.");
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
                GenerationGameContextOrigin.ReusedUserMemory,
                recalled.Origin,
                "Recalled provenance.");
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
        using var editor = new StudioEditorialMetadataViewModel(
            session,
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator()),
            new ClipEditorialProfileSession());
        editor.Bind(project, asset);
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

    private static async Task StudioRerollRequiresCleanDraft()
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
        const string unsavedTitle = "My unsaved title must not mask a reroll";
        editor.Title = unsavedTitle;

        TestAssert.False(
            editor.RerollCommand.CanExecute(null),
            "A reroll must stay disabled while title, description, or tag edits are unsaved.");
        TestAssert.True(
            editor.RerollProviderText.Contains(
                "Save",
                StringComparison.OrdinalIgnoreCase),
            "The disabled reroll must explain how to preserve the pending edit.");

        editor.SaveCommand.Execute(null);
        TestAssert.False(
            editor.HasUnsavedChanges,
            "Saving the visible metadata must establish a clean reroll boundary.");
        TestAssert.True(
            editor.RerollCommand.CanExecute(null),
            "The same reroll action must become available after the edit is saved.");

        await ((AsyncDelegateCommand)editor.RerollCommand).ExecuteAsync();

        TestAssert.Equal(
            2,
            generator.LastRequest!.PriorAcceptedTitleExclusions.Count,
            "Studio must exclude the generated title and the saved user edit from the next exact-cut reroll.");
        ClipEditorialMetadataDraft rerolled = session.Current!.PrimaryAsset
            .EditorialMetadata!;
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
        await generator.Started;
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
                "boundaries changed",
                StringComparison.OrdinalIgnoreCase),
            "The superseded-cut failure must be actionable.");
        TestAssert.Same(
            newerCut,
            session.Current!.Assets[0],
            "The latest cut must survive the rejected metadata result.");
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
                    [[90, 80]]))
            .Find(request);
        GenerationHiddenMomentDeck deck =
            GenerationHiddenMomentPlanner.Create(moments);
        var ai = new RecordingBatchMetadataGenerator();
        var service = new GenerationEditorialMetadataService(
            new ClipEditorialMetadataGenerationService(
                new HeuristicClipEditorialMetadataGenerator(),
                ai),
            new ClipEditorialProfileSession());
        GenerationHiddenMomentDeck hydrated =
            await service.GenerateHiddenAsync(
                deck,
                candidateIntelligence: null,
                CancellationToken.None);

        GenerationHiddenMoment accepted =
            await service.PrepareAcceptedHiddenAsync(
                hydrated.Moments[0],
                captions: null,
                CancellationToken.None);

        TestAssert.Equal(1, ai.SingleCalls, "Accepted review invokes AI once.");
        TestAssert.Equal(
            ClipEditorialMetadataOrigin.AiAssisted,
            accepted.EditorialMetadata!.Origin,
            "The accepted alternate replaces its deck placeholder with grounded AI metadata in Thorough mode.");
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
                "one space followed by exact hashtag",
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
            "A blue figure pulsed beneath the illuminated sign.",
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
            "I got attacked before I reached the doorway.",
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

    private static async Task WorkingLabelsRequireApproval()
    {
        ClipEditorialMetadataDraft working =
            await new HeuristicClipEditorialMetadataGenerator().GenerateAsync(
                new ClipEditorialMetadataRequest(
                    CreateContext(),
                    ClipEditorialProfile.Default,
                    0),
                CancellationToken.None);
        TestAssert.False(working.IsPublishReady, "Working-label readiness.");
        ClipEditorialMetadataDraft approved = working.WithUserEdits(
            "Choosing my next skill #ExampleGame",
            "I compare the available upgrades and confirm the one I want.",
            ["ExampleGame", "skill menu"]);
        TestAssert.Equal(
            ClipEditorialMetadataReadiness.UserApproved,
            approved.Readiness,
            "Saving a Studio edit explicitly approves the metadata.");
        TestAssert.True(approved.IsPublishReady, "Approved metadata readiness.");
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
        ClipEditorialMetadataDraft Metadata)> CreateAssetAsync()
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
                context.SourceDuration),
            outputFullPath: null,
            context.SourceStart,
            context.SourceEnd,
            context.DeterministicScore,
            70,
            GenerationCandidateSelectionReason.QualityQualified,
            context.DeterministicReason,
            editorialContext: context,
            editorialMetadata: metadata);
        return (asset, metadata);
    }

    private static ClipEditorialContext CreateContext(
        IEnumerable<ClipEditorialTranscriptContext>? transcripts = null,
        string candidateId = "candidate-01") =>
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
            [
                new ClipEditorialEvidenceReference(
                    "gameplay-onset",
                    ClipEditorialEvidenceKind.DeterministicMoment,
                    "Gameplay onset was locally prominent."),
                new ClipEditorialEvidenceReference(
                    "audio-novelty",
                    ClipEditorialEvidenceKind.DeterministicMoment,
                    "Audio novelty aligned with the gameplay change."),
            ]);

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
                request.Context.Evidence);
    }

    private sealed class ModelFreeGroundedExecutorFixture : IDisposable
    {
        private readonly string _root;
        private readonly VisualSemanticInputManifest _review;

        public ModelFreeGroundedExecutorFixture()
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
                Path.GetFullPath(Path.Combine(
                    "eng",
                    "visual-semantic-host",
                    promptFileName)),
                Path.Combine(hostDirectory, promptFileName));
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

        public ClipEditorialMetadataRequest CreateRequestWithVisualText()
        {
            ClipEditorialContext context = CreateContext();
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

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string intent = request.VariantIntent.ToString();
            return Task.FromResult(new ClipEditorialMetadataDraft(
                $"{intent} grounded title {request.Context.GameContext.GameHashtag}",
                $"I completed the {intent} grounded event in this bounded clip.",
                [request.Context.GameContext.GameName, intent],
                ClipEditorialMetadataOrigin.AiAssisted,
                Identity,
                request.Attempt,
                request.Context.Evidence));
        }
    }

    private sealed class RecordingRequestMetadataGenerator :
        IClipEditorialMetadataGenerationService
    {
        private readonly HeuristicClipEditorialMetadataGenerator _inner = new();

        public bool IsAiAvailable => false;

        public ClipEditorialMetadataRequest? LastRequest { get; private set; }

        public Task<ClipEditorialMetadataDraft> GenerateAsync(
            ClipEditorialMetadataRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _inner.GenerateAsync(request, cancellationToken);
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
                request.Context.Evidence));
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
                request.Context.Evidence));
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
                request.Context.Evidence);
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
