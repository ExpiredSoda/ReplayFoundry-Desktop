using System;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

public sealed class SelectedVideoSource
{
    public SelectedVideoSource(
        string fullPath,
        bool isReference)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "A selected video must have a path.",
                nameof(fullPath));
        }

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "A selected video path must be fully qualified.",
                nameof(fullPath));
        }

        string fileName =
            Path.GetFileName(fullPath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException(
                "A selected video path must contain a file name.",
                nameof(fullPath));
        }

        FullPath = fullPath;
        FileName = fileName;
        DirectoryPath =
            Path.GetDirectoryName(fullPath) ??
            string.Empty;

        IsReference = isReference;
    }

    public string FullPath { get; }

    public string FileName { get; }

    public string DirectoryPath { get; }

    public bool IsReference { get; }
}
