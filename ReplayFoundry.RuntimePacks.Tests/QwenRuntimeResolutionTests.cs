using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Platform.RuntimePacks;
using ReplayFoundry.RuntimePacks;

namespace ReplayFoundry.RuntimePacks.Tests;

internal static class QwenRuntimeResolutionTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Qwen defaults to the verified active pack in every build",
            QwenHonorsBuildBoundary),
        new(
            "Qwen cannot divert Release when verified active packs are missing",
            MissingVerifiedPacksDoNotDivertRelease),
        new(
            "Qwen cannot divert Release when an active pack is corrupt",
            CorruptVerifiedPackDoesNotDivertRelease),
        new(
            "Qwen rejects a valid but stale active runtime",
            ValidStaleRuntimeFailsClosed),
        new(
            "Qwen rejects a valid but stale active model",
            ValidStaleModelFailsClosed),
        new(
            "Qwen rejects a model sealed to a different active runtime",
            ModelRuntimeManifestMismatchFailsClosed),
        new(
            "Qwen rejects a runtime sealed to different active media tools",
            RuntimeMediaManifestMismatchFailsClosed),
#if DEBUG
        new(
            "Debug Qwen requires exact opt-in before reading development overrides",
            DebugRequiresDevelopmentOptIn),
        new(
            "Debug Qwen retains field-level override then verified-pack fallback order",
            DebugRetainsPartialOverrideOrder),
        new(
            "Debug Qwen retains verified dependencies for an explicit verified Python path",
            DebugRetainsVerifiedPythonEnvironment),
        new(
            "Debug Qwen uses the checked-out host with the verified active pack",
            DebugUsesCheckedOutHost),
#endif
    ];

    private static async Task QwenHonorsBuildBoundary()
    {
        using var fixture = new Fixture();
        QwenRuntimePaths verified =
            await fixture.InstallAndResolveVerifiedAsync();
        using var development =
            new DevelopmentCandidateScope(
                fixture.Root);

        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.Resolve(
                verified);
        Assert(
            selection is not null,
            "Qwen resolution rejected all available candidates.");

        AssertVerifiedSelection(
            selection!,
            verified);
        development.AssertUnchanged();
    }

    private static Task MissingVerifiedPacksDoNotDivertRelease()
    {
        using var fixture = new Fixture();
        using var development =
            new DevelopmentCandidateScope(
                fixture.Root);
        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.Resolve(
                verifiedActivePack: null);

        Assert(
            selection is null,
            "Qwen accepted an unverified development override without explicit opt-in and active packs.");
        development.AssertUnchanged();
        return Task.CompletedTask;
    }

    private static async Task CorruptVerifiedPackDoesNotDivertRelease()
    {
        using var fixture = new Fixture();
        _ = await fixture.InstallAndResolveVerifiedAsync();
        using var development =
            new DevelopmentCandidateScope(
                fixture.Root);
        await File.AppendAllTextAsync(
            fixture.ActivePythonPath!,
            "corrupt");
        QwenRuntimePaths? corrupt =
            await fixture.TryResolveVerifiedAsync();
        Assert(
            corrupt is null,
            "Runtime-pack verification exposed a corrupt active Qwen runtime.");

        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.Resolve(
                corrupt);
        Assert(
            selection is null,
            "Qwen diverted to a development override after active-pack corruption without explicit opt-in.");
        development.AssertUnchanged();
    }

    private static async Task ValidStaleRuntimeFailsClosed()
    {
        using var fixture = new Fixture();
        await fixture.InstallValidStaleRuntimeAsync();
        AssertIncompatiblePackSetFailsClosed(
            fixture,
            "older than 0.8.21");
    }

    private static async Task ValidStaleModelFailsClosed()
    {
        using var fixture = new Fixture();
        await fixture.InstallValidStaleModelAsync();
        AssertIncompatiblePackSetFailsClosed(
            fixture,
            "older than 4.0.17");
    }

    private static async Task ModelRuntimeManifestMismatchFailsClosed()
    {
        using var fixture = new Fixture();
        await fixture.InstallModelRuntimeMismatchAsync();
        AssertIncompatiblePackSetFailsClosed(
            fixture,
            "not sealed to the active Qwen runtime manifest and version");
    }

    private static async Task RuntimeMediaManifestMismatchFailsClosed()
    {
        using var fixture = new Fixture();
        await fixture.InstallRuntimeMediaMismatchAsync();
        AssertIncompatiblePackSetFailsClosed(
            fixture,
            "not sealed to the active media-tools manifest and version");
    }

    private static void AssertIncompatiblePackSetFailsClosed(
        Fixture fixture,
        string expectedStatus)
    {
        ReplayFoundryRuntimeEnvironment environment =
            ReplayFoundryRuntimeEnvironment.Discover(
                fixture.Paths);
        Assert(
            environment.Qwen is null,
            "Runtime discovery exposed an incompatible active Qwen pack set.");
        ReplayFoundryRuntimeCapabilityStatus[] qwenStatuses =
            environment.Capabilities
                .Where(status => status.Name.Contains(
                    "Qwen",
                    StringComparison.Ordinal))
                .ToArray();
        Assert(
            qwenStatuses.Length == 2 &&
            qwenStatuses.All(status => !status.IsAvailable) &&
            qwenStatuses.All(status =>
                status.Detail?.Contains(
                    expectedStatus,
                    StringComparison.Ordinal) == true) &&
            qwenStatuses.All(status =>
                status.Detail?.Contains(
                    "Repair or update Advanced AI",
                    StringComparison.Ordinal) == true),
            "Settings did not explain the incompatible Advanced AI pack set.");

        using var development =
            new DevelopmentCandidateScope(
                fixture.Root);
        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.Resolve(
                environment.Qwen);
        Assert(
            selection is null,
            "Qwen accepted an incompatible active pack set or a development override without explicit opt-in.");
        development.AssertUnchanged();
    }

#if DEBUG
    private static async Task DebugRequiresDevelopmentOptIn()
    {
        using var fixture = new Fixture();
        QwenRuntimePaths verified =
            await fixture.InstallAndResolveVerifiedAsync();
        using var development =
            new DevelopmentCandidateScope(
                fixture.Root,
                enableOverrides: true);

        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.Resolve(
                verified);
        Assert(
            selection is not null,
            "Debug Qwen rejected an explicitly enabled development runtime.");
        AssertDevelopmentSelection(
            selection!,
            development);
        development.AssertUnchanged();
    }

    private static async Task DebugRetainsPartialOverrideOrder()
    {
        using var fixture = new Fixture();
        QwenRuntimePaths verified =
            await fixture.InstallAndResolveVerifiedAsync();
        string hostOverride =
            Path.Combine(
                fixture.Root,
                "partial-host.py");
        await File.WriteAllTextAsync(
            hostOverride,
            "partial-host");
        var values = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["REPLAYFOUNDRY_QWEN_HOST_SCRIPT"] = hostOverride,
        };

        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.ResolveDevelopmentCandidates(
                verified,
                name => values.GetValueOrDefault(name));
        Assert(
            selection is not null &&
            selection.HostScriptPath == hostOverride &&
            selection.PythonExecutablePath == verified.PythonExecutablePath &&
            selection.FfmpegSharedDirectoryPath ==
                verified.FfmpegSharedDirectoryPath &&
            selection.ModelManifestPath == verified.ModelManifestPath &&
            selection.PromptManifestPath == verified.PromptManifestPath &&
            selection.QualificationLockPath ==
                verified.QualificationLockPath &&
            selection.ModelDirectoryOverride == verified.ModelDirectoryPath &&
            ReferenceEquals(
                selection.EnvironmentVariables,
                verified.EnvironmentVariables),
            "Debug Qwen did not preserve field-level override then verified-pack fallback behavior.");
    }

    private static async Task DebugUsesCheckedOutHost()
    {
        using var fixture = new Fixture();
        QwenRuntimePaths verified =
            await fixture.InstallAndResolveVerifiedAsync();
        string checkedOutHost = Path.Combine(
            fixture.Root,
            "checked-out-host.py");
        await File.WriteAllTextAsync(
            checkedOutHost,
            "checked-out-host");

        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.ResolveDevelopmentCandidates(
                verified,
                static _ => null,
                checkedOutHost);
        Assert(
            selection is not null &&
            selection.HostScriptPath == checkedOutHost &&
            selection.PythonExecutablePath == verified.PythonExecutablePath &&
            selection.ModelManifestPath == verified.ModelManifestPath &&
            selection.ModelDirectoryOverride == verified.ModelDirectoryPath &&
            ReferenceEquals(
                selection.EnvironmentVariables,
                verified.EnvironmentVariables),
            "Debug Qwen did not combine the checked-out host with the verified active pack.");
    }

    private static async Task DebugRetainsVerifiedPythonEnvironment()
    {
        using var fixture = new Fixture();
        QwenRuntimePaths verified =
            await fixture.InstallAndResolveVerifiedAsync();
        var values = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["REPLAYFOUNDRY_QWEN_PYTHON"] =
                verified.PythonExecutablePath.ToUpperInvariant(),
        };

        QwenRuntimeSelection? selection =
            QwenRuntimeResolver.ResolveDevelopmentCandidates(
                verified,
                name => values.GetValueOrDefault(name));

        Assert(
            selection is not null &&
            ReferenceEquals(
                selection.EnvironmentVariables,
                verified.EnvironmentVariables),
            "Debug Qwen discarded verified dependencies when its explicit Python path selected the verified runtime.");
    }
#endif

    private static void AssertVerifiedSelection(
        QwenRuntimeSelection selection,
        QwenRuntimePaths verified)
    {
        Assert(
            selection.PythonExecutablePath == verified.PythonExecutablePath &&
            selection.HostScriptPath == verified.HostScriptPath &&
            selection.FfmpegSharedDirectoryPath ==
                verified.FfmpegSharedDirectoryPath &&
            selection.ModelManifestPath == verified.ModelManifestPath &&
            selection.PromptManifestPath == verified.PromptManifestPath &&
            selection.QualificationLockPath ==
                verified.QualificationLockPath &&
            selection.ModelDirectoryOverride == verified.ModelDirectoryPath &&
            ReferenceEquals(
                selection.EnvironmentVariables,
                verified.EnvironmentVariables),
            "Release Qwen did not retain the exact verified active-pack runtime.");
    }

#if DEBUG
    private static void AssertDevelopmentSelection(
        QwenRuntimeSelection selection,
        DevelopmentCandidateScope development)
    {
        Assert(
            selection.PythonExecutablePath == development.PythonPath &&
            selection.HostScriptPath == development.HostScriptPath &&
            selection.FfmpegSharedDirectoryPath ==
                development.FfmpegDirectoryPath &&
            selection.ModelManifestPath == development.ModelManifestPath &&
            selection.PromptManifestPath == development.PromptManifestPath &&
            selection.QualificationLockPath ==
                development.QualificationLockPath &&
            selection.ModelDirectoryOverride is null &&
            selection.EnvironmentVariables is null,
            "Debug Qwen did not retain its complete explicit development runtime.");
    }
#endif

    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    private sealed class DevelopmentCandidateScope : IDisposable
    {
        private static readonly string[] VariableNames =
        [
            "REPLAYFOUNDRY_QWEN_PYTHON",
            "REPLAYFOUNDRY_QWEN_HOST_SCRIPT",
            "REPLAYFOUNDRY_QWEN_FFMPEG_SHARED",
            "REPLAYFOUNDRY_QWEN_MODEL_MANIFEST",
            "REPLAYFOUNDRY_QWEN_PROMPT_MANIFEST",
            "REPLAYFOUNDRY_QWEN_QUALIFICATION_LOCK",
#if DEBUG
            QwenRuntimeResolver.DevelopmentOverrideOptInVariable,
#endif
        ];

        private readonly IReadOnlyDictionary<string, string?> _previous;
        private readonly IReadOnlyDictionary<string, string> _active;

        public DevelopmentCandidateScope(
            string fixtureRoot,
            bool enableOverrides = false)
        {
            string root =
                Path.Combine(
                    fixtureRoot,
                    "stale-valid-development-runtime");
            FfmpegDirectoryPath =
                Path.Combine(
                    root,
                    "ffmpeg");
            Directory.CreateDirectory(
                FfmpegDirectoryPath);
            PythonPath =
                Write(
                    root,
                    "python.exe",
                    "development-python");
            HostScriptPath =
                Write(
                    root,
                    "host.py",
                    "development-host");
            ModelManifestPath =
                Write(
                    root,
                    "model-manifest.json",
                    "{}");
            PromptManifestPath =
                Write(
                    root,
                    "prompt-manifest.json",
                    "{}");
            QualificationLockPath =
                Write(
                    root,
                    "qualification-lock.json",
                    "{}");
            var active =
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    [VariableNames[0]] = PythonPath,
                    [VariableNames[1]] = HostScriptPath,
                    [VariableNames[2]] = FfmpegDirectoryPath,
                    [VariableNames[3]] = ModelManifestPath,
                    [VariableNames[4]] = PromptManifestPath,
                    [VariableNames[5]] = QualificationLockPath,
                };
#if DEBUG
            active[VariableNames[6]] = enableOverrides ? "1" : "0";
#endif
            _active = active;
            _previous = VariableNames.ToDictionary(
                static name => name,
                static name => Environment.GetEnvironmentVariable(name),
                StringComparer.Ordinal);
            foreach ((string name, string value) in _active)
            {
                Environment.SetEnvironmentVariable(
                    name,
                    value);
            }
        }

        public string PythonPath { get; }

        public string HostScriptPath { get; }

        public string FfmpegDirectoryPath { get; }

        public string ModelManifestPath { get; }

        public string PromptManifestPath { get; }

        public string QualificationLockPath { get; }

        public void AssertUnchanged()
        {
            Assert(
                _active.All(pair =>
                    Environment.GetEnvironmentVariable(pair.Key) ==
                        pair.Value),
                "Qwen resolution mutated the caller's development environment.");
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _previous)
            {
                Environment.SetEnvironmentVariable(
                    name,
                    value);
            }
        }

        private static string Write(
            string root,
            string name,
            string value)
        {
            Directory.CreateDirectory(
                root);
            string path =
                Path.Combine(
                    root,
                    name);
            File.WriteAllText(
                path,
                value);
            return path;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly DateTimeOffset Created =
            new(
                2026,
                8,
                10,
                0,
                0,
                0,
                TimeSpan.Zero);

        public Fixture()
        {
            Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "ReplayFoundry-QwenRuntimeResolutionTests",
                    Guid.NewGuid().ToString("N"));
            Paths =
                new ReplayFoundryRuntimePackStorePaths(
                    Path.Combine(
                        Root,
                        "store"));
        }

        public string Root { get; }

        public ReplayFoundryRuntimePackStorePaths Paths { get; }

        public string? ActivePythonPath { get; private set; }

        public async Task<QwenRuntimePaths> InstallAndResolveVerifiedAsync()
        {
            using var store =
                new ReplayFoundryRuntimePackStore(
                    Paths);
            InstalledReplayFoundryRuntimePack media =
                await InstallMediaAsync(
                    store);
            InstalledReplayFoundryRuntimePack runtime =
                await InstallVisualRuntimeAsync(
                    store,
                    media.Manifest);
            _ = await InstallVisualModelAsync(
                store,
                runtime.Manifest);
            ActivePythonPath =
                runtime.Resolve(
                    ReplayFoundryRuntimeFileRole.PythonExecutable);
            return await ResolveVerifiedAsync(
                store);
        }

        public async Task InstallValidStaleRuntimeAsync()
        {
            using var store =
                new ReplayFoundryRuntimePackStore(
                    Paths);
            InstalledReplayFoundryRuntimePack media =
                await InstallMediaAsync(
                    store);
            InstalledReplayFoundryRuntimePack runtime =
                await InstallVisualRuntimeAsync(
                    store,
                    media.Manifest,
                    version: "0.8.19");
            _ = await InstallVisualModelAsync(
                store,
                runtime.Manifest,
                version: "4.0.15",
                runtimeMinimumVersion: "0.8.19");
        }

        public async Task InstallValidStaleModelAsync()
        {
            using var store =
                new ReplayFoundryRuntimePackStore(
                    Paths);
            InstalledReplayFoundryRuntimePack media =
                await InstallMediaAsync(
                    store);
            InstalledReplayFoundryRuntimePack runtime =
                await InstallVisualRuntimeAsync(
                    store,
                    media.Manifest);
            _ = await InstallVisualModelAsync(
                store,
                runtime.Manifest,
                version: "4.0.15");
        }

        public async Task InstallModelRuntimeMismatchAsync()
        {
            using var store =
                new ReplayFoundryRuntimePackStore(
                    Paths);
            InstalledReplayFoundryRuntimePack media =
                await InstallMediaAsync(
                    store);
            InstalledReplayFoundryRuntimePack firstRuntime =
                await InstallVisualRuntimeAsync(
                    store,
                    media.Manifest,
                    sourceName: "visual-runtime-first",
                    version: "0.8.21");
            _ = await InstallVisualModelAsync(
                store,
                firstRuntime.Manifest);
            _ = await InstallVisualRuntimeAsync(
                store,
                media.Manifest,
                sourceName: "visual-runtime-second",
                version: "0.8.21");
        }

        public async Task InstallRuntimeMediaMismatchAsync()
        {
            using var store =
                new ReplayFoundryRuntimePackStore(
                    Paths);
            InstalledReplayFoundryRuntimePack firstMedia =
                await InstallMediaAsync(
                    store,
                    sourceName: "media-first",
                    version: "8.1.2.32");
            InstalledReplayFoundryRuntimePack runtime =
                await InstallVisualRuntimeAsync(
                    store,
                    firstMedia.Manifest);
            _ = await InstallVisualModelAsync(
                store,
                runtime.Manifest);
            _ = await InstallMediaAsync(
                store,
                sourceName: "media-second",
                version: "8.1.2.33");
        }

        public async Task<QwenRuntimePaths?> TryResolveVerifiedAsync()
        {
            using var store =
                new ReplayFoundryRuntimePackStore(
                    Paths);
            try
            {
                return await ResolveVerifiedAsync(
                    store);
            }
            catch (Exception exception) when (
                exception is IOException or
                InvalidDataException or
                UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (!Directory.Exists(
                    Root))
            {
                return;
            }
            foreach (string file in Directory.EnumerateFiles(
                         Root,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(
                    file,
                    FileAttributes.Normal);
            }
            Directory.Delete(
                Root,
                recursive: true);
        }

        private static async Task<QwenRuntimePaths> ResolveVerifiedAsync(
            ReplayFoundryRuntimePackStore store)
        {
            InstalledReplayFoundryRuntimePack media =
                await store.ResolveActiveAsync(
                    ReplayFoundryRuntimePackKind.MediaTools);
            InstalledReplayFoundryRuntimePack runtime =
                await store.ResolveActiveAsync(
                    ReplayFoundryRuntimePackKind.VisualRuntime);
            InstalledReplayFoundryRuntimePack model =
                await store.ResolveActiveAsync(
                    ReplayFoundryRuntimePackKind.VisualModel);
            QwenRuntimePaths? paths =
                ReplayFoundryRuntimeEnvironment.CreateCompatibleQwenPaths(
                    runtime,
                    model,
                    media,
                    out string? incompatibility);
            return paths ??
                throw new InvalidDataException(
                    incompatibility);
        }

        private async Task<InstalledReplayFoundryRuntimePack>
            InstallMediaAsync(
                ReplayFoundryRuntimePackStore store,
                string sourceName = "media",
                string version = "8.1.2.32")
        {
            string source =
                Source(
                    sourceName);
            Write(
                source,
                "bin/ffmpeg.exe",
                "verified-ffmpeg");
            Write(
                source,
                "bin/ffprobe.exe",
                "verified-ffprobe");
            Write(
                source,
                "LICENSE.txt",
                "media-license");
            ReplayFoundryRuntimePackManifest manifest =
                Manifest(
                    source,
                    "replayfoundry-media-tools",
                    ReplayFoundryRuntimePackKind.MediaTools,
                    version,
                    ReplayFoundryRuntimeBackend.Cpu,
                    [
                        ("bin/ffmpeg.exe", ReplayFoundryRuntimeFileRole.FfmpegExecutable),
                        ("bin/ffprobe.exe", ReplayFoundryRuntimeFileRole.FfprobeExecutable),
                        ("LICENSE.txt", ReplayFoundryRuntimeFileRole.License),
                    ],
                    []);
            await RuntimePackManifestJson.WriteAsync(
                manifest,
                Path.Combine(
                    source,
                    RuntimePackManifestJson.FileName));
            return await store.InstallAsync(
                source,
                activate: true);
        }

        private async Task<InstalledReplayFoundryRuntimePack>
            InstallVisualRuntimeAsync(
                ReplayFoundryRuntimePackStore store,
                ReplayFoundryRuntimePackManifest media,
                string sourceName = "visual-runtime",
                string version = "0.8.21")
        {
            string source =
                Source(
                    sourceName);
            Write(
                source,
                "python/python.exe",
                "verified-python");
            Write(
                source,
                "host/qwen3_vl_batch_host.py",
                "verified-host-" + sourceName);
            Write(
                source,
                "LICENSE.txt",
                "runtime-license");
            ReplayFoundryRuntimePackManifest manifest =
                Manifest(
                    source,
                    "replayfoundry-qwen3-vl-runtime",
                    ReplayFoundryRuntimePackKind.VisualRuntime,
                    version,
                    ReplayFoundryRuntimeBackend.Cuda,
                    [
                        ("python/python.exe", ReplayFoundryRuntimeFileRole.PythonExecutable),
                        ("host/qwen3_vl_batch_host.py", ReplayFoundryRuntimeFileRole.VisualHostScript),
                        ("LICENSE.txt", ReplayFoundryRuntimeFileRole.License),
                    ],
                    [
                        new ReplayFoundryRuntimePackDependency(
                            "replayfoundry-media-tools",
                            "8.1.2.32",
                            media.ManifestHash),
                    ]);
            await RuntimePackManifestJson.WriteAsync(
                manifest,
                Path.Combine(
                    source,
                    RuntimePackManifestJson.FileName));
            return await store.InstallAsync(
                source,
                activate: true);
        }

        private async Task<InstalledReplayFoundryRuntimePack>
            InstallVisualModelAsync(
                ReplayFoundryRuntimePackStore store,
                ReplayFoundryRuntimePackManifest runtime,
                string version = "4.0.17",
                string runtimeMinimumVersion = "0.8.21")
        {
            string source =
                Source(
                    "visual-model");
            Write(
                source,
                "config/model-manifest.json",
                "{}");
            Write(
                source,
                "config/prompt-manifest.json",
                "{}");
            string pythonHash =
                Hash(
                    "verified-python");
            Write(
                source,
                "config/qualification-lock.json",
                JsonSerializer.Serialize(
                    new
                    {
                        pythonExecutableSha256 = pythonHash,
                        capabilitySucceeded = true,
                        unconstrainedFallbackPermitted = false,
                        semanticRepairPermitted = false,
                    }));
            Write(
                source,
                "model/model.bin",
                "verified-model");
            Write(
                source,
                "LICENSE.txt",
                "model-license");
            ReplayFoundryRuntimePackManifest manifest =
                Manifest(
                    source,
                    "replayfoundry-qwen3-vl-4b-instruct",
                    ReplayFoundryRuntimePackKind.VisualModel,
                    version,
                    ReplayFoundryRuntimeBackend.Cuda,
                    [
                        ("config/model-manifest.json", ReplayFoundryRuntimeFileRole.QwenModelManifest),
                        ("config/prompt-manifest.json", ReplayFoundryRuntimeFileRole.QwenPromptManifest),
                        ("config/qualification-lock.json", ReplayFoundryRuntimeFileRole.QwenQualificationLock),
                        ("model/model.bin", ReplayFoundryRuntimeFileRole.Asset),
                        ("LICENSE.txt", ReplayFoundryRuntimeFileRole.License),
                    ],
                    [
                        new ReplayFoundryRuntimePackDependency(
                            "replayfoundry-qwen3-vl-runtime",
                            runtimeMinimumVersion,
                            runtime.ManifestHash),
                    ]);
            await RuntimePackManifestJson.WriteAsync(
                manifest,
                Path.Combine(
                    source,
                    RuntimePackManifestJson.FileName));
            return await store.InstallAsync(
                source,
                activate: true);
        }

        private static ReplayFoundryRuntimePackManifest Manifest(
            string source,
            string packageId,
            ReplayFoundryRuntimePackKind kind,
            string version,
            ReplayFoundryRuntimeBackend backend,
            IEnumerable<(string RelativePath, ReplayFoundryRuntimeFileRole Role)>
                entries,
            IEnumerable<ReplayFoundryRuntimePackDependency> dependencies)
        {
            ReplayFoundryRuntimePackFile[] files = entries
                .Select(entry =>
                {
                    string path =
                        Path.Combine(
                            source,
                            entry.RelativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar));
                    var info =
                        new FileInfo(
                            path);
                    return new ReplayFoundryRuntimePackFile(
                        entry.RelativePath,
                        info.Length,
                        FileHash(
                            path),
                        entry.Role);
                })
                .ToArray();
            ReplayFoundryRuntimePackFile license = files.Single(file =>
                file.Role == ReplayFoundryRuntimeFileRole.License);
            return ReplayFoundryRuntimePackManifest.Create(
                new ReplayFoundryRuntimePackIdentity(
                    packageId,
                    kind,
                    version),
                packageId,
                backend,
                files,
                dependencies,
                [
                    new ReplayFoundryRuntimePackLicense(
                        "Fixture",
                        "MIT",
                        license.RelativePath,
                        license.Sha256,
                        "https://example.test/license",
                        "Fixture"),
                ],
                [
                    new ReplayFoundryRuntimePackSource(
                        "https://example.test/runtime.zip",
                        version,
                        Hash(
                            packageId + version)),
                ],
                "0.1.0",
                "1.0.0",
                Created);
        }

        private string Source(
            string name)
        {
            string path =
                Path.Combine(
                    Root,
                    "sources",
                    name);
            Directory.CreateDirectory(
                path);
            return path;
        }

        private static void Write(
            string root,
            string relativePath,
            string value)
        {
            string path =
                Path.Combine(
                    root,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                value);
        }

        private static string FileHash(
            string path) =>
            Convert.ToHexString(
                SHA256.HashData(
                    File.ReadAllBytes(path)));

        private static string Hash(
            string value) =>
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)));
    }
}
