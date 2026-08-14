using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ReplayFoundry.Desktop.Platform.Processes;

internal sealed class ProcessRunRequest
{
    private readonly ReadOnlyCollection<string> _arguments;
    private readonly ReadOnlyDictionary<string, string> _environmentVariables;

    public ProcessRunRequest(
        string executablePath,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        string? workingDirectory = null,
        int maxStandardOutputCharacters = 8 * 1024 * 1024,
        int maxStandardErrorCharacters = 1024 * 1024,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException(
                "A process request requires an executable path.",
                nameof(executablePath));
        }

        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "The process executable path must be fully qualified.",
                nameof(executablePath));
        }

        ArgumentNullException.ThrowIfNull(arguments);

        string[] argumentSnapshot =
            arguments.ToArray();

        if (argumentSnapshot.Any(static argument => argument is null))
        {
            throw new ArgumentException(
                "Process arguments cannot contain null values.",
                nameof(arguments));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "Process timeout must be greater than zero.");
        }

        if (maxStandardOutputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxStandardOutputCharacters),
                maxStandardOutputCharacters,
                "The standard-output limit must be positive.");
        }

        if (maxStandardErrorCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxStandardErrorCharacters),
                maxStandardErrorCharacters,
                "The standard-error limit must be positive.");
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) &&
            !Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The process working directory does not exist: '{workingDirectory}'.");
        }

        ExecutablePath = executablePath;
        _arguments = Array.AsReadOnly(argumentSnapshot);
        var environmentSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in environmentVariables ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || value is null ||
                !environmentSnapshot.TryAdd(name, value))
            {
                throw new ArgumentException(
                    "Process environment variables require unique valid names and non-null values.",
                    nameof(environmentVariables));
            }
        }
        _environmentVariables = new ReadOnlyDictionary<string, string>(environmentSnapshot);
        Timeout = timeout;
        WorkingDirectory = workingDirectory;
        MaxStandardOutputCharacters =
            maxStandardOutputCharacters;
        MaxStandardErrorCharacters =
            maxStandardErrorCharacters;
    }

    public string ExecutablePath { get; }

    public IReadOnlyList<string> Arguments =>
        _arguments;

    public TimeSpan Timeout { get; }

    public string? WorkingDirectory { get; }

    public int MaxStandardOutputCharacters { get; }

    public int MaxStandardErrorCharacters { get; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables =>
        _environmentVariables;
}
