using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.Progress;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Intelligence;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Caption editor uses current cut and preserves measured words", CaptionEditingUsesCurrentCutAndPreservesMeasuredWords),
        new("Caption trim and extension round trips retain measured source clocks", CaptionCutRoundTripsKeepAbsoluteWordClocks),
        new("Named caption look catalogs migrate legacy arrays explicitly", NamedCaptionLookCatalogMigratesExplicitly),
        new("Missing caption fonts resolve consistently in preview and export", MissingCaptionFontsResolveConsistently),
        new("Custom caption safe areas report measured text overflow", CustomCaptionSafeAreasReportMeasuredOverflow),
        new("Caption editor supports structure undo and word timing", CaptionEditorSupportsStructureUndoAndWordTiming),
        new("Corrected-text alignment stages an auditable undoable draft proposal", CorrectedAlignmentStagesAndPersistsAReviewableProposal),
        new("Corrected-text alignment rejects failed canceled and stale results", CorrectedAlignmentRejectsFailuresAndStaleResults),
        new("Caption word timing handles stay bounded and undo as one edit", CaptionWordHandlesAreBoundedAndUndoAsOneEdit),
        new("Silent videos support manual caption authoring without audio operations", SilentVideoSupportsManualCaptionAuthoring),
        new("English corrected-text alignment stays independent of Whisper availability", EnglishAlignmentIsIndependentOfWhisperAvailability),
        new("Caption language selection survives draft notifications and Save", CaptionLanguageSelectionSurvivesDraftNotifications),
        new("Saving another caption preserves partial offcut word evidence exactly", SavingAnotherCaptionPreservesPartialOffcutEvidence),
        new("Caption preview timing warnings use only visible presentation coverage", CaptionPreviewWarningsUseOnlyVisiblePresentationCoverage),
        new("Pop displays edited punctuation and case without rewriting measured words", PopDisplaysEditedCueSlicesWithoutRewritingMeasuredWords),
        new("Foreign-stream alignment cannot inherit the original editorial speech role", ForeignStreamAlignmentCannotInheritOriginalEditorialRole),
        new("Subtitle sidecars round-trip and clip to the cut", SubtitleSidecarsRoundTripAndClipToTheCut),
        new("Named caption typography persists and renders", NamedCaptionTypographyPersistsAndRenders),
        new("Generate captures complete named caption looks before discovery", GenerateCapturesNamedCaptionLook),
        new("Clean video retains cut-aware SRT and WebVTT delivery", CleanCaptionDeliveryPreservesCutClocks),
        new("Independent caption styling survives persistence and render projection", IndependentCaptionStyleControlsRender),
        new("Caption casing and line placement preserve observed word clocks", CaptionStyleProjectionPreservesSpeech),
        new("Caption readability and vocabulary remain explicit", CaptionReadabilityAndVocabularyRemainExplicit),
        new("Folder import rejects active and changing recordings", FolderImportRejectsActiveAndChangingRecordings),
        new("Marker import preserves bounded source guidance", MarkerImportPreservesBoundedSourceGuidance),
        new("Platform preset and publishing package preserve edits", PlatformPresetAndPublishingPackagePreserveEdits),
        new("Caption audition uses the chosen stream and current cut", CaptionAuditionUsesChosenStreamAndCurrentCut),
        new("Audition view projections preserve playback coordination and shutdown draining", AuditionViewsPreserveSessionLifetime),
        new("Output settings persist and survive other Studio edits", OutputSettingsPersistAndSurviveEdits),
        new("Resolution choices preserve display geometry and keep proxies bounded", ResolutionProfilesPreserveGeometryAndBoundPreviews),
        new("Resolution survives legacy reload, presets and cut-list editing", ResolutionSurvivesReloadAndCutListEdits),
        new("High-resolution export receives an appropriate encoding budget", HighResolutionReceivesEncodingBudget),
        new("Output composition crops gameplay and stacks facecam", OutputCompositionCropsAndStacks),
        new("Output audio applies gains, mute, and voice ducking", OutputAudioAppliesMuteGainAndDucking),
        new("Render checkpoints reuse only intact completed outputs", RenderCheckpointReusesOnlyIntactOutput),
        new("Render profiles preserve film and fractional source cadence", OutputProfilePreservesSourceCadence),
        new("Hardware qualification requires decode and retries software on rejection", HardwareEncodingQualifiesAndFallsBack),
        new("Hardware validation failure retries and validates the software output", HardwareValidationFailureRetriesSoftware),
        new("Canceled hardware work never launches a fallback encoder", HardwareCancellationDoesNotRetry),
        new("Framing keyframes and text overlays retain source timing across preview and trim", FrameEditsRetainSourceTiming),
        new("Precomposed portrait recordings preserve the full source by default", PortraitCompositionPreservesFullRecording),
        new("Cut lists split, reorder and omit sections without losing source timing", CutListsPreserveSourceTiming),
        new("Frame-edit input drafts stay anchored when clip starts change", FrameEditInputsRebaseAfterTrim),
        new("Final validation requires 8-bit SDR and complete HDR conversion color tags", RenderValidationChecksColorContract),
        new("Mix mastering and before-after audition share the saved cut and audio graph", AudioMasteringAndAuditionUseSavedCut),
        new("Reusable layouts retain game identity and preserve audio while composing HUD layers", ReusableLayoutsPreserveAudioAndComposeHud),
        new("Clip render profile is bounded and preserves orientation", ProfileIsBounded),
        new("Clip command mixes exact audio streams and keeps paths atomic", SegmentCommandMapsExactStreams),
        new("Clip command supplies silence when source has no audio", SegmentCommandSuppliesSilence),
        new("Captioned clip command uses ASS and preserves the full audio mix", CaptionedCommandPreservesAudioMix),
        new("ASS subtitle builder supports five deterministic styles", AssBuilderSupportsFiveEffects),
        new("ASS subtitle position uses a bounded vertical percentage", AssBuilderPositionsCaptions),
        new("Caption presentation defaults preserve legacy rendering", CaptionPresentationDefaultsRemainCompatible),
        new("Caption presentation matches Studio preview and final ASS", CaptionPresentationMatchesPreviewAndRender),
        new("Timed captions appear around speech and clear during silence", TimedCaptionsClearDuringSilence),
        new("Punchy captions follow exact words and internal pauses", PunchyCaptionsFollowWordsAndInternalPauses),
        new("Caption presentation persists through Studio and caption edits", CaptionPresentationPersistsThroughStudioEdits),
        new("Caption corrections replace text without fabricating word timing", CaptionCorrectionsPreserveTruthfulTiming),
        new("Editorial caption transcripts stay bounded with timed spans", EditorialCaptionTranscriptsStayBounded),
        new("Editorial transcript cuts stay identical across caption routes", EditorialTranscriptCutsStayIdenticalAcrossRoutes),
        new("Editorial transcripts support Studio cuts beyond the caption window", EditorialTranscriptsSupportExtendedStudioCuts),
        new("Invalid Whisper word timing falls back to truthful phrase timing", InvalidWhisperWordsFallBackToPhraseTiming),
        new("One invalid Whisper word stays inside its measured speech run", PartiallyInvalidWhisperTimingStaysInsideSpeechRun),
        new("Caption source language is explicit and options remain immutable", CaptionLanguagePolicyIsExplicit),
        new("Reopened caption preparation uses the exact retained source window", RetainedCaptionPreparationUsesExactWindow),
        new("Source transcription reuses bounded chunks and invalidates changed vocabulary or media", SourceTranscriptCacheReusesBoundedChunks),
        new("Semantic review receives reliable transcript text only inside its cut", SemanticTranscriptContextRespectsCut),
        new("Approximate transcript chunks recover clipped speech with an exact-cut retry", ApproximateTranscriptRecoversExactCut),
        new("Caption cache misses transcribe only the exact cut and memoize it", CaptionCacheMissReadsOnlyExactCut),
        new("Cut captions agree in preview and exports while retaining source text", CutCaptionsAgreeAcrossPreviewAndExports),
        new("Initial portrait captions avoid confirmed presenter regions while preserving saved looks", InitialPortraitCaptionsAvoidConfirmedPresenter),
        new("Manual clips reach unselected sources and persist across reopening", ManualClipCanReachUnselectedSourceAndPersist),
        new("Repetitive low-information captions are suppressed without losing provenance", RepetitiveCaptionsAreSuppressed),
        new("Bracketed non-speech tokens stay in provenance but never render", NonSpeechTokensDoNotRender),
        new("Low-probability sparse whisper segments stay in provenance but never render", LowConfidenceSparseSegmentsDoNotRender),
        new("Whisper aggregate captions defer to their tighter fragments without losing provenance", AggregateWhisperCaptionDefersToFragments),
        new("Suppressed captions never enter the Studio render handoff", SuppressedCaptionsDoNotReachStudio),
        new("Studio video treatments map to bounded FFmpeg filters", VideoTreatmentsMapToFilters),
        new("Studio graphic overlays are immutable and validated", GraphicOverlayContractsAreValidated),
        new("Studio graphic overlays enter one bounded FFmpeg graph", GraphicOverlaysEnterRenderGraph),
        new("Studio graphic editor persists through the output boundary", GraphicEditorPersistsOverlay),
        new("Studio final render writes one ASS script per selected clip and cleans it", CaptionRenderingUsesOwnedScripts),
        new("Studio clip boundaries retain exactly one minute of edit context", StudioBoundaryContextIsBounded),
        new("Studio preview context covers the complete one-minute trim envelope", StudioPreviewContextCoversEdits),
        new("Studio preview cache ignores live-only caption styling", StudioPreviewCacheSeparatesLiveCaptionStyling),
        new("Studio CC toggle hides the actual caption overlay", StudioCaptionToggleHidesActualOverlay),
        new("Studio project switching protects every manual-save draft", StudioProjectSwitchProtectsManualSaveDrafts),
        new("Studio project switching preserves simultaneous manual drafts", StudioProjectSwitchPreservesSimultaneousDrafts),
        new("Studio applies one caption look to every captioned clip atomically", StudioCaptionLookAppliesToAll),
        new("Studio output replacement preserves project identity", StudioReplacementPreservesProject),
        new("Repeated identical generation results receive distinct project identities", RepeatedGenerationResultsHaveDistinctProjectIdentity),
        new("Montage concatenation does not re-encode video", ConcatenationCopiesStreams),
        new("Montage timestamp normalization retains bounded fractional source clocks", MontageClockCommandsAreBounded),
        new("A successful montage copy that fails media inspection is normalized and revalidated once", MontageValidationFailureRecoversOnce),
        new("Failed or cancelled montage copying never starts an extra encoder", MontageCopyFailureAndCancellationDoNotRecover),
        new("Library thumbnail command samples one bounded rendered frame", ThumbnailCommandIsBounded),
        new("Individual rendering commits complete files atomically", IndividualRenderingCommitsAtomically),
        new("Individual output filenames use the saved audience title", IndividualOutputFilenameUsesSavedTitle),
        new("Accepted completed rendering releases ownership and preserves output", CompletedRenderAcceptReleasesOwnership),
        new("Discarded completed rendering removes its owned output and permits retry", CompletedRenderDiscardEnablesRetry),
        new("Montage renders each segment once then joins once", MontageRendersOncePerSegment),
        new("Rendering failure removes the owned staging directory", FailureRemovesStaging),
        new("Rendering cancellation removes the owned staging directory", CancellationRemovesStaging),
        new("Generation pipeline finds moments without rendering clips", PipelineFindsThenRenders),
        new("Generation pipeline publishes one validated workspace handoff", PipelinePublishesWorkspaceHandoff),
        new("Generation pipeline does not render an empty moment result", PipelineRejectsNoMoments),
    ];

}
