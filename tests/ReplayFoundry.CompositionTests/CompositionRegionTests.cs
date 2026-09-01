using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.CompositionTests;

internal static class CompositionRegionTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Composition region separates role, behavior, and provenance", SeparatesRoleBehaviorAndProvenance),
        new("Composition region rejects undefined trait bits", RejectsUndefinedTraits),
        new("Composition region rejects Static plus Dynamic", RejectsContradictoryTraits),
        new("User-confirmed geometry requires certain confidence", RequiresCertainUserGeometry),
        new("User-confirmed role requires certain confidence", RequiresCertainUserRole),
        new("Composition confidence rejects non-finite and out-of-range values", RejectsInvalidConfidence),
        new("Automatic analyzer may retain an unknown role", AllowsAutomaticUnknownRole),
        new("Unavailable role is explicit and confidence-free", AllowsExplicitlyUnavailableRole),
        new("Unavailable provenance cannot describe a known role", RejectsUnavailableKnownRole),
        new("Available geometry requires provenance", RejectsUnavailableGeometry),
        new("Region warning cannot reference a different region", RejectsMismatchedWarning),
        new("Overlapping regions are valid within one layout", AllowsOverlappingRegions),
    ];

    private static void SeparatesRoleBehaviorAndProvenance()
    {
        var region = new CompositionRegion(
            " presenter ",
            new NormalizedRectangle(0.75, 0.65, 0.25, 0.35),
            CompositionRegionRole.Presenter,
            CompositionRegionTraits.Dynamic | CompositionRegionTraits.Occluding,
            geometryConfidence: CompositionConfidence.Certain,
            roleConfidence: new CompositionConfidence(0.8),
            geometrySource: CompositionValueSource.UserConfirmed,
            roleSource: CompositionValueSource.AutomaticAnalyzer);

        TestAssert.Equal("presenter", region.Id, "Region IDs should be normalized.");
        TestAssert.Equal(
            CompositionRegionRole.Presenter,
            region.Role,
            "Semantic role should be preserved.");
        TestAssert.True(
            region.Traits.HasFlag(CompositionRegionTraits.Occluding),
            "Behavior traits should be independent from the semantic role.");
        TestAssert.True(
            region.IsGeometryUserConfirmed,
            "Geometry provenance should remain independently queryable.");
        TestAssert.False(
            region.IsRoleUserConfirmed,
            "Role provenance should remain independently queryable.");
    }

    private static void RejectsUndefinedTraits()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new CompositionRegion(
                "region",
                NormalizedRectangle.FullFrame,
                CompositionRegionRole.Gameplay,
                (CompositionRegionTraits)(1 << 10),
                CompositionConfidence.None,
                CompositionConfidence.None,
                CompositionValueSource.DefaultAssumption,
                CompositionValueSource.DefaultAssumption),
            "Undefined trait bits should be rejected.");
    }

    private static void RejectsContradictoryTraits()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionRegion(
                "region",
                NormalizedRectangle.FullFrame,
                CompositionRegionRole.Gameplay,
                CompositionRegionTraits.Static | CompositionRegionTraits.Dynamic,
                CompositionConfidence.None,
                CompositionConfidence.None,
                CompositionValueSource.DefaultAssumption,
                CompositionValueSource.DefaultAssumption),
            "A region cannot be both static and dynamic.");
    }

    private static void RequiresCertainUserGeometry()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionRegion(
                "region",
                NormalizedRectangle.FullFrame,
                CompositionRegionRole.Gameplay,
                CompositionRegionTraits.Dynamic,
                new CompositionConfidence(0.99),
                CompositionConfidence.None,
                CompositionValueSource.UserConfirmed,
                CompositionValueSource.DefaultAssumption),
            "User-confirmed geometry should require confidence one.");
    }

    private static void RequiresCertainUserRole()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionRegion(
                "region",
                NormalizedRectangle.FullFrame,
                CompositionRegionRole.Presenter,
                CompositionRegionTraits.Dynamic,
                CompositionConfidence.None,
                new CompositionConfidence(0.99),
                CompositionValueSource.DefaultAssumption,
                CompositionValueSource.UserConfirmed),
            "User-confirmed roles should require confidence one.");
    }

    private static void RejectsInvalidConfidence()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new CompositionConfidence(double.NaN),
            "NaN confidence should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new CompositionConfidence(double.PositiveInfinity),
            "Infinite confidence should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new CompositionConfidence(-0.01),
            "Negative confidence should be rejected.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new CompositionConfidence(1.01),
            "Confidence greater than one should be rejected.");
    }

    private static void AllowsAutomaticUnknownRole()
    {
        var region = CompositionTestData.CreateAutomaticRegion();

        TestAssert.Equal(
            CompositionRegionRole.Unknown,
            region.Role,
            "Automatic analysis must be allowed to express uncertainty.");
        TestAssert.Equal(
            CompositionValueSource.AutomaticAnalyzer,
            region.RoleSource,
            "Unknown must not discard evidence provenance.");
    }

    private static void AllowsExplicitlyUnavailableRole()
    {
        var region = CompositionTestData.CreateUnknownRoleWithConfirmedGeometry();

        TestAssert.Equal(
            CompositionValueSource.NotAvailable,
            region.RoleSource,
            "Unavailable role provenance should be retained.");
        TestAssert.Equal(
            CompositionConfidence.None,
            region.RoleConfidence,
            "An unavailable role should carry no confidence.");
    }

    private static void RejectsUnavailableKnownRole()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionRegion(
                "region",
                NormalizedRectangle.FullFrame,
                CompositionRegionRole.Gameplay,
                CompositionRegionTraits.Dynamic,
                CompositionConfidence.Certain,
                CompositionConfidence.None,
                CompositionValueSource.UserConfirmed,
                CompositionValueSource.NotAvailable),
            "Known roles cannot use unavailable provenance.");
    }

    private static void RejectsUnavailableGeometry()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionRegion(
                "region",
                NormalizedRectangle.FullFrame,
                CompositionRegionRole.Unknown,
                CompositionRegionTraits.None,
                CompositionConfidence.None,
                CompositionConfidence.None,
                CompositionValueSource.NotAvailable,
                CompositionValueSource.NotAvailable),
            "Materialized geometry cannot have unavailable provenance.");
    }

    private static void RejectsMismatchedWarning()
    {
        var warning = new CompositionWarning(
            CompositionWarningCode.LowRoleConfidence,
            "Role confidence is low.",
            "different-region");

        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionRegion(
                "region",
                NormalizedRectangle.FullFrame,
                CompositionRegionRole.Unknown,
                CompositionRegionTraits.None,
                new CompositionConfidence(0.5),
                new CompositionConfidence(0.5),
                CompositionValueSource.AutomaticAnalyzer,
                CompositionValueSource.AutomaticAnalyzer,
                [warning]),
            "A region-level warning must reference that region.");
    }

    private static void AllowsOverlappingRegions()
    {
        var gameplay = CompositionTestData.CreateUserConfirmedRegion(
            "gameplay",
            CompositionRegionRole.Gameplay,
            new NormalizedRectangle(0, 0, 1, 1));
        var presenter = CompositionTestData.CreateUserConfirmedRegion(
            "presenter",
            CompositionRegionRole.Presenter,
            new NormalizedRectangle(0.75, 0.65, 0.25, 0.35));

        var interval = new CompositionLayoutInterval(
            TimeSpan.Zero,
            CompositionTestData.SourceDuration,
            [gameplay, presenter]);

        TestAssert.Equal(2, interval.Regions.Count, "Overlapping semantic regions are valid.");
        TestAssert.True(
            gameplay.Geometry.Intersects(presenter.Geometry),
            "The fixture should prove overlap was preserved.");
    }
}
