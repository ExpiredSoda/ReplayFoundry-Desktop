using System;
using System.Collections.Generic;
using System.IO;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform;
using ReplayFoundry.Desktop.Platform.RuntimePacks;

namespace ReplayFoundry.Desktop.Platform.Media;

internal interface IFfmpegToolLocator
{
    string LocateFfprobe();

    string LocateFfmpeg();
}

internal sealed class FfmpegToolLocator :
    IFfmpegToolLocator
{
#if DEBUG
    private const string FfprobeOverrideVariable =
        "REPLAYFOUNDRY_FFPROBE_PATH";

    private const string FfmpegOverrideVariable =
        "REPLAYFOUNDRY_FFMPEG_PATH";
#endif

    private readonly ReplayFoundryRuntimeEnvironment _runtimeEnvironment;

    public FfmpegToolLocator()
        : this(
            ReplayFoundryRuntimeEnvironment.Current)
    {
    }

    internal FfmpegToolLocator(
        ReplayFoundryRuntimeEnvironment runtimeEnvironment)
    {
        ArgumentNullException.ThrowIfNull(runtimeEnvironment);

        _runtimeEnvironment = runtimeEnvironment;
    }

    public string LocateFfprobe()
    {
        return LocateTool(
            "ffprobe.exe");
    }

    public string LocateFfmpeg()
    {
        return LocateTool(
            "ffmpeg.exe");
    }

    private string LocateTool(
        string executableName)
    {
        foreach (string candidate in GetCandidatePaths(
                     executableName))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            return Path.GetFullPath(candidate);
        }

#if DEBUG
        string recovery =
            $"place a development copy under 'Tools\\FFmpeg', or set " +
            $"{GetOverrideEnvironmentVariable(executableName)} explicitly.";
#else
        const string recovery =
            "repair the Base media-tools pack in Settings.";
#endif

        throw new MediaToolNotFoundException(
            $"Replay Foundry cannot find a verified {executableName} runtime. " +
            $"Please {recovery}");
    }

    private IEnumerable<string> GetCandidatePaths(
        string executableName)
    {
#if DEBUG
        string overrideEnvironmentVariable =
            GetOverrideEnvironmentVariable(
                executableName);

        string? configuredPath =
            ExplicitRuntimeEnvironment.Read(
                overrideEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string trimmed =
                configuredPath
                    .Trim()
                    .Trim('"');

            yield return Directory.Exists(trimmed)
                ? Path.Combine(trimmed, executableName)
                : trimmed;
        }
#endif

        string? packagedPath = string.Equals(
                executableName,
                "ffprobe.exe",
                StringComparison.OrdinalIgnoreCase)
            ? _runtimeEnvironment.FfprobePath
            : _runtimeEnvironment.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(packagedPath))
        {
            yield return packagedPath;
        }

#if DEBUG
        string applicationDirectory =
            AppContext.BaseDirectory;

        yield return Path.Combine(
            applicationDirectory,
            "Tools",
            "FFmpeg",
            executableName);

        yield return Path.Combine(
            applicationDirectory,
            "Tools",
            "FFmpeg",
            "bin",
            executableName);

        yield return Path.Combine(
            applicationDirectory,
            executableName);

        yield return Path.GetFullPath(
            Path.Combine(
                applicationDirectory,
                "..",
                "..",
                "..",
                "Tools",
                "FFmpeg",
                executableName));

        yield return Path.GetFullPath(
            Path.Combine(
                applicationDirectory,
                "..",
                "..",
                "..",
                "Tools",
                "FFmpeg",
                "bin",
                executableName));

        string? pathValue =
            Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (string entry in
                 pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            string directory =
                entry.Trim('"');

            if (directory.Length == 0)
            {
                continue;
            }

            yield return Path.Combine(
                directory,
                executableName);
        }
#endif
    }

#if DEBUG
    private static string GetOverrideEnvironmentVariable(
        string executableName)
    {
        return string.Equals(
                executableName,
                "ffprobe.exe",
                StringComparison.OrdinalIgnoreCase)
            ? FfprobeOverrideVariable
            : FfmpegOverrideVariable;
    }
#endif
}
