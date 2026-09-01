using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

public sealed class VideoSourceValidator
{
    private static readonly HashSet<string> SupportedExtensions =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".mkv",
            ".mov",
            ".avi",
        };

    public bool TryValidate(
        string candidatePath,
        out string normalizedPath,
        out string errorMessage)
    {
        normalizedPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            errorMessage =
                "An empty file path cannot be selected.";

            return false;
        }

        try
        {
            normalizedPath =
                Path.GetFullPath(candidatePath);
        }
        catch (ArgumentException)
        {
            errorMessage =
                $"The path '{candidatePath}' is invalid.";

            return false;
        }
        catch (NotSupportedException)
        {
            errorMessage =
                $"The path '{candidatePath}' uses an unsupported format.";

            return false;
        }
        catch (PathTooLongException)
        {
            errorMessage =
                $"The path '{candidatePath}' is too long.";

            return false;
        }

        if (Directory.Exists(normalizedPath))
        {
            errorMessage =
                $"Folders are not supported: '{normalizedPath}'.";

            return false;
        }

        if (!File.Exists(normalizedPath))
        {
            errorMessage =
                $"The file could not be found: '{normalizedPath}'.";

            return false;
        }

        string extension =
            Path.GetExtension(normalizedPath);

        if (!SupportedExtensions.Contains(extension))
        {
            errorMessage =
                $"Unsupported video format '{extension}' " +
                $"for '{Path.GetFileName(normalizedPath)}'.";

            return false;
        }

        return true;
    }
}
