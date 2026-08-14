using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class GenerationSourceFreshnessValidator
{
    private readonly IGenerationSourceFileSnapshotProvider
        _snapshotProvider;

    public GenerationSourceFreshnessValidator(
        IGenerationSourceFileSnapshotProvider snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        _snapshotProvider = snapshotProvider;
    }

    public void EnsureFresh(
        GenerationSourcePreparationResult preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        foreach (PreparedGenerationSource source in
                 preparation.Sources)
        {
            GenerationSourceFileSnapshot current =
                _snapshotProvider.Capture(
                    source.Source.FullPath);

            EnsureUnchanged(
                source.FileSnapshot,
                current);
        }
    }

    public static void EnsureUnchanged(
        GenerationSourceFileSnapshot expected,
        GenerationSourceFileSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        bool pathMatches =
            string.Equals(
                expected.FullPath,
                actual.FullPath,
                StringComparison.OrdinalIgnoreCase);

        if (pathMatches &&
            expected.FileLength == actual.FileLength &&
            expected.LastWriteTimeUtc == actual.LastWriteTimeUtc)
        {
            return;
        }

        string sourcePath =
            pathMatches
                ? expected.FullPath
                : actual.FullPath;

        throw new GenerationSourcePreparationException(
            sourcePath,
            $"The selected source '{Path.GetFileName(sourcePath)}' " +
            "changed after it was inspected.",
            "The file path, length, or UTC last-write timestamp no longer " +
            "matches the retained preparation result.");
    }
}
