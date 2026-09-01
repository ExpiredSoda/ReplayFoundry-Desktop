using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.CompositionTests;

internal static partial class CompositionReviewTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Composition review request requires preparation",
            RequestRequiresPreparation),
        new(
            "Composition review requires exactly one plan per source",
            ResultRequiresEverySource),
        new(
            "Composition review preserves source order and identity",
            ResultPreservesOrderAndIdentity),
        new(
            "Composition review preserves an explicit non-first reference",
            ResultPreservesNonFirstReference),
        new(
            "Composition review rejects duplicate missing and foreign plans",
            ResultRejectsInvalidSourceSets),
        new(
            "Prepared source plan rejects path mismatch",
            SourcePlanRejectsPathMismatch),
        new(
            "Prepared source plan rejects duration mismatch",
            SourcePlanRejectsDurationMismatch),
        new(
            "Prepared source plan requires Gameplay",
            SourcePlanRequiresGameplay),
        new(
            "Composition review result snapshots plan order",
            ResultIsImmutableSnapshot),
        new(
            "Draft move remains inside normalized bounds",
            DraftMoveClampsToBounds),
        new(
            "Draft resize enforces minimum size and bounds",
            DraftResizeClampsSizeAndBounds),
        new(
            "Draft overlap remains valid",
            DraftOverlapRemainsValid),
        new(
            "Draft rejects undefined traits and Static plus Dynamic",
            DraftRejectsInvalidTraits),
        new(
            "Draft trait toggles keep Static and Dynamic independent",
            DraftTraitTogglesPreventConflict),
        new(
            "Draft edits invalidate source confirmation",
            DraftEditInvalidatesConfirmation),
        new(
            "Explicit full-frame confirmation creates a manual plan",
            FullFrameCreatesManualPlan),
        new(
            "Manual known and unknown roles preserve required provenance",
            RoleProvenanceIsCorrect),
        new(
            "Draft region identifiers are deterministic and unique",
            DraftIdsAreDeterministicAndUnique),
        new(
            "Manual plans allow no Presenter and several Presenters",
            PresenterCardinalityIsFlexible),
        new(
            "Source confirmation requires Gameplay",
            ConfirmationRequiresGameplay),
        new(
            "Completion requires every source to be confirmed",
            CompletionRequiresEverySource),
        new(
            "Batch copy is explicit and creates independent source plans",
            BatchCopyIsExplicitAndIndependent),
        new(
            "Editing one copied source does not mutate another",
            CopiedDraftsAreIndependent),
        new(
            "Prior results restore independent confirmed drafts",
            PriorResultRestoresIndependentDrafts),
        new(
            "Cancelling review does not mutate a prior result",
            CancelDoesNotMutatePriorResult),
        new(
            "Foreign prior review results are rejected",
            ForeignPriorResultIsRejected),
        new(
            "Completing edits creates a new immutable result",
            CompletingEditsCreatesNewResult),
    ];

}
