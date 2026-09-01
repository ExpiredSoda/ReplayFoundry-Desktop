using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.CompositionTests;

internal static class CompositionPlanTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Layout intervals require positive time and at least one region", RequiresValidLayoutInterval),
        new("Layout interval region IDs are unique ignoring case", RequiresUniqueRegionIds),
        new("Layout intervals snapshot their region collection", SnapshotsRegions),
        new("Composition plan resolves exact contiguous boundaries", ResolvesContiguousBoundaries),
        new("Composition plan rejects any timeline gap", RejectsTimelineGap),
        new("Composition plan rejects any timeline overlap", RejectsTimelineOverlap),
        new("Composition plan must start at source zero", RequiresZeroStart),
        new("Composition plan must end at source duration", RequiresSourceEnd),
        new("Composition plan requires matching coverage duration", RequiresMatchingCoverageDuration),
        new("Composition plan requires a fully qualified source path", RequiresFullyQualifiedPath),
        new("Composition plan exposes gameplay and presenter presence", ExposesRolePresence),
        new("Composition manifest requires an actual UTC timestamp", RequiresUtcManifestTimestamp),
        new("Composition plan rejects unsupported contract versions", RejectsUnsupportedVersions),
        new("Composition plan origin must match coverage kind", RequiresCompatibleOriginAndCoverage),
        new("Plan warning must reference a known region", RejectsUnknownWarningRegion),
    ];

    private static void RequiresValidLayoutInterval()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new CompositionLayoutInterval(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                [region]),
            "Zero-duration intervals should be rejected.");
        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionLayoutInterval(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                []),
            "Intervals should require at least one region.");
    }

    private static void RequiresUniqueRegionIds()
    {
        var first = CompositionTestData.CreateUserConfirmedRegion("Presenter");
        var duplicate = CompositionTestData.CreateUserConfirmedRegion("presenter");

        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionLayoutInterval(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                [first, duplicate]),
            "Region IDs should be unique without case sensitivity.");
    }

    private static void SnapshotsRegions()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var sourceRegions = new List<CompositionRegion> { region };
        var interval = new CompositionLayoutInterval(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            sourceRegions);

        sourceRegions.Clear();

        TestAssert.Equal(
            1,
            interval.Regions.Count,
            "Mutating the caller collection must not mutate the interval.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<CompositionRegion>)interval.Regions).Add(region),
            "The exposed region collection should be read-only.");
    }

    private static void ResolvesContiguousBoundaries()
    {
        var firstRegion = CompositionTestData.CreateUserConfirmedRegion("first");
        var secondRegion = CompositionTestData.CreateUserConfirmedRegion("second");
        var boundary = TimeSpan.FromMinutes(4);
        var first = new CompositionLayoutInterval(
            TimeSpan.Zero,
            boundary,
            [firstRegion]);
        var second = new CompositionLayoutInterval(
            boundary,
            CompositionTestData.SourceDuration,
            [secondRegion]);

        var plan = CompositionTestData.CreateManualPlan([first, second]);

        TestAssert.Same(
            first,
            plan.GetLayoutAt(boundary - TimeSpan.FromTicks(1)),
            "Time before the boundary should resolve to the first interval.");
        TestAssert.Same(
            second,
            plan.GetLayoutAt(boundary),
            "The exact boundary should resolve to the second interval.");
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => plan.GetLayoutAt(CompositionTestData.SourceDuration),
            "The source end should be exclusive.");
    }

    private static void RejectsTimelineGap()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var firstEnd = TimeSpan.FromMinutes(4);
        var secondStart = firstEnd + TimeSpan.FromTicks(1);

        TestAssert.Throws<ArgumentException>(
            () => _ = CompositionTestData.CreateManualPlan(
            [
                new CompositionLayoutInterval(TimeSpan.Zero, firstEnd, [region]),
                new CompositionLayoutInterval(
                    secondStart,
                    CompositionTestData.SourceDuration,
                    [region]),
            ]),
            "Even a one-tick gap should be rejected.");
    }

    private static void RejectsTimelineOverlap()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var firstEnd = TimeSpan.FromMinutes(4);
        var secondStart = firstEnd - TimeSpan.FromTicks(1);

        TestAssert.Throws<ArgumentException>(
            () => _ = CompositionTestData.CreateManualPlan(
            [
                new CompositionLayoutInterval(TimeSpan.Zero, firstEnd, [region]),
                new CompositionLayoutInterval(
                    secondStart,
                    CompositionTestData.SourceDuration,
                    [region]),
            ]),
            "Even a one-tick overlap should be rejected.");
    }

    private static void RequiresZeroStart()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();

        TestAssert.Throws<ArgumentException>(
            () => _ = CompositionTestData.CreateManualPlan(
            [
                new CompositionLayoutInterval(
                    TimeSpan.FromTicks(1),
                    CompositionTestData.SourceDuration,
                    [region]),
            ]),
            "The first interval should begin at exact source zero.");
    }

    private static void RequiresSourceEnd()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();

        TestAssert.Throws<ArgumentException>(
            () => _ = CompositionTestData.CreateManualPlan(
            [
                new CompositionLayoutInterval(
                    TimeSpan.Zero,
                    CompositionTestData.SourceDuration - TimeSpan.FromTicks(1),
                    [region]),
            ]),
            "The final interval should end at the exact source duration.");
    }

    private static void RequiresMatchingCoverageDuration()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var interval = new CompositionLayoutInterval(
            TimeSpan.Zero,
            CompositionTestData.SourceDuration,
            [region]);

        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionPlan(
                CompositionTestData.SourcePath,
                CompositionTestData.SourceDuration,
                CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
                [interval],
                CompositionCoverage.CreateManual(
                    CompositionTestData.SourceDuration - TimeSpan.FromSeconds(1)),
                CompositionTestData.CreateManifest()),
            "Coverage duration should equal source duration.");
    }

    private static void RequiresFullyQualifiedPath()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var interval = new CompositionLayoutInterval(
            TimeSpan.Zero,
            CompositionTestData.SourceDuration,
            [region]);

        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionPlan(
                "relative.mp4",
                CompositionTestData.SourceDuration,
                CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
                [interval],
                CompositionCoverage.CreateManual(CompositionTestData.SourceDuration),
                CompositionTestData.CreateManifest()),
            "Relative source paths should be rejected.");
    }

    private static void ExposesRolePresence()
    {
        var gameplay = CompositionTestData.CreateUserConfirmedRegion();
        var gameplayOnly = CompositionTestData.CreateManualPlan(
        [
            new CompositionLayoutInterval(
                TimeSpan.Zero,
                CompositionTestData.SourceDuration,
                [gameplay]),
        ]);

        TestAssert.True(gameplayOnly.HasGameplay, "Gameplay should be discoverable.");
        TestAssert.False(gameplayOnly.HasPresenter, "Presenter should not be inferred.");

        var presenter = CompositionTestData.CreateUserConfirmedRegion(
            "presenter",
            CompositionRegionRole.Presenter);
        var withPresenter = CompositionTestData.CreateManualPlan(
        [
            new CompositionLayoutInterval(
                TimeSpan.Zero,
                CompositionTestData.SourceDuration,
                [gameplay, presenter]),
        ]);

        TestAssert.True(withPresenter.HasPresenter, "Presenter should be discoverable.");
    }

    private static void RequiresUtcManifestTimestamp()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionPlanManifest(
                CompositionPlan.CurrentSchemaVersion,
                CompositionPlan.CurrentCoordinateSpaceVersion,
                "ReplayFoundry.CompositionTests",
                "1.0",
                CompositionPlanOrigin.Manual,
                new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.FromHours(1))),
            "A non-zero timestamp offset must not be labeled as UTC.");
    }

    private static void RejectsUnsupportedVersions()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var interval = new CompositionLayoutInterval(
            TimeSpan.Zero,
            CompositionTestData.SourceDuration,
            [region]);

        TestAssert.Throws<ArgumentException>(
            () => _ = CreatePlan(
                interval,
                CompositionTestData.CreateManifest(schemaVersion: "2.0")),
            "Unknown schema versions should be rejected.");
        TestAssert.Throws<ArgumentException>(
            () => _ = CreatePlan(
                interval,
                CompositionTestData.CreateManifest(coordinateSpaceVersion: "2.0")),
            "Unknown coordinate-space versions should be rejected.");
    }

    private static void RequiresCompatibleOriginAndCoverage()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var interval = new CompositionLayoutInterval(
            TimeSpan.Zero,
            CompositionTestData.SourceDuration,
            [region]);

        TestAssert.Throws<ArgumentException>(
            () => _ = new CompositionPlan(
                CompositionTestData.SourcePath,
                CompositionTestData.SourceDuration,
                CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
                [interval],
                CompositionCoverage.CreateManual(CompositionTestData.SourceDuration),
                CompositionTestData.CreateManifest(CompositionPlanOrigin.AutomaticProposal)),
            "Automatic proposals should require sampled coverage.");
    }

    private static void RejectsUnknownWarningRegion()
    {
        var region = CompositionTestData.CreateUserConfirmedRegion();
        var interval = new CompositionLayoutInterval(
            TimeSpan.Zero,
            CompositionTestData.SourceDuration,
            [region]);
        var warning = new CompositionWarning(
            CompositionWarningCode.LowGeometryConfidence,
            "Geometry confidence is low.",
            "missing");

        TestAssert.Throws<ArgumentException>(
            () => _ = CompositionTestData.CreateManualPlan([interval], warnings: [warning]),
            "Plan warnings must refer to a region in the plan.");
    }

    private static CompositionPlan CreatePlan(
        CompositionLayoutInterval interval,
        CompositionPlanManifest manifest) =>
        new(
            CompositionTestData.SourcePath,
            CompositionTestData.SourceDuration,
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
            [interval],
            CompositionCoverage.CreateManual(CompositionTestData.SourceDuration),
            manifest);
}
