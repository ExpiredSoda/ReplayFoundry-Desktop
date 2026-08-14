using System.Diagnostics;
using System.IO;
using System.Reflection;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.RuntimePacks;

public sealed class RuntimePackMaintenanceLauncher : IRuntimePackMaintenanceActions
{
    private const string AdvancedInstallerEnvironment = "REPLAYFOUNDRY_ADVANCED_INSTALLER";
    private readonly string _storeRoot;
    private readonly string? _advancedInstallerTarget;
    private readonly string? _runtimeInstaller;

    public RuntimePackMaintenanceLauncher(string storeRoot)
    {
        _storeRoot = Path.GetFullPath(storeRoot);
        _advancedInstallerTarget = FindAdvancedInstallerTarget();
        _runtimeInstaller = FindRuntimeInstaller();
    }

    public bool CanAddAdvanced => _advancedInstallerTarget is not null;
    public bool CanRepair => _advancedInstallerTarget is not null && File.Exists(_advancedInstallerTarget);
    public bool CanRemoveAdvanced => _runtimeInstaller is not null;

    public void AddAdvanced() => LaunchInstaller();
    public void Repair() => LaunchInstaller();

    public void RemoveAdvanced()
    {
        if (_runtimeInstaller is null) throw new InvalidOperationException("The runtime maintenance tool is not installed.");
        Start(_runtimeInstaller, ["remove-advanced", "--store-root", _storeRoot]);
    }

    public void OpenPackageFolder()
    {
        Directory.CreateDirectory(_storeRoot);
        Process.Start(new ProcessStartInfo(_storeRoot) { UseShellExecute = true });
    }

    private void LaunchInstaller()
    {
        if (_advancedInstallerTarget is null) throw new InvalidOperationException("The Advanced AI installer is not available on this PC.");
        if (Uri.TryCreate(_advancedInstallerTarget, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return;
        }
        Start(_advancedInstallerTarget, []);
    }

    private static string? FindAdvancedInstallerTarget()
    {
        string? explicitPath = ExplicitRuntimeEnvironment.Read(AdvancedInstallerEnvironment);
        string[] candidates =
        [
            explicitPath ?? string.Empty,
            Path.Combine(AppContext.BaseDirectory, "Maintenance", "ReplayFoundry-Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "Maintenance", "ReplayFoundry-Advanced-Setup.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayFoundry", "Installers", "ReplayFoundry-Setup.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayFoundry", "Installers", "ReplayFoundry-Advanced-Setup.exe"),
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
        Process.Start(start);
    }
}
