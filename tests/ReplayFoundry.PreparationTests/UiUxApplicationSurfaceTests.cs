using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using ReplayFoundry.Desktop;
using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Library.Sections;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Sections;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Browser;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Presentation.Controls;
using ReplayFoundry.Desktop.Presentation.Converters;
using ReplayFoundry.Desktop.Presentation.Feedback;
using ReplayFoundry.Desktop.Presentation.Accessibility;
using ReplayFoundry.Desktop.Presentation.Workspaces;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Shell;
using ReplayFoundry.Desktop.Shell.Guidance;
using ReplayFoundry.Desktop.Shell.Navigation;
using ReplayFoundry.Desktop.Shell.Windowing;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        .. MenuConsolidationTests(),
        new("Preview ratings follow the selected clip without implicit feedback", PreviewFeedbackTracksCurrentClip),
        new("Manual range gestures undo atomically, cancel and clamp without resizing", ManualRangeEditsAreReversible),
        new("Manual shortcuts step actual source frames and preserve range length", ManualShortcutsUseSourceFrames),
        new("Effect comparison leaves the saved draft intact and resets only video effects", EffectComparisonPreservesDraft),
        new("Effect comparison requests original video and survives autosave without changing exports", EffectComparisonChangesPreviewMedia),
        new("Custom clips start writing with their own range and persist the result", CustomClipsStartWriting),
        new("Moment categories survive project saves without inventing legacy tags", MomentTagsSurviveProjects),
        new("Suggested moment previews preserve trim until explicitly applied", SuggestedMomentsDoNotChangeTrim),
        new("Hidden moment filters preserve independent review state", HiddenMomentFiltersPreserveReviewState),
        new("Timeline marker groups retain every candidate and honor zoom bounds", TimelineMarkerGroupsPreserveCandidates),
        new("Waveforms expose bounded pointer and accessible range seeking", WaveformSeekingSupportsAccessibility),
        new("Face suggestions reject ambiguity and expire with the preview frame", FaceSuggestionsAreExplicitAndExpire),
        new("Manual timeline preserves the cut while browsing and clamps zoom", ManualTimelineKeepsSelection),
        new("Manual workspace replaces Studio and fits a compact window", ManualWorkspaceReplacesStudio),
        new("Recent projects scroll only the actual card overflow in pixels", RecentProjectsScrollInPixels),
        new("Foundry loading animation follows visibility and activity", FoundryLoadingMotionLifecycle),
        new("Timeline filmstrip caches source-timed frames and invalidates changed files", TimelineFilmstripCachesRealFrames),
        new("Closing the clip maker cancels and drains thumbnail extraction", TimelineFilmstripDrainsCancelledWork),
        new("Caption hover samples use the real effect engine and stop offscreen", CaptionEffectSamplesFollowRealEffects),
        new("Shell registers all five explicit instances", ShellRegistersAllInstances),
        new("Application composition disposes its editorial provider", ApplicationCompositionDisposesEditorialProvider),
        new("Shell defaults to Generate", ShellDefaultsToGenerate),
        new("Shell navigation changes both projections", ShellNavigationChangesBothProjections),
        new("Shell chrome projects the active workspace identity", ShellChromeProjectsActiveWorkspaceIdentity),
        new("Shell preserves each workspace instance", ShellPreservesWorkspaceInstances),
        new("Shell rejects unknown destinations explicitly", ShellRejectsUnknownDestination),
        new("Implicit templates cover all five workspaces", ImplicitTemplatesCoverAllWorkspaces),
        new("Studio selected tool changes its empty projection", StudioSelectionProjectsEmptyState),
        new("Studio selected inspector changes its empty projection", StudioInspectorProjectsEmptyState),
        new("Studio project chrome uses one clear editing hierarchy", StudioProjectChromeIsCoherent),
        new("Studio CC control targets actual caption content", StudioCaptionControlTargetsActualContent),
        new("Studio render readiness opens the blocking Inspector section", StudioRenderReadinessIsActionable),
        new("Studio browser exposes clips and graphics only", StudioVisualEditingMapsUpdate),
        new("Studio browser cards preserve readable clip identities", StudioBrowserCardsPreserveReadableIdentity),
        new("Studio omits the redundant static layer dump", StudioOmitsStaticLayerDump),
        new("Studio saves boundary drafts without rendering", StudioAppliesBoundaryDraft),
        new("Studio final render snapshots visible edits before Library handoff", StudioFinalRenderSnapshotsVisibleEdits),
        new("Studio render queue starts empty and preserves retained clips", StudioRenderQueueStartsEmptyAndPreservesClips),
        new("Studio browser inclusion persists decisions and research feedback", StudioBrowserInclusionPersistsFeedback),
        new("Studio render queue finalizes exactly its queued subset", StudioRenderQueueFinalizesExactSubset),
        new("Studio render copies keep the draft editable and follow Library removal", StudioRenderCopiesRemainEditableAndLibraryAware),
        new("Studio discards completed output when Library commit fails", StudioRenderCommitFailureRollsBackAndDiscardsOutput),
        new("Studio render queue does not autoqueue accepted Hidden Moments", StudioHiddenMomentDoesNotAutoqueue),
        new("Studio browser actions preserve a different clip's pending edit", StudioBrowserActionPreservesPendingEdit),
        new("Studio project switching commits pending clip appearance and metadata edits", StudioProjectSwitchCommitsPendingEdits),
        new("Studio project switching protects a prepared render queue", StudioProjectSwitchBlocksQueuedDraft),
        new("Studio project switching blocks while a render is active", StudioProjectSwitchBlocksActiveRender),
        new("Studio mutation commands lock while a render is active", StudioMutationCommandsLockDuringRender),
        new("Studio render refuses to overwrite a newer project version", StudioRenderPreservesConcurrentProjectMutation),
        new("Studio render cancellation and failure preserve the queue", StudioRenderFailurePreservesQueue),
        new("Studio Add saves the exact visible metadata before queueing", StudioQueueCommitsVisibleMetadata),
        new("Studio metadata review state never gates queue, render, or handoff", StudioMetadataReadinessDoesNotGateWorkflow),
        new("Studio browser explains source, rank, and selection in plain language", StudioBrowserExplainsClipSelectionPlainly),
        new("Studio pending trims queue the visible cut without publish metadata", StudioPendingTrimRevalidatesMetadata),
        new("Studio invalid trim drafts cannot queue the previously saved cut", StudioInvalidTrimCannotQueueOldCut),
        new("Studio preview rejects stale ticks after a user seek", StudioPreviewRejectsStaleSeekTicks),
        new("Studio trim clamps synchronize the native player before resuming", StudioPreviewTrimClampSynchronizesNativePlayer),
        new("Studio playback recovers when Slider consumes scrub release", StudioPreviewRecoversConsumedScrubRelease),
        new("Studio playback clock advances when native MediaElement time stalls", StudioPreviewClockAdvancesWhenNativePositionStalls),
        new("Studio caption appearance edits preserve the preview playhead", StudioCaptionAppearanceEditsPreservePlayhead),
        new("Studio clip selection resets the preview to the selected start", StudioClipSelectionResetsPreviewPlayhead),
        new("Studio clip switching rejects events from superseded media sessions", StudioPreviewClipSwitchRejectsSupersededEvents),
        new("Studio manual preview reload replaces a reused native media session", StudioPreviewManualReloadStartsNewSession),
        new("Studio native preview retry retires the failed media graph", StudioPreviewRetryRetiresFailedGraph),
        new("Studio ignores a delayed end event from a retired media graph", StudioPreviewRejectsRetiredGraphEnd),
        new("Studio preview synchronizes pre-roll before playback and captions advance", StudioPreviewSynchronizesPrerollBeforePlayback),
        new("Studio MediaOpened does not reset the native media source", StudioPreviewMediaOpenedDoesNotResetSource),
        new("Studio preview reports a seek that never converges", StudioPreviewReportsNeverConvergingSeek),
        new("Studio time labels use whole seconds", StudioTimeLabelsUseWholeSeconds),
        new("Studio read-only render progress binds one way", StudioReadOnlyRenderProgressBindsOneWay),
        new("Studio Hidden Moments overlay binds to its own open state", StudioHiddenMomentsVisibilityUsesChildDataContext),
        new("Studio alternate-moment previews render only the review selection", StudioHiddenMomentPreviewUsesExactSelection),
        new("Hidden Moments browsing is neutral and position-aware", StudioHiddenMomentNavigationIsNeutral),
        new("Studio accepts a reopened captioned alternate exactly once and advances", StudioAcceptsReopenedCaptionedAlternate),
        new("Long Hidden Moments AI work reports truthful elapsed liveness", StudioHiddenMomentLongMetadataShowsElapsedLiveness),
        new("Closing Hidden Moments keeps queued preparation running", StudioHiddenMomentCloseCancelsCleanly),
        new("Canceling at Hidden Moments commit never appends the clip", StudioHiddenMomentCommitStageCancellationWins),
        new("An accepted decision cannot hide an uncommitted alternate", StudioAcceptedDecisionDoesNotHideUncommittedAlternate),
        new("Hidden Moments keeps a committed clip when decision storage fails", StudioHiddenMomentDecisionFailureKeepsCommittedClip),
        new("Studio warms the first alternate only after its primary preview settles", StudioFirstAlternateWarmupWaitsForPrimarySynchronization),
        new("Studio alternate review prepares two successors before navigation", StudioAlternateReviewWarmsOnlyNextMoment),
        new("Studio cancels alternate warmup when the project changes", StudioAlternateWarmupCancelsOnProjectChange),
        new("Studio preview defers the initial seek until MediaElement is open", StudioPreviewDefersInitialSeek),
        new("Studio preview opens the exact cut and lazily obtains trim context", StudioPreviewLoadsExactCutBeforeTrimContext),
        new("Studio preview honors a trim that moves during initial loading", StudioPreviewHandlesTrimDuringInitialLoad),
        new("Studio preview stops on insufficient provider coverage", StudioPreviewRejectsInsufficientProviderCoverage),
        new("Studio preview cache retains every active playback lease", StudioPreviewCacheRetainsActiveLeases),
        new("Studio preview detaches old media before releasing its cache lease", StudioPreviewReleasesAfterMediaPathNotification),
        new("Studio project commands stay disabled without a project", StudioProjectCommandsStayDisabled),
        new("Empty workspaces preserve their useful anatomy", EmptyWorkspacesPreserveAnatomy),
        new("Only Studio finalization activates Library and Publish", GeneratedOutputActivatesDownstreamWorkspaces),
        new("All workspace surface states are exclusive", WorkspaceStatesAreExclusive),
        new("Library category and filter projections update", LibraryDerivedStateWorks),
        new("Library clear filters updates CanExecute", LibraryClearFiltersUpdatesCanExecute),
        new("Library grid and list state stays in memory", LibraryViewModeWorks),
        new("Library runtime collection remains empty", LibraryRuntimeCollectionRemainsEmpty),
        new("Library organization selector renders friendly labels", LibraryOrganizationSelectorRendersLabel),
        new("Library grid virtualizes grouped card rows", LibraryGridUsesSupportedGroupedVirtualization),
        new("Library details retain an internal scroll boundary", LibraryDetailsOwnTheirScrollBoundary),
        new("Generate failure details disclose cleanly", GenerateFailureDetailsDiscloseCleanly),
        new("Library populated details bind read-only metadata one way", LibraryPopulatedDetailsBindOneWay),
        new("Library and Publish share whole-second media time", WorkspaceMediaTimeFormattingIsConsistent),
        new("Library thumbnails load without URI cache state", LibraryThumbnailConverterLoadsStream),
        new("Corrupt local thumbnails cannot crash the UI dispatcher", CorruptLocalThumbnailIsNonfatal),
        new("Publish exposes one explicit YouTube destination", PublishSelectionWorks),
        new("Publish opens on a complete local planning calendar", PublishCalendarDefaultsWork),
        new("Publish calendar view and platform filters stay in memory", PublishCalendarFilteringWorks),
        new("Publish Library filters finished videos by date and folder", PublishLibraryOrganizationWorks),
        new("Publish Library drag starts only from an asset card", PublishLibraryDragStartsOnlyFromAssetCard),
        new("Publish drag preview spans the planner and calendar targets expose explicit feedback", PublishPlannerDragFeedbackIsExplicit),
        new("Publish review uses themed media and scheduling controls", PublishReviewControlsAreThemedAndContinuous),
        new("Publish scheduling templates preserve their calendar grid and conditional watermark", PublishSchedulingTemplatesRetainRequiredShape),
        new("Publish checkboxes use a centered DPI-stable glyph", PublishCheckboxGlyphIsCenteredAndDpiStable),
        new("Publish manual preview primes media after assigning its source", PublishManualPreviewPrimesAssignedSource),
        new("Publish metadata counts and validation update", PublishMetadataValidationWorks),
        new("Publish output draft updates its readiness checklist", PublishOutputDraftUpdatesChecklist),
        new("Publish read-only status bindings remain one way", PublishReadOnlyStatusBindingsAreOneWay),
        new("Publish export and publish stay disabled", PublishCommandsStayDisabled),
        new("Publish queue and history remain empty", PublishRuntimeCollectionsRemainEmpty),
        new("Settings reaches every section", SettingsReachesEverySection),
        new("Compact Settings keeps all sections visible and preserves selection across resize", CompactSettingsNavigationPreservesSelection),
        new("Settings exposes only functional sections", SettingsShowsOnlyFunctionalSections),
        new("Settings preview does not invent installed capabilities", SettingsPreviewDoesNotInventCapabilities),
        new("Shell service icons report Local AI and Wikidata state", ShellServiceIconsReportTruthfulState),
        new("Shell AI indicator lights for every usable capability combination", ShellAiIndicatorCapabilityMatrix),
        new("Publish initialization stays offline while YouTube is disabled", PublishInitializationRespectsPermission),
        new("Disposing Publish cancels active YouTube initialization", PublishDisposalCancelsInitialization),
        new("Stopping Publish waits for its complete view-model workflow", PublishStopWaitsForViewModelWorkflow),
        new("Stopping Publish waits for async command finalization", PublishStopWaitsForCommandFinalization),
        new("Research contribution is opt-out by default", ResearchContributionDefaultsOff),
        new("Research contribution records only pseudonymous typed feedback", ResearchContributionIsPseudonymous),
        new("Research contribution consent and deletion persist locally", ResearchContributionPersistsAndDeletes),
        new("Bug reports remain local and opt-out by default", BugReportsDefaultOffline),
        new("Desktop builds embed the fixed official report endpoint", BugReportOfficialEndpointIsEmbedded),
        new("Bug report diagnostics redact local paths and tokens", BugReportDiagnosticsAreSanitized),
        new("Bug report delivery requires consent and an explicit send", BugReportDeliveryRequiresConsentAndExplicitSend),
        new("Bug report transport timeout remains a retryable local failure", BugReportTimeoutRemainsLocal),
        new("Bug report outbox verifies attachments and tolerates corrupt entries", BugReportOutboxVerifiesAttachments),
        new("Bug report outbox item template binds read-only projections one way", BugReportOutboxTemplateBindsReadOnlyProjectionsOneWay),
        new("Crash capture is local, bounded, and best effort", CrashCaptureIsBestEffort),
        new("Local cache cleanup preserves projects, Library records, outputs, and runtime packs", LocalCacheCleanupPreservesDurableData),
        new("Local data reset is scheduled and honors explicit destructive choices", LocalDataResetIsScheduledAndScoped),
        new("Game context recovery follows the preference reset boundary", GameContextRecoveryFollowsPreferenceReset),
        new("Publish destination glyphs are semantic", PublishDestinationGlyphsAreSemantic),
        new("UI-03 theme resources expose expected control families", Ui03ThemeResourcesLoad),
        new("Shared text styles resolve and Publish history labels stay readable", SharedTextStylesResolveAndPublishHistoryLabelsStayReadable),
        new("UI-03 icon shape resolves semantic geometry", Ui03IconShapeResolves),
        new("Custom shell chrome preserves native window semantics", CustomShellChromeIsConfigured),
        new("Shell AI and contextual game lookup status stay compact and truthful", ShellAndLibraryStatusIconsAreCompact),
        new("Shared chrome and selection controls stay visually aligned", SharedChromeAndSelectionControlsStayAligned),
        new("Drop-down fields open from the complete control surface", DropDownFieldsUseCompleteHitTarget),
        new("Kinetic canvas controls preserve semantic interaction", KineticCanvasControlsPreserveSemanticInteraction),
        new("Video previews restart from zero after reaching the end", VideoPreviewsRestartFromZeroAfterEnd),
        new("Kinetic canvas surfaces reuse shared styles", KineticCanvasSurfacesReuseSharedStyles),
        new("Studio clip cards select from their complete surface and preserve nested actions", StudioClipCardsUseCompleteHitSurface),
        new("Settings navigation stays aligned while section content scrolls", SettingsNavigationStaysAlignedWhileContentScrolls),
        new("Studio caption script uses the shared card editor", StudioCaptionScriptUsesSharedEditor),
        new("UI-04 shell starts maximized without consuming the taskbar", Ui04StartupPolicyIsExplicit),
        new("UI-04 caption controls remain in native chrome hit testing", Ui04CaptionHitTestingIsExplicit),
        new("UI-04 work area bounds preserve taskbar and monitor offsets", Ui04WorkAreaBoundsPreserveWorkArea),
        new("UI-04 maximized shell preserves every auto-hidden taskbar edge", Ui04AutoHideTaskbarEdgeRemainsReachable),
        new("UI-04 off-screen recovery centers a usable restore rectangle", Ui04OffScreenRecoveryCentersRestoreBounds),
        new("UI-04 off-screen shell recovers when it becomes visible", Ui04OffScreenShellRecoversOnLoad),
        new("UI-04 does not advertise an unconsumed text-scale subsystem", Ui04AvoidsFalseTextScaleState),
        new("High contrast replaces and restores semantic brushes", HighContrastPaletteReplacesSemanticBrushes),
        new("Responsive layouts instantiate one expensive feature tree", ResponsiveLayoutsAvoidDuplicateFeatureTrees),
        new("Hidden Moments stacks and contains keyboard focus", HiddenMomentsReflowsAndContainsFocus),
        new("UI-04 guidance surfaces are searchable and reopenable", Ui04GuidanceSurfacesAreSearchable),
        new("UI-04 issue references are stable and human readable", Ui04IssueReferencesAreStable),
        new("UI-04 target and motion resources are accessible by default", Ui04AccessibilityResourcesArePresent),
        new("Workspace continuation cue tracks remaining scroll content", WorkspaceContinuationCueTracksScrollExtent),
        new("Priority Moment marks occupy the full timeline track", PriorityMomentMarksOccupyTimelineTrack),
        new("Responsive roots expose compact standard and wide states", ResponsiveBreakpointsWork),
        new("Setup step navigation opens the next step at its beginning", SetupStepNavigationResetsScroll),
        new("Layout review stacks within a compact dialog and keeps its footer visible", CompositionReviewFitsCompactDialog),
        new("New workspace views and resources instantiate", ViewsInstantiateWithAppResources),
    ];

    private static Task PublishReadOnlyStatusBindingsAreOneWay()
    {
        RunOnSta(() =>
        {
            var view = new PublishChecklistView();
            Binding? value = BindingOperations.GetBinding(
                view.UploadProgress,
                RangeBase.ValueProperty);
            TestAssert.Equal(
                BindingMode.OneWay,
                value?.Mode,
                "Read-only progress must never activate a TwoWay WPF binding.");
            Binding? details = BindingOperations.GetBinding(
                view.TechnicalDetailsTextBox,
                TextBox.TextProperty);
            TestAssert.Equal(
                BindingMode.OneWay,
                details?.Mode,
                "Read-only technical details must never activate a TwoWay WPF binding.");
        });

        return Task.CompletedTask;
    }

}
