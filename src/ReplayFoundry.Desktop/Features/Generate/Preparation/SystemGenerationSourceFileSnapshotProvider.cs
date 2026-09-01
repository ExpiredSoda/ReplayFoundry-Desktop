using System.IO;
using System.Security;

namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public sealed class SystemGenerationSourceFileSnapshotProvider :
    IGenerationSourceFileSnapshotProvider
{
    public GenerationSourceFileSnapshot Capture(
        string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException(
                "A source snapshot requires a path.",
                nameof(sourcePath));
        }

        if (!Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException(
                "A source snapshot path must be fully qualified.",
                nameof(sourcePath));
        }

        try
        {
            FileAttributes attributes =
                File.GetAttributes(sourcePath);

            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw CreateMissingFailure(sourcePath);
            }

            var file = new FileInfo(sourcePath);

            file.Refresh();

            return new GenerationSourceFileSnapshot(
                file.FullName,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc));
        }
        catch (GenerationSourcePreparationException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw CreateMissingFailure(
                sourcePath,
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw CreateMissingFailure(
                sourcePath,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw CreateInaccessibleFailure(
                sourcePath,
                exception);
        }
        catch (SecurityException exception)
        {
            throw CreateInaccessibleFailure(
                sourcePath,
                exception);
        }
        catch (IOException exception)
        {
            throw CreateInaccessibleFailure(
                sourcePath,
                exception);
        }
    }

    private static GenerationSourcePreparationException
        CreateMissingFailure(
            string sourcePath,
            Exception? innerException = null)
    {
        return new GenerationSourcePreparationException(
            sourcePath,
            $"The selected source '{Path.GetFileName(sourcePath)}' " +
            "could not be found.",
            "The file was missing when Replay Foundry checked its freshness.",
            innerException);
    }

    private static GenerationSourcePreparationException
        CreateInaccessibleFailure(
            string sourcePath,
            Exception innerException)
    {
        return new GenerationSourcePreparationException(
            sourcePath,
            $"The selected source '{Path.GetFileName(sourcePath)}' " +
            "could not be accessed.",
            innerException.Message,
            innerException);
    }
}
