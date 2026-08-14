using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Media.Composition;

namespace ReplayFoundry.PreparationTests;

internal static class PreparedGenerationWorkflowTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Generation Setup request exposes prepared reference data",
            SetupRequestExposesPreparation),
        new(
            "Generation request supports a non-first explicit reference",
            GenerationRequestSupportsNonFirstReference),
        new(
            "Generation preflight has no media-probe dependency",
            PreflightHasNoProbeDependency),
        new(
            "Generation preflight has no preview-frame dependency",
            PreflightHasNoPreviewDependency),
        new(
            "Generation preflight has no analyzer or evidence-service dependency",
            PreflightHasNoEvidenceDependency),
        new(
            "Generation request rejects missing or mismatched composition",
            GenerationRequestRequiresMatchingComposition),
        new(
            "Generation request requires complete matching evidence",
            GenerationRequestRequiresMatchingEvidence),
        new(
            "Visual-only generation accepts a prepared reference without audio",
            VisualOnlyGenerationAcceptsReferenceWithoutAudio),
        new(
            "Generation preflight validates retained layouts and evidence without media work",
            PreflightReportsLayoutValidation),
        new(
            "Generation preflight completes before moment finding",
            PreflightCompletesBeforeMomentFinding),
        new(
            "Obsolete inspection-context types are absent",
            ObsoleteTypesAreAbsent),
    ];

    private static Task SetupRequestExposesPreparation()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "setup-first.mkv"),
                    false,
                    false),
                (
                    TestMediaFactory.CreateSourcePath(
                        "setup-reference.mkv"),
                    true,
                    true),
            ]);

        var request =
            new GenerationSetupRequest(
                GenerationMode.Montage,
                preparation);

        TestAssert.Same(
            preparation,
            request.Preparation,
            "The setup request should retain the preparation result.");

        TestAssert.Same(
            preparation.ReferenceSource,
            request.ReferencePreparedSource,
            "The prepared reference should remain explicit.");

        TestAssert.Same(
            preparation.ReferenceSource.Media,
            request.ReferenceMedia,
            "Reference media should come from the prepared reference.");

        TestAssert.Equal(
            2,
            request.PreparedSources.Count,
            "Every prepared source should be exposed.");

        return Task.CompletedTask;
    }

    private static Task GenerationRequestSupportsNonFirstReference()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "generation-first.mkv"),
                    false,
                    false),
                (
                    TestMediaFactory.CreateSourcePath(
                        "generation-reference.mkv"),
                    true,
                    true),
            ]);

        GenerationRequest request =
            CreateGenerationRequest(
                preparation);

        TestAssert.Same(
            preparation.Request.Sources[1],
            request.ReferenceSource,
            "Generation should honor the explicit non-first reference.");

        TestAssert.Same(
            preparation.ReferenceSource,
            request.ReferencePreparedSource,
            "Generation should expose the prepared reference.");

        return Task.CompletedTask;
    }

    private static Task PreflightHasNoProbeDependency()
    {
        System.Reflection.ConstructorInfo[] constructors =
            typeof(GenerationPreflightRunner)
                .GetConstructors(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

        TestAssert.Equal(
            1,
            constructors.Length,
            "Preflight should have one focused constructor.");

        TestAssert.Equal(
            0,
            constructors[0].GetParameters().Length,
            "Preflight should not accept a media probe.");

        return Task.CompletedTask;
    }

    private static Task PreflightHasNoPreviewDependency()
    {
        Type runnerType =
            typeof(GenerationPreflightRunner);

        bool hasPreviewDependency =
            runnerType
                .GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)
                .Any(
                    field =>
                        typeof(
                            ReplayFoundry.Desktop.Media.Preview
                                .IVideoPreviewFrameProvider)
                            .IsAssignableFrom(
                                field.FieldType));

        TestAssert.False(
            hasPreviewDependency,
            "Preflight must not extract preview frames.");

        return Task.CompletedTask;
    }

    private static Task PreflightHasNoEvidenceDependency()
    {
        Type runnerType =
            typeof(GenerationPreflightRunner);

        bool hasEvidenceDependency =
            runnerType
                .GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)
                .Any(
                    field =>
                        typeof(
                            ReplayFoundry.Desktop.Media.Analysis
                                .IMediaEvidenceAnalyzer)
                            .IsAssignableFrom(
                                field.FieldType));

        bool acceptsEvidenceAnalyzer =
            runnerType
                .GetConstructors(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)
                .SelectMany(
                    static constructor =>
                        constructor.GetParameters())
                .Any(
                    parameter =>
                        typeof(
                            ReplayFoundry.Desktop.Media.Analysis
                                .IMediaEvidenceAnalyzer)
                            .IsAssignableFrom(
                                parameter.ParameterType));

        bool hasEvidenceServiceDependency =
            runnerType
                .GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)
                .Any(
                    field =>
                        typeof(
                            IGenerationEvidenceAnalysisService)
                            .IsAssignableFrom(
                                field.FieldType));

        TestAssert.False(
            hasEvidenceDependency ||
            acceptsEvidenceAnalyzer ||
            hasEvidenceServiceDependency,
            "Desktop preflight must only validate retained evidence.");

        return Task.CompletedTask;
    }

    private static Task
        GenerationRequestRequiresMatchingEvidence()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "matching-evidence.mkv"),
                    true,
                    true),
            ]);

        GenerationCompositionReviewResult composition =
            CreateCompositionReview(
                preparation);

        TestAssert.Throws<ArgumentNullException>(
            () =>
                _ = new GenerationRequest(
                    preparation,
                    CreateOptions(),
                    composition,
                    null!),
            "Generation must reject missing evidence.");

        GenerationSourcePreparationResult foreign =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "foreign-evidence-request.mkv"),
                    true,
                    true),
            ]);

        GenerationCompositionReviewResult foreignComposition =
            CreateCompositionReview(
                foreign);

        GenerationEvidenceAnalysisResult foreignEvidence =
            TestMediaFactory.CreateEvidenceAnalysisResult(
                new GenerationEvidenceAnalysisRequest(
                    foreign,
                    foreignComposition,
                    GenerationEvidenceAnalysisSettings
                        .CreateDefault()));

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationRequest(
                    preparation,
                    CreateOptions(),
                    composition,
                    foreignEvidence),
            "Generation must reject evidence from another preparation.");

        return Task.CompletedTask;
    }

    private static Task
        GenerationRequestRequiresMatchingComposition()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "matching-composition.mkv"),
                    true,
                    true),
            ]);

        GenerationSourcePreparationResult foreignPreparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "foreign-composition.mkv"),
                    true,
                    true),
            ]);

        TestAssert.Throws<ArgumentNullException>(
            () =>
                _ = new GenerationRequest(
                    preparation,
                    CreateOptions(),
                    null!,
                    null!),
            "Generation must reject missing composition.");

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationRequest(
                    preparation,
                    CreateOptions(),
                    CreateCompositionReview(
                        foreignPreparation),
                    null!),
            "Generation must reject composition from another preparation.");

        return Task.CompletedTask;
    }

    private static Task VisualOnlyGenerationAcceptsReferenceWithoutAudio()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "audio-non-reference.mkv"),
                    false,
                    true),
                (
                    TestMediaFactory.CreateSourcePath(
                        "audio-reference.mkv"),
                    true,
                    false),
            ]);

        var runner =
            new GenerationPreflightRunner();

        runner.Validate(
            CreateGenerationRequest(
                preparation),
            new RecordingProgress<
                GenerationProgressUpdate>(),
            CancellationToken.None);

        return Task.CompletedTask;
    }

    private static Task
        PreflightReportsLayoutValidation()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "layout-progress.mkv"),
                    true,
                    true),
            ]);

        var progress =
            new RecordingProgress<
                GenerationProgressUpdate>();

        var runner =
            new GenerationPreflightRunner();

        runner.Validate(
            CreateGenerationRequest(
                preparation),
            progress,
            CancellationToken.None);

        TestAssert.True(
            progress.Values.Any(
                static update =>
                    string.Equals(
                        update.Title,
                        "Confirmed video layouts accepted",
                        StringComparison.Ordinal)),
            "Preflight should report the layout-validation stage.");

        TestAssert.True(
            progress.Values.Any(
                static update =>
                    string.Equals(
                        update.Title,
                        "Deterministic evidence ready",
                        StringComparison.Ordinal)),
            "Preflight should validate complete retained evidence.");

        return Task.CompletedTask;
    }

    private static Task PreflightCompletesBeforeMomentFinding()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
            [
                (
                    TestMediaFactory.CreateSourcePath(
                        "intentional-stop.mkv"),
                    true,
                    true),
            ]);

        var runner =
            new GenerationPreflightRunner();

        var progress =
            new RecordingProgress<
                GenerationProgressUpdate>();

        runner.Validate(
            CreateGenerationRequest(
                preparation),
            progress,
            CancellationToken.None);

        TestAssert.Equal(
            20d,
            progress.Values[^1].ProgressPercent,
            "Preflight should finish at the moment-finding boundary.");

        return Task.CompletedTask;
    }

    private static Task ObsoleteTypesAreAbsent()
    {
        System.Reflection.Assembly assembly =
            typeof(GenerationPreflightRunner).Assembly;

        TestAssert.Null(
            assembly.GetType(
                "ReplayFoundry.Desktop.Features.Generate.Workflow." +
                "InspectedGenerationSource"),
            "The obsolete inspected-source type should be removed.");

        TestAssert.Null(
            assembly.GetType(
                "ReplayFoundry.Desktop.Features.Generate.Workflow." +
                "GenerationInspectionContext"),
            "The obsolete inspection context should be removed.");

        return Task.CompletedTask;
    }

    internal static GenerationSetupOptions CreateOptions(
        GenerationMode mode =
            GenerationMode.IndividualClips)
    {
        return new GenerationSetupOptions(
            mode,
            DetectionMethod.Heuristics,
            AudioSelectionMode.Auto,
            desiredResultCount: 10,
            qualityThreshold: 70,
            ContentEmphasis.Balanced,
            analysisDepth: GenerationAnalysisDepth.Fast);
    }

    internal static GenerationRequest
        CreateGenerationRequest(
            GenerationSourcePreparationResult preparation,
            GenerationSetupOptions? options = null,
            GenerationCompositionReviewResult? composition = null)
    {
        GenerationCompositionReviewResult actualComposition =
            composition ??
            CreateCompositionReview(
                preparation);

        var evidenceRequest =
            new GenerationEvidenceAnalysisRequest(
                preparation,
                actualComposition,
                GenerationEvidenceAnalysisSettings
                    .CreateDefault());

        GenerationEvidenceAnalysisResult evidence =
            TestMediaFactory
                .CreateEvidenceAnalysisResult(
                    evidenceRequest);

        return new GenerationRequest(
            preparation,
            options ?? CreateOptions(),
            actualComposition,
            evidence);
    }

    internal static GenerationCompositionReviewResult
        CreateCompositionReview(
            GenerationSourcePreparationResult preparation)
    {
        var request =
            new GenerationCompositionReviewRequest(
                preparation);

        return new GenerationCompositionReviewResult(
            request,
            preparation.Sources.Select(
                source =>
                {
                    var region =
                        new CompositionRegion(
                            "gameplay",
                            NormalizedRectangle.FullFrame,
                            CompositionRegionRole.Gameplay,
                            CompositionRegionTraits.Dynamic,
                            CompositionConfidence.Certain,
                            CompositionConfidence.Certain,
                            CompositionValueSource.UserConfirmed,
                            CompositionValueSource.UserConfirmed);

                    CompositionPlan plan =
                        ManualCompositionPlanFactory
                            .CreateUserConfirmedSingleInterval(
                                source.Source.FullPath,
                                source.Media.Duration,
                                [region],
                                new DateTimeOffset(
                                    2026,
                                    7,
                                    26,
                                    12,
                                    0,
                                    0,
                                    TimeSpan.Zero));

                    return new PreparedSourceCompositionPlan(
                        source,
                        plan);
                }));
    }

    internal static GenerationSourcePreparationResult
        CreatePreparation(
            IEnumerable<(
                string Path,
                bool IsReference,
                bool HasAudio)> sources)
    {
        (
            string Path,
            bool IsReference,
            bool HasAudio)[] snapshot =
            sources.ToArray();

        var request =
            new GenerationSourcePreparationRequest(
                snapshot.Select(
                    source =>
                        new SelectedVideoSource(
                            source.Path,
                            source.IsReference)));

        return new GenerationSourcePreparationResult(
            request,
            request.Sources.Select(
                (source, index) =>
                    new PreparedGenerationSource(
                        source,
                        TestMediaFactory.Create(
                            source.FullPath,
                            hasAudio:
                                snapshot[index].HasAudio),
                        TestMediaFactory.CreateSnapshot(
                            source.FullPath))));
    }
}
