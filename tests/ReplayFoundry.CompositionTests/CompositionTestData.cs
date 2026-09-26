using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.CompositionTests;

internal static class CompositionTestData
{
    public static readonly TimeSpan SourceDuration = TimeSpan.FromMinutes(10);

    public static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    public static readonly string SourcePath =
        Path.GetFullPath(
            Path.Combine(
                Path.GetTempPath(),
                "ReplayFoundry",
                "composition-contract-tests.mp4"));

    public static CompositionRegion CreateUserConfirmedRegion(
        string id = "gameplay",
        CompositionRegionRole role = CompositionRegionRole.Gameplay,
        NormalizedRectangle? geometry = null,
        CompositionRegionTraits traits = CompositionRegionTraits.Dynamic) =>
        new(
            id,
            geometry ?? NormalizedRectangle.FullFrame,
            role,
            traits,
            geometryConfidence: CompositionConfidence.Certain,
            roleConfidence: CompositionConfidence.Certain,
            geometrySource: CompositionValueSource.UserConfirmed,
            roleSource: CompositionValueSource.UserConfirmed);

    public static CompositionRegion CreateUnknownRoleWithConfirmedGeometry(
        string id = "region") =>
        new(
            id,
            NormalizedRectangle.FullFrame,
            CompositionRegionRole.Unknown,
            CompositionRegionTraits.None,
            geometryConfidence: CompositionConfidence.Certain,
            roleConfidence: CompositionConfidence.None,
            geometrySource: CompositionValueSource.UserConfirmed,
            roleSource: CompositionValueSource.NotAvailable);

    public static CompositionRegion CreateAutomaticRegion(
        string id = "automatic") =>
        new(
            id,
            NormalizedRectangle.FullFrame,
            CompositionRegionRole.Unknown,
            CompositionRegionTraits.None,
            geometryConfidence: new CompositionConfidence(0.75),
            roleConfidence: new CompositionConfidence(0.25),
            geometrySource: CompositionValueSource.AutomaticAnalyzer,
            roleSource: CompositionValueSource.AutomaticAnalyzer);

    public static CompositionPlanManifest CreateManifest(
        CompositionPlanOrigin origin = CompositionPlanOrigin.Manual,
        string? schemaVersion = null,
        string? coordinateSpaceVersion = null) =>
        new(
            schemaVersion ?? CompositionPlan.CurrentSchemaVersion,
            coordinateSpaceVersion ?? CompositionPlan.CurrentCoordinateSpaceVersion,
            "ReplayFoundry.CompositionTests",
            "1.0",
            origin,
            CreatedAtUtc);

    public static CompositionPlan CreateManualPlan(
        IEnumerable<CompositionLayoutInterval> intervals,
        TimeSpan? sourceDuration = null,
        IEnumerable<CompositionWarning>? warnings = null) =>
        new(
            SourcePath,
            sourceDuration ?? SourceDuration,
            CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop,
            intervals,
            CompositionCoverage.CreateManual(sourceDuration ?? SourceDuration),
            CreateManifest(),
            warnings);

    public static GenerationSourcePreparationResult
        CreatePreparation(
            params (
                string FileName,
                bool IsReference,
                TimeSpan? Duration)[] sources)
    {
        var request =
            new GenerationSourcePreparationRequest(
                sources.Select(
                    source =>
                        new SelectedVideoSource(
                            Path.GetFullPath(
                                Path.Combine(
                                    Path.GetTempPath(),
                                    "ReplayFoundryCompositionReviewTests",
                                    source.FileName)),
                            source.IsReference)));

        return new GenerationSourcePreparationResult(
            request,
            request.Sources.Select(
                (source, index) =>
                    new PreparedGenerationSource(
                        source,
                        CreateMedia(
                            source.FullPath,
                            sources[index].Duration ??
                            SourceDuration),
                        new GenerationSourceFileSnapshot(
                            source.FullPath,
                            1024 + index,
                            CreatedAtUtc))));
    }

    public static PreparedSourceCompositionPlan
        CreateSourcePlan(
            PreparedGenerationSource source,
            IEnumerable<CompositionRegion>? regions = null,
            string? sourcePath = null,
            TimeSpan? duration = null)
    {
        CompositionPlan plan =
            ManualCompositionPlanFactory
                .CreateUserConfirmedSingleInterval(
                    sourcePath ??
                    source.Source.FullPath,
                    duration ??
                    source.Media.Duration,
                    regions ??
                    [
                        CreateUserConfirmedRegion(),
                    ],
                    CreatedAtUtc);

        return new PreparedSourceCompositionPlan(
            source,
            plan);
    }

    public static GenerationCompositionReviewResult
        CreateReviewResult(
            GenerationSourcePreparationResult preparation)
    {
        var request =
            new GenerationCompositionReviewRequest(
                preparation);

        return new GenerationCompositionReviewResult(
            request,
            preparation.Sources.Select(
                source =>
                    CreateSourcePlan(
                        source)));
    }

    private static MediaProbeResult CreateMedia(
        string fullPath,
        TimeSpan duration)
    {
        var container =
            new MediaContainerInfo(
                "matroska",
                "Matroska",
                duration,
                TimeSpan.Zero,
                1024,
                8_000_000,
                100,
                null);

        var video =
            new VideoStreamInfo(
                0,
                "h264",
                "H.264",
                null,
                1920,
                1080,
                1920,
                1080,
                new MediaRational(60, 1),
                new MediaRational(60, 1),
                "yuv420p",
                8,
                MediaValueSource.ReportedByProbe,
                new MediaRational(1, 1),
                MediaValueSource.ReportedByProbe,
                new MediaRational(16, 9),
                MediaValueSource.ReportedByProbe,
                null,
                null,
                "tv",
                "bt709",
                "bt709",
                "bt709",
                "left",
                null,
                duration,
                true);

        var manifest =
            new MediaInspectionManifest(
                "CompositionReviewTests",
                "1.0",
                "ffprobe",
                "ffprobe test",
                Path.GetFullPath(
                    Path.Combine(
                        Path.GetTempPath(),
                        "ffprobe.exe")),
                CreatedAtUtc);

        return new MediaProbeResult(
            fullPath,
            container,
            [video],
            [],
            manifest);
    }
}
