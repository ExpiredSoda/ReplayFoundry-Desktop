using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.CompositionTests;

internal static partial class CompositionReviewTests
{
    private static void DraftMoveClampsToBounds()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        CompositionRegionDraftViewModel region =
            source.SelectedRegion!;

        region.SetGeometry(
            0.25,
            0.25,
            0.4,
            0.4);

        region.MoveBy(
            2,
            -2);

        TestAssert.Equal(
            0.6,
            region.X,
            "Move should clamp at the right edge.");

        TestAssert.Equal(
            0d,
            region.Y,
            "Move should clamp at the top edge.");
    }

    private static void DraftResizeClampsSizeAndBounds()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        CompositionRegionDraftViewModel region =
            source.SelectedRegion!;

        region.SetGeometry(
            0.8,
            0.8,
            0.15,
            0.15);

        region.ResizeBy(
            2,
            2);

        TestAssert.True(
            Math.Abs(
                region.Width -
                0.2) <
            0.0000001,
            "Resize should clamp to the right edge.");

        TestAssert.True(
            Math.Abs(
                region.Height -
                0.2) <
            0.0000001,
            "Resize should clamp to the bottom edge.");

        region.ResizeBy(
            -2,
            -2);

        TestAssert.True(
            Math.Abs(
                region.Width -
                CompositionRegionDraftViewModel.MinimumSize) <
            0.0000001,
            "Resize should preserve the minimum width.");

        TestAssert.True(
            Math.Abs(
                region.Height -
                CompositionRegionDraftViewModel.MinimumSize) <
            0.0000001,
            "Resize should preserve the minimum height.");
    }

    private static void DraftOverlapRemainsValid()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        source.SelectedRegion!.SetGeometry(
            0.1,
            0.1,
            0.6,
            0.6);

        CompositionRegionDraftViewModel presenter =
            source.AddRegion(
                CompositionRegionRole.Presenter);

        presenter.SetGeometry(
            0.4,
            0.4,
            0.4,
            0.4);

        TestAssert.True(
            source.Regions[0]
                .CreateRegion()
                .Geometry
                .Intersects(
                    presenter.CreateRegion().Geometry),
            "Overlapping drafts should remain valid.");
    }

    private static void DraftRejectsInvalidTraits()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        CompositionRegionDraftViewModel region =
            source.SelectedRegion!;

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () =>
                region.Traits =
                    (CompositionRegionTraits)(1 << 12),
            "Undefined trait bits must be rejected.");

        TestAssert.Throws<ArgumentException>(
            () =>
                region.Traits =
                    CompositionRegionTraits.Static |
                    CompositionRegionTraits.Dynamic,
            "Static and Dynamic cannot coexist.");
    }

    private static void DraftTraitTogglesPreventConflict()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        CompositionRegionDraftViewModel region =
            source.SelectedRegion!;

        region.IsStatic = true;

        TestAssert.True(
            region.IsStatic,
            "Static should be enabled.");

        TestAssert.False(
            region.IsDynamic,
            "Enabling Static should clear Dynamic.");

        region.IsDynamic = true;

        TestAssert.True(
            region.IsDynamic,
            "Dynamic should be enabled.");

        TestAssert.False(
            region.IsStatic,
            "Enabling Dynamic should clear Static.");
    }

    private static void DraftEditInvalidatesConfirmation()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        source.SelectedRegion!.SetGeometry(
            0.05,
            0.05,
            0.9,
            0.9);

        TestAssert.False(
            source.IsConfirmed,
            "Geometry edits should invalidate confirmation.");

        TestAssert.True(
            source.IsDirty,
            "Geometry edits should mark the source dirty.");
    }

    private static void FullFrameCreatesManualPlan()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        source.UseFullFrameGameplay();

        TestAssert.True(
            source.TryConfirm(
                CompositionTestData.CreatedAtUtc),
            "A restored source should be confirmable after explicit full-frame review.");

        CompositionPlan plan =
            source.ConfirmedPlan!.Plan;

        TestAssert.Equal(
            CompositionPlanOrigin.Manual,
            plan.Manifest.Origin,
            "Explicit review must create a manual plan.");

        TestAssert.Equal(
            1d,
            plan.Intervals[0].Regions[0]
                .Geometry.Width,
            "Full-frame review should retain complete width.");
    }

    private static void RoleProvenanceIsCorrect()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        source.SelectedRegion!.Role =
            CompositionRegionRole.Unknown;

        source.SelectedRegion.Traits =
            CompositionRegionTraits.None;

        CompositionRegionDraftViewModel knownDraft =
            source.AddRegion(
                CompositionRegionRole.Gameplay);

        TestAssert.True(
            source.TryConfirm(
                CompositionTestData.CreatedAtUtc),
            "Known and unknown roles should create a valid plan when Gameplay is present.");

        CompositionRegion unknown =
            source.ConfirmedPlan!.Plan
                .Intervals[0]
                .Regions
                .Single(
                    static region =>
                        region.Role ==
                        CompositionRegionRole.Unknown);

        CompositionRegion known =
            knownDraft.CreateRegion();

        TestAssert.Equal(
            CompositionValueSource.NotAvailable,
            unknown.RoleSource,
            "Unknown roles should use NotAvailable provenance.");

        TestAssert.Equal(
            CompositionConfidence.None,
            unknown.RoleConfidence,
            "Unknown roles should have no confidence.");

        TestAssert.Equal(
            CompositionValueSource.UserConfirmed,
            known.RoleSource,
            "Known roles should be user-confirmed.");

        TestAssert.Equal(
            CompositionConfidence.Certain,
            known.RoleConfidence,
            "Known roles should use certain confidence.");
    }

    private static void DraftIdsAreDeterministicAndUnique()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        CompositionRegionDraftViewModel first =
            source.AddRegion(
                CompositionRegionRole.Presenter);

        CompositionRegionDraftViewModel second =
            source.AddRegion(
                CompositionRegionRole.Presenter);

        TestAssert.Equal(
            "presenter",
            first.Id,
            "The first role ID should be deterministic.");

        TestAssert.Equal(
            "presenter-2",
            second.Id,
            "Repeated roles should receive deterministic unique suffixes.");
    }

    private static void PresenterCardinalityIsFlexible()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        source.UseFullFrameGameplay();

        TestAssert.True(
            source.TryConfirm(
                CompositionTestData.CreatedAtUtc),
            "Presenter should remain optional.");

        source.AddRegion(
            CompositionRegionRole.Presenter);

        source.AddRegion(
            CompositionRegionRole.Presenter);

        TestAssert.True(
            source.TryConfirm(
                CompositionTestData.CreatedAtUtc),
            "Several Presenter regions should remain valid.");

        TestAssert.Equal(
            2,
            source.ConfirmedPlan!.Plan
                .Intervals[0]
                .Regions
                .Count(
                    static region =>
                        region.Role ==
                        CompositionRegionRole.Presenter),
            "Both Presenter regions should reach the plan.");
    }

    private static void ConfirmationRequiresGameplay()
    {
        CompositionReviewSourceViewModel source =
            CreateConfirmedSource();

        source.RemoveSelectedRegion();

        source.AddRegion(
            CompositionRegionRole.Presenter);

        TestAssert.False(
            source.TryConfirm(
                CompositionTestData.CreatedAtUtc),
            "A presenter-only source must not confirm.");

        TestAssert.True(
            source.ValidationError?.Contains(
                "Gameplay",
                StringComparison.Ordinal) == true,
            "The validation error should explain the Gameplay requirement.");
    }

}
