using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.RuntimePacks;

namespace ReplayFoundry.RuntimePacks.Tests;

internal static class RuntimePackTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Manifest paths reject absolute and traversal values", PathsRejectEscapes),
        new("Manifest files reject case-insensitive duplicates", DuplicateFilesReject),
        new("Manifest snapshots caller collections", ManifestSnapshots),
        new("Manifest hash is deterministic", ManifestHashDeterministic),
        new("Manifest tampering rejects during read", ManifestTamperRejects),
        new("Valid package verifies every declared file", ValidPackageVerifies),
        new("Corrupt package file fails SHA-256 verification", CorruptFileFails),
        new("Missing package file fails verification", MissingFileFails),
        new("Unexpected package file policy is strict", ExtraFileFails),
        new("Runtime startup verifies visual entry points without rescanning assets", RuntimeStartupIsBounded),
        new("Package installation stages and resolves atomically", InstallAndResolve),
        new("Content-addressed install paths remain compact and collision safe", CompactContentAddressedPath),
        new("Pre-cancelled installation leaves no staging payload", CancellationCleansStage),
        new("Abandoned staging cleanup is bounded to the store", AbandonedStageCleans),
        new("Package versions install side by side", SideBySideVersions),
        new("Inactive package pruning retains only active dependency closure", InactivePruningRetainsActiveClosure),
        new("Zero-dependency install skips unrelated installed payloads", ZeroDependencySkipsUnrelatedPayloads),
        new("Zero-dependency reinstall still verifies the target payload", ZeroDependencyReinstallVerifiesTarget),
        new("Package dependency must already be installed", MissingDependencyRejects),
        new("Exact-hash dependency ignores other side-by-side payloads", ExactHashDependencyIsTargeted),
        new("Same-package exact dependency permits an immutable predecessor", SamePackageExactDependencyPermitsPredecessor),
        new("Same-package range dependency remains ambiguous and rejects", SamePackageRangeDependencyRejects),
        new("Compatible dependency selects the highest installed version", CompatibleDependencySelectsHighestVersion),
        new("Required dependency corruption is isolated from unrelated packs", RequiredDependencyCorruptionIsIsolated),
        new("Reachable package dependency cycle is rejected", ReachableDependencyCycleRejects),
        new("Required dependency cannot be removed", DependencySafeRemoval),
        new("Repair restores a corrupt installed package", RepairRestores),
        new("Unverified package never resolves", UnverifiedNeverResolves),
        new("Visual runtime requires Python and host entries", VisualRuntimeRolesRequired),
        new("Active selection changes only after successful install", FailedInstallKeepsActive),
        new("ZIP path traversal is rejected", ZipTraversalRejects),
        new("Catalog rejects non-HTTPS package URLs", CatalogRequiresHttps),
        new("Catalog download rejects an unapproved redirect host", CatalogRejectsRedirect),
        new("Catalog download rejects a SHA-256 mismatch and cleans partials", CatalogRejectsHashMismatch),
        new("Catalog requires the exact fixed profile kinds", CatalogRequiresFixedProfile),
        new("Advanced catalog failure rolls back newly installed packs", CatalogRollsBackPartialProfile),
        new("Empty package-store cleanup removes only empty store state", EmptyStoreCleanup),
    ];

    private static Task PathsRejectEscapes()
    {
        AssertThrows<ArgumentException>(() => new ReplayFoundryRuntimePackFile("C:/tool.exe", 1, Hash("x")));
        AssertThrows<ArgumentException>(() => new ReplayFoundryRuntimePackFile("../tool.exe", 1, Hash("x")));
        return Task.CompletedTask;
    }

    private static async Task DuplicateFilesReject()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackFile[] files =
        [
            fixture.File("bin/ffmpeg.exe", ReplayFoundryRuntimeFileRole.FfmpegExecutable),
            fixture.File("BIN/FFMPEG.EXE", ReplayFoundryRuntimeFileRole.Asset),
            fixture.File("bin/ffprobe.exe", ReplayFoundryRuntimeFileRole.FfprobeExecutable),
            fixture.File("LICENSE.txt", ReplayFoundryRuntimeFileRole.License),
        ];
        AssertThrows<ArgumentException>(() => CreateManifest(fixture, files: files));
        await Task.CompletedTask;
    }

    private static Task ManifestSnapshots()
    {
        using Fixture fixture = new();
        var files = fixture.DefaultFiles().ToList();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture, files: files);
        files.Clear();
        Assert(manifest.Files.Count == 4, "The manifest retained caller mutation.");
        return Task.CompletedTask;
    }

    private static Task ManifestHashDeterministic()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest first = CreateManifest(fixture);
        ReplayFoundryRuntimePackManifest second = CreateManifest(fixture);
        Assert(first.ManifestHash == second.ManifestHash, "Manifest hash changed for identical input.");
        return Task.CompletedTask;
    }

    private static async Task ManifestTamperRejects()
    {
        using Fixture fixture = new();
        await fixture.SealAsync(CreateManifest(fixture));
        string path = Path.Combine(fixture.Source, RuntimePackManifestJson.FileName);
        string json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("Media tools", "Changed tools", StringComparison.Ordinal));
        await AssertThrowsAsync<InvalidDataException>(() => RuntimePackManifestJson.ReadAsync(path));
    }

    private static async Task ValidPackageVerifies()
    {
        using Fixture fixture = new();
        await fixture.SealAsync(CreateManifest(fixture));
        ReplayFoundryRuntimePackVerificationResult result = await new ReplayFoundryRuntimePackVerifier().VerifyAsync(fixture.Source);
        Assert(result.IsValid, string.Join("; ", result.Errors));
    }

    private static async Task CorruptFileFails()
    {
        using Fixture fixture = new();
        await fixture.SealAsync(CreateManifest(fixture));
        await File.AppendAllTextAsync(Path.Combine(fixture.Source, "bin", "ffmpeg.exe"), "corrupt");
        ReplayFoundryRuntimePackVerificationResult result = await new ReplayFoundryRuntimePackVerifier().VerifyAsync(fixture.Source);
        Assert(!result.IsValid, "A corrupt file verified.");
    }

    private static async Task MissingFileFails()
    {
        using Fixture fixture = new();
        await fixture.SealAsync(CreateManifest(fixture));
        File.Delete(Path.Combine(fixture.Source, "bin", "ffprobe.exe"));
        ReplayFoundryRuntimePackVerificationResult result = await new ReplayFoundryRuntimePackVerifier().VerifyAsync(fixture.Source);
        Assert(!result.IsValid, "A missing file verified.");
    }

    private static async Task ExtraFileFails()
    {
        using Fixture fixture = new();
        await fixture.SealAsync(CreateManifest(fixture));
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "extra.bin"), "extra");
        ReplayFoundryRuntimePackVerificationResult result = await new ReplayFoundryRuntimePackVerifier().VerifyAsync(fixture.Source);
        Assert(!result.IsValid && result.Errors.Any(error => error.Contains("Unexpected", StringComparison.Ordinal)), "Extra-file policy was not strict.");
    }

    private static async Task RuntimeStartupIsBounded()
    {
        using Fixture fixture = new();
        Directory.CreateDirectory(Path.Combine(fixture.Source, "host"));
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Source, "python.exe"),
            "python");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Source, "host", "qwen.py"),
            "host");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Source, "large-asset.bin"),
            "asset");
        ReplayFoundryRuntimePackManifest manifest =
            ReplayFoundryRuntimePackManifest.Create(
                new(
                    "replayfoundry-qwen-runtime",
                    ReplayFoundryRuntimePackKind.VisualRuntime,
                    "1.0.0"),
                "Qwen runtime",
                ReplayFoundryRuntimeBackend.Cuda,
                [
                    fixture.File(
                        "python.exe",
                        ReplayFoundryRuntimeFileRole.PythonExecutable),
                    fixture.File(
                        "host/qwen.py",
                        ReplayFoundryRuntimeFileRole.VisualHostScript),
                    fixture.File(
                        "large-asset.bin",
                        ReplayFoundryRuntimeFileRole.Asset),
                    fixture.File(
                        "LICENSE.txt",
                        ReplayFoundryRuntimeFileRole.License),
                ],
                [],
                fixture.Licenses(),
                fixture.Sources(),
                "0.1.0",
                "1.0.0",
                Fixture.Created);
        await fixture.SealAsync(manifest);

        await File.WriteAllTextAsync(
            Path.Combine(fixture.Source, "large-asset.bin"),
            "changed after verified installation");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Source, "unlisted-cache.pyc"),
            "cache");
        var verifier = new ReplayFoundryRuntimePackVerifier();
        ReplayFoundryRuntimePackVerificationResult startup =
            await verifier.VerifyAsync(
                fixture.Source,
                mode: ReplayFoundryRuntimePackVerificationMode.RuntimeStartup);
        ReplayFoundryRuntimePackVerificationResult full =
            await verifier.VerifyAsync(fixture.Source);

        Assert(
            startup.IsValid,
            "Startup verification must remain bounded to trusted manifest and entry-point files.");
        Assert(
            !full.IsValid,
            "Full install and repair verification must still reject changed or extra payload files.");

        await File.WriteAllTextAsync(
            Path.Combine(fixture.Source, "host", "qwen.py"),
            "changed host");
        ReplayFoundryRuntimePackVerificationResult changedEntry =
            await verifier.VerifyAsync(
                fixture.Source,
                mode: ReplayFoundryRuntimePackVerificationMode.RuntimeStartup);
        Assert(
            !changedEntry.IsValid,
            "Startup verification must reject a changed executable host entry point.");
    }

    private static async Task InstallAndResolve()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture);
        await fixture.SealAsync(manifest);
        ReplayFoundryRuntimePackStore store = fixture.Store();
        InstalledReplayFoundryRuntimePack installed = await store.InstallAsync(fixture.Source, activate: true);
        InstalledReplayFoundryRuntimePack resolved = await store.ResolveActiveAsync(ReplayFoundryRuntimePackKind.MediaTools);
        Assert(installed.Manifest.ManifestHash == resolved.Manifest.ManifestHash && File.Exists(resolved.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable)), "Installed pack did not resolve.");
    }

    private static async Task CancellationCleansStage()
    {
        using Fixture fixture = new();
        await fixture.SealAsync(CreateManifest(fixture));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await AssertThrowsAsync<OperationCanceledException>(() => fixture.Store().InstallAsync(fixture.Source, true, cancellation.Token));
        Assert(!Directory.Exists(Path.Combine(fixture.StoreRoot, ".staging")) || !Directory.EnumerateDirectories(Path.Combine(fixture.StoreRoot, ".staging")).Any(), "Cancelled staging remained.");
    }

    private static async Task CompactContentAddressedPath()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture);
        await fixture.SealAsync(manifest);
        InstalledReplayFoundryRuntimePack installed = await fixture.Store().InstallAsync(fixture.Source, activate: true);
        string relative = Path.GetRelativePath(fixture.StoreRoot, installed.RootDirectory);
        Assert(!relative.Contains(Path.DirectorySeparatorChar) && relative.Length == 43,
            "The content-addressed directory was not the compact full-SHA-256 base64url identity.");
        Assert(!relative.Contains('=') && !relative.Contains('+') && !relative.Contains('/'),
            "The content identity was not filesystem-safe base64url.");
    }

    private static async Task AbandonedStageCleans()
    {
        using Fixture fixture = new();
        string abandoned = Path.Combine(fixture.StoreRoot, ".staging", "abandoned");
        Directory.CreateDirectory(abandoned);
        await File.WriteAllTextAsync(Path.Combine(abandoned, "partial"), "x");
        await fixture.Store().CleanupAbandonedStagingAsync();
        Assert(!Directory.Exists(abandoned), "Abandoned stage remained.");
    }

    private static async Task SideBySideVersions()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest first = CreateManifest(fixture, version: "1.0.0");
        await fixture.SealAsync(first);
        ReplayFoundryRuntimePackStore store = fixture.Store();
        await store.InstallAsync(fixture.Source, true);
        File.Delete(Path.Combine(fixture.Source, RuntimePackManifestJson.FileName));
        ReplayFoundryRuntimePackManifest second = CreateManifest(fixture, version: "2.0.0");
        await fixture.SealAsync(second);
        await store.InstallAsync(fixture.Source, true);
        IReadOnlyList<InstalledReplayFoundryRuntimePack> all = await store.ListInstalledAsync();
        Assert(all.Count == 2, "Side-by-side versions were not retained.");
    }

    private static async Task InactivePruningRetainsActiveClosure()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackStore store = fixture.Store();
        ReplayFoundryRuntimePackManifest first = CreateManifest(fixture, version: "1.0.0");
        await fixture.SealAsync(first);
        await store.InstallAsync(fixture.Source, true);

        File.Delete(Path.Combine(fixture.Source, RuntimePackManifestJson.FileName));
        ReplayFoundryRuntimePackManifest second = CreateManifest(fixture, version: "2.0.0");
        await fixture.SealAsync(second);
        await store.InstallAsync(fixture.Source, true);

        File.Delete(Path.Combine(fixture.Source, RuntimePackManifestJson.FileName));
        ReplayFoundryRuntimePackManifest speech = CreateSpeechManifest(
            fixture,
            [new("replayfoundry-media-tools", "1.0.0", first.ManifestHash)]);
        await fixture.SealAsync(speech);
        await store.InstallAsync(fixture.Source, true);

        int pruned = await store.PruneInactiveAsync();
        IReadOnlyList<InstalledReplayFoundryRuntimePack> installed = await store.ListInstalledAsync();
        Assert(pruned == 0, "Pruning removed a dependency required by an active pack.");
        Assert(installed.Count == 3, "Pruning did not preserve the active dependency closure.");

        await store.RemoveAsync(speech.Identity.PackageId, speech.ManifestHash);
        pruned = await store.PruneInactiveAsync();
        installed = await store.ListInstalledAsync();
        Assert(pruned == 1, "Pruning did not remove the superseded package version.");
        Assert(installed.Count == 1 && installed[0].Manifest.ManifestHash == second.ManifestHash,
            "Pruning did not retain exactly the active package version.");
    }

    private static async Task ZeroDependencySkipsUnrelatedPayloads()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackStore store = fixture.Store();
        ReplayFoundryRuntimePackManifest first = CreateManifest(fixture, version: "1.0.0");
        await fixture.SealAsync(first);
        InstalledReplayFoundryRuntimePack installed = await store.InstallAsync(fixture.Source, activate: true);

        ReplayFoundryRuntimePackManifest second = CreateManifest(fixture, version: "2.0.0");
        await fixture.SealAsync(second);
        using (new FileStream(
                   installed.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable),
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None))
        {
            InstalledReplayFoundryRuntimePack result =
                await store.InstallAsync(fixture.Source, activate: true);
            Assert(
                result.Manifest.ManifestHash == second.ManifestHash,
                "The independent package was not installed while an unrelated payload was locked.");
        }
    }

    private static async Task ZeroDependencyReinstallVerifiesTarget()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture);
        await fixture.SealAsync(manifest);
        ReplayFoundryRuntimePackStore store = fixture.Store();
        InstalledReplayFoundryRuntimePack installed =
            await store.InstallAsync(fixture.Source, activate: true);
        await File.AppendAllTextAsync(
            installed.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable),
            "corrupt");

        await AssertThrowsAsync<InvalidDataException>(
            () => store.InstallAsync(fixture.Source, activate: true));
    }

    private static async Task MissingDependencyRejects()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackDependency dependency = new("replayfoundry-media-tools", "1.0.0");
        ReplayFoundryRuntimePackManifest speech = CreateSpeechManifest(fixture, [dependency]);
        await fixture.SealAsync(speech);
        await AssertThrowsAsync<InvalidOperationException>(() => fixture.Store().InstallAsync(fixture.Source, true));
    }

    private static async Task ExactHashDependencyIsTargeted()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackStore store = fixture.Store();
        ReplayFoundryRuntimePackManifest first = CreateManifest(
            fixture,
            version: "1.0.0",
            packageId: "dependency-media");
        await fixture.SealAsync(first);
        await store.InstallAsync(fixture.Source, activate: true);

        ReplayFoundryRuntimePackManifest second = CreateManifest(
            fixture,
            version: "2.0.0",
            packageId: "dependency-media");
        await fixture.SealAsync(second);
        InstalledReplayFoundryRuntimePack installedSecond =
            await store.InstallAsync(fixture.Source, activate: true);

        ReplayFoundryRuntimePackManifest dependent = CreateSpeechManifest(
            fixture,
            [new("dependency-media", "1.0.0", first.ManifestHash)]);
        await fixture.SealAsync(dependent);
        using (new FileStream(
                   installedSecond.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable),
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None))
        {
            InstalledReplayFoundryRuntimePack installedDependent =
                await store.InstallAsync(fixture.Source, activate: true);
            Assert(
                installedDependent.Manifest.ManifestHash == dependent.ManifestHash,
                "An exact-hash dependency inspected an unrequested side-by-side payload.");
        }
    }

    private static async Task SamePackageExactDependencyPermitsPredecessor()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackStore store = fixture.Store();
        ReplayFoundryRuntimePackManifest predecessor = CreateManifest(
            fixture,
            version: "1.0.0",
            packageId: "versioned-media");
        await fixture.SealAsync(predecessor);
        await store.InstallAsync(fixture.Source, activate: true);

        ReplayFoundryRuntimePackManifest successor = CreateManifest(
            fixture,
            version: "2.0.0",
            packageId: "versioned-media",
            dependencies:
            [
                new("versioned-media", "1.0.0", predecessor.ManifestHash),
            ]);
        await fixture.SealAsync(successor);
        InstalledReplayFoundryRuntimePack installed =
            await store.InstallAsync(fixture.Source, activate: true);

        Assert(
            installed.Manifest.ManifestHash == successor.ManifestHash,
            "A successor could not depend on an exact immutable predecessor of the same package.");
    }

    private static Task SamePackageRangeDependencyRejects()
    {
        using Fixture fixture = new();
        AssertThrows<ArgumentException>(() => CreateManifest(
            fixture,
            version: "2.0.0",
            packageId: "versioned-media",
            dependencies:
            [
                new("versioned-media", "1.0.0"),
            ]));
        return Task.CompletedTask;
    }

    private static async Task CompatibleDependencySelectsHighestVersion()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackStore store = fixture.Store();
        ReplayFoundryRuntimePackManifest first = CreateManifest(
            fixture,
            version: "1.0.0",
            packageId: "compatible-media");
        await fixture.SealAsync(first);
        InstalledReplayFoundryRuntimePack installedFirst =
            await store.InstallAsync(fixture.Source, activate: true);

        ReplayFoundryRuntimePackManifest second = CreateManifest(
            fixture,
            version: "2.0.0",
            packageId: "compatible-media");
        await fixture.SealAsync(second);
        await store.InstallAsync(fixture.Source, activate: true);

        ReplayFoundryRuntimePackManifest dependent = CreateSpeechManifest(
            fixture,
            [new("compatible-media", "1.0.0")]);
        await fixture.SealAsync(dependent);
        using (new FileStream(
                   installedFirst.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable),
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None))
        {
            await store.InstallAsync(fixture.Source, activate: true);
        }
    }

    private static async Task RequiredDependencyCorruptionIsIsolated()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackStore store = fixture.Store();
        ReplayFoundryRuntimePackManifest required = CreateManifest(
            fixture,
            packageId: "required-media");
        await fixture.SealAsync(required);
        InstalledReplayFoundryRuntimePack installedRequired =
            await store.InstallAsync(fixture.Source, activate: true);

        ReplayFoundryRuntimePackManifest unrelated = CreateManifest(
            fixture,
            packageId: "unrelated-media");
        await fixture.SealAsync(unrelated);
        InstalledReplayFoundryRuntimePack installedUnrelated =
            await store.InstallAsync(fixture.Source, activate: true);
        await File.AppendAllTextAsync(
            installedUnrelated.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable),
            "corrupt");

        ReplayFoundryRuntimePackManifest dependent = CreateSpeechManifest(
            fixture,
            [new("required-media", "1.0.0", required.ManifestHash)]);
        await fixture.SealAsync(dependent);
        await store.InstallAsync(fixture.Source, activate: true);

        await File.AppendAllTextAsync(
            installedRequired.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable),
            "corrupt");
        await AssertThrowsAsync<InvalidDataException>(
            () => store.InstallAsync(fixture.Source, activate: true));
    }

    private static async Task ReachableDependencyCycleRejects()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest packA = CreateManifest(
            fixture,
            version: "1.0.0",
            packageId: "cycle-a",
            dependencies: [new("cycle-b", "1.0.0")]);
        await fixture.SealAsync(packA);
        fixture.PlaceSourceInStore(packA);

        ReplayFoundryRuntimePackManifest packB = CreateSpeechManifest(
            fixture,
            [new("cycle-a", "1.0.0")],
            packageId: "cycle-b");
        await fixture.SealAsync(packB);
        fixture.PlaceSourceInStore(packB);

        ReplayFoundryRuntimePackManifest root = CreateManifest(
            fixture,
            packageId: "cycle-root",
            dependencies: [new("cycle-a", "1.0.0", packA.ManifestHash)]);
        await fixture.SealAsync(root);
        await AssertThrowsAsync<InvalidDataException>(
            () => fixture.Store().InstallAsync(fixture.Source, activate: true));
    }

    private static async Task DependencySafeRemoval()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackStore store = fixture.Store();
        ReplayFoundryRuntimePackManifest media = CreateManifest(fixture);
        await fixture.SealAsync(media);
        await store.InstallAsync(fixture.Source, true);
        File.Delete(Path.Combine(fixture.Source, RuntimePackManifestJson.FileName));
        ReplayFoundryRuntimePackManifest speech = CreateSpeechManifest(fixture, [new("replayfoundry-media-tools", "1.0.0", media.ManifestHash)]);
        await fixture.SealAsync(speech);
        await store.InstallAsync(fixture.Source, true);
        await AssertThrowsAsync<InvalidOperationException>(() => store.RemoveAsync("replayfoundry-media-tools"));
    }

    private static async Task RepairRestores()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture);
        await fixture.SealAsync(manifest);
        ReplayFoundryRuntimePackStore store = fixture.Store();
        InstalledReplayFoundryRuntimePack installed = await store.InstallAsync(fixture.Source, true);
        await File.AppendAllTextAsync(installed.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable), "bad");
        await store.RepairAsync(fixture.Source);
        ReplayFoundryRuntimePackVerificationResult result = await new ReplayFoundryRuntimePackVerifier().VerifyAsync(installed.RootDirectory);
        Assert(result.IsValid, "Repair did not restore the package.");
    }

    private static async Task UnverifiedNeverResolves()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture);
        await fixture.SealAsync(manifest);
        ReplayFoundryRuntimePackStore store = fixture.Store();
        InstalledReplayFoundryRuntimePack installed = await store.InstallAsync(fixture.Source, true);
        await File.AppendAllTextAsync(installed.Resolve(ReplayFoundryRuntimeFileRole.FfmpegExecutable), "bad");
        await AssertThrowsAsync<InvalidDataException>(() => store.ResolveActiveAsync(ReplayFoundryRuntimePackKind.MediaTools));
    }

    private static Task VisualRuntimeRolesRequired()
    {
        using Fixture fixture = new();
        AssertThrows<ArgumentException>(() => ReplayFoundryRuntimePackManifest.Create(
            new("replayfoundry-qwen-runtime", ReplayFoundryRuntimePackKind.VisualRuntime, "1.0.0"),
            "Qwen runtime", ReplayFoundryRuntimeBackend.Cuda,
            [fixture.File("LICENSE.txt", ReplayFoundryRuntimeFileRole.License)],
            [], fixture.Licenses(), fixture.Sources(), "0.1.0", "1.0.0", Fixture.Created));
        return Task.CompletedTask;
    }

    private static async Task FailedInstallKeepsActive()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture);
        await fixture.SealAsync(manifest);
        ReplayFoundryRuntimePackStore store = fixture.Store();
        await store.InstallAsync(fixture.Source, true);
        await File.AppendAllTextAsync(Path.Combine(fixture.Source, "bin", "ffmpeg.exe"), "bad");
        await AssertThrowsAsync<InvalidDataException>(() => store.InstallAsync(fixture.Source, true));
        InstalledReplayFoundryRuntimePack active = await store.ResolveActiveAsync(ReplayFoundryRuntimePackKind.MediaTools);
        Assert(active.Manifest.ManifestHash == manifest.ManifestHash, "Failed installation replaced active selection.");
    }

    private static async Task ZipTraversalRejects()
    {
        using Fixture fixture = new();
        string zip = Path.Combine(fixture.Root, "traversal.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
            await using StreamWriter writer = new(entry.Open());
            await writer.WriteAsync("bad");
        }
        await AssertThrowsAsync<InvalidDataException>(() => fixture.Store().InstallAsync(zip, true));
    }

    private static Task CatalogRequiresHttps()
    {
        AssertThrows<ArgumentException>(() => new ReplayFoundryRuntimePackCatalogItem(
            "replayfoundry-media-tools", ReplayFoundryRuntimePackKind.MediaTools, "1.0.0",
            new Uri("http://downloads.example.test/media.zip"), 1, Hash("x"), []));
        return Task.CompletedTask;
    }

    private static async Task CatalogRejectsRedirect()
    {
        using Fixture fixture = new();
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        ReplayFoundryRuntimePackCatalog catalog = Catalog(payload, HashBytes(payload));
        using var client = new HttpClient(new StaticResponseHandler(payload, new Uri("https://unapproved.example.test/media.zip")));
        var installer = new ReplayFoundryRuntimePackCatalogInstaller(client, fixture.Store());
        string downloads = Path.Combine(fixture.Root, "downloads");
        await AssertThrowsAsync<InvalidDataException>(() => installer.InstallAsync(catalog, downloads));
        Assert(!Directory.Exists(downloads) || !Directory.EnumerateFiles(downloads).Any(), "Rejected redirect left a downloaded payload.");
    }

    private static async Task CatalogRejectsHashMismatch()
    {
        using Fixture fixture = new();
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        ReplayFoundryRuntimePackCatalog catalog = Catalog(payload, Hash("different"));
        using var client = new HttpClient(new StaticResponseHandler(payload, new Uri("https://downloads.example.test/media.zip")));
        var installer = new ReplayFoundryRuntimePackCatalogInstaller(client, fixture.Store());
        string downloads = Path.Combine(fixture.Root, "downloads");
        await AssertThrowsAsync<InvalidDataException>(() => installer.InstallAsync(catalog, downloads));
        Assert(!Directory.Exists(downloads) || !Directory.EnumerateFiles(downloads).Any(), "Hash failure left a downloaded payload.");
    }

    private static Task CatalogRequiresFixedProfile()
    {
        ReplayFoundryRuntimePackCatalogItem media = new(
            "replayfoundry-media-tools", ReplayFoundryRuntimePackKind.MediaTools, "1.0.0",
            new Uri("https://downloads.example.test/media.zip"), 1, Hash("x"), []);
        _ = new ReplayFoundryRuntimePackCatalog(
            ReplayFoundryRuntimePackCatalog.Schema, "Base", [media], Fixture.Created);
        AssertThrows<ArgumentException>(() => new ReplayFoundryRuntimePackCatalog(
            ReplayFoundryRuntimePackCatalog.Schema, "Advanced", [media], Fixture.Created));
        return Task.CompletedTask;
    }

    private static async Task CatalogRollsBackPartialProfile()
    {
        using Fixture fixture = new();
        await fixture.SealAsync(CreateManifest(fixture));
        string mediaZip = Path.Combine(fixture.Root, "media.zip");
        ZipFile.CreateFromDirectory(fixture.Source, mediaZip);
        byte[] validMedia = await File.ReadAllBytesAsync(mediaZip);
        byte[] invalid = Encoding.UTF8.GetBytes("not a runtime pack");
        ReplayFoundryRuntimePackCatalogItem Item(
            string id,
            ReplayFoundryRuntimePackKind kind,
            string file,
            byte[] bytes) => new(
                id, kind, "1.0.0", new Uri("https://downloads.example.test/" + file),
                bytes.Length, HashBytes(bytes), []);
        var catalog = new ReplayFoundryRuntimePackCatalog(
            ReplayFoundryRuntimePackCatalog.Schema,
            "Advanced",
            [
                Item("replayfoundry-media-tools", ReplayFoundryRuntimePackKind.MediaTools, "media.zip", validMedia),
                Item("replayfoundry-speech", ReplayFoundryRuntimePackKind.SpeechActivity, "speech.zip", invalid),
                Item("replayfoundry-transcription-runtime", ReplayFoundryRuntimePackKind.TranscriptionRuntime, "tr.zip", invalid),
                Item("replayfoundry-transcription-model", ReplayFoundryRuntimePackKind.TranscriptionModel, "tm.zip", invalid),
                Item("replayfoundry-visual-runtime", ReplayFoundryRuntimePackKind.VisualRuntime, "vr.zip", invalid),
                Item("replayfoundry-visual-model", ReplayFoundryRuntimePackKind.VisualModel, "vm.zip", invalid),
            ],
            Fixture.Created);
        var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["/media.zip"] = validMedia,
            ["/speech.zip"] = invalid,
            ["/tr.zip"] = invalid,
            ["/tm.zip"] = invalid,
            ["/vr.zip"] = invalid,
            ["/vm.zip"] = invalid,
        };
        using var client = new HttpClient(new RoutingResponseHandler(responses));
        var installer = new ReplayFoundryRuntimePackCatalogInstaller(client, fixture.Store());
        await AssertThrowsAsync<InvalidDataException>(() => installer.InstallAsync(catalog, Path.Combine(fixture.Root, "downloads")));
        Assert((await fixture.Store().ListInstalledAsync()).Count == 0, "Failed Advanced profile left a partial installed pack.");
    }

    private static async Task EmptyStoreCleanup()
    {
        using Fixture fixture = new();
        ReplayFoundryRuntimePackManifest manifest = CreateManifest(fixture);
        await fixture.SealAsync(manifest);
        ReplayFoundryRuntimePackStore store = fixture.Store();
        await store.InstallAsync(fixture.Source, activate: true);
        await store.CleanupEmptyStoreAsync();
        Assert(Directory.Exists(fixture.StoreRoot), "Cleanup removed a nonempty store.");
        await store.RemoveAsync(manifest.Identity.PackageId);
        await store.CleanupEmptyStoreAsync();
        Assert(!Directory.Exists(fixture.StoreRoot), "Cleanup left an empty package store.");
    }

    private static ReplayFoundryRuntimePackCatalog Catalog(byte[] payload, string hash) => new(
        ReplayFoundryRuntimePackCatalog.Schema,
        "Base",
        [new ReplayFoundryRuntimePackCatalogItem(
            "replayfoundry-media-tools", ReplayFoundryRuntimePackKind.MediaTools, "1.0.0",
            new Uri("https://downloads.example.test/media.zip"), payload.Length, hash, [])],
        Fixture.Created);

    private static ReplayFoundryRuntimePackManifest CreateManifest(
        Fixture fixture,
        string version = "1.0.0",
        IEnumerable<ReplayFoundryRuntimePackFile>? files = null,
        string packageId = "replayfoundry-media-tools",
        IEnumerable<ReplayFoundryRuntimePackDependency>? dependencies = null) =>
        ReplayFoundryRuntimePackManifest.Create(
            new(packageId, ReplayFoundryRuntimePackKind.MediaTools, version),
            "Media tools", ReplayFoundryRuntimeBackend.Cpu,
            files ?? fixture.DefaultFiles(), dependencies, fixture.Licenses(), fixture.Sources(), "0.1.0", "1.0.0", Fixture.Created);

    private static ReplayFoundryRuntimePackManifest CreateSpeechManifest(
        Fixture fixture,
        IEnumerable<ReplayFoundryRuntimePackDependency> dependencies,
        string packageId = "replayfoundry-speech",
        string version = "1.0.0") =>
        ReplayFoundryRuntimePackManifest.Create(
            new(packageId, ReplayFoundryRuntimePackKind.SpeechActivity, version),
            "Speech activity", ReplayFoundryRuntimeBackend.Cpu,
            [
                fixture.File("model.onnx", ReplayFoundryRuntimeFileRole.SpeechActivityModel),
                fixture.File("LICENSE.txt", ReplayFoundryRuntimeFileRole.License),
                fixture.File("bin/ffmpeg.exe", ReplayFoundryRuntimeFileRole.Asset),
                fixture.File("bin/ffprobe.exe", ReplayFoundryRuntimeFileRole.Asset),
            ],
            dependencies, fixture.Licenses(), fixture.Sources(), "0.1.0", "1.0.0", Fixture.Created);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly DateTimeOffset Created = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ReplayFoundry-RuntimePackTests", Guid.NewGuid().ToString("N"));
            Source = Path.Combine(Root, "source");
            StoreRoot = Path.Combine(Root, "store");
            Directory.CreateDirectory(Path.Combine(Source, "bin"));
            System.IO.File.WriteAllText(Path.Combine(Source, "bin", "ffmpeg.exe"), "ffmpeg");
            System.IO.File.WriteAllText(Path.Combine(Source, "bin", "ffprobe.exe"), "ffprobe");
            System.IO.File.WriteAllText(Path.Combine(Source, "LICENSE.txt"), "license");
            System.IO.File.WriteAllText(Path.Combine(Source, "model.onnx"), "model");
        }
        public string Root { get; }
        public string Source { get; }
        public string StoreRoot { get; }
        public ReplayFoundryRuntimePackStore Store() => new(new ReplayFoundryRuntimePackStorePaths(StoreRoot));
        public void PlaceSourceInStore(ReplayFoundryRuntimePackManifest manifest)
        {
            string destination = new ReplayFoundryRuntimePackStorePaths(StoreRoot)
                .FinalDirectory(manifest);
            Directory.CreateDirectory(destination);
            foreach (string sourceFile in Directory.EnumerateFiles(
                         Source,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(Source, sourceFile);
                string destinationFile = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                System.IO.File.Copy(sourceFile, destinationFile);
            }
        }
        public ReplayFoundryRuntimePackFile File(string relative, ReplayFoundryRuntimeFileRole role)
        {
            string path = Path.Combine(Source, relative.Replace('/', Path.DirectorySeparatorChar));
            FileInfo info = new(path);
            return new(relative, info.Length, RuntimePackTests.Hash(System.IO.File.ReadAllText(path)), role);
        }
        public ReplayFoundryRuntimePackFile[] DefaultFiles() =>
        [
            File("bin/ffmpeg.exe", ReplayFoundryRuntimeFileRole.FfmpegExecutable),
            File("bin/ffprobe.exe", ReplayFoundryRuntimeFileRole.FfprobeExecutable),
            File("LICENSE.txt", ReplayFoundryRuntimeFileRole.License),
            File("model.onnx", ReplayFoundryRuntimeFileRole.Asset),
        ];
        public ReplayFoundryRuntimePackLicense[] Licenses() =>
        [new("Fixture", "MIT", "LICENSE.txt", RuntimePackTests.Hash("license"), "https://example.com/license", "Test fixture only.")];
        public ReplayFoundryRuntimePackSource[] Sources() =>
        [new("https://example.com/runtime.zip", "v1", RuntimePackTests.Hash("archive"))];
        public Task SealAsync(ReplayFoundryRuntimePackManifest manifest) => RuntimePackManifestJson.WriteAsync(manifest, Path.Combine(Source, RuntimePackManifestJson.FileName));
        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class StaticResponseHandler(byte[] payload, Uri finalUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var finalRequest = new HttpRequestMessage(HttpMethod.Get, finalUri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = finalRequest,
                Content = new ByteArrayContent(payload),
            });
        }
    }

    private sealed class RoutingResponseHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri ?? throw new InvalidOperationException("Request URI missing.");
            byte[] payload = responses[uri.AbsolutePath];
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, uri),
                Content = new ByteArrayContent(payload),
            });
        }
    }
}
