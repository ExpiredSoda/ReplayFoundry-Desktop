using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ReplayFoundry.RuntimePacks;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.RuntimePacks;

public sealed record ReplayFoundryRuntimeCapabilityStatus(
    string Name,
    bool IsAvailable,
    string Status,
    string Storage,
    string License,
    string? Detail = null);

public sealed class ReplayFoundryRuntimeEnvironment
{
    private const string MediaToolsPackageId =
        "replayfoundry-media-tools";

    private const string QwenRuntimePackageId =
        "replayfoundry-qwen3-vl-runtime";

    private const string QwenModelPackageId =
        "replayfoundry-qwen3-vl-4b-instruct";

    private static readonly Version MinimumMediaToolsVersion =
        new(8, 1, 2, 32);

    private static readonly Version MinimumQwenRuntimeVersion =
        new(0, 8, 21);

    private static readonly Version MinimumQwenModelVersion =
        new(4, 0, 17);

    private static readonly Lazy<ReplayFoundryRuntimeEnvironment> CurrentLazy =
        new(() => Discover(ReplayFoundryRuntimePackStorePaths.CreateDefault()));

    private readonly ReadOnlyCollection<ReplayFoundryRuntimeCapabilityStatus> _capabilities;

    private ReplayFoundryRuntimeEnvironment(
        string? ffmpegPath,
        string? ffprobePath,
        string? sileroModelPath,
        string? whisperVadModelPath,
        string? whisperExecutablePath,
        string? whisperModelPath,
        QwenRuntimePaths? qwen,
        IEnumerable<ReplayFoundryRuntimeCapabilityStatus> capabilities,
        string packageStoreRoot)
    {
        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;
        SileroModelPath = sileroModelPath;
        WhisperVadModelPath = whisperVadModelPath;
        WhisperExecutablePath = whisperExecutablePath;
        WhisperModelPath = whisperModelPath;
        Qwen = qwen;
        _capabilities = Array.AsReadOnly(capabilities.ToArray());
        PackageStoreRoot = packageStoreRoot;
    }

    public static ReplayFoundryRuntimeEnvironment Current => CurrentLazy.Value;
    public string? FfmpegPath { get; }
    public string? FfprobePath { get; }
    public string? SileroModelPath { get; }
    public string? WhisperVadModelPath { get; }
    public string? WhisperExecutablePath { get; }
    public string? WhisperModelPath { get; }
    public QwenRuntimePaths? Qwen { get; }
    public IReadOnlyList<ReplayFoundryRuntimeCapabilityStatus> Capabilities => _capabilities;
    public string PackageStoreRoot { get; }
    public bool IsBaseReady => FfmpegPath is not null && FfprobePath is not null;
    public bool IsBalancedReady => IsBaseReady && SileroModelPath is not null;
    public bool IsThoroughReady => IsBalancedReady && Qwen is not null;

    internal static ReplayFoundryRuntimeEnvironment Discover(
        ReplayFoundryRuntimePackStorePaths paths)
    {
        using var store = new ReplayFoundryRuntimePackStore(paths);
        InstalledReplayFoundryRuntimePack? media = TryResolve(store, ReplayFoundryRuntimePackKind.MediaTools);
        InstalledReplayFoundryRuntimePack? speech = TryResolve(store, ReplayFoundryRuntimePackKind.SpeechActivity);
        InstalledReplayFoundryRuntimePack? transcriptionRuntime = TryResolve(store, ReplayFoundryRuntimePackKind.TranscriptionRuntime);
        InstalledReplayFoundryRuntimePack? transcriptionModel = TryResolve(store, ReplayFoundryRuntimePackKind.TranscriptionModel);
        InstalledReplayFoundryRuntimePack? visualRuntime = TryResolve(store, ReplayFoundryRuntimePackKind.VisualRuntime);
        InstalledReplayFoundryRuntimePack? visualModel = TryResolve(store, ReplayFoundryRuntimePackKind.VisualModel);

        string? ffmpeg = ResolveOptional(media, ReplayFoundryRuntimeFileRole.FfmpegExecutable);
        string? ffprobe = ResolveOptional(media, ReplayFoundryRuntimeFileRole.FfprobeExecutable);
        string? silero = ResolveOptional(speech, ReplayFoundryRuntimeFileRole.SpeechActivityModel);
        string? whisperVad = ResolveOptional(speech, ReplayFoundryRuntimeFileRole.WhisperVadModel);
        string? whisperExe = ResolveOptional(transcriptionRuntime, ReplayFoundryRuntimeFileRole.WhisperExecutable);
        string? whisperModel = ResolveOptional(transcriptionModel, ReplayFoundryRuntimeFileRole.WhisperModel);
        string? qwenPackFailure;
        QwenRuntimePaths? compatibleQwen =
            visualRuntime is null || visualModel is null || media is null
                ? MissingQwenPackSet(
                    out qwenPackFailure)
                : CreateCompatibleQwenPaths(
                    visualRuntime,
                    visualModel,
                    media,
                    out qwenPackFailure);
        bool cudaDriverAvailable = HasNvidiaCudaDriver();
        bool qualificationMatches = compatibleQwen is not null &&
            HasMatchingQwenQualification(
                visualRuntime,
                visualModel);
        bool qwenUsable = compatibleQwen is not null &&
            cudaDriverAvailable &&
            qualificationMatches;
        string qwenDetail = qwenPackFailure ??
            (!cudaDriverAvailable
                ? "A compatible NVIDIA CUDA driver was not detected."
                : !qualificationMatches
                    ? "The installed structured-decoding lock does not authorize this packaged Python runtime. Repair or update Advanced AI."
                    : "CUDA driver and deployment-qualified Python runtime detected; model-load memory checks still apply.");
        QwenRuntimePaths? qwen = qwenUsable
            ? compatibleQwen
            : null;

        var capabilities = new[]
        {
            Status("Deterministic media analysis", media, "LGPL/OpenH264 notices"),
            Status("Speech activity", speech, "MIT"),
            Status("Local transcription runtime", transcriptionRuntime, "MIT"),
            Status("Multilingual transcription model", transcriptionModel, "MIT · provisional research selection"),
            Status("Qwen visual runtime", visualRuntime, "Python/PyTorch component notices", qwenUsable, qwenDetail),
            Status("Qwen3-VL 4B model", visualModel, "Apache-2.0 · locally qualified AI", qwenUsable,
                qwenUsable ? "Locally qualified for Replay Foundry's bounded, reviewable metadata workflow." : qwenDetail),
        };
        return new(ffmpeg, ffprobe, silero, whisperVad, whisperExe, whisperModel, qwen, capabilities, paths.RootDirectory);
    }

    private static InstalledReplayFoundryRuntimePack? TryResolve(
        ReplayFoundryRuntimePackStore store,
        ReplayFoundryRuntimePackKind kind)
    {
        try
        {
            // Runtime discovery is exposed synchronously to the composition root, but
            // the store intentionally performs asynchronous file IO. Run the entire
            // operation away from a WPF SynchronizationContext so startup cannot
            // deadlock while an awaited continuation tries to return to the UI thread.
            return Task.Run(() => store.ResolveActiveAsync(kind))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SafeDiagnosticTrace.Write(
                $"Runtime pack {kind} is unavailable",
                exception);
            return null;
        }
    }

    private static string? ResolveOptional(
        InstalledReplayFoundryRuntimePack? pack,
        ReplayFoundryRuntimeFileRole role) => pack is null ? null : pack.Resolve(role);

    private static ReplayFoundryRuntimeCapabilityStatus Status(
        string name,
        InstalledReplayFoundryRuntimePack? pack,
        string license,
        bool isUsable = true,
        string? detail = null) => pack is null
        ? new(name, false, "Not installed", "Per-user runtime pack", license)
        : new(
            name,
            isUsable,
            isUsable
                ? $"Ready · {pack.Manifest.Identity.SemanticVersion}"
                : $"Installed · unavailable · {pack.Manifest.Identity.SemanticVersion}",
            FormatBytes(pack.Manifest.Files.Sum(file => file.ByteLength)),
            license,
            detail is null ? pack.Manifest.ManifestHash : detail + " Manifest " + pack.Manifest.ManifestHash);

    private static bool HasNvidiaCudaDriver()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!NativeLibrary.TryLoad("nvcuda.dll", out nint handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }

    private static bool HasMatchingQwenQualification(
        InstalledReplayFoundryRuntimePack? runtime,
        InstalledReplayFoundryRuntimePack? model)
    {
        if (runtime is null || model is null) return false;
        try
        {
            string python = runtime.Resolve(ReplayFoundryRuntimeFileRole.PythonExecutable);
            string qualificationLock = model.Resolve(ReplayFoundryRuntimeFileRole.QwenQualificationLock);
            return QualificationAuthorizes(python, qualificationLock);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            SafeDiagnosticTrace.Write(
                "Qwen deployment qualification is unavailable",
                exception);
            return false;
        }
    }

    internal static QwenRuntimePaths? CreateCompatibleQwenPaths(
        InstalledReplayFoundryRuntimePack runtime,
        InstalledReplayFoundryRuntimePack model,
        InstalledReplayFoundryRuntimePack media,
        out string? incompatibility)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(media);

        incompatibility = GetQwenPackSetIncompatibility(
            runtime,
            model,
            media);
        return incompatibility is null
            ? CreateQwenPaths(
                runtime,
                model,
                media)
            : null;
    }

    private static QwenRuntimePaths? MissingQwenPackSet(
        out string? incompatibility)
    {
        incompatibility =
            "The installed Advanced AI pack set is incomplete. Repair or update Advanced AI.";
        return null;
    }

    private static string? GetQwenPackSetIncompatibility(
        InstalledReplayFoundryRuntimePack runtime,
        InstalledReplayFoundryRuntimePack model,
        InstalledReplayFoundryRuntimePack media)
    {
        if (!IsCurrentPack(
                media.Manifest,
                MediaToolsPackageId,
                ReplayFoundryRuntimePackKind.MediaTools,
                MinimumMediaToolsVersion))
        {
            return
                "The active media-tools pack is older than the Advanced AI runtime requires. Repair or update Advanced AI.";
        }

        if (!IsCurrentPack(
                runtime.Manifest,
                QwenRuntimePackageId,
                ReplayFoundryRuntimePackKind.VisualRuntime,
                MinimumQwenRuntimeVersion))
        {
            return
                "The active Qwen runtime is older than 0.8.21 or has an unexpected identity. Repair or update Advanced AI.";
        }

        if (!IsCurrentPack(
                model.Manifest,
                QwenModelPackageId,
                ReplayFoundryRuntimePackKind.VisualModel,
                MinimumQwenModelVersion))
        {
            return
                "The active Qwen model pack is older than 4.0.17 or has an unexpected identity. Repair or update Advanced AI.";
        }

        if (!HasExactCurrentDependency(
                runtime.Manifest,
                media.Manifest,
                MediaToolsPackageId,
                MinimumMediaToolsVersion))
        {
            return
                "The active Qwen runtime is not sealed to the active media-tools manifest and version. Repair or update Advanced AI.";
        }

        if (!HasExactCurrentDependency(
                model.Manifest,
                runtime.Manifest,
                QwenRuntimePackageId,
                MinimumQwenRuntimeVersion))
        {
            return
                "The active Qwen model is not sealed to the active Qwen runtime manifest and version. Repair or update Advanced AI.";
        }

        return null;
    }

    private static bool IsCurrentPack(
        ReplayFoundryRuntimePackManifest manifest,
        string packageId,
        ReplayFoundryRuntimePackKind kind,
        Version minimumVersion) =>
        string.Equals(
            manifest.Identity.PackageId,
            packageId,
            StringComparison.OrdinalIgnoreCase) &&
        manifest.Identity.Kind == kind &&
        Version.TryParse(
            manifest.Identity.SemanticVersion,
            out Version? version) &&
        version >= minimumVersion;

    private static bool HasExactCurrentDependency(
        ReplayFoundryRuntimePackManifest owner,
        ReplayFoundryRuntimePackManifest activeDependency,
        string packageId,
        Version minimumVersion)
    {
        ReplayFoundryRuntimePackDependency? dependency =
            owner.Dependencies.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.PackageId,
                    packageId,
                    StringComparison.OrdinalIgnoreCase));
        return dependency is not null &&
            dependency.RequiredManifestHash is not null &&
            Version.TryParse(
                dependency.MinimumVersion,
                out Version? dependencyMinimum) &&
            dependencyMinimum >= minimumVersion &&
            dependency.Accepts(
                activeDependency);
    }

    internal static bool QualificationAuthorizes(string pythonPath, string qualificationLockPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(qualificationLockPath));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("pythonExecutableSha256", out JsonElement hashElement) ||
                !root.TryGetProperty("capabilitySucceeded", out JsonElement capabilityElement) ||
                !root.TryGetProperty("unconstrainedFallbackPermitted", out JsonElement fallbackElement) ||
                !root.TryGetProperty("semanticRepairPermitted", out JsonElement repairElement) ||
                capabilityElement.ValueKind != JsonValueKind.True ||
                fallbackElement.ValueKind != JsonValueKind.False ||
                repairElement.ValueKind != JsonValueKind.False)
                return false;
            string? expected = hashElement.GetString();
            using FileStream stream = new(pythonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    internal static QwenRuntimePaths CreateQwenPaths(
        InstalledReplayFoundryRuntimePack runtime,
        InstalledReplayFoundryRuntimePack model,
        InstalledReplayFoundryRuntimePack media)
    {
        string python = runtime.Resolve(ReplayFoundryRuntimeFileRole.PythonExecutable);
        string pythonHome = Path.GetDirectoryName(python)!;
        string pythonPath = Path.Combine(runtime.RootDirectory, "site-packages");
        string hostPath = Path.Combine(runtime.RootDirectory, "host");
        string mediaDirectory = Path.GetDirectoryName(media.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable))!;
        IReadOnlyDictionary<string, string> environment = BuildQwenEnvironment(
            pythonHome, pythonPath, hostPath, mediaDirectory);
        return new QwenRuntimePaths(
            python,
            runtime.Resolve(ReplayFoundryRuntimeFileRole.VisualHostScript),
            mediaDirectory,
            model.Resolve(ReplayFoundryRuntimeFileRole.QwenModelManifest),
            model.Resolve(ReplayFoundryRuntimeFileRole.QwenPromptManifest),
            model.Resolve(ReplayFoundryRuntimeFileRole.QwenQualificationLock),
            Path.Combine(model.RootDirectory, "model"),
            environment);
    }

    internal static IReadOnlyDictionary<string, string> BuildQwenEnvironment(
        string pythonHome,
        string pythonPath,
        string hostPath,
        string mediaDirectory)
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string boundedPath = string.Join(Path.PathSeparator,
        [
            pythonHome,
            Path.Combine(pythonHome, "DLLs"),
            Path.Combine(pythonPath, "torch", "lib"),
            Path.Combine(pythonPath, "tvm_ffi", "lib"),
            mediaDirectory,
            Path.Combine(windowsDirectory, "System32"),
            windowsDirectory,
        ]);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PYTHONHOME"] = pythonHome,
            ["PYTHONPATH"] = string.Join(Path.PathSeparator, hostPath, pythonPath),
            ["PATH"] = boundedPath,
            ["HF_HUB_OFFLINE"] = "1",
            ["TRANSFORMERS_OFFLINE"] = "1",
            ["PYTHONDONTWRITEBYTECODE"] = "1",
        };
        return new ReadOnlyDictionary<string, string>(environment);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GiB local",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MiB local",
        _ => $"{bytes / 1024d:0.0} KiB local",
    };
}

public sealed record QwenRuntimePaths(
    string PythonExecutablePath,
    string HostScriptPath,
    string FfmpegSharedDirectoryPath,
    string ModelManifestPath,
    string PromptManifestPath,
    string QualificationLockPath,
    string ModelDirectoryPath,
    IReadOnlyDictionary<string, string> EnvironmentVariables);
