using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ReplayFoundry.RuntimePacks;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Storage;

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
        new(0, 8, 25);

    private static readonly Version MinimumQwenModelVersion =
        new(4, 0, 21);

    private static readonly ReplayFoundryRuntimePackKind[] RuntimePackKinds =
        Enum.GetValues<ReplayFoundryRuntimePackKind>();

    private static readonly Lazy<ReplayFoundryRuntimeEnvironment> CurrentLazy =
        new(() => Discover(new ReplayFoundryRuntimePackStorePaths(
            ReplayFoundryLocalDataPaths.Current.SharedRuntimeRoot)));

    private readonly ReadOnlyCollection<ReplayFoundryRuntimeCapabilityStatus> _capabilities;

    private ReplayFoundryRuntimeEnvironment(
        string? ffmpegPath,
        string? ffprobePath,
        string? sileroModelPath,
        string? whisperVadModelPath,
        string? whisperExecutablePath,
        string? whisperModelPath,
        QwenRuntimePaths? qwen,
        string? qwenUnavailableReason,
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
        QwenUnavailableReason = qwenUnavailableReason;
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
    public string? QwenUnavailableReason { get; }
    public IReadOnlyList<ReplayFoundryRuntimeCapabilityStatus> Capabilities => _capabilities;
    public string PackageStoreRoot { get; }
    public bool IsBaseReady => FfmpegPath is not null && FfprobePath is not null;
    public bool IsBalancedReady => IsBaseReady && SileroModelPath is not null;
    public bool IsThoroughReady => IsBalancedReady && Qwen is not null;

    internal static ReplayFoundryRuntimeEnvironment Discover(
        ReplayFoundryRuntimePackStorePaths paths)
    {
        using var store = new ReplayFoundryRuntimePackStore(paths);
        IReadOnlyList<ReplayFoundryRuntimePackResolution> snapshot =
            ResolveSnapshot(store);
        InstalledReplayFoundryRuntimePack? media = TryResolve(
            snapshot,
            ReplayFoundryRuntimePackKind.MediaTools);
        InstalledReplayFoundryRuntimePack? speech = TryResolve(
            snapshot,
            ReplayFoundryRuntimePackKind.SpeechActivity);
        InstalledReplayFoundryRuntimePack? transcriptionRuntime = TryResolve(
            snapshot,
            ReplayFoundryRuntimePackKind.TranscriptionRuntime);
        InstalledReplayFoundryRuntimePack? transcriptionModel = TryResolve(
            snapshot,
            ReplayFoundryRuntimePackKind.TranscriptionModel);
        InstalledReplayFoundryRuntimePack? visualRuntime = TryResolve(
            snapshot,
            ReplayFoundryRuntimePackKind.VisualRuntime);
        InstalledReplayFoundryRuntimePack? visualModel = TryResolve(
            snapshot,
            ReplayFoundryRuntimePackKind.VisualModel);

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
        return new(
            ffmpeg,
            ffprobe,
            silero,
            whisperVad,
            whisperExe,
            whisperModel,
            qwen,
            qwenUsable ? null : qwenDetail,
            capabilities,
            paths.RootDirectory);
    }

    private static IReadOnlyList<ReplayFoundryRuntimePackResolution>
        ResolveSnapshot(ReplayFoundryRuntimePackStore store)
    {
        try
        {
            // Composition consumes one immutable startup snapshot. Runtime-pack
            // maintenance is external and takes effect after the next app start.
            return Task.Run(() => store.ResolveActiveSnapshotAsync(
                    RuntimePackKinds))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
                UnauthorizedAccessException)
        {
            SafeDiagnosticTrace.Write(
                "Runtime pack selection is unavailable",
                exception);
            return [];
        }
    }

    private static InstalledReplayFoundryRuntimePack? TryResolve(
        IReadOnlyList<ReplayFoundryRuntimePackResolution> snapshot,
        ReplayFoundryRuntimePackKind kind)
    {
        ReplayFoundryRuntimePackResolution? resolution =
            snapshot.SingleOrDefault(item => item.Kind == kind);
        if (resolution?.Failure is not null)
            SafeDiagnosticTrace.Write(
                $"Runtime pack {kind} is unavailable",
                resolution.Failure);
        return resolution?.Pack;
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

    internal static bool HasMatchingQwenQualification(
        InstalledReplayFoundryRuntimePack? runtime,
        InstalledReplayFoundryRuntimePack? model)
    {
        if (runtime is null || model is null) return false;
        try
        {
            ReplayFoundryRuntimePackFile python = runtime.Manifest.Entry(
                    ReplayFoundryRuntimeFileRole.PythonExecutable) ??
                throw new InvalidDataException(
                    "The verified Qwen runtime has no Python executable.");
            string qualificationLock = model.Resolve(
                ReplayFoundryRuntimeFileRole.QwenQualificationLock);
            return QualificationAuthorizesVerifiedHash(
                python.Sha256,
                qualificationLock);
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
            return DescribePackRequirementFailure(
                runtime.Manifest,
                "Qwen runtime",
                QwenRuntimePackageId,
                ReplayFoundryRuntimePackKind.VisualRuntime,
                MinimumQwenRuntimeVersion);
        }

        if (!IsCurrentPack(
                model.Manifest,
                QwenModelPackageId,
                ReplayFoundryRuntimePackKind.VisualModel,
                MinimumQwenModelVersion))
        {
            return DescribePackRequirementFailure(
                model.Manifest,
                "Qwen model pack",
                QwenModelPackageId,
                ReplayFoundryRuntimePackKind.VisualModel,
                MinimumQwenModelVersion);
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

    private static string DescribePackRequirementFailure(
        ReplayFoundryRuntimePackManifest manifest,
        string displayName,
        string expectedPackageId,
        ReplayFoundryRuntimePackKind expectedKind,
        Version minimumVersion)
    {
        if (!string.Equals(
                manifest.Identity.PackageId,
                expectedPackageId,
                StringComparison.OrdinalIgnoreCase) ||
            manifest.Identity.Kind != expectedKind)
        {
            return
                $"The active {displayName} has unexpected identity " +
                $"'{manifest.Identity.PackageId}' ({manifest.Identity.Kind}). " +
                $"Expected '{expectedPackageId}' ({expectedKind}) at version " +
                $"{minimumVersion} or newer. Repair or update Advanced AI.";
        }

        if (!Version.TryParse(
                manifest.Identity.SemanticVersion,
                out Version? installedVersion))
        {
            return
                $"The active {displayName} reports invalid version " +
                $"'{manifest.Identity.SemanticVersion}'; required version " +
                $"{minimumVersion} or newer. Repair or update Advanced AI.";
        }

        return
            $"The active {displayName} is incompatible. Installed version " +
            $"{installedVersion}; required version {minimumVersion} or newer. " +
            "Repair or update Advanced AI.";
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

    internal static bool QualificationAuthorizes(
        string pythonPath,
        string qualificationLockPath)
    {
        try
        {
            using FileStream stream = new(
                pythonPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            string pythonHash = Convert.ToHexString(SHA256.HashData(stream));
            return QualificationAuthorizesVerifiedHash(
                pythonHash,
                qualificationLockPath);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or
                InvalidOperationException or UnauthorizedAccessException or
                CryptographicException)
        {
            return false;
        }
    }

    private static bool QualificationAuthorizesVerifiedHash(
        string verifiedPythonSha256,
        string qualificationLockPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(qualificationLockPath));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty(
                    "pythonExecutableSha256",
                    out JsonElement hashElement) ||
                !root.TryGetProperty(
                    "capabilitySucceeded",
                    out JsonElement capabilityElement) ||
                !root.TryGetProperty(
                    "unconstrainedFallbackPermitted",
                    out JsonElement fallbackElement) ||
                !root.TryGetProperty(
                    "semanticRepairPermitted",
                    out JsonElement repairElement) ||
                capabilityElement.ValueKind != JsonValueKind.True ||
                fallbackElement.ValueKind != JsonValueKind.False ||
                repairElement.ValueKind != JsonValueKind.False)
                return false;
            string? expected = hashElement.GetString();
            return string.Equals(
                verifiedPythonSha256,
                expected,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or
                InvalidOperationException or UnauthorizedAccessException)
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
            ["HF_DATASETS_OFFLINE"] = "1",
            ["HF_HUB_DISABLE_TELEMETRY"] = "1",
            ["DO_NOT_TRACK"] = "1",
            ["TOKENIZERS_PARALLELISM"] = "false",
            ["TRANSFORMERS_NO_ADVISORY_WARNINGS"] = "1",
            ["FORCE_QWENVL_VIDEO_READER"] = "torchcodec",
            ["PYTHONDONTWRITEBYTECODE"] = "1",
            // The bounded worker has no APPDATA. Give CUDA its normal per-user
            // kernel cache explicitly so each worker can reuse compiled kernels.
            ["CUDA_CACHE_PATH"] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NVIDIA", "ComputeCache"),
            // PyTorch asks getpass.getuser() for its cache namespace while
            // TorchCodec imports. A scrubbed Windows environment otherwise
            // falls through to the unavailable Unix-only pwd module.
            ["USERNAME"] = "ReplayFoundry",
        };
        foreach (string name in new[]
                 {
                     "SYSTEMROOT", "WINDIR", "TEMP", "TMP",
                     "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE",
                 })
        {
            string? value = Environment.GetEnvironmentVariable(
                name,
                EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(value)) environment[name] = value;
        }
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
