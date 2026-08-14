using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Media.Inspection;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationSourcePreparationServiceTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Preparation service probes each source exactly once in order and reports progress",
            InspectsEachSourceOnce),
        new(
            "Preparation service preserves the explicit reference source",
            PreservesReferenceSource),
        new(
            "Preparation service translates probe failure with source diagnostics",
            TranslatesProbeFailure),
        new(
            "Preparation service preserves media-tool-not-found failure",
            PreservesToolNotFound),
        new(
            "Preparation service preserves cancellation",
            HonorsCancellation),
    ];

    private static async Task InspectsEachSourceOnce()
    {
        string referencePath =
            TestMediaFactory.CreateSourcePath(
                "reference.mkv");

        string secondaryPath =
            TestMediaFactory.CreateSourcePath(
                "secondary.mkv");

        var probe =
            new FakeMediaProbe();

        probe.AddResult(
            TestMediaFactory.Create(referencePath));

        probe.AddResult(
            TestMediaFactory.Create(secondaryPath));

        var service =
            new GenerationSourcePreparationService(
                probe,
                CreateSnapshotProvider(
                    referencePath,
                    secondaryPath));

        var request =
            new GenerationSourcePreparationRequest(
            [
                new SelectedVideoSource(
                    referencePath,
                    isReference: true),
                new SelectedVideoSource(
                    secondaryPath,
                    isReference: false),
            ]);

        var progress =
            new RecordingProgress<
                GenerationSourcePreparationProgress>();

        GenerationSourcePreparationResult result =
            await service.PrepareAsync(
                request,
                progress,
                CancellationToken.None);

        TestAssert.Equal(
            2,
            probe.Requests.Count,
            "Every source should be inspected exactly once.");

        TestAssert.Equal(
            referencePath,
            probe.Requests[0],
            "Inspection should preserve request order.");

        TestAssert.Equal(
            secondaryPath,
            probe.Requests[1],
            "Inspection should remain sequential.");

        TestAssert.Same(
            request.Sources[1],
            result.Sources[1].Source,
            "Prepared results should preserve selected-source identity.");

        TestAssert.Equal(
            100d,
            progress.Values[^1].ProgressPercent,
            "Successful preparation should report completion.");

        TestAssert.True(
            progress.Values.Any(
                update =>
                    update.SourceNumber == 2 &&
                    string.Equals(
                        update.SourceName,
                        "secondary.mkv",
                        StringComparison.Ordinal)),
            "Progress should identify the active source.");
    }

    private static async Task PreservesReferenceSource()
    {
        string firstPath =
            TestMediaFactory.CreateSourcePath(
                "first.mkv");

        string referencePath =
            TestMediaFactory.CreateSourcePath(
                "reference-second.mkv");

        var probe =
            new FakeMediaProbe();

        probe.AddResult(
            TestMediaFactory.Create(firstPath));

        probe.AddResult(
            TestMediaFactory.Create(referencePath));

        var service =
            new GenerationSourcePreparationService(
                probe,
                CreateSnapshotProvider(
                    firstPath,
                    referencePath));

        var request =
            new GenerationSourcePreparationRequest(
            [
                new SelectedVideoSource(
                    firstPath,
                    isReference: false),
                new SelectedVideoSource(
                    referencePath,
                    isReference: true),
            ]);

        GenerationSourcePreparationResult result =
            await service.PrepareAsync(
                request,
                progress: null,
                CancellationToken.None);

        TestAssert.Same(
            request.ReferenceSource,
            result.ReferenceSource.Source,
            "The explicit reference should survive preparation when it is not first.");
    }

    private static async Task TranslatesProbeFailure()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "broken.mkv");

        var probe =
            new FakeMediaProbe();

        probe.AddFailure(
            path,
            new MediaProbeException(
                "Synthetic probe failure.",
                "probe diagnostics"));

        var service =
            new GenerationSourcePreparationService(
                probe,
                CreateSnapshotProvider(path));

        var request =
            CreateSingleSourceRequest(path);

        GenerationSourcePreparationException exception =
            await TestAssert.ThrowsAsync<
                GenerationSourcePreparationException>(
                () => service.PrepareAsync(
                    request,
                    progress: null,
                    CancellationToken.None),
                "Probe failures should be translated.");

        TestAssert.Equal(
            path,
            exception.SourcePath,
            "Failure should identify the source.");

        TestAssert.Equal(
            "probe diagnostics",
            exception.DiagnosticDetails,
            "Probe diagnostics should be retained.");

        TestAssert.True(
            exception.InnerException is MediaProbeException,
            "The source probe failure should remain the inner exception.");
    }

    private static async Task PreservesToolNotFound()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "missing-tool.mkv");

        var expected =
            new MediaToolNotFoundException(
                "Synthetic tool failure.");

        var probe =
            new FakeMediaProbe();

        probe.AddFailure(
            path,
            expected);

        var service =
            new GenerationSourcePreparationService(
                probe,
                CreateSnapshotProvider(path));

        MediaToolNotFoundException actual =
            await TestAssert.ThrowsAsync<
                MediaToolNotFoundException>(
                () => service.PrepareAsync(
                    CreateSingleSourceRequest(path),
                    progress: null,
                    CancellationToken.None),
                "Tool discovery failures should not be translated.");

        TestAssert.Same(
            expected,
            actual,
            "The original tool-not-found exception should be preserved.");
    }

    private static async Task HonorsCancellation()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "cancelled.mkv");

        var probe =
            new FakeMediaProbe();

        probe.AddResult(
            TestMediaFactory.Create(path));

        var service =
            new GenerationSourcePreparationService(
                probe,
                CreateSnapshotProvider(path));

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        OperationCanceledException exception =
            await TestAssert.ThrowsAsync<
                OperationCanceledException>(
                () => service.PrepareAsync(
                    CreateSingleSourceRequest(path),
                    progress: null,
                    cancellationSource.Token),
                "Cancellation should be preserved.");

        TestAssert.Equal(
            cancellationSource.Token,
            exception.CancellationToken,
            "Cancellation should retain the caller token.");

        TestAssert.Equal(
            0,
            probe.Requests.Count,
            "A pre-cancelled preparation must not inspect a source.");
    }

    private static GenerationSourcePreparationRequest CreateSingleSourceRequest(
        string path) =>
        new(
        [
            new SelectedVideoSource(
                path,
                isReference: true),
        ]);

    private static FakeGenerationSourceFileSnapshotProvider
        CreateSnapshotProvider(
            params string[] paths)
    {
        var provider =
            new FakeGenerationSourceFileSnapshotProvider();

        foreach (string path in paths)
        {
            provider.SetDefault(
                TestMediaFactory.CreateSnapshot(path));
        }

        return provider;
    }
}
