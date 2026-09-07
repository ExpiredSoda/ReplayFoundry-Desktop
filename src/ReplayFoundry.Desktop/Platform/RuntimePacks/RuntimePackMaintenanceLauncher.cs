using System.Diagnostics;
using System.IO;
using System.Reflection;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Platform.RuntimePacks;

public sealed class RuntimePackMaintenanceLauncher : IRuntimePackMaintenanceActions
{
    private const string AdvancedInstallerEnvironment = "REPLAYFOUNDRY_ADVANCED_INSTALLER";
    private readonly string _storeRoot;
    private readonly Func<string?> _resolveInstaller;
    private readonly Action<string, IReadOnlyList<string>> _launch;
    private readonly bool _hasAdvancedTools;
    private readonly string? _runtimeInstaller;

    public RuntimePackMaintenanceLauncher(string storeRoot, bool hasAdvancedTools = false)
        : this(storeRoot, FindAdvancedInstallerTarget, Start, hasAdvancedTools)
    {
    }

    internal RuntimePackMaintenanceLauncher(string storeRoot,
        Func<string?> resolveInstaller, Action<string, IReadOnlyList<string>> launch,
        bool hasAdvancedTools = false)
    {
        _storeRoot = Path.GetFullPath(storeRoot);
        _resolveInstaller = resolveInstaller;
        _launch = launch;
        _hasAdvancedTools = hasAdvancedTools;
        _runtimeInstaller = FindRuntimeInstaller();
    }

    public bool CanAddAdvanced => _resolveInstaller() is not null;
    public bool CanRepair => _resolveInstaller() is not null;
    public bool CanRemoveAdvanced => _runtimeInstaller is not null;

    public void AddAdvanced() => LaunchInstaller(addAdvanced: true);
    public void Repair() => LaunchInstaller(addAdvanced: _hasAdvancedTools);

    public void RemoveAdvanced()
    {
        if (_runtimeInstaller is null) throw new InvalidOperationException("The Advanced AI removal tool is not installed.");
        Start(
            _runtimeInstaller,
            [
                "remove-advanced",
                "--store-root",
                _storeRoot,
                "--wait-for-parent",
                Environment.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ]);
        RuntimeMaintenanceShutdownCoordinator.Request(
            new WpfRuntimeMaintenanceApplicationLifecycle());
    }

    public void OpenPackageFolder()
    {
        Directory.CreateDirectory(_storeRoot);
        Process.Start(new ProcessStartInfo(_storeRoot) { UseShellExecute = true });
    }

    private void LaunchInstaller(bool addAdvanced)
    {
        // Resolve at click time: a cached setup may have been removed since startup.
        string? target = _resolveInstaller();
        if (target is null) throw new InvalidOperationException("The Replay Foundry installer is not available on this PC.");
        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            _launch(uri.AbsoluteUri, []);
            return;
        }
        _launch(target, addAdvanced ? ["/MERGETASKS=advancedai"] : []);
    }

    private static string? FindAdvancedInstallerTarget()
    {
        string? explicitPath = ExplicitRuntimeEnvironment.Read(AdvancedInstallerEnvironment);
        string[] candidates =
        [
            explicitPath ?? string.Empty,
            Path.Combine(AppContext.BaseDirectory, "Maintenance", "ReplayFoundry-Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "Maintenance", "ReplayFoundry-Advanced-Setup.exe"),
            Path.Combine(ReplayFoundryLocalDataPaths.Current.SharedInstallerRoot, "ReplayFoundry-Setup.exe"),
            Path.Combine(ReplayFoundryLocalDataPaths.Current.SharedInstallerRoot, "ReplayFoundry-Advanced-Setup.exe"),
        ];
        string? file = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (file is not null) return file;
        string? configuredUri = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "ReplayFoundry.AdvancedInstallerUri")?.Value;
        return Uri.TryCreate(configuredUri, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }

    private static string? FindRuntimeInstaller()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Tools", "RuntimeInstaller", "ReplayFoundry.RuntimeInstaller.exe"),
            Path.Combine(AppContext.BaseDirectory, "ReplayFoundry.RuntimeInstaller.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void Start(string executable, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new InvalidOperationException(
            "Windows did not start the Advanced AI setup tool.");
    }
}
