using System.Text.Json;
using ReplayFoundry.RuntimePacks;

return await RuntimeInstallerApplication.RunAsync(args);

internal enum RuntimeInstallerExitCode
{
    Success = 0,
    UsageError = 2,
    VerificationFailed = 3,
    DependencyConflict = 4,
    Cancelled = 130,
    UnexpectedFailure = 255,
}

internal static class RuntimeInstallerApplication
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string[] AdvancedPackIds =
    [
        "replayfoundry-silero-vad",
        "replayfoundry-whisper-cpp",
        // Retain the historical base ID so Advanced removal also cleans
        // installations upgraded from pre-small-model builds.
        "replayfoundry-whisper-base-multilingual",
        "replayfoundry-whisper-small-multilingual",
        "replayfoundry-qwen3-vl-runtime",
        "replayfoundry-qwen3-vl-4b-instruct",
    ];

    public static async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            if (arguments.Count == 0) return Usage("A command is required.");
            Dictionary<string, string> options = ParseOptions(arguments.Skip(1));
            string storeRoot = options.TryGetValue("--store-root", out string? root)
                ? Path.GetFullPath(root)
                : ReplayFoundryRuntimePackStorePaths.CreateDefault().RootDirectory;
            using var store = new ReplayFoundryRuntimePackStore(
                new ReplayFoundryRuntimePackStorePaths(storeRoot));
            switch (arguments[0].ToLowerInvariant())
            {
                case "create-manifest":
                    return await CreateManifestAsync(options, cancellation.Token);
                case "verify":
                    return await VerifyAsync(Required(options, "--source"), cancellation.Token);
                case "install":
                    await store.InstallAsync(Required(options, "--source"), activate: true, cancellation.Token);
                    int installPruned = await store.PruneInactiveAsync(cancellation.Token);
                    Console.WriteLine($"Runtime pack installed and activated. Pruned {installPruned} inactive pack(s).");
                    return (int)RuntimeInstallerExitCode.Success;
                case "install-catalog":
                    int catalogResult = await InstallCatalogAsync(store, options, cancellation.Token);
                    if (catalogResult == (int)RuntimeInstallerExitCode.Success)
                    {
                        int catalogPruned = await store.PruneInactiveAsync(cancellation.Token);
                        Console.WriteLine($"Pruned {catalogPruned} inactive pack(s).");
                    }
                    return catalogResult;
                case "repair":
                    await store.RepairAsync(Required(options, "--source"), cancellation.Token);
                    int repairPruned = await store.PruneInactiveAsync(cancellation.Token);
                    Console.WriteLine($"Runtime pack repaired and activated. Pruned {repairPruned} inactive pack(s).");
                    return (int)RuntimeInstallerExitCode.Success;
                case "remove":
                    await store.RemoveAsync(Required(options, "--pack-id"), options.GetValueOrDefault("--manifest-hash"), cancellation.Token);
                    Console.WriteLine("Runtime pack removed.");
                    return (int)RuntimeInstallerExitCode.Success;
                case "remove-advanced":
                    foreach (string id in AdvancedPackIds.Reverse())
                        await store.RemoveAsync(id, cancellationToken: cancellation.Token);
                    Console.WriteLine("Advanced AI runtime packs removed. Base media tools were retained.");
                    return (int)RuntimeInstallerExitCode.Success;
                case "list":
                    return await ListAsync(store, cancellation.Token);
                case "cleanup":
                    await store.CleanupAbandonedStagingAsync(cancellation.Token);
                    Console.WriteLine("Abandoned runtime-pack staging directories removed.");
                    return (int)RuntimeInstallerExitCode.Success;
                case "cleanup-store":
                    await store.CleanupEmptyStoreAsync(cancellation.Token);
                    Console.WriteLine("Empty runtime-pack store directories removed.");
                    return (int)RuntimeInstallerExitCode.Success;
                case "prune-inactive":
                    int pruned = await store.PruneInactiveAsync(cancellation.Token);
                    Console.WriteLine($"Pruned {pruned} inactive pack(s).");
                    return (int)RuntimeInstallerExitCode.Success;
                default:
                    return Usage($"Unknown command '{arguments[0]}'.");
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Runtime-pack operation cancelled before commit.");
            return (int)RuntimeInstallerExitCode.Cancelled;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)RuntimeInstallerExitCode.DependencyConflict;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)RuntimeInstallerExitCode.VerificationFailed;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)RuntimeInstallerExitCode.UnexpectedFailure;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<int> CreateManifestAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        string source = Required(options, "--source");
        ReplayFoundryRuntimePackRecipe recipe = await ReplayFoundryRuntimePackBuilder.ReadRecipeAsync(
            Required(options, "--recipe"), cancellationToken);
        ReplayFoundryRuntimePackManifest manifest = await ReplayFoundryRuntimePackBuilder.BuildAsync(
            source, recipe, cancellationToken);
        string output = options.GetValueOrDefault("--output") ?? Path.Combine(source, RuntimePackManifestJson.FileName);
        await RuntimePackManifestJson.WriteAsync(manifest, output, cancellationToken);
        Console.WriteLine(manifest.ManifestHash);
        return (int)RuntimeInstallerExitCode.Success;
    }

    private static async Task<int> VerifyAsync(string source, CancellationToken cancellationToken)
    {
        var verifier = new ReplayFoundryRuntimePackVerifier();
        ReplayFoundryRuntimePackVerificationResult result = await verifier.VerifyAsync(source, cancellationToken: cancellationToken);
        if (!result.IsValid)
        {
            foreach (string error in result.Errors) Console.Error.WriteLine(error);
            return (int)RuntimeInstallerExitCode.VerificationFailed;
        }
        Console.WriteLine($"Verified {result.Manifest!.Identity.PackageId} {result.Manifest.Identity.SemanticVersion} ({result.Manifest.ManifestHash}).");
        return (int)RuntimeInstallerExitCode.Success;
    }

    private static async Task<int> ListAsync(ReplayFoundryRuntimePackStore store, CancellationToken cancellationToken)
    {
        IReadOnlyList<InstalledReplayFoundryRuntimePack> installed = await store.ListInstalledAsync(cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(installed.Select(pack => new
        {
            pack.Manifest.Identity.PackageId,
            kind = pack.Manifest.Identity.Kind.ToString(),
            pack.Manifest.Identity.SemanticVersion,
            pack.Manifest.ManifestHash,
            pack.RootDirectory,
        }), ReportJsonOptions));
        return (int)RuntimeInstallerExitCode.Success;
    }

    private static async Task<int> InstallCatalogAsync(
        ReplayFoundryRuntimePackStore store,
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        ReplayFoundryRuntimePackCatalog catalog = await ReplayFoundryRuntimePackCatalogReader.ReadAsync(
            Required(options, "--catalog"), cancellationToken);
        string downloadRoot = options.GetValueOrDefault("--download-root") ??
            Path.Combine(Path.GetTempPath(), "ReplayFoundry-RuntimeDownloads", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromHours(4),
        };
        var progress = new Progress<(int Completed, int Total, string PackageId)>(value =>
            Console.WriteLine($"[{value.Completed}/{value.Total}] Installed {value.PackageId}."));
        await new ReplayFoundryRuntimePackCatalogInstaller(client, store).InstallAsync(
            catalog, downloadRoot, progress, cancellationToken);
        return (int)RuntimeInstallerExitCode.Success;
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> values)
    {
        string[] args = values.ToArray();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length || !result.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException($"Invalid or incomplete option '{args[index]}'.");
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");

    private static int Usage(string error)
    {
        Console.Error.WriteLine(error);
        Console.WriteLine("ReplayFoundry.RuntimeInstaller create-manifest --source <dir> --recipe <json> [--output <json>]");
        Console.WriteLine("ReplayFoundry.RuntimeInstaller verify|install|repair --source <dir-or-zip> [--store-root <dir>]");
        Console.WriteLine("ReplayFoundry.RuntimeInstaller install-catalog --catalog <json> [--download-root <dir>] [--store-root <dir>]");
        Console.WriteLine("ReplayFoundry.RuntimeInstaller list|cleanup|cleanup-store|prune-inactive [--store-root <dir>]");
        Console.WriteLine("ReplayFoundry.RuntimeInstaller remove --pack-id <id> [--manifest-hash <sha256>] [--store-root <dir>]");
        Console.WriteLine("ReplayFoundry.RuntimeInstaller remove-advanced [--store-root <dir>]");
        return (int)RuntimeInstallerExitCode.UsageError;
    }
}
