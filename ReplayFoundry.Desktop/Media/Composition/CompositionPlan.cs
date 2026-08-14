using System.IO;

namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// Immutable, source-relative description of composition regions across an entire recording.
/// </summary>
public sealed class CompositionPlan
{
    public const string CurrentSchemaVersion = "1.0";
    public const string CurrentCoordinateSpaceVersion = "1.0";

    public CompositionPlan(
        string sourcePath,
        TimeSpan sourceDuration,
        CompositionCoordinateSpace coordinateSpace,
        IEnumerable<CompositionLayoutInterval> intervals,
        CompositionCoverage coverage,
        CompositionPlanManifest manifest,
        IEnumerable<CompositionWarning>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        }

        var trimmedSourcePath = sourcePath.Trim();
        if (!Path.IsPathFullyQualified(trimmedSourcePath))
        {
            throw new ArgumentException(
                "Source path must be fully qualified.",
                nameof(sourcePath));
        }

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                "Source duration must be greater than zero.");
        }

        if (!Enum.IsDefined(coordinateSpace))
        {
            throw new ArgumentOutOfRangeException(nameof(coordinateSpace));
        }

        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(manifest);

        if (coverage.SourceDuration != sourceDuration)
        {
            throw new ArgumentException(
                "Coverage duration must match the plan source duration.",
                nameof(coverage));
        }

        if (!string.Equals(
                manifest.SchemaVersion,
                CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported composition schema version '{manifest.SchemaVersion}'.",
                nameof(manifest));
        }

        if (!string.Equals(
                manifest.CoordinateSpaceVersion,
                CurrentCoordinateSpaceVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported coordinate-space version '{manifest.CoordinateSpaceVersion}'.",
                nameof(manifest));
        }

        ValidateOriginAndCoverage(manifest.Origin, coverage.Kind);

        var intervalArray = intervals.ToArray();
        if (intervalArray.Length == 0)
        {
            throw new ArgumentException(
                "At least one layout interval is required.",
                nameof(intervals));
        }

        if (intervalArray.Any(static interval => interval is null))
        {
            throw new ArgumentException(
                "Layout intervals cannot contain null values.",
                nameof(intervals));
        }

        if (intervalArray[0].Start != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The first layout interval must start at zero.",
                nameof(intervals));
        }

        for (var index = 0; index < intervalArray.Length; index++)
        {
            var interval = intervalArray[index];
            if (interval.End > sourceDuration)
            {
                throw new ArgumentException(
                    "Layout intervals must remain within the source duration.",
                    nameof(intervals));
            }

            if (index == 0)
            {
                continue;
            }

            var previous = intervalArray[index - 1];
            if (interval.Start > previous.End)
            {
                throw new ArgumentException(
                    "Layout intervals must cover the timeline without gaps.",
                    nameof(intervals));
            }

            if (interval.Start < previous.End)
            {
                throw new ArgumentException(
                    "Layout intervals cannot overlap.",
                    nameof(intervals));
            }
        }

        if (intervalArray[^1].End != sourceDuration)
        {
            throw new ArgumentException(
                "The final layout interval must end at the source duration.",
                nameof(intervals));
        }

        var warningArray = (warnings ?? []).ToArray();
        if (warningArray.Any(static warning => warning is null))
        {
            throw new ArgumentException(
                "Plan warnings cannot contain null values.",
                nameof(warnings));
        }

        var regionIds = intervalArray
            .SelectMany(static interval => interval.Regions)
            .Select(static region => region.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var warning in warningArray)
        {
            if (warning.RegionId is not null && !regionIds.Contains(warning.RegionId))
            {
                throw new ArgumentException(
                    $"Plan warning references unknown region '{warning.RegionId}'.",
                    nameof(warnings));
            }
        }

        SourcePath = trimmedSourcePath;
        SourceDuration = sourceDuration;
        CoordinateSpace = coordinateSpace;
        Intervals = Array.AsReadOnly(intervalArray);
        Coverage = coverage;
        Manifest = manifest;
        Warnings = Array.AsReadOnly(warningArray);
    }

    public string SourcePath { get; }

    public TimeSpan SourceDuration { get; }

    public CompositionCoordinateSpace CoordinateSpace { get; }

    public IReadOnlyList<CompositionLayoutInterval> Intervals { get; }

    public CompositionCoverage Coverage { get; }

    public CompositionPlanManifest Manifest { get; }

    public IReadOnlyList<CompositionWarning> Warnings { get; }

    public bool HasGameplay =>
        Intervals.Any(static interval =>
            interval.Regions.Any(static region => region.Role == CompositionRegionRole.Gameplay));

    public bool HasPresenter =>
        Intervals.Any(static interval =>
            interval.Regions.Any(static region => region.Role == CompositionRegionRole.Presenter));

    public CompositionLayoutInterval GetLayoutAt(TimeSpan position)
    {
        if (position < TimeSpan.Zero || position >= SourceDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "Position must be within the source timeline.");
        }

        return Intervals.Single(interval => interval.Contains(position));
    }

    private static void ValidateOriginAndCoverage(
        CompositionPlanOrigin origin,
        CompositionCoverageKind coverageKind)
    {
        var valid = origin switch
        {
            CompositionPlanOrigin.Manual or CompositionPlanOrigin.DefaultFullFrameGameplay =>
                coverageKind == CompositionCoverageKind.Manual,
            CompositionPlanOrigin.RecordingProfile =>
                coverageKind == CompositionCoverageKind.RecordingProfile,
            CompositionPlanOrigin.AutomaticProposal =>
                coverageKind == CompositionCoverageKind.FullTimelineSampled,
            CompositionPlanOrigin.Mixed => true,
            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException(
                $"Plan origin '{origin}' is incompatible with coverage kind '{coverageKind}'.");
        }
    }
}
