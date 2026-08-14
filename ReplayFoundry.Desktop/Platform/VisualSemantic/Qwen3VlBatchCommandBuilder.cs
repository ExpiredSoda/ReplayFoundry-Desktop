using System.Collections.ObjectModel;
using System.IO;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal sealed record Qwen3VlBatchCommand
{
    private readonly ReadOnlyCollection<string> _arguments;

    public Qwen3VlBatchCommand(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] snapshot = arguments.ToArray();

        if (snapshot.Length == 0 ||
            snapshot.Any(static value => value is null))
        {
            throw new ArgumentException(
                "A Qwen host command requires non-null ArgumentList values.",
                nameof(arguments));
        }

        _arguments = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<string> Arguments => _arguments;
}

internal static class Qwen3VlBatchCommandBuilder
{
    public static Qwen3VlBatchCommand BuildProbe(
        Qwen3VlBatchHostSettings settings,
        Qwen3VlBatchWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspace);

        var arguments =
            new List<string>
            {
                "-B",
                settings.HostScriptPath,
                "probe",
                "--model",
                settings.ModelDirectoryPath,
                "--input",
                workspace.InputBatchPath,
                "--output",
                workspace.ProbeOutputPath,
                "--video-backend",
                settings.VideoBackend,
                "--ffmpeg-shared-library-dir",
                settings.FfmpegSharedLibraryDirectoryPath,
            };
        AppendFailureOutput(
            settings,
            workspace,
            arguments);

        return new Qwen3VlBatchCommand(arguments);
    }

    public static Qwen3VlBatchCommand BuildRun(
        Qwen3VlBatchHostSettings settings,
        Qwen3VlBatchWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspace);

        var arguments =
            new List<string>
            {
                "-B",
                settings.HostScriptPath,
                "run",
                "--model",
                settings.ModelDirectoryPath,
                "--input",
                workspace.InputBatchPath,
                "--output",
                workspace.OutputBatchPath,
                "--attempt-output",
                workspace.AttemptOutputPath,
                "--video-backend",
                settings.VideoBackend,
                "--ffmpeg-shared-library-dir",
                settings.FfmpegSharedLibraryDirectoryPath,
            };
        AppendFailureOutput(
            settings,
            workspace,
            arguments);

        if (settings.RawAuditOutputPath is not null)
        {
            RequireExternalRawAuditPath(
                settings.RawAuditOutputPath,
                workspace);
            arguments.Add("--raw-audit-output");
            arguments.Add(settings.RawAuditOutputPath);
        }

        return new Qwen3VlBatchCommand(arguments);
    }

    public static Qwen3VlBatchCommand BuildSamplingAudit(
        Qwen3VlBatchHostSettings settings,
        Qwen3VlBatchWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspace);

        var arguments =
            new List<string>
            {
                "-B",
                settings.HostScriptPath,
                "audit-video-sampling",
                "--input",
                workspace.InputBatchPath,
                "--output",
                workspace.SamplingAuditOutputPath,
                "--video-backend",
                settings.VideoBackend,
                "--ffmpeg-shared-library-dir",
                settings.FfmpegSharedLibraryDirectoryPath,
            };
        AppendFailureOutput(
            settings,
            workspace,
            arguments);

        return new Qwen3VlBatchCommand(arguments);
    }

    public static Qwen3VlBatchCommand BuildQualifiedEditorialRun(
        Qwen3VlBatchHostSettings settings,
        Qwen3VlBatchWorkspace workspace,
        string qualificationLockPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(qualificationLockPath) ||
            !Path.IsPathFullyQualified(qualificationLockPath))
        {
            throw new ArgumentException(
                "The Qwen qualification lock path must be explicit.",
                nameof(qualificationLockPath));
        }

        var arguments = new List<string>
        {
            "-B",
            settings.HostScriptPath,
            "run-qualified-editorial-batch",
            "--model",
            settings.ModelDirectoryPath,
            "--input",
            workspace.InputBatchPath,
            "--qualification-lock",
            Path.GetFullPath(qualificationLockPath),
            "--output",
            workspace.OutputBatchPath,
            "--attempt-output",
            workspace.AttemptOutputPath,
            "--video-backend",
            settings.VideoBackend,
            "--ffmpeg-shared-library-dir",
            settings.FfmpegSharedLibraryDirectoryPath,
        };
        AppendFailureOutput(settings, workspace, arguments);
        return new Qwen3VlBatchCommand(arguments);
    }

    public static Qwen3VlBatchCommand BuildGroundedMetadataRun(
        Qwen3VlBatchHostSettings settings,
        Qwen3VlBatchWorkspace workspace,
        string qualificationLockPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(qualificationLockPath) ||
            !Path.IsPathFullyQualified(qualificationLockPath))
        {
            throw new ArgumentException(
                "The Qwen qualification lock path must be explicit.",
                nameof(qualificationLockPath));
        }

        var arguments = new List<string>
        {
            "-B",
            settings.HostScriptPath,
            "run-grounded-editorial-metadata-batch",
            "--model",
            settings.ModelDirectoryPath,
            "--input",
            workspace.InputBatchPath,
            "--qualification-lock",
            Path.GetFullPath(qualificationLockPath),
            "--output",
            workspace.OutputBatchPath,
            "--video-backend",
            settings.VideoBackend,
            "--ffmpeg-shared-library-dir",
            settings.FfmpegSharedLibraryDirectoryPath,
        };
        AppendFailureOutput(
            settings,
            workspace,
            arguments,
            useOwnedWorkspaceFallback: true);
        return new Qwen3VlBatchCommand(arguments);
    }

    private static void AppendFailureOutput(
        Qwen3VlBatchHostSettings settings,
        Qwen3VlBatchWorkspace workspace,
        ICollection<string> arguments,
        bool useOwnedWorkspaceFallback = false)
    {
        string? failureOutputPath = settings.FailureOutputPath ??
            (useOwnedWorkspaceFallback
                ? workspace.FailureOutputPath
                : null);
        if (failureOutputPath is null)
        {
            return;
        }

        if (settings.FailureOutputPath is not null)
        {
            RequireExternalDiagnosticPath(
                failureOutputPath,
                workspace,
                nameof(settings.FailureOutputPath),
                "failure-envelope");
        }
        arguments.Add("--failure-output");
        arguments.Add(failureOutputPath);
    }

    private static void RequireExternalRawAuditPath(
        string rawAuditOutputPath,
        Qwen3VlBatchWorkspace workspace)
    {
        RequireExternalDiagnosticPath(
            rawAuditOutputPath,
            workspace,
            nameof(rawAuditOutputPath),
            "raw-audit");
    }

    private static void RequireExternalDiagnosticPath(
        string outputPath,
        Qwen3VlBatchWorkspace workspace,
        string parameterName,
        string description)
    {
        string workspaceDirectory =
            workspace.DirectoryPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        string workspacePrefix =
            workspaceDirectory +
            Path.DirectorySeparatorChar;

        if (string.Equals(
                outputPath,
                workspaceDirectory,
                StringComparison.OrdinalIgnoreCase) ||
            outputPath.StartsWith(
                workspacePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The optional Qwen {description} output must remain outside the owned batch workspace so cleanup cannot remove it.",
                parameterName);
        }
    }
}
