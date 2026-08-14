using System.Collections.ObjectModel;
using System.IO;

namespace ReplayFoundry.Desktop.Features.Settings;

public interface IRuntimePackMaintenanceActions
{
    bool CanAddAdvanced { get; }
    bool CanRepair { get; }
    bool CanRemoveAdvanced { get; }
    void AddAdvanced();
    void Repair();
    void RemoveAdvanced();
    void OpenPackageFolder();
}

public sealed class SettingsRuntimeCapabilitySnapshot
{
    private readonly ReadOnlyCollection<SettingsCapabilityItem> _capabilities;

    public SettingsRuntimeCapabilitySnapshot(
        bool isBaseReady,
        bool isBalancedReady,
        bool isThoroughReady,
        bool hasAdvancedCapability,
        string packageStoreRoot,
        IEnumerable<SettingsCapabilityItem> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageStoreRoot);
        ArgumentNullException.ThrowIfNull(capabilities);
        SettingsCapabilityItem[] snapshot = capabilities.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null))
            throw new ArgumentException("At least one non-null runtime capability is required.", nameof(capabilities));
        IsBaseReady = isBaseReady;
        IsBalancedReady = isBalancedReady;
        IsThoroughReady = isThoroughReady;
        HasAdvancedCapability = hasAdvancedCapability;
        PackageStoreRoot = Path.GetFullPath(packageStoreRoot);
        _capabilities = Array.AsReadOnly(snapshot);
    }

    public bool IsBaseReady { get; }
    public bool IsBalancedReady { get; }
    public bool IsThoroughReady { get; }
    public string PackageStoreRoot { get; }
    public IReadOnlyList<SettingsCapabilityItem> Capabilities => _capabilities;
    public bool HasAdvancedCapability { get; }
}
