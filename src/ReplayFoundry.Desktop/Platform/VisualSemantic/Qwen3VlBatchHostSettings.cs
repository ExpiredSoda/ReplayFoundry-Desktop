using System.IO;
using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

public sealed class Qwen3VlBatchHostSettings
{
    public const string SupportedVideoBackend = "torchcodec";

    public Qwen3VlBatchHostSettings(
        string pythonExecutablePath,
        string hostScriptPath,
        string modelDirectoryPath,
        string videoBackend,
        string ffmpegSharedLibraryDirectoryPath,
        TimeSpan processTimeout,
        int maximumStandardOutputCharacters = 512 * 1024,
        int maximumStandardErrorCharacters = 2 * 1024 * 1024,
        int maximumStructuredOutputBytes = 4 * 1024 * 1024,
        string? rawAuditOutputPath = null,
        string? failureOutputPath = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        PythonExecutablePath = RequireFullPath(
            pythonExecutablePath,
            nameof(pythonExecutablePath));
        HostScriptPath = RequireFullPath(
            hostScriptPath,
            nameof(hostScriptPath));
        ModelDirectoryPath = RequireFullPath(
            modelDirectoryPath,
            nameof(modelDirectoryPath));
        FfmpegSharedLibraryDirectoryPath = RequireFullPath(
            ffmpegSharedLibraryDirectoryPath,
            nameof(ffmpegSharedLibraryDirectoryPath));

        if (!string.Equals(
                videoBackend,
                SupportedVideoBackend,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The Qwen host video backend must be exactly '{SupportedVideoBackend}'.",
                nameof(videoBackend));
        }

        if (processTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTimeout));
        }

        if (maximumStandardOutputCharacters <= 0 ||
            maximumStandardErrorCharacters <= 0 ||
            maximumStructuredOutputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumStandardOutputCharacters),
                "Qwen host output limits must be positive.");
        }

        ProcessTimeout = processTimeout;
        VideoBackend = videoBackend;
        MaximumStandardOutputCharacters =
            maximumStandardOutputCharacters;
        MaximumStandardErrorCharacters =
            maximumStandardErrorCharacters;
        MaximumStructuredOutputBytes =
            maximumStructuredOutputBytes;
        RawAuditOutputPath =
            RequireOptionalFullPath(
                rawAuditOutputPath,
                nameof(rawAuditOutputPath));
        FailureOutputPath =
            RequireOptionalFullPath(
                failureOutputPath,
                nameof(failureOutputPath));
        var environmentSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in environmentVariables ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || value is null ||
                !environmentSnapshot.TryAdd(name, value))
            {
                throw new ArgumentException(
                    "Qwen host environment variables require unique valid names and non-null values.",
                    nameof(environmentVariables));
            }
        }
        EnvironmentVariables = new ReadOnlyDictionary<string, string>(environmentSnapshot);

        if (RawAuditOutputPath is not null)
        {
            RequireDiagnosticPathSeparation(
                RawAuditOutputPath,
                nameof(rawAuditOutputPath),
                "raw-audit");
        }

        if (FailureOutputPath is not null)
        {
            RequireDiagnosticPathSeparation(
                FailureOutputPath,
                nameof(failureOutputPath),
                "failure-envelope");
        }

        if (FailureOutputPath is not null &&
            string.Equals(
                FailureOutputPath,
                RawAuditOutputPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Qwen raw-audit and failure-envelope outputs must use distinct paths.",
                nameof(failureOutputPath));
        }
    }

    public string PythonExecutablePath { get; }

    public string HostScriptPath { get; }

    public string ModelDirectoryPath { get; }

    public string VideoBackend { get; }

    public string FfmpegSharedLibraryDirectoryPath { get; }

    public TimeSpan ProcessTimeout { get; }

    public int MaximumStandardOutputCharacters { get; }

    public int MaximumStandardErrorCharacters { get; }

    public int MaximumStructuredOutputBytes { get; }

    public string? RawAuditOutputPath { get; }

    public string? FailureOutputPath { get; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    private void RequireDiagnosticPathSeparation(
        string path,
        string parameterName,
        string label)
    {
        RequireOutsideProtectedDirectory(
            path,
            ModelDirectoryPath,
            "model directory",
            parameterName,
            label);
        RequireOutsideProtectedDirectory(
            path,
            FfmpegSharedLibraryDirectoryPath,
            "shared FFmpeg directory",
            parameterName,
            label);
        string pythonDirectory =
            Path.GetDirectoryName(
                PythonExecutablePath)!;
        RequireOutsideProtectedDirectory(
            path,
            pythonDirectory,
            "Python environment",
            parameterName,
            label);

        if (string.Equals(
                Path.GetFileName(
                    pythonDirectory),
                "Scripts",
                StringComparison.OrdinalIgnoreCase))
        {
            string? environmentRoot =
                Path.GetDirectoryName(
                    pythonDirectory);

            if (environmentRoot is not null)
            {
                RequireOutsideProtectedDirectory(
                    path,
                    environmentRoot,
                    "Python environment root",
                    parameterName,
                    label);
            }
        }

        RequireOutsideProtectedDirectory(
            path,
            Path.GetDirectoryName(
                HostScriptPath)!,
            "host-script directory",
            parameterName,
            label);
    }

    private static string RequireFullPath(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(
                "Qwen host paths must be explicit and fully qualified.",
                parameterName);
        }

        return Path.GetFullPath(value);
    }

    private static string? RequireOptionalFullPath(
        string? value,
        string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return RequireFullPath(
            value,
            parameterName);
    }

    private static void RequireOutsideProtectedDirectory(
        string path,
        string protectedDirectory,
        string description,
        string parameterName,
        string label)
    {
        string directory =
            protectedDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        string prefix =
            directory +
            Path.DirectorySeparatorChar;

        if (string.Equals(
                path,
                directory,
                StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The optional Qwen {label} output must remain outside the {description}.",
                parameterName);
        }
    }
}
