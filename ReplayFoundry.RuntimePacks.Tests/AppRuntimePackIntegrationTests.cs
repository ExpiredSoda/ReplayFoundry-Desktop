using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.RuntimePacks;
using ReplayFoundry.RuntimePacks;

namespace ReplayFoundry.RuntimePacks.Tests;

internal static class AppRuntimePackIntegrationTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Desktop reports an empty store as Base unavailable", EmptyStoreIsUnavailable),
        new("Desktop resolves a verified Base media pack", VerifiedBaseResolves),
        new("Desktop runtime discovery cannot deadlock a UI synchronization context", RuntimeDiscoveryDoesNotDeadlockUiContext),
        new("Settings projects capability state and delegates maintenance", SettingsProjectsCapabilities),
        new("Process request snapshots runtime environment variables", ProcessEnvironmentSnapshots),
        new("Qwen deployment lock must authorize the packaged Python executable", QwenDeploymentLockMatchesPython),
        new("Qwen runtime environment is offline bounded and read only", QwenEnvironmentIsBounded),
    ];

    private static Task EmptyStoreIsUnavailable()
    {
        using var fixture = new Fixture();
        ReplayFoundryRuntimeEnvironment environment = ReplayFoundryRuntimeEnvironment.Discover(fixture.Paths);
        Assert(!environment.IsBaseReady && !environment.IsBalancedReady && !environment.IsThoroughReady,
            "An empty store exposed a runtime capability.");
        Assert(environment.Capabilities.Count == 6 && environment.Capabilities.All(item => !item.IsAvailable),
            "The fixed capability inventory was not reported as unavailable.");
        return Task.CompletedTask;
    }

    private static async Task VerifiedBaseResolves()
    {
        using var fixture = new Fixture();
        await fixture.InstallMediaAsync();
        ReplayFoundryRuntimeEnvironment environment = ReplayFoundryRuntimeEnvironment.Discover(fixture.Paths);
        Assert(environment.IsBaseReady && !environment.IsBalancedReady && !environment.IsThoroughReady,
            "Base capability boundaries were not preserved.");
        Assert(File.Exists(environment.FfmpegPath) && File.Exists(environment.FfprobePath),
            "Verified media entry points did not resolve.");
    }

    private static async Task RuntimeDiscoveryDoesNotDeadlockUiContext()
    {
        using var fixture = new Fixture();
        await fixture.InstallMediaAsync();

        ReplayFoundryRuntimeEnvironment? environment = null;
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new NonPumpingSynchronizationContext());
            try
            {
                environment = ReplayFoundryRuntimeEnvironment.Discover(
                    fixture.Paths);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "ReplayFoundry runtime discovery UI-context regression",
        };

        thread.Start();
        Assert(
            completed.Wait(TimeSpan.FromSeconds(5)),
            "Runtime discovery deadlocked while resolving an active pack from a UI synchronization context.");
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "Runtime discovery failed on the simulated UI thread.",
                failure);
        }

        Assert(
            environment?.IsBaseReady == true,
            "Runtime discovery completed but did not retain the verified Base pack.");
    }

    private static async Task SettingsProjectsCapabilities()
    {
        using var fixture = new Fixture();
        await fixture.InstallMediaAsync();
        ReplayFoundryRuntimeEnvironment environment = ReplayFoundryRuntimeEnvironment.Discover(fixture.Paths);
        List<SettingsCapabilityItem> projectedCapabilities = environment.Capabilities.Select(capability =>
            new SettingsCapabilityItem(
                capability.Name,
                capability.Status,
                capability.Storage,
                capability.License,
                capability.Detail)).ToList();
        var snapshot = new SettingsRuntimeCapabilitySnapshot(
            environment.IsBaseReady,
            environment.IsBalancedReady,
            environment.IsThoroughReady,
            hasAdvancedCapability: false,
            environment.PackageStoreRoot,
            projectedCapabilities);
        projectedCapabilities.Clear();
        var launcher = new RecordingMaintenanceLauncher();
        using var settings = new SettingsViewModel(
            new YouTubeConnectionPermissionState(new InMemoryYouTubeConnectionPermissionStore()),
            snapshot,
            launcher);
        Assert(settings.RuntimeProfileStatus == "Base installed", "Settings did not report Base readiness in user-facing language.");
        Assert(settings.AiCapabilities.Count == environment.Capabilities.Count,
            "Settings retained caller-owned capability collection state.");
        Assert(settings.AiCapabilities.Single(item => item.Capability == "Deterministic media analysis").Status.StartsWith("Ready", StringComparison.Ordinal),
            "Settings did not project the verified media pack.");
        settings.AddAdvancedAiCommand.Execute(null);
        settings.RepairRuntimePacksCommand.Execute(null);
        settings.OpenRuntimePackFolderCommand.Execute(null);
        Assert(launcher.AddCalls == 1 && launcher.RepairCalls == 1 && launcher.OpenCalls == 1,
            "Settings did not delegate runtime maintenance through its focused boundary.");
    }

    private static Task ProcessEnvironmentSnapshots()
    {
        var values = new Dictionary<string, string> { ["PYTHONHOME"] = "first" };
        var request = new ProcessRunRequest(
            Path.Combine(Path.GetTempPath(), "tool.exe"),
            [],
            TimeSpan.FromSeconds(1),
            environmentVariables: values);
        values["PYTHONHOME"] = "changed";
        Assert(request.EnvironmentVariables["PYTHONHOME"] == "first", "Process request retained caller-owned environment state.");
        try
        {
            ((IDictionary<string, string>)request.EnvironmentVariables)["NEW"] = "bad";
            throw new InvalidOperationException("Process environment collection remained mutable.");
        }
        catch (NotSupportedException) { }
        return Task.CompletedTask;
    }

    private static async Task QwenDeploymentLockMatchesPython()
    {
        string root = Path.Combine(Path.GetTempPath(), "ReplayFoundry-QwenLockTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string python = Path.Combine(root, "python.exe");
            string qualificationLock = Path.Combine(root, "qualification-lock.json");
            await File.WriteAllTextAsync(python, "qualified-python");
            string pythonHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("qualified-python")));
            await File.WriteAllTextAsync(qualificationLock, JsonSerializer.Serialize(new
            {
                pythonExecutableSha256 = pythonHash,
                capabilitySucceeded = true,
                unconstrainedFallbackPermitted = false,
                semanticRepairPermitted = false,
            }));
            Assert(ReplayFoundryRuntimeEnvironment.QualificationAuthorizes(python, qualificationLock),
                "A matching strict deployment lock was rejected.");
            await File.AppendAllTextAsync(python, "changed");
            Assert(!ReplayFoundryRuntimeEnvironment.QualificationAuthorizes(python, qualificationLock),
                "A lock authorized a different Python executable.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Task QwenEnvironmentIsBounded()
    {
        IReadOnlyDictionary<string, string> environment = ReplayFoundryRuntimeEnvironment.BuildQwenEnvironment(
            @"C:\RF\python", @"C:\RF\site-packages", @"C:\RF\host", @"C:\RF\media");
        Assert(environment["PYTHONDONTWRITEBYTECODE"] == "1" &&
               environment["HF_HUB_OFFLINE"] == "1" &&
               environment["TRANSFORMERS_OFFLINE"] == "1",
            "The packaged Qwen process was not forced offline/read-only.");
        Assert(environment["PYTHONPATH"].Split(Path.PathSeparator).SequenceEqual(
                new[] { @"C:\RF\host", @"C:\RF\site-packages" }),
            "The packaged host and site-packages paths were not explicit.");
        Assert(!environment["PATH"].Contains(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, StringComparison.Ordinal),
            "The packaged process inherited the caller PATH instead of its bounded DLL search path.");
        return Task.CompletedTask;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingMaintenanceLauncher : IRuntimePackMaintenanceActions
    {
        public bool CanAddAdvanced => true;
        public bool CanRepair => true;
        public bool CanRemoveAdvanced => false;
        public int AddCalls { get; private set; }
        public int RepairCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public void AddAdvanced() => AddCalls++;
        public void Repair() => RepairCalls++;
        public void RemoveAdvanced() => throw new InvalidOperationException();
        public void OpenPackageFolder() => OpenCalls++;
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // A WPF UI thread cannot process queued continuations while blocked in a
            // synchronous composition-root call. The regression test intentionally
            // leaves posts unpumped to expose that deadlock deterministically.
        }
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly DateTimeOffset Created = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ReplayFoundry-AppRuntimeTests", Guid.NewGuid().ToString("N"));
            Source = Path.Combine(Root, "source");
            Paths = new ReplayFoundryRuntimePackStorePaths(Path.Combine(Root, "store"));
            Directory.CreateDirectory(Path.Combine(Source, "bin"));
            File.WriteAllText(Path.Combine(Source, "bin", "ffmpeg.exe"), "ffmpeg");
            File.WriteAllText(Path.Combine(Source, "bin", "ffprobe.exe"), "ffprobe");
            File.WriteAllText(Path.Combine(Source, "LICENSE.txt"), "license");
        }
        public string Root { get; }
        public string Source { get; }
        public ReplayFoundryRuntimePackStorePaths Paths { get; }

        public async Task InstallMediaAsync()
        {
            ReplayFoundryRuntimePackManifest manifest = ReplayFoundryRuntimePackManifest.Create(
                new("replayfoundry-media-tools", ReplayFoundryRuntimePackKind.MediaTools, "1.0.0"),
                "Media tools", ReplayFoundryRuntimeBackend.Cpu,
                [
                    FileEntry("bin/ffmpeg.exe", ReplayFoundryRuntimeFileRole.FfmpegExecutable),
                    FileEntry("bin/ffprobe.exe", ReplayFoundryRuntimeFileRole.FfprobeExecutable),
                    FileEntry("LICENSE.txt", ReplayFoundryRuntimeFileRole.License),
                ],
                [],
                [new("Fixture", "MIT", "LICENSE.txt", Hash("license"), "https://example.test/license", "Fixture")],
                [new("https://example.test/media.zip", "1.0.0", Hash("archive"))],
                "0.1.0", "1.0.0", Created);
            await RuntimePackManifestJson.WriteAsync(manifest, Path.Combine(Source, RuntimePackManifestJson.FileName));
            await new ReplayFoundryRuntimePackStore(Paths).InstallAsync(Source, activate: true);
        }

        private ReplayFoundryRuntimePackFile FileEntry(string relativePath, ReplayFoundryRuntimeFileRole role)
        {
            string value = File.ReadAllText(Path.Combine(Source, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return new(relativePath, Encoding.UTF8.GetByteCount(value), Hash(value), role);
        }
        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
