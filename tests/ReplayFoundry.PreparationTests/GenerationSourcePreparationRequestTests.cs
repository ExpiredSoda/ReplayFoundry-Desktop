using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationSourcePreparationRequestTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Preparation request requires non-null sources and exactly one reference",
            RequiresSourcesAndReference),
        new(
            "Preparation request rejects duplicate paths case-insensitively",
            RejectsDuplicatePaths),
        new(
            "Preparation request snapshots source order and reference identity",
            SnapshotsSourceOrder),
        new(
            "Preparation result rejects substituted selected-source identity",
            RejectsSubstitutedSourceIdentity),
        new(
            "Preparation result snapshots prepared sources",
            SnapshotsPreparedSources),
    ];

    private static Task RequiresSourcesAndReference()
    {
        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourcePreparationRequest([]),
            "Empty source sets should fail.");

        string firstPath =
            TestMediaFactory.CreateSourcePath(
                "first.mkv");

        string secondPath =
            TestMediaFactory.CreateSourcePath(
                "second.mkv");

        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourcePreparationRequest(
            [
                null!,
            ]),
            "Null source entries should fail.");

        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourcePreparationRequest(
            [
                new SelectedVideoSource(
                    firstPath,
                    isReference: false),
                new SelectedVideoSource(
                    secondPath,
                    isReference: false),
            ]),
            "Missing reference should fail.");

        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourcePreparationRequest(
            [
                new SelectedVideoSource(
                    firstPath,
                    isReference: true),
                new SelectedVideoSource(
                    secondPath,
                    isReference: true),
            ]),
            "Several references should fail.");

        return Task.CompletedTask;
    }

    private static Task RejectsDuplicatePaths()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "duplicate.mkv");

        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourcePreparationRequest(
            [
                new SelectedVideoSource(
                    path,
                    isReference: true),
                new SelectedVideoSource(
                    path.ToUpperInvariant(),
                    isReference: false),
            ]),
            "Duplicate paths should fail case-insensitively.");

        return Task.CompletedTask;
    }

    private static Task SnapshotsSourceOrder()
    {
        var reference =
            new SelectedVideoSource(
                TestMediaFactory.CreateSourcePath(
                    "reference.mkv"),
                isReference: true);

        var secondary =
            new SelectedVideoSource(
                TestMediaFactory.CreateSourcePath(
                    "secondary.mkv"),
                isReference: false);

        var sources =
            new List<SelectedVideoSource>
            {
                secondary,
                reference,
            };

        var request =
            new GenerationSourcePreparationRequest(
                sources);

        sources.Reverse();
        sources.Clear();

        TestAssert.Same(
            secondary,
            request.Sources[0],
            "Request should preserve its original source order.");

        TestAssert.Same(
            reference,
            request.ReferenceSource,
            "Explicit reference identity should be preserved independently of order.");

        return Task.CompletedTask;
    }

    private static Task RejectsSubstitutedSourceIdentity()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "identity.mkv");

        var requestedSource =
            new SelectedVideoSource(
                path,
                isReference: true);

        var request =
            new GenerationSourcePreparationRequest(
                [requestedSource]);

        var substitutedSource =
            new SelectedVideoSource(
                path,
                isReference: true);

        var prepared =
            new PreparedGenerationSource(
                substitutedSource,
                TestMediaFactory.Create(path),
                TestMediaFactory.CreateSnapshot(path));

        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourcePreparationResult(
                request,
                [prepared]),
            "An equivalent path must not replace the request's selected-source identity.");

        return Task.CompletedTask;
    }

    private static Task SnapshotsPreparedSources()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "prepared-snapshot.mkv");

        var source =
            new SelectedVideoSource(
                path,
                isReference: true);

        var request =
            new GenerationSourcePreparationRequest(
                [source]);

        var prepared =
            new PreparedGenerationSource(
                source,
                TestMediaFactory.Create(path),
                TestMediaFactory.CreateSnapshot(path));

        var sourceResults =
            new List<PreparedGenerationSource>
            {
                prepared,
            };

        var result =
            new GenerationSourcePreparationResult(
                request,
                sourceResults);

        sourceResults.Clear();

        TestAssert.Equal(
            1,
            result.Sources.Count,
            "Mutating the caller collection must not alter the result.");

        TestAssert.Same(
            prepared,
            result.ReferenceSource,
            "Prepared reference identity should remain explicit.");

        return Task.CompletedTask;
    }
}
