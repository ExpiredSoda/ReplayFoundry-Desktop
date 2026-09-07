using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.Dialogs;
using ReplayFoundry.Desktop.Platform.RuntimePacks;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record SettingsFeatureDependencies(
    ReplayFoundryRuntimeEnvironment Runtime,
    ReplayFoundryLocalDataMaintenanceService LocalDataMaintenance,
    ApplicationPreferenceServices Preferences,
    EditorialFeedbackServices Feedback,
    EditorialServices Editorial,
    PublishFeatureServices Publish,
    DiagnosticReportingServices Diagnostics,
    WindowsLocalFolderLauncher FolderLauncher);

internal static class SettingsFeatureComposition
{
    public static SettingsViewModel Create(
        SettingsFeatureDependencies dependencies)
    {
        var model = new SettingsViewModel(
            dependencies.Publish.ConnectionPermission,
            CreateRuntimeCapabilities(dependencies.Runtime),
            new RuntimePackMaintenanceLauncher(
                dependencies.Runtime.PackageStoreRoot,
                dependencies.Runtime.Capabilities.Any(capability =>
                    capability.IsAvailable &&
                    capability.Name != "Deterministic media analysis")),
            dependencies.Feedback.ResearchParticipation,
            dependencies.Feedback.ResearchStore,
            dependencies.Preferences.OutputLocation,
            new WindowsOutputFolderPicker(),
            dependencies.FolderLauncher,
            dependencies.Editorial.ProfileSession,
            new BugReportSettingsViewModel(
                dependencies.Diagnostics.Consent,
                dependencies.Diagnostics.Outbox,
                dependencies.Diagnostics.Coordinator),
            new LocalDataSettingsViewModel(
                dependencies.LocalDataMaintenance,
                new WindowsLocalDataCleanupConfirmation()),
            dependencies.Preferences.EditorialReroll,
            dependencies.Preferences.MetadataLearningConsent);
        model.AttachTasteLearning(dependencies.Feedback.TasteLearning);
        return model;
    }

    private static SettingsRuntimeCapabilitySnapshot CreateRuntimeCapabilities(
        ReplayFoundryRuntimeEnvironment runtime) =>
        new(
            runtime.IsBaseReady,
            runtime.IsBalancedReady,
            runtime.IsThoroughReady,
            runtime.Capabilities.Any(capability =>
                capability.IsAvailable &&
                capability.Name != "Deterministic media analysis"),
            runtime.PackageStoreRoot,
            runtime.Capabilities.Select(CreateCapability));

    private static SettingsCapabilityItem CreateCapability(
        ReplayFoundryRuntimeCapabilityStatus capability)
    {
        (string name, string detail) = capability.Name switch
        {
            "Deterministic media analysis" =>
                ("Core video analysis", "Finds visual and audio changes locally."),
            "Speech activity" =>
                ("Speech detection", "Locates speech-like sections for Balanced and Thorough analysis."),
            "Local transcription runtime" =>
                ("Local transcription", "Creates subtitles and speech context on this PC."),
            "Multilingual transcription model" =>
                ("Multilingual speech model", "Supports local transcription across multiple languages."),
            "Qwen visual runtime" =>
                ("Picture tool", "Checks promising moments more closely on compatible graphics hardware."),
            "Qwen3-VL 4B model" =>
                ("Picture understanding", "Adds more detail during Thorough clip finding."),
            _ => (capability.Name, "Installed local tool."),
        };
        string status = capability.IsAvailable
            ? "Ready"
            : capability.Status.StartsWith("Installed", StringComparison.Ordinal)
                ? "Needs attention"
                : "Not installed";
        return new SettingsCapabilityItem(
            name,
            status,
            capability.Storage,
            capability.License,
            detail);
    }
}
