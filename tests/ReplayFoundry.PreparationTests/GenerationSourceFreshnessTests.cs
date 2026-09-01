using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.PreparationTests;

internal static class GenerationSourceFreshnessTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Source snapshot validates path size and UTC timestamp",
            ValidatesSnapshotContract),
        new(
            "Preparation rejects a source changed during probing",
            RejectsChangeDuringProbe),
        new(
            "System snapshot provider rejects a missing source",
            RejectsMissingSource),
        new(
            "Freshness rejects a changed file length",
            RejectsChangedLength),
        new(
            "Freshness rejects a changed last-write timestamp",
            RejectsChangedTimestamp),
        new(
            "Freshness accepts an unchanged source",
            AcceptsUnchangedSource),
    ];

    private static Task ValidatesSnapshotContract()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "snapshot-contract.mkv");

        var snapshot =
            new GenerationSourceFileSnapshot(
                path,
                0,
                new DateTimeOffset(
                    2026,
                    7,
                    25,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));

        TestAssert.Equal(
            0L,
            snapshot.FileLength,
            "A nonnegative file size should be retained.");

        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourceFileSnapshot(
                "relative.mkv",
                1,
                DateTimeOffset.UtcNow),
            "Snapshot paths should be fully qualified.");

        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => _ = new GenerationSourceFileSnapshot(
                path,
                -1,
                DateTimeOffset.UtcNow),
            "Snapshot sizes cannot be negative.");

        TestAssert.Throws<ArgumentException>(
            () => _ = new GenerationSourceFileSnapshot(
                path,
                1,
                new DateTimeOffset(
                    2026,
                    7,
                    25,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(1))),
            "Snapshot timestamps should use UTC.");

        return Task.CompletedTask;
    }

    private static async Task RejectsChangeDuringProbe()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "changed-during-probe.mkv");

        var probe =
            new FakeMediaProbe();

        probe.AddResult(
            TestMediaFactory.Create(path));

        var snapshots =
            new FakeGenerationSourceFileSnapshotProvider();

        snapshots.Enqueue(
            path,
            TestMediaFactory.CreateSnapshot(
                path,
                fileLength: 100));

        snapshots.Enqueue(
            path,
            TestMediaFactory.CreateSnapshot(
                path,
                fileLength: 101));

        var service =
            new GenerationSourcePreparationService(
                probe,
                snapshots);

        GenerationSourcePreparationException exception =
            await TestAssert.ThrowsAsync<
                GenerationSourcePreparationException>(
                () => service.PrepareAsync(
                    CreateRequest(path),
                    progress: null,
                    CancellationToken.None),
                "A source changed during inspection should fail.");

        TestAssert.Equal(
            path,
            exception.SourcePath,
            "The failure should identify the changed source.");
    }

    private static Task RejectsMissingSource()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                $"{Guid.NewGuid():N}-missing.mkv");

        var provider =
            new SystemGenerationSourceFileSnapshotProvider();

        GenerationSourcePreparationException exception =
            TestAssert.Throws<
                GenerationSourcePreparationException>(
                () => provider.Capture(path),
                "A missing source should fail snapshot capture.");

        TestAssert.True(
            exception.Message.Contains(
                "could not be found",
                StringComparison.OrdinalIgnoreCase),
            "The missing-source message should be clear.");

        return Task.CompletedTask;
    }

    private static Task RejectsChangedLength()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "changed-length.mkv");

        GenerationSourceFileSnapshot retained =
            TestMediaFactory.CreateSnapshot(
                path,
                fileLength: 100);

        var provider =
            new FakeGenerationSourceFileSnapshotProvider();

        provider.SetDefault(
            TestMediaFactory.CreateSnapshot(
                path,
                fileLength: 101));

        var validator =
            new GenerationSourceFreshnessValidator(
                provider);

        TestAssert.Throws<
            GenerationSourcePreparationException>(
            () => validator.EnsureFresh(
                CreatePreparation(
                    path,
                    retained)),
            "A changed file length should be stale.");

        return Task.CompletedTask;
    }

    private static Task RejectsChangedTimestamp()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "changed-timestamp.mkv");

        GenerationSourceFileSnapshot retained =
            TestMediaFactory.CreateSnapshot(path);

        var provider =
            new FakeGenerationSourceFileSnapshotProvider();

        provider.SetDefault(
            TestMediaFactory.CreateSnapshot(
                path,
                lastWriteTimeUtc:
                    retained.LastWriteTimeUtc.AddSeconds(1)));

        var validator =
            new GenerationSourceFreshnessValidator(
                provider);

        TestAssert.Throws<
            GenerationSourcePreparationException>(
            () => validator.EnsureFresh(
                CreatePreparation(
                    path,
                    retained)),
            "A changed last-write timestamp should be stale.");

        return Task.CompletedTask;
    }

    private static Task AcceptsUnchangedSource()
    {
        string path =
            TestMediaFactory.CreateSourcePath(
                "unchanged.mkv");

        GenerationSourceFileSnapshot retained =
            TestMediaFactory.CreateSnapshot(path);

        var provider =
            new FakeGenerationSourceFileSnapshotProvider();

        provider.SetDefault(retained);

        var validator =
            new GenerationSourceFreshnessValidator(
                provider);

        validator.EnsureFresh(
            CreatePreparation(
                path,
                retained));

        TestAssert.Equal(
            1,
            provider.Requests.Count,
            "Freshness should capture the current file once.");

        return Task.CompletedTask;
    }

    private static GenerationSourcePreparationRequest CreateRequest(
        string path)
    {
        return new GenerationSourcePreparationRequest(
        [
            new SelectedVideoSource(
                path,
                isReference: true),
        ]);
    }

    private static GenerationSourcePreparationResult CreatePreparation(
        string path,
        GenerationSourceFileSnapshot snapshot)
    {
        GenerationSourcePreparationRequest request =
            CreateRequest(path);

        return new GenerationSourcePreparationResult(
            request,
        [
            new PreparedGenerationSource(
                request.Sources[0],
                TestMediaFactory.Create(path),
                snapshot),
        ]);
    }
}
