namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// Creates deterministic plans from explicit user choices or a documented full-frame default.
/// </summary>
public static class ManualCompositionPlanFactory
{
    public const string PlannerName = "ReplayFoundry.ManualCompositionPlanFactory";
    public const string PlannerVersion = "1.0";

    public static CompositionPlan CreateFullFrameGameplay(
        string sourcePath,
        TimeSpan sourceDuration,
        DateTimeOffset createdAtUtc)
    {
        const string regionId = "gameplay";
        var warning = new CompositionWarning(
            CompositionWarningCode.DefaultAssumptionApplied,
            "The entire effective display was assumed to be gameplay.",
            regionId);

        var region = new CompositionRegion(
            regionId,
            NormalizedRectangle.FullFrame,
            CompositionRegionRole.Gameplay,
            CompositionRegionTraits.Dynamic,
            geometryConfidence: CompositionConfidence.None,
            roleConfidence: CompositionConfidence.None,
            geometrySource: CompositionValueSource.DefaultAssumption,
            roleSource: CompositionValueSource.DefaultAssumption,
            warnings: [warning]);

        return new CompositionPlan(
            sourcePath,
            sourceDuration,
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
            [new CompositionLayoutInterval(TimeSpan.Zero, sourceDuration, [region])],
            CompositionCoverage.CreateManual(sourceDuration),
            CreateManifest(CompositionPlanOrigin.DefaultFullFrameGameplay, createdAtUtc),
            [warning]);
    }

    public static CompositionPlan CreateUserConfirmedSingleInterval(
        string sourcePath,
        TimeSpan sourceDuration,
        IEnumerable<CompositionRegion> regions,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var regionArray = regions.ToArray();

        foreach (var region in regionArray)
        {
            ArgumentNullException.ThrowIfNull(region);

            if (!region.IsGeometryUserConfirmed)
            {
                throw new ArgumentException(
                    $"Region '{region.Id}' geometry must be user-confirmed.",
                    nameof(regions));
            }

            var roleIsConfirmed = region.IsRoleUserConfirmed;
            var roleIsExplicitlyUnavailable =
                region.Role == CompositionRegionRole.Unknown &&
                region.RoleSource == CompositionValueSource.NotAvailable &&
                region.RoleConfidence == CompositionConfidence.None;

            if (!roleIsConfirmed && !roleIsExplicitlyUnavailable)
            {
                throw new ArgumentException(
                    $"Region '{region.Id}' role must be user-confirmed or explicitly unavailable.",
                    nameof(regions));
            }
        }

        return new CompositionPlan(
            sourcePath,
            sourceDuration,
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
            [new CompositionLayoutInterval(TimeSpan.Zero, sourceDuration, regionArray)],
            CompositionCoverage.CreateManual(sourceDuration),
            CreateManifest(CompositionPlanOrigin.Manual, createdAtUtc));
    }

    private static CompositionPlanManifest CreateManifest(
        CompositionPlanOrigin origin,
        DateTimeOffset createdAtUtc) =>
        new(
            CompositionPlan.CurrentSchemaVersion,
            CompositionPlan.CurrentCoordinateSpaceVersion,
            PlannerName,
            PlannerVersion,
            origin,
            createdAtUtc);
}
