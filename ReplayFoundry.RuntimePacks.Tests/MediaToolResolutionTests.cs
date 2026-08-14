using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.RuntimePacks;
using ReplayFoundry.RuntimePacks;

namespace ReplayFoundry.RuntimePacks.Tests;

internal static class MediaToolResolutionTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new(
            "Media tools honor the Debug and Release resolution boundary",
            MediaToolsHonorBuildBoundary),
        new(
            "Release media tools reject overrides when the verified pack is missing or corrupt",
            UnavailableMediaPackDoesNotDivertRelease),
    ];

    private static async Task MediaToolsHonorBuildBoundary()
    {
        using var fixture = new Fixture();
        await fixture.InstallMediaAsync();
        ReplayFoundryRuntimeEnvironment environment =
            ReplayFoundryRuntimeEnvironment.Discover(
                fixture.Paths);
        using var overrides =
            new DevelopmentCandidateScope(
                fixture.Root);
        var locator =
            new FfmpegToolLocator(
                environment);

#if DEBUG
        string expectedFfprobe = overrides.FfprobePath;
        string expectedFfmpeg = overrides.FfmpegPath;
#else
        string expectedFfprobe = environment.FfprobePath!;
        string expectedFfmpeg = environment.FfmpegPath!;
#endif

        Assert(
            string.Equals(
                locator.LocateFfprobe(),
                expectedFfprobe,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                locator.LocateFfmpeg(),
                expectedFfmpeg,
                StringComparison.OrdinalIgnoreCase),
            "Media-tool resolution crossed the Debug/Release trust boundary.");
        overrides.AssertUnchanged();
    }

    private static async Task UnavailableMediaPackDoesNotDivertRelease()
    {
        using var fixture = new Fixture();
        using var overrides =
            new DevelopmentCandidateScope(
                fixture.Root);
        ReplayFoundryRuntimeEnvironment missing =
            ReplayFoundryRuntimeEnvironment.Discover(
                fixture.Paths);
        Assert(
            !missing.IsBaseReady,
            "An empty runtime store exposed media tools.");
        AssertUnavailableBoundary(
            new FfmpegToolLocator(missing),
            overrides);

        await fixture.InstallMediaAsync();
        ReplayFoundryRuntimeEnvironment verified =
            ReplayFoundryRuntimeEnvironment.Discover(
                fixture.Paths);
        await File.AppendAllTextAsync(
            verified.FfprobePath!,
            "corrupt");
        ReplayFoundryRuntimeEnvironment corrupted =
            ReplayFoundryRuntimeEnvironment.Discover(
                fixture.Paths);
        Assert(
            !corrupted.IsBaseReady &&
            corrupted.FfprobePath is null &&
            corrupted.FfmpegPath is null,
            "Runtime discovery exposed a corrupt media-tools pack.");

        AssertUnavailableBoundary(
            new FfmpegToolLocator(corrupted),
            overrides);
        overrides.AssertUnchanged();
    }

    private static void AssertUnavailableBoundary(
        FfmpegToolLocator locator,
        DevelopmentCandidateScope overrides)
    {
#if DEBUG
        Assert(
            string.Equals(
                locator.LocateFfprobe(),
                overrides.FfprobePath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                locator.LocateFfmpeg(),
                overrides.FfmpegPath,
                StringComparison.OrdinalIgnoreCase),
            "Debug media-tool resolution lost its explicit development overrides.");
#else
        AssertReleaseRejects(
            locator.LocateFfprobe);
        AssertReleaseRejects(
            locator.LocateFfmpeg);
#endif
    }

#if !DEBUG
    private static void AssertReleaseRejects(
        Func<string> locate)
    {
        try
        {
            _ = locate();
            throw new InvalidOperationException(
                "Release media-tool resolution accepted an unverified development candidate.");
        }
        catch (MediaToolNotFoundException exception)
        {
            Assert(
                exception.Message.Contains(
                    "repair the Base media-tools pack",
                    StringComparison.Ordinal),
                "Release media-tool failure did not direct the user to verified-pack repair.");
        }
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
        private const string FfprobeVariable =
            "REPLAYFOUNDRY_FFPROBE_PATH";

        private const string FfmpegVariable =
            "REPLAYFOUNDRY_FFMPEG_PATH";

        private readonly string? _previousFfprobe;
        private readonly string? _previousFfmpeg;
        private readonly string? _previousPath;
        private readonly string _ffprobeOverrideValue;
        private readonly string _pathOverrideValue;
        private readonly string _applicationToolsDirectory;
        private readonly string _applicationToolsParent;
        private readonly string _applicationFfprobePath;
        private readonly string _applicationFfmpegPath;
        private readonly bool _createdApplicationToolsDirectory;
        private readonly bool _createdApplicationToolsParent;
        private readonly bool _createdApplicationFfprobe;
        private readonly bool _createdApplicationFfmpeg;

        public DevelopmentCandidateScope(
            string root)
        {
            string overrideDirectory =
                Path.Combine(
                    root,
                    "unverified-overrides");
            Directory.CreateDirectory(
                overrideDirectory);
            FfprobePath =
                Path.Combine(
                    overrideDirectory,
                    "ffprobe.exe");
            FfmpegPath =
                Path.Combine(
                    overrideDirectory,
                    "ffmpeg.exe");
            File.WriteAllText(
                FfprobePath,
                "unverified-ffprobe");
            File.WriteAllText(
                FfmpegPath,
                "unverified-ffmpeg");
            _ffprobeOverrideValue =
                overrideDirectory;

            _applicationToolsParent =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Tools");
            _applicationToolsDirectory =
                Path.Combine(
                    _applicationToolsParent,
                    "FFmpeg");
            _createdApplicationToolsParent =
                !Directory.Exists(
                    _applicationToolsParent);
            _createdApplicationToolsDirectory =
                !Directory.Exists(
                    _applicationToolsDirectory);
            Directory.CreateDirectory(
                _applicationToolsDirectory);
            _applicationFfprobePath =
                Path.Combine(
                    _applicationToolsDirectory,
                    "ffprobe.exe");
            _applicationFfmpegPath =
                Path.Combine(
                    _applicationToolsDirectory,
                    "ffmpeg.exe");
            _createdApplicationFfprobe =
                WriteCandidateIfMissing(
                    _applicationFfprobePath,
                    "unverified-application-ffprobe");
            _createdApplicationFfmpeg =
                WriteCandidateIfMissing(
                    _applicationFfmpegPath,
                    "unverified-application-ffmpeg");

            _previousFfprobe =
                Environment.GetEnvironmentVariable(
                    FfprobeVariable);
            _previousFfmpeg =
                Environment.GetEnvironmentVariable(
                    FfmpegVariable);
            _previousPath =
                Environment.GetEnvironmentVariable(
                    "PATH");
            _pathOverrideValue =
                string.IsNullOrEmpty(_previousPath)
                    ? overrideDirectory
                    : string.Join(
                        Path.PathSeparator,
                        overrideDirectory,
                        _previousPath);
            Environment.SetEnvironmentVariable(
                FfprobeVariable,
                _ffprobeOverrideValue);
            Environment.SetEnvironmentVariable(
                FfmpegVariable,
                FfmpegPath);
            Environment.SetEnvironmentVariable(
                "PATH",
                _pathOverrideValue);
        }

        public string FfprobePath { get; }

        public string FfmpegPath { get; }

        public void AssertUnchanged()
        {
            Assert(
                string.Equals(
                    Environment.GetEnvironmentVariable(
                        FfprobeVariable),
                    _ffprobeOverrideValue,
                    StringComparison.Ordinal) &&
                string.Equals(
                    Environment.GetEnvironmentVariable(
                        FfmpegVariable),
                    FfmpegPath,
                    StringComparison.Ordinal) &&
                string.Equals(
                    Environment.GetEnvironmentVariable(
                        "PATH"),
                    _pathOverrideValue,
                    StringComparison.Ordinal),
                "Media-tool resolution mutated the caller's environment.");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                FfprobeVariable,
                _previousFfprobe);
            Environment.SetEnvironmentVariable(
                FfmpegVariable,
                _previousFfmpeg);
            Environment.SetEnvironmentVariable(
                "PATH",
                _previousPath);
            DeleteIfCreated(
                _applicationFfprobePath,
                _createdApplicationFfprobe);
            DeleteIfCreated(
                _applicationFfmpegPath,
                _createdApplicationFfmpeg);
            DeleteEmptyDirectoryIfCreated(
                _applicationToolsDirectory,
                _createdApplicationToolsDirectory);
            DeleteEmptyDirectoryIfCreated(
                _applicationToolsParent,
                _createdApplicationToolsParent);
        }

        private static bool WriteCandidateIfMissing(
            string path,
            string contents)
        {
            if (File.Exists(path))
            {
                return false;
            }
            File.WriteAllText(
                path,
                contents);
            return true;
        }

        private static void DeleteIfCreated(
            string path,
            bool created)
        {
            if (created && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void DeleteEmptyDirectoryIfCreated(
            string path,
            bool created)
        {
            if (created &&
                Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly DateTimeOffset Created =
            new(
                2026,
                8,
                2,
                0,
                0,
                0,
                TimeSpan.Zero);

        public Fixture()
        {
            Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "ReplayFoundry-MediaToolResolutionTests",
                    Guid.NewGuid().ToString("N"));
            Source =
                Path.Combine(
                    Root,
                    "source");
            Paths =
                new ReplayFoundryRuntimePackStorePaths(
                    Path.Combine(
                        Root,
                        "store"));
            Directory.CreateDirectory(
                Path.Combine(
                    Source,
                    "bin"));
            File.WriteAllText(
                Path.Combine(Source, "bin", "ffmpeg.exe"),
                "ffmpeg");
            File.WriteAllText(
                Path.Combine(Source, "bin", "ffprobe.exe"),
                "ffprobe");
            File.WriteAllText(
                Path.Combine(Source, "LICENSE.txt"),
                "license");
        }

        public string Root { get; }

        public string Source { get; }

        public ReplayFoundryRuntimePackStorePaths Paths { get; }

        public async Task InstallMediaAsync()
        {
            ReplayFoundryRuntimePackManifest manifest =
                ReplayFoundryRuntimePackManifest.Create(
                    new(
                        "replayfoundry-media-tools",
                        ReplayFoundryRuntimePackKind.MediaTools,
                        "1.0.0"),
                    "Media tools",
                    ReplayFoundryRuntimeBackend.Cpu,
                    [
                        FileEntry(
                            "bin/ffmpeg.exe",
                            ReplayFoundryRuntimeFileRole.FfmpegExecutable),
                        FileEntry(
                            "bin/ffprobe.exe",
                            ReplayFoundryRuntimeFileRole.FfprobeExecutable),
                        FileEntry(
                            "LICENSE.txt",
                            ReplayFoundryRuntimeFileRole.License),
                    ],
                    [],
                    [
                        new(
                            "Fixture",
                            "MIT",
                            "LICENSE.txt",
                            Hash("license"),
                            "https://example.test/license",
                            "Fixture"),
                    ],
                    [
                        new(
                            "https://example.test/media.zip",
                            "1.0.0",
                            Hash("archive")),
                    ],
                    "0.1.0",
                    "1.0.0",
                    Created);
            await RuntimePackManifestJson.WriteAsync(
                manifest,
                Path.Combine(
                    Source,
                    RuntimePackManifestJson.FileName));
            using var store =
                new ReplayFoundryRuntimePackStore(
                    Paths);
            await store.InstallAsync(
                Source,
                activate: true);
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
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

        private ReplayFoundryRuntimePackFile FileEntry(
            string relativePath,
            ReplayFoundryRuntimeFileRole role)
        {
            string value =
                File.ReadAllText(
                    Path.Combine(
                        Source,
                        relativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
            return new(
                relativePath,
                Encoding.UTF8.GetByteCount(value),
                Hash(value),
                role);
        }

        private static string Hash(
            string value)
        {
            return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)));
        }
    }
}
