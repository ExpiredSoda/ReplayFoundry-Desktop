using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationSourcePreparationCoordinatorTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Preparation coordinator reuses a fresh cached result",
            ReusesFreshResult),
        new(
            "Preparation coordinator re-prepares a stale cached result",
            RepreparesStaleResult),
        new(
            "Preparation coordinator rejects source-order or reference mismatch",
            RejectsRequestMismatch),
        new(
            "Preparation coordinator supports a non-first explicit reference",
            SupportsNonFirstReference),
    ];

    private static async Task ReusesFreshResult()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "coordinator-reuse.mkv");

        TestContext context =
            CreateContext(path);

        GenerationSourcePreparationRequest request =
            CreateRequest(
            [
                (path, true),
            ]);

        GenerationSourcePreparationResult first =
            await context.Coordinator.GetOrPrepareAsync(
                request,
                progress: null,
                CancellationToken.None);

        GenerationSourcePreparationResult second =
            await context.Coordinator.GetOrPrepareAsync(
                request,
                progress: null,
                CancellationToken.None);

        TestAssert.Same(
            first,
            second,
            "A fresh matching result should be reused.");

        TestAssert.Equal(
            1,
            context.Probe.Requests.Count,
            "Reuse should not perform a second probe.");
    }

    private static async Task RepreparesStaleResult()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "coordinator-stale.mkv");

        TestContext context =
            CreateContext(path);

        GenerationSourcePreparationRequest request =
            CreateRequest(
            [
                (path, true),
            ]);

        GenerationSourcePreparationResult first =
            await context.Coordinator.GetOrPrepareAsync(
                request,
                progress: null,
                CancellationToken.None);

        context.Snapshots.SetDefault(
            TestMediaFactory.CreateSnapshot(
                path,
                fileLength: 2048));

        GenerationSourcePreparationResult second =
            await context.Coordinator.GetOrPrepareAsync(
                request,
                progress: null,
                CancellationToken.None);

        TestAssert.False(
            ReferenceEquals(first, second),
            "A stale result should be replaced.");

        TestAssert.Equal(
            2,
            context.Probe.Requests.Count,
            "Staleness should trigger another probe.");

        TestAssert.Equal(
            2048L,
            second.Sources[0].FileSnapshot.FileLength,
            "The replacement should retain the current snapshot.");
    }

    private static async Task RejectsRequestMismatch()
    {
        string firstPath =
            TestMediaFactory.CreateSourcePath(
                "coordinator-first.mkv");

        string referencePath =
            TestMediaFactory.CreateSourcePath(
                "coordinator-reference.mkv");

        TestContext context =
            CreateContext(
                firstPath,
                referencePath);

        await context.Coordinator.GetOrPrepareAsync(
            CreateRequest(
            [
                (firstPath, false),
                (referencePath, true),
            ]),
            progress: null,
            CancellationToken.None);

        await context.Coordinator.GetOrPrepareAsync(
            CreateRequest(
            [
                (referencePath, true),
                (firstPath, false),
            ]),
            progress: null,
            CancellationToken.None);

        TestAssert.Equal(
            4,
            context.Probe.Requests.Count,
            "Changing order should prevent reuse and inspect both sources again.");
    }

    private static async Task SupportsNonFirstReference()
    {
        string firstPath =
            TestMediaFactory.CreateSourcePath(
                "coordinator-non-reference.mkv");

        string referencePath =
            TestMediaFactory.CreateSourcePath(
                "coordinator-reference-second.mkv");

        TestContext context =
            CreateContext(
                firstPath,
                referencePath);

        GenerationSourcePreparationResult result =
            await context.Coordinator.GetOrPrepareAsync(
                CreateRequest(
                [
                    (firstPath, false),
                    (referencePath, true),
                ]),
                progress: null,
                CancellationToken.None);

        TestAssert.Equal(
            referencePath,
            result.ReferenceSource.Source.FullPath,
            "The explicit reference should not depend on list position.");
    }

    private static TestContext CreateContext(
        params string[] paths)
    {
        var probe =
            new FakeMediaProbe();

        var snapshots =
            new FakeGenerationSourceFileSnapshotProvider();

        foreach (string path in paths)
        {
            probe.AddResult(
                TestMediaFactory.Create(path));

            snapshots.SetDefault(
                TestMediaFactory.CreateSnapshot(path));
        }

        var service =
            new GenerationSourcePreparationService(
                probe,
                snapshots);

        var validator =
            new GenerationSourceFreshnessValidator(
                snapshots);

        return new TestContext(
            probe,
            snapshots,
            new GenerationSourcePreparationCoordinator(
                service,
                validator));
    }

    private static GenerationSourcePreparationRequest CreateRequest(
        IEnumerable<(string Path, bool IsReference)> sources)
    {
        return new GenerationSourcePreparationRequest(
            sources.Select(
                source =>
                    new SelectedVideoSource(
                        source.Path,
                        source.IsReference)));
    }

    private sealed record TestContext(
        FakeMediaProbe Probe,
        FakeGenerationSourceFileSnapshotProvider Snapshots,
        GenerationSourcePreparationCoordinator Coordinator);
}
