using ReplayFoundry.Desktop.Features.Generate;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerateViewModelWorkflowTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Generate source selection preserves order reference display and duplicate validation",
            SourceSelectionPreservesOrderReferenceAndValidation),
        new(
            "Generate single-file picker adds exactly its selected source",
            SingleFilePickerAddsSource),
        new(
            "Generate multi-file picker preserves selected source order",
            MultipleFilePickerPreservesOrder),
        new(
            "Generate source selection raises the live projection notifications",
            SourceSelectionRaisesProjectionNotifications),
        new(
            "Generate ViewModel preserves the live binding and command surface",
            PreservesBindingAndCommandSurface),
        new(
            "Generate does not show setup before preparation finishes",
            DialogWaitsForPreparation),
        new(
            "Generate confirms media rights once for the exact source selection",
            MediaRightsConfirmationGatesExactSourceSelection),
        new(
            "Recent Generate projects open cached Studio drafts without restoring sources",
            RecentProjectsOpenCachedStudioDrafts),
        new(
            "Recent Generate projects require clear-all confirmation",
            RecentProjectsRequireClearConfirmation),
        new(
            "Generate enters the PreparingSources state",
            EntersPreparingState),
        new(
            "Cached preparation opens setup without flashing the progress screen",
            CachedPreparationDoesNotFlashProgress),
        new(
            "Generate disables source commands while preparing",
            DisablesSourceCommands),
        new(
            "Generate forwards preparation progress",
            ForwardsPreparationProgress),
        new(
            "Preparation cancellation does not open setup",
            CancellationSkipsSetup),
        new(
            "Preparation failure does not open setup",
            FailureSkipsSetup),
        new(
            "Successful preparation opens setup with the retained result",
            OpensSetupWithRetainedResult),
        new(
            "Cancelling setup retains prepared sources",
            SetupCancellationRetainsPreparation),
        new(
            "Reopening setup reuses prepared sources",
            ReopeningSetupReusesPreparation),
        new(
            "Source mutation invalidates preparation and setup",
            SourceMutationInvalidatesState),
        new(
            "Mode change clears setup but retains preparation",
            ModeChangeRetainsPreparation),
        new(
            "Staleness while setup is open blocks generation",
            StalenessAfterSetupBlocksGeneration),
        new(
            "Successful setup creates generation from retained preparation",
            SetupCreatesPreparedGenerationRequest),
        new(
            "Composition review opens after Setup and before generation",
            CompositionReviewOpensInSequence),
        new(
            "Cancelling composition review preserves preparation and setup",
            CompositionReviewCancellationPreservesState),
        new(
            "Completed composition review is passed into generation",
            CompletedCompositionReviewReachesGeneration),
        new(
            "Staleness after composition review invalidates all source state",
            StalenessAfterReviewInvalidatesAll),
        new(
            "Reviewing composition disables source editing without showing progress",
            ReviewingCompositionDisablesSourceEditing),
        new(
            "Cancelling reopened review preserves the prior composition result",
            ReopenedReviewCancellationPreservesPrior),
        new(
            "Disposing Generate cancels active preparation",
            DisposeCancelsPreparation),
        new(
            "Evidence analysis opens after layout review and before preflight",
            EvidenceOpensBeforePreflight),
        new(
            "Unsupported setup is rejected before evidence analysis",
            UnsupportedSetupSkipsEvidence),
        new(
            "Evidence cancellation preserves preparation setup and composition",
            EvidenceCancellationPreservesPriorState),
        new(
            "Evidence failure preserves retryable prior state",
            EvidenceFailurePreservesPriorState),
        new(
            "Stale evidence analysis invalidates every source-dependent state",
            EvidenceStalenessInvalidatesAll),
        new(
            "Successful evidence is passed into GenerationRequest",
            SuccessfulEvidenceReachesGeneration),
        new(
            "Analyzing evidence disables editing and exposes truthful progress",
            AnalyzingEvidenceDisablesEditing),
        new(
            "Mode and setup preference changes reuse compatible evidence",
            ModeAndSetupChangesReuseEvidence),
        new(
            "Semantically unchanged reopened layout reuses evidence",
            UnchangedLayoutReusesEvidence),
        new(
            "Changed layout reruns evidence analysis",
            ChangedLayoutRerunsEvidence),
        new(
            "Composition-review cancellation retains prior evidence",
            ReviewCancellationRetainsEvidence),
        new(
            "Source changes invalidate evidence with other source state",
            SourceChangeInvalidatesEvidence),
        new(
            "CancelActiveOperation cancels evidence analysis",
            CancelCommandCancelsEvidence),
        new(
            "Disposing Generate cancels active evidence analysis",
            DisposeCancelsEvidence),
        new(
            "Return to source selection is blocked during evidence analysis",
            ReturnIsBlockedDuringEvidence),
        new(
            "Generate disposal detaches focused state notifications",
            DisposalDetachesStateNotifications),
    ];

}
