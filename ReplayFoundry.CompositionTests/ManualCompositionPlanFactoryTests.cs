using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.CompositionTests;

internal static class ManualCompositionPlanFactoryTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Default factory creates an explicit full-frame gameplay assumption", CreatesDefaultGameplayAssumption),
        new("Manual factory preserves fully user-confirmed regions", PreservesUserConfirmedRegions),
        new("Manual factory allows confirmed geometry with unknown role", AllowsUnknownRole),
        new("Manual factory rejects automatic proposals", RejectsAutomaticProposals),
    ];

    private static void CreatesDefaultGameplayAssumption()
    {
        var plan = ManualCompositionPlanFactory.CreateFullFrameGameplay(
            CompositionTestData.SourcePath,
            CompositionTestData.SourceDuration,
            CompositionTestData.CreatedAtUtc);
        var region = plan.Intervals[0].Regions[0];

        TestAssert.Equal(
            CompositionPlanOrigin.DefaultFullFrameGameplay,
            plan.Manifest.Origin,
            "The manifest should disclose the default origin.");
        TestAssert.Equal(
            CompositionCoverageKind.Manual,
            plan.Coverage.Kind,
            "A deterministic default should not claim sampled analysis.");
        TestAssert.Equal(
            CompositionRegionRole.Gameplay,
            region.Role,
            "The full frame should be identified as gameplay.");
        TestAssert.Equal(0d, region.Geometry.X, "Full-frame X.");
        TestAssert.Equal(0d, region.Geometry.Y, "Full-frame Y.");
        TestAssert.Equal(1d, region.Geometry.Width, "Full-frame width.");
        TestAssert.Equal(1d, region.Geometry.Height, "Full-frame height.");
        TestAssert.Equal(
            CompositionValueSource.DefaultAssumption,
            region.GeometrySource,
            "Default geometry must not masquerade as user confirmation.");
        TestAssert.Equal(
            CompositionValueSource.DefaultAssumption,
            region.RoleSource,
            "Default role must not masquerade as user confirmation.");
        TestAssert.False(region.IsFullyUserConfirmed, "The default should remain an assumption.");
        TestAssert.Equal(1, plan.Warnings.Count, "The assumption should be visible as a warning.");
    }

    private static void PreservesUserConfirmedRegions()
    {
        var gameplay = CompositionTestData.CreateUserConfirmedRegion();
        var presenter = CompositionTestData.CreateUserConfirmedRegion(
            "presenter",
            CompositionRegionRole.Presenter,
            new NormalizedRectangle(0.75, 0.65, 0.25, 0.35));

        var plan = ManualCompositionPlanFactory.CreateUserConfirmedSingleInterval(
            CompositionTestData.SourcePath,
            CompositionTestData.SourceDuration,
            [gameplay, presenter],
            CompositionTestData.CreatedAtUtc);

        TestAssert.Equal(
            CompositionPlanOrigin.Manual,
            plan.Manifest.Origin,
            "The manifest should disclose a manual origin.");
        TestAssert.Equal(2, plan.Intervals[0].Regions.Count, "Both choices should be preserved.");
        TestAssert.Same(
            presenter,
            plan.Intervals[0].FindRegion("PRESENTER")!,
            "The factory should preserve the confirmed region snapshot.");
        TestAssert.True(plan.HasGameplay, "Gameplay should remain explicit.");
        TestAssert.True(plan.HasPresenter, "Presenter should remain explicit.");
    }

    private static void AllowsUnknownRole()
    {
        var region = CompositionTestData.CreateUnknownRoleWithConfirmedGeometry();

        var plan = ManualCompositionPlanFactory.CreateUserConfirmedSingleInterval(
            CompositionTestData.SourcePath,
            CompositionTestData.SourceDuration,
            [region],
            CompositionTestData.CreatedAtUtc);

        var result = plan.Intervals[0].Regions[0];
        TestAssert.True(
            result.IsGeometryUserConfirmed,
            "The user-confirmed boundary should be preserved.");
        TestAssert.Equal(
            CompositionRegionRole.Unknown,
            result.Role,
            "The factory should not invent a semantic role.");
        TestAssert.Equal(
            CompositionValueSource.NotAvailable,
            result.RoleSource,
            "Unavailable role evidence should remain explicit.");
    }

    private static void RejectsAutomaticProposals()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = ManualCompositionPlanFactory.CreateUserConfirmedSingleInterval(
                CompositionTestData.SourcePath,
                CompositionTestData.SourceDuration,
                [CompositionTestData.CreateAutomaticRegion()],
                CompositionTestData.CreatedAtUtc),
            "Manual construction should not accept an automatic proposal as confirmation.");
    }
}
