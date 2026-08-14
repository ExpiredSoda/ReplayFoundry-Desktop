using System.IO;
using System.Security;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlGroundedFailureArchiveResult(
    string? ArchivedPath,
    string? Warning);

internal interface IQwen3VlGroundedFailureArchive
{
    Qwen3VlGroundedFailureArchiveResult Archive(
        string sourcePath,
        int maximumBytes);
}

internal sealed class NullQwen3VlGroundedFailureArchive :
    IQwen3VlGroundedFailureArchive
{
    internal static NullQwen3VlGroundedFailureArchive Instance { get; } =
        new();

    public Qwen3VlGroundedFailureArchiveResult Archive(
        string sourcePath,
        int maximumBytes) => new(null, null);
}

internal sealed class SystemQwen3VlGroundedFailureArchive :
    IQwen3VlGroundedFailureArchive
{
    internal const int MaximumRetainedFiles = 16;
    private readonly string _root;

    internal SystemQwen3VlGroundedFailureArchive(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ReplayFoundry",
            "Diagnostics",
            "VisualSemanticFailures"));
        if (!Path.IsPathFullyQualified(_root) ||
            Path.GetPathRoot(_root)?.TrimEnd(Path.DirectorySeparatorChar) ==
                _root.TrimEnd(Path.DirectorySeparatorChar))
        {
            throw new ArgumentException(
                "The grounded failure archive root must be focused and fully qualified.",
                nameof(root));
        }
    }

    public Qwen3VlGroundedFailureArchiveResult Archive(
        string sourcePath,
        int maximumBytes)
    {
        try
        {
            string source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source)) return new(null, null);
            var sourceInfo = new FileInfo(source);
            if (maximumBytes <= 0 || sourceInfo.Length is <= 0 ||
                sourceInfo.Length > maximumBytes)
            {
                return new(
                    null,
                    "Grounded failure diagnostics were not retained because their size was invalid.");
            }

            Directory.CreateDirectory(_root);
            string destination = Path.Combine(
                _root,
                $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
            File.Copy(source, destination, overwrite: false);
            Prune();
            return new(destination, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            SecurityException or ArgumentException or NotSupportedException)
        {
            return new(
                null,
                $"Grounded failure diagnostics could not be retained: {exception.GetType().Name}.");
        }
    }

    private void Prune()
    {
        foreach (FileInfo stale in new DirectoryInfo(_root)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .ThenByDescending(static file => file.Name, StringComparer.Ordinal)
            .Skip(MaximumRetainedFiles))
        {
            stale.Delete();
        }
    }
}
