using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Media.Analysis;
using ReplayFoundry.Desktop.Media.Analysis.Summaries;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationEvidenceAnalysisTests
{
    private static Task SettingsSnapshotInputs()
    {
        var mutableRoles =
            new List<CompositionRegionRole>
            {
                CompositionRegionRole.Presenter,
                CompositionRegionRole.Gameplay,
            };

        MediaEvidenceAnalysisOptions options =
            MediaEvidenceAnalysisOptions
                .CreateFullPrecisionDefaults()
                .WithSceneThresholdPercent(23);

        var settings =
            new GenerationEvidenceAnalysisSettings(
                options,
                mutableRoles,
                MediaEvidenceSummaryOptions
                    .CreateDefault());

        mutableRoles.Clear();

        TestAssert.False(
            ReferenceEquals(
                options,
                settings.Options),
            "Settings should snapshot every low-level option value.");

        TestAssert.Equal(
            23d,
            settings.Options.SceneThresholdPercent,
            "The snapped scene threshold should be preserved.");
        TestAssert.Equal(
            options.VisualSignalSampleInterval,
            settings.Options
                .VisualSignalSampleInterval,
            "Settings should snapshot visual signal cadence.");
        TestAssert.Equal(
            options.AudioSignalWindowDuration,
            settings.Options
                .AudioSignalWindowDuration,
            "Settings should snapshot audio signal windows.");

        TestAssert.True(
            settings.IncludedRegionRoles.SequenceEqual(
            [
                CompositionRegionRole.Gameplay,
                CompositionRegionRole.Presenter,
            ]),
            "Included roles should be canonical, immutable, Gameplay then Presenter.");

        return Task.CompletedTask;
    }

    private static Task SettingsRejectInvalidRoles()
    {
        MediaEvidenceAnalysisOptions options =
            MediaEvidenceAnalysisOptions
                .CreateFullPrecisionDefaults();

        MediaEvidenceSummaryOptions summaryOptions =
            MediaEvidenceSummaryOptions
                .CreateDefault();

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationEvidenceAnalysisSettings(
                    options,
                    [
                        CompositionRegionRole.Gameplay,
                        CompositionRegionRole.Gameplay,
                    ],
                    summaryOptions),
            "Duplicate roles should be rejected.");

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () =>
                _ = new GenerationEvidenceAnalysisSettings(
                    options,
                    [
                        CompositionRegionRole.Gameplay,
                        (CompositionRegionRole)999,
                    ],
                    summaryOptions),
            "Undefined roles should be rejected.");

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationEvidenceAnalysisSettings(
                    options,
                    [CompositionRegionRole.Presenter],
                    summaryOptions),
            "Gameplay should be mandatory for Desktop evidence.");

        return Task.CompletedTask;
    }

    private static Task
        RequestValidatesCompositionAndReference()
    {
        GenerationSourcePreparationResult preparation =
            CreatePreparation(
                sourceCount: 2,
                referenceIndex: 1);

        GenerationCompositionReviewResult review =
            CreateReview(preparation);

        var request =
            new GenerationEvidenceAnalysisRequest(
                preparation,
                review,
                GenerationEvidenceAnalysisSettings
                    .CreateDefault());

        TestAssert.Same(
            preparation.ReferenceSource,
            request.ReferenceSource,
            "The explicit non-first preparation reference should be preserved.");

        TestAssert.Same(
            review.ReferencePlan,
            request.ReferencePlan,
            "The matching reference composition plan should be preserved.");

        GenerationSourcePreparationResult foreign =
            CreatePreparation();

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationEvidenceAnalysisRequest(
                    preparation,
                    CreateReview(foreign),
                    request.Settings),
            "A foreign composition review should be rejected.");

        TestAssert.Throws<ArgumentNullException>(
            () =>
                _ = new GenerationEvidenceAnalysisRequest(
                    preparation,
                    review,
                    null!),
            "Settings are required.");

        return Task.CompletedTask;
    }

    private static Task AnalyzedSourceValidatesPayload()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest();

        GenerationEvidenceAnalysisResult valid =
            TestMediaFactory
                .CreateEvidenceAnalysisResult(
                    request);

        AnalyzedGenerationSource source =
            valid.Sources[0];

        TestAssert.Same(
            request.PreparedSources[0],
            source.PreparedSource,
            "Analyzed source identity should be preserved.");

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new AnalyzedGenerationSource(
                    CreatePreparation()
                        .Sources[0],
                    request.SourcePlans[0],
                    source.Evidence,
                    source.Summary,
                    request.Settings),
            "Foreign prepared identity should be rejected.");

        MediaProbeResult foreignMedia =
            TestMediaFactory.Create(
                TestMediaFactory.CreateSourcePath(
                    "foreign-evidence.mkv"),
                duration:
                    source.Evidence.SourceDuration,
                hasAudio: true);

        MediaEvidenceAnalysisRequest foreignRequest =
            MediaEvidenceAnalysisRequest
                .CreateCompositionAware(
                    foreignMedia,
                    CreatePlanForMedia(
                        foreignMedia),
                    request.Settings.Options,
                    request.Settings
                        .IncludedRegionRoles);

        MediaEvidenceResult foreignEvidence =
            TestMediaFactory
                .CreateMediaEvidenceResult(
                    foreignRequest);

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new AnalyzedGenerationSource(
                    request.PreparedSources[0],
                    request.SourcePlans[0],
                    foreignEvidence,
                    source.Summary,
                    request.Settings),
            "Path-mismatched evidence should be rejected.");

        return Task.CompletedTask;
    }

    private static Task BatchValidatesCompletenessAndOrder()
    {
        GenerationEvidenceAnalysisRequest request =
            CreateRequest(
                sourceCount: 2,
                referenceIndex: 1);

        GenerationEvidenceAnalysisResult valid =
            TestMediaFactory
                .CreateEvidenceAnalysisResult(
                    request);

        TestAssert.Same(
            request.PreparedSources[1],
            valid.ReferenceSource.PreparedSource,
            "Batch evidence should preserve the non-first reference.");

        TestAssert.Same(
            request.PreparedSources[0],
            valid.Sources[0].PreparedSource,
            "Preparation order should be preserved.");

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationEvidenceAnalysisResult(
                    request,
                    [valid.Sources[0]]),
            "Missing source evidence should be rejected.");

        TestAssert.Throws<ArgumentException>(
            () =>
                _ = new GenerationEvidenceAnalysisResult(
                    request,
                    [
                        valid.Sources[0],
                        valid.Sources[0],
                    ]),
            "Duplicate source evidence should be rejected.");

        TestAssert.Throws<NotSupportedException>(
            () =>
                ((IList<AnalyzedGenerationSource>)
                    valid.Sources)
                    .Add(valid.Sources[0]),
            "Batch collections should be immutable.");

        return Task.CompletedTask;
    }

}
