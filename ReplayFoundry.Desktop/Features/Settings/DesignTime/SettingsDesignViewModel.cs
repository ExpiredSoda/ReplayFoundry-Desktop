using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.Desktop.Features.Settings.DesignTime;

public sealed class SettingsDesignViewModel
{
    public IReadOnlyList<SettingsSectionItem> Sections { get; } = new[]
    {
        new SettingsSectionItem(SettingsSection.Storage, "Files & storage", "Icon.Folder", "Where finished videos are saved"),
        new SettingsSectionItem(SettingsSection.CreatorVoice, "Creator voice", "Icon.Edit", "Default wording for titles and descriptions"),
        new SettingsSectionItem(SettingsSection.AiModels, "Local tools & AI", "Icon.Spark", "What is installed on this PC"),
        new SettingsSectionItem(SettingsSection.PrivacyDiagnostics, "Privacy & connections", "Icon.Lock", "YouTube and optional research sharing"),
        new SettingsSectionItem(SettingsSection.About, "About Replay Foundry", "Icon.Info", "Version and local-first promise"),
    };
    public SettingsSection SelectedSection => SettingsSection.Storage;
    public SettingsSectionItem SelectedSectionItem => Sections[0];
    public CreatorVoiceSettingsViewModel CreatorVoice { get; } =
        new(new ClipEditorialProfileSession());
    public BugReportSettingsViewModel BugReports { get; } = new();
    public LocalDataSettingsViewModel LocalData { get; } = new();
    public string PersistenceBannerText => "Changes on this screen take effect immediately and are saved on this PC.";
    public WorkspaceSurfaceState SurfaceState => WorkspaceSurfaceState.ContentReady;
    public string StorageStatus => "Ready · future Studio renders use this saved folder";
    public string OutputRootDirectory => @"C:\Users\Creator\Videos\ReplayFoundry";
    public string OutputRootModeText => "Windows Videos default";
    public bool UsesCustomOutputRoot => false;
    public string StorageNotice => string.Empty;
    public bool HasStorageNotice => false;
    public string ToolchainStatus => "Core video tools are ready.";
    public string AiStatus => "Advanced local analysis is installed.";
    public string RuntimeProfileStatus => "Advanced installed";
    public string AdvancedAiActionLabel => "Update Advanced AI";
    public string RuntimePackStoreText => @"C:\Users\Creator\AppData\Local\ReplayFoundry\RuntimePacks";
    public string RuntimePackNotice => string.Empty;
    public bool HasRuntimePackNotice => false;
    public bool UseLocalAiForEditorialRerolls { get; set; }
    public string EditorialRerollPreferenceDetail =>
        UseLocalAiForEditorialRerolls
            ? "Each Studio or Publish reroll uses the qualified local " +
              "model for richer grounded wording. It can take longer and " +
              "use more PC resources. If it is unavailable, Replay " +
              "Foundry stops instead of substituting heuristics."
            : "Each Studio or Publish reroll uses the fast deterministic grounded generator.";
    public string EditorialRerollPreferencePersistence =>
        "This choice is saved on this PC.";
    public string EditorialRerollPreferenceNotice => string.Empty;
    public bool HasEditorialRerollPreferenceNotice => false;
    public bool IsEditorialMetadataPreferenceLearningEnabled => false;
    public string EditorialMetadataPreferenceLearningStatus =>
        "Local style learning off";
    public string EditorialMetadataPreferenceLearningDetail =>
        "Local learning is off. If enabled, Studio Save corrections contribute only numeric lengths, capitalization, punctuation, line count, and tag count.";
    public string EditorialMetadataPreferenceLearningPrivacy =>
        "Nothing is uploaded. Words, game names, transcripts, paths, channel IDs, and model text are not retained.";
    public string EditorialMetadataPreferenceLearningEnabledAtText =>
        "No local style-learning consent is saved.";
    public string EditorialMetadataPreferenceLearningPersistence =>
        "This choice is saved on this PC.";
    public string EditorialMetadataPreferenceLearningNotice => string.Empty;
    public bool HasEditorialMetadataPreferenceLearningNotice => false;
    public IReadOnlyList<SettingsCapabilityItem> AiCapabilities { get; } =
    [
        new("Speech detection", "Ready", "On this PC", "Bundled notice", "Locates speech-like sections."),
        new("Local transcription", "Ready", "On this PC", "Bundled notice", "Creates subtitles on this PC."),
        new("Visual review", "Ready", "On this PC", "Bundled notice", "Adds deeper visual context."),
    ];
    public bool IsYouTubeConnectionEnabled => false;
    public string YouTubeConnectionPermissionStatus => "YouTube access off";
    public string YouTubeConnectionPermissionDetail => "Replay Foundry will not contact Google or upload to YouTube.";
    public string YouTubeConnectionEnabledAtText => "No YouTube permission is saved.";
    public bool IsResearchParticipationEnabled => false;
    public string ResearchParticipationStatus => "Optional research sharing off";
    public string ResearchParticipationDetail => "Local preference learning still works.";
    public string ResearchDeliveryStatus => "0 anonymous records stored locally.";
    public string OnlineConnectionNotice => string.Empty;
    public bool HasOnlineConnectionNotice => false;
    public string PrivacySummary => "Finding clips, editing, and rendering stay on this PC.";
    public string YouTubeDataSummary => "Only actions you start in Publish contact Google.";
    public string YouTubeStorageSummary => "Windows Credential Manager protects the connection.";
    public string DiagnosticsStatus => "Local by default · no automatic telemetry · reviewed reports only";
    public string VersionText => "ReplayFoundry Desktop · development build";
    public ICommand? SelectSectionCommand => null;
    public ICommand? EnableYouTubeConnectionsCommand => null;
    public ICommand? DisableYouTubeConnectionsCommand => null;
    public ICommand? EnableResearchParticipationCommand => null;
    public ICommand? DisableResearchParticipationCommand => null;
    public ICommand? DeleteResearchFeedbackCommand => null;
    public ICommand? EnableEditorialMetadataPreferenceLearningCommand =>
        null;
    public ICommand? DisableEditorialMetadataPreferenceLearningCommand =>
        null;
    public ICommand? AddAdvancedAiCommand => null;
    public ICommand? RepairRuntimePacksCommand => null;
    public ICommand? RemoveAdvancedAiCommand => null;
    public ICommand? OpenRuntimePackFolderCommand => null;
    public ICommand? ChooseOutputFolderCommand => null;
    public ICommand? UseDefaultOutputFolderCommand => null;
    public ICommand? OpenOutputFolderCommand => null;
}
