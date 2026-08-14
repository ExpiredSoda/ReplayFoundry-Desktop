using System.IO;

namespace ReplayFoundry.Desktop.Platform.RuntimePacks;

internal sealed record QwenRuntimeSelection(
    string PythonExecutablePath,
    string HostScriptPath,
    string FfmpegSharedDirectoryPath,
    string ModelManifestPath,
    string PromptManifestPath,
    string QualificationLockPath,
    string? ModelDirectoryOverride,
    IReadOnlyDictionary<string, string>? EnvironmentVariables);

internal static class QwenRuntimeResolver
{
#if DEBUG
    internal const string DevelopmentOverrideOptInVariable =
        "REPLAYFOUNDRY_ENABLE_QWEN_DEVELOPMENT_OVERRIDES";
#endif

    internal static QwenRuntimeSelection? Resolve(
        QwenRuntimePaths? verifiedActivePack)
    {
#if DEBUG
        return string.Equals(
            ExplicitRuntimeEnvironment.Read(
                DevelopmentOverrideOptInVariable),
            "1",
            StringComparison.Ordinal)
                ? ResolveDevelopmentCandidates(
                    verifiedActivePack,
                    ExplicitRuntimeEnvironment.Read,
                    ResolveCheckedOutHostScript())
                : FromVerifiedActivePack(
                    verifiedActivePack);
#else
        return FromVerifiedActivePack(
            verifiedActivePack);
#endif
    }

    private static QwenRuntimeSelection? FromVerifiedActivePack(
        QwenRuntimePaths? verifiedActivePack) =>
        verifiedActivePack is null
            ? null
            : new QwenRuntimeSelection(
                verifiedActivePack.PythonExecutablePath,
                verifiedActivePack.HostScriptPath,
                verifiedActivePack.FfmpegSharedDirectoryPath,
                verifiedActivePack.ModelManifestPath,
                verifiedActivePack.PromptManifestPath,
                verifiedActivePack.QualificationLockPath,
                verifiedActivePack.ModelDirectoryPath,
                verifiedActivePack.EnvironmentVariables);

#if DEBUG
    internal static QwenRuntimeSelection? ResolveDevelopmentCandidates(
        QwenRuntimePaths? verifiedActivePack,
        Func<string, string?> readDevelopmentCandidate,
        string? checkedOutHostScript = null)
    {
        ArgumentNullException.ThrowIfNull(
            readDevelopmentCandidate);
        string? explicitPython = readDevelopmentCandidate(
            "REPLAYFOUNDRY_QWEN_PYTHON");
        string? explicitHostScript = readDevelopmentCandidate(
            "REPLAYFOUNDRY_QWEN_HOST_SCRIPT") ??
            checkedOutHostScript;
        string? explicitFfmpegDirectory = readDevelopmentCandidate(
            "REPLAYFOUNDRY_QWEN_FFMPEG_SHARED");
        string? explicitModelManifest = readDevelopmentCandidate(
            "REPLAYFOUNDRY_QWEN_MODEL_MANIFEST");
        string? explicitPromptManifest = readDevelopmentCandidate(
            "REPLAYFOUNDRY_QWEN_PROMPT_MANIFEST");
        string? explicitQualificationLock = readDevelopmentCandidate(
            "REPLAYFOUNDRY_QWEN_QUALIFICATION_LOCK");
        if (new[]
            {
                explicitPython,
                explicitHostScript,
                explicitFfmpegDirectory,
                explicitModelManifest,
                explicitPromptManifest,
                explicitQualificationLock,
            }.All(static candidate => candidate is null))
        {
            return FromVerifiedActivePack(
                verifiedActivePack);
        }

        bool usePackagedModel =
            explicitModelManifest is null && verifiedActivePack is not null;
        string? python = explicitPython ??
            verifiedActivePack?.PythonExecutablePath;
        string? hostScript = explicitHostScript ??
            verifiedActivePack?.HostScriptPath;
        string? ffmpegDirectory = explicitFfmpegDirectory ??
            verifiedActivePack?.FfmpegSharedDirectoryPath;
        string? modelManifest = explicitModelManifest ??
            verifiedActivePack?.ModelManifestPath;
        string? promptManifest = explicitPromptManifest ??
            verifiedActivePack?.PromptManifestPath;
        string? qualificationLock = explicitQualificationLock ??
            verifiedActivePack?.QualificationLockPath;
        if (new[]
            {
                python,
                hostScript,
                ffmpegDirectory,
                modelManifest,
                promptManifest,
                qualificationLock,
            }.Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        bool usesVerifiedPython = verifiedActivePack is not null &&
            PathsReferToSameFile(
                python!,
                verifiedActivePack.PythonExecutablePath);

        return new QwenRuntimeSelection(
            python!,
            hostScript!,
            ffmpegDirectory!,
            modelManifest!,
            promptManifest!,
            qualificationLock!,
            usePackagedModel
                ? verifiedActivePack!.ModelDirectoryPath
                : null,
            usesVerifiedPython
                ? verifiedActivePack?.EnvironmentVariables
                : null);
    }

    private static bool PathsReferToSameFile(
        string first,
        string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private static string? ResolveCheckedOutHostScript()
    {
        string candidate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "eng",
            "visual-semantic-host",
            "qwen3_vl_batch_host.py"));
        return File.Exists(candidate) ? candidate : null;
    }
#endif
}
