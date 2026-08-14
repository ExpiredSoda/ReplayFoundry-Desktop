namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// Identifies the producer and schema used to create a composition plan.
/// </summary>
public sealed class CompositionPlanManifest
{
    public CompositionPlanManifest(
        string schemaVersion,
        string coordinateSpaceVersion,
        string plannerName,
        string plannerVersion,
        CompositionPlanOrigin origin,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            throw new ArgumentException("Schema version is required.", nameof(schemaVersion));
        }

        if (string.IsNullOrWhiteSpace(coordinateSpaceVersion))
        {
            throw new ArgumentException(
                "Coordinate-space version is required.",
                nameof(coordinateSpaceVersion));
        }

        if (string.IsNullOrWhiteSpace(plannerName))
        {
            throw new ArgumentException("Planner name is required.", nameof(plannerName));
        }

        if (string.IsNullOrWhiteSpace(plannerVersion))
        {
            throw new ArgumentException("Planner version is required.", nameof(plannerVersion));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Manifest creation time must use the UTC offset.",
                nameof(createdAtUtc));
        }

        SchemaVersion = schemaVersion.Trim();
        CoordinateSpaceVersion = coordinateSpaceVersion.Trim();
        PlannerName = plannerName.Trim();
        PlannerVersion = plannerVersion.Trim();
        Origin = origin;
        CreatedAtUtc = createdAtUtc;
    }

    public string SchemaVersion { get; }

    public string CoordinateSpaceVersion { get; }

    public string PlannerName { get; }

    public string PlannerVersion { get; }

    public CompositionPlanOrigin Origin { get; }

    public DateTimeOffset CreatedAtUtc { get; }
}
