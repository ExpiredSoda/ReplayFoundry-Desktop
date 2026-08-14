using System.IO;
using System.Reflection;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.Desktop.Features.Settings;

public enum SettingsSection
{
    Storage,
    CreatorVoice,
    AiModels,
    PrivacyDiagnostics,
    About,
}

public sealed record SettingsSectionItem(
    SettingsSection Key,
    string Label,
    string Glyph,
    string Description);

public sealed record SettingsCapabilityItem(
    string Capability,
    string Status,
    string Storage,
    string License,
    string? Detail = null);

public sealed class SettingsViewModel :
    ObservableObject,
    IWorkspaceChromeSource,
    IDisposable
{
    private readonly YouTubeConnectionPermissionState _youtubePermission;
    private readonly ResearchParticipationState _researchParticipation;
    private readonly IResearchFeedbackStore? _researchFeedbackStore;
    private readonly SettingsRuntimeCapabilitySnapshot? _runtimeCapabilities;
    private readonly IRuntimePackMaintenanceActions? _runtimeMaintenance;
    private readonly GenerationOutputLocationState _outputLocation;
    private readonly IOutputFolderPicker? _outputFolderPicker;
    private readonly ILocalFolderLauncher? _folderLauncher;
    private readonly EditorialRerollPreferenceState
        _editorialRerollPreference;
    private readonly EditorialMetadataPreferenceLearningConsentState
        _editorialMetadataPreferenceLearningConsent;
    private readonly WorkspaceSurfaceState _surfaceState;
    private readonly DelegateCommand _enableYouTubeCommand;
    private readonly DelegateCommand _disableYouTubeCommand;
    private readonly DelegateCommand _enableResearchCommand;
    private readonly DelegateCommand _disableResearchCommand;
    private readonly DelegateCommand _deleteResearchFeedbackCommand;
    private readonly DelegateCommand
        _enableEditorialMetadataPreferenceLearningCommand;
    private readonly DelegateCommand
        _disableEditorialMetadataPreferenceLearningCommand;
    private readonly DelegateCommand _addAdvancedAiCommand;
    private readonly DelegateCommand _repairRuntimePacksCommand;
    private readonly DelegateCommand _removeAdvancedAiCommand;
    private readonly DelegateCommand _openRuntimePackFolderCommand;
    private readonly DelegateCommand _chooseOutputFolderCommand;
    private readonly DelegateCommand _useDefaultOutputFolderCommand;
    private readonly DelegateCommand _openOutputFolderCommand;
    private SettingsSection _selectedSection = SettingsSection.Storage;
    private string _onlineNotice = string.Empty;
    private string _storageNotice = string.Empty;
    private string _runtimeNotice = string.Empty;
    private string _editorialRerollPreferenceNotice = string.Empty;
    private string _editorialMetadataPreferenceLearningNotice = string.Empty;
    private bool _isDisposed;

    public SettingsViewModel()
        : this(
            new YouTubeConnectionPermissionState(
                new InMemoryYouTubeConnectionPermissionStore()),
            WorkspaceSurfaceState.Unavailable,
            runtimeCapabilities: null,
            runtimeMaintenance: null,
            researchParticipation: null,
            researchFeedbackStore: null,
            outputLocation: null,
            outputFolderPicker: null,
            folderLauncher: null)
    {
    }

    public SettingsViewModel(
        YouTubeConnectionPermissionState youtubeConnectionPermission)
        : this(
            youtubeConnectionPermission,
            WorkspaceSurfaceState.Unavailable,
            runtimeCapabilities: null,
            runtimeMaintenance: null,
            researchParticipation: null,
            researchFeedbackStore: null,
            outputLocation: null,
            outputFolderPicker: null,
            folderLauncher: null)
    {
    }

    public SettingsViewModel(
        YouTubeConnectionPermissionState youtubeConnectionPermission,
        SettingsRuntimeCapabilitySnapshot runtimeCapabilities,
        IRuntimePackMaintenanceActions runtimeMaintenance)
        : this(
            youtubeConnectionPermission,
            WorkspaceSurfaceState.ContentReady,
            runtimeCapabilities,
            runtimeMaintenance,
            researchParticipation: null,
            researchFeedbackStore: null,
            outputLocation: null,
            outputFolderPicker: null,
            folderLauncher: null)
    {
    }

    public SettingsViewModel(
        YouTubeConnectionPermissionState youtubeConnectionPermission,
        SettingsRuntimeCapabilitySnapshot runtimeCapabilities,
        IRuntimePackMaintenanceActions runtimeMaintenance,
        ResearchParticipationState researchParticipation,
        IResearchFeedbackStore researchFeedbackStore)
        : this(
            youtubeConnectionPermission,
            WorkspaceSurfaceState.ContentReady,
            runtimeCapabilities,
            runtimeMaintenance,
            researchParticipation,
            researchFeedbackStore,
            outputLocation: null,
            outputFolderPicker: null,
            folderLauncher: null)
    {
    }

    public SettingsViewModel(
        YouTubeConnectionPermissionState youtubeConnectionPermission,
        SettingsRuntimeCapabilitySnapshot runtimeCapabilities,
        IRuntimePackMaintenanceActions runtimeMaintenance,
        ResearchParticipationState researchParticipation,
        IResearchFeedbackStore researchFeedbackStore,
        GenerationOutputLocationState outputLocation,
        IOutputFolderPicker outputFolderPicker,
        ILocalFolderLauncher folderLauncher,
        ICreatorVoiceSettingsEditor? editorialProfile = null,
        BugReportSettingsViewModel? bugReports = null,
        LocalDataSettingsViewModel? localData = null,
        EditorialRerollPreferenceState? editorialRerollPreference = null,
        EditorialMetadataPreferenceLearningConsentState?
            editorialMetadataPreferenceLearningConsent = null)
        : this(
            youtubeConnectionPermission,
            WorkspaceSurfaceState.ContentReady,
            runtimeCapabilities,
            runtimeMaintenance,
            researchParticipation,
            researchFeedbackStore,
            outputLocation,
            outputFolderPicker,
            folderLauncher,
            editorialProfile,
            bugReports,
            localData,
            editorialRerollPreference,
            editorialMetadataPreferenceLearningConsent)
    {
    }

    private SettingsViewModel(
        YouTubeConnectionPermissionState youtubeConnectionPermission,
        WorkspaceSurfaceState surfaceState,
        SettingsRuntimeCapabilitySnapshot? runtimeCapabilities,
        IRuntimePackMaintenanceActions? runtimeMaintenance,
        ResearchParticipationState? researchParticipation,
        IResearchFeedbackStore? researchFeedbackStore,
        GenerationOutputLocationState? outputLocation,
        IOutputFolderPicker? outputFolderPicker,
        ILocalFolderLauncher? folderLauncher,
        ICreatorVoiceSettingsEditor? editorialProfile = null,
        BugReportSettingsViewModel? bugReports = null,
        LocalDataSettingsViewModel? localData = null,
        EditorialRerollPreferenceState? editorialRerollPreference = null,
        EditorialMetadataPreferenceLearningConsentState?
            editorialMetadataPreferenceLearningConsent = null)
    {
        _youtubePermission = youtubeConnectionPermission ??
            throw new ArgumentNullException(nameof(youtubeConnectionPermission));
        _surfaceState = surfaceState;
        _runtimeCapabilities = runtimeCapabilities;
        _runtimeMaintenance = runtimeMaintenance;
        _researchParticipation = researchParticipation ??
            new ResearchParticipationState(
                new InMemoryResearchParticipationStore());
        _researchFeedbackStore = researchFeedbackStore;
        _outputLocation = outputLocation ??
            new GenerationOutputLocationState(
                new InMemoryGenerationOutputLocationStore());
        _outputFolderPicker = outputFolderPicker;
        _folderLauncher = folderLauncher;
        _editorialRerollPreference = editorialRerollPreference ??
            new EditorialRerollPreferenceState(
                new InMemoryEditorialRerollPreferenceStore());
        _editorialMetadataPreferenceLearningConsent =
            editorialMetadataPreferenceLearningConsent ??
            new EditorialMetadataPreferenceLearningConsentState(
                new InMemoryEditorialMetadataPreferenceLearningConsentStore());
        CreatorVoice = new CreatorVoiceSettingsViewModel(
            editorialProfile ?? new ClipEditorialProfileSession());
        BugReports = bugReports ?? new BugReportSettingsViewModel();
        LocalData = localData ?? new LocalDataSettingsViewModel();

        Sections = Array.AsReadOnly(new[]
        {
            new SettingsSectionItem(
                SettingsSection.Storage,
                "Files & storage",
                "Icon.Folder",
                "Where finished videos are saved"),
            new SettingsSectionItem(
                SettingsSection.CreatorVoice,
                "Creator voice",
                "Icon.Edit",
                "Default wording for titles and descriptions"),
            new SettingsSectionItem(
                SettingsSection.AiModels,
                "Local tools & AI",
                "Icon.Spark",
                "What is installed on this PC"),
            new SettingsSectionItem(
                SettingsSection.PrivacyDiagnostics,
                "Privacy & connections",
                "Icon.Lock",
                "YouTube and optional research sharing"),
            new SettingsSectionItem(
                SettingsSection.About,
                "About Replay Foundry",
                "Icon.Info",
                "Version and local-first promise"),
        });
        AiCapabilities = Array.AsReadOnly(
            runtimeCapabilities?.Capabilities.ToArray() ?? []);

        SelectSectionCommand = new DelegateCommand<SettingsSectionItem>(
            selected => SelectedSection = selected.Key);
        _enableYouTubeCommand = new DelegateCommand(
            EnableYouTubeConnections,
            () => !IsYouTubeConnectionEnabled);
        _disableYouTubeCommand = new DelegateCommand(
            DisableYouTubeConnections,
            () => IsYouTubeConnectionEnabled);
        _enableResearchCommand = new DelegateCommand(
            EnableResearchParticipation,
            () => !IsResearchParticipationEnabled);
        _disableResearchCommand = new DelegateCommand(
            DisableResearchParticipation,
            () => IsResearchParticipationEnabled);
        _deleteResearchFeedbackCommand = new DelegateCommand(
            DeleteResearchFeedback,
            () => ResearchFeedbackCount > 0);
        _enableEditorialMetadataPreferenceLearningCommand =
            new DelegateCommand(
                EnableEditorialMetadataPreferenceLearning,
                () => !IsEditorialMetadataPreferenceLearningEnabled);
        _disableEditorialMetadataPreferenceLearningCommand =
            new DelegateCommand(
                DisableEditorialMetadataPreferenceLearning,
                () => IsEditorialMetadataPreferenceLearningEnabled);
        _addAdvancedAiCommand = new DelegateCommand(
            AddAdvancedAi,
            () => _runtimeMaintenance?.CanAddAdvanced == true);
        _repairRuntimePacksCommand = new DelegateCommand(
            RepairRuntimePacks,
            () => _runtimeMaintenance?.CanRepair == true);
        _removeAdvancedAiCommand = new DelegateCommand(
            RemoveAdvancedAi,
            () => _runtimeMaintenance?.CanRemoveAdvanced == true &&
                  _runtimeCapabilities?.HasAdvancedCapability == true);
        _openRuntimePackFolderCommand = new DelegateCommand(
            () => _runtimeMaintenance?.OpenPackageFolder(),
            () => _runtimeMaintenance is not null);
        _chooseOutputFolderCommand = new DelegateCommand(
            ChooseOutputFolder,
            () => _outputFolderPicker is not null);
        _useDefaultOutputFolderCommand = new DelegateCommand(
            UseDefaultOutputFolder,
            () => _outputLocation.UsesCustomRoot);
        _openOutputFolderCommand = new DelegateCommand(
            OpenOutputFolder,
            () => _folderLauncher is not null);

        _youtubePermission.Changed += YouTubePermission_Changed;
        _researchParticipation.Changed += ResearchParticipation_Changed;
        _outputLocation.Changed += OutputLocation_Changed;
        _editorialRerollPreference.Changed +=
            EditorialRerollPreference_Changed;
        _editorialMetadataPreferenceLearningConsent.Changed +=
            EditorialMetadataPreferenceLearningConsent_Changed;
        LocalData.ResetScheduled += LocalData_ResetScheduled;
    }

    internal SettingsViewModel(WorkspaceSurfaceState surfaceState)
        : this(
            new YouTubeConnectionPermissionState(
                new InMemoryYouTubeConnectionPermissionStore()),
            surfaceState,
            runtimeCapabilities: null,
            runtimeMaintenance: null,
            researchParticipation: null,
            researchFeedbackStore: null,
            outputLocation: null,
            outputFolderPicker: null,
            folderLauncher: null)
    {
    }

    internal SettingsViewModel(
        GenerationOutputLocationState outputLocation,
        IOutputFolderPicker outputFolderPicker,
        ILocalFolderLauncher folderLauncher)
        : this(
            new YouTubeConnectionPermissionState(
                new InMemoryYouTubeConnectionPermissionStore()),
            WorkspaceSurfaceState.ContentReady,
            runtimeCapabilities: null,
            runtimeMaintenance: null,
            researchParticipation: null,
            researchFeedbackStore: null,
            outputLocation,
            outputFolderPicker,
            folderLauncher)
    {
    }

    internal SettingsViewModel(
        ICreatorVoiceSettingsEditor editorialProfile)
        : this(
            new YouTubeConnectionPermissionState(
                new InMemoryYouTubeConnectionPermissionStore()),
            WorkspaceSurfaceState.ContentReady,
            runtimeCapabilities: null,
            runtimeMaintenance: null,
            researchParticipation: null,
            researchFeedbackStore: null,
            outputLocation: null,
            outputFolderPicker: null,
            folderLauncher: null,
            editorialProfile ??
                throw new ArgumentNullException(nameof(editorialProfile)))
    {
    }

    internal SettingsViewModel(
        EditorialMetadataPreferenceLearningConsentState
            editorialMetadataPreferenceLearningConsent)
        : this(
            new YouTubeConnectionPermissionState(
                new InMemoryYouTubeConnectionPermissionStore()),
            WorkspaceSurfaceState.ContentReady,
            runtimeCapabilities: null,
            runtimeMaintenance: null,
            researchParticipation: null,
            researchFeedbackStore: null,
            outputLocation: null,
            outputFolderPicker: null,
            folderLauncher: null,
            editorialMetadataPreferenceLearningConsent:
                editorialMetadataPreferenceLearningConsent ??
                throw new ArgumentNullException(
                    nameof(editorialMetadataPreferenceLearningConsent)))
    {
    }

    public IReadOnlyList<SettingsSectionItem> Sections { get; }
    public IReadOnlyList<SettingsCapabilityItem> AiCapabilities { get; }
    public CreatorVoiceSettingsViewModel CreatorVoice { get; }
    public BugReportSettingsViewModel BugReports { get; }
    public LocalDataSettingsViewModel LocalData { get; }
    public WorkspaceSurfaceState SurfaceState => _surfaceState;
    public bool IsEmpty => SurfaceState == WorkspaceSurfaceState.Empty;
    public bool IsContentReady => SurfaceState == WorkspaceSurfaceState.ContentReady;
    public bool IsLoading => SurfaceState == WorkspaceSurfaceState.Loading;
    public bool IsError => SurfaceState == WorkspaceSurfaceState.Error;
    public bool IsUnavailable => SurfaceState == WorkspaceSurfaceState.Unavailable;

    public SettingsSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (!Enum.IsDefined(value) || _selectedSection == value) return;
            _selectedSection = value;
            if (value == SettingsSection.CreatorVoice)
            {
                CreatorVoice.Reload();
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSectionItem));
            OnPropertyChanged(nameof(SelectedSectionLabel));
            OnPropertyChanged(nameof(PersistenceBannerText));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public SettingsSectionItem SelectedSectionItem
    {
        get => GetSection(SelectedSection);
        set
        {
            if (value is not null) SelectedSection = value.Key;
        }
    }

    public string SelectedSectionLabel => GetSection(SelectedSection).Label;
    public string PersistenceBannerText =>
        SelectedSection == SettingsSection.CreatorVoice
            ? "Creator voice defaults last for this app session."
            : SelectedSection == SettingsSection.AiModels &&
              !_editorialRerollPreference.IsPersistent
                ? "The reroll-provider choice can be kept only for this app session in this preview."
            : _youtubePermission.IsPersistent && _outputLocation.IsPersistent
            ? "Changes on this screen take effect immediately and are saved on this PC."
            : "This preview cannot save one or more choices permanently; available choices still take effect for this session.";
    public string WorkspaceEyebrow => "SETTINGS / LOCAL";
    public string WorkspaceTitle => "Simple, local controls";
    public string WorkspaceDescription =>
        "Choose where videos go, set creator wording, manage local tools, and decide when Replay Foundry may connect online.";
    public string StatusText =>
        SelectedSection == SettingsSection.CreatorVoice ||
        SelectedSection == SettingsSection.AiModels &&
        !_editorialRerollPreference.IsPersistent
            ? "Session only"
            : _youtubePermission.IsPersistent && _outputLocation.IsPersistent
            ? "Saved on this PC"
            : "Session only";
    public string ErrorSummary => "Settings could not load.";

    public string StorageStatus => _outputLocation.IsPersistent
        ? "Ready · future Studio renders use this saved folder"
        : "Available for this session only";
    public string OutputRootDirectory => _outputLocation.OutputRootDirectory;
    public string OutputRootModeText => _outputLocation.UsesCustomRoot
        ? "Your chosen folder"
        : "Windows Videos default";
    public bool UsesCustomOutputRoot => _outputLocation.UsesCustomRoot;
    public string StorageNotice => _storageNotice;
    public bool HasStorageNotice => !string.IsNullOrWhiteSpace(StorageNotice);

    public string ToolchainStatus => _runtimeCapabilities is null
        ? "Installation status is unavailable in this preview."
        : _runtimeCapabilities.IsBaseReady
            ? "Core video tools are ready."
            : "Core video tools need repair before Replay Foundry can process videos.";
    public string AiStatus => _runtimeCapabilities is null
        ? "Installed AI status is unavailable in this preview."
        : _runtimeCapabilities.IsThoroughReady
            ? "Advanced local analysis is installed."
            : _runtimeCapabilities.IsBalancedReady
                ? "Speech-assisted analysis is installed; visual review is not installed."
                : "Clip discovery works with local video signals. Add Advanced AI for speech and visual review.";
    public string RuntimeProfileStatus => _runtimeCapabilities is null
        ? "Status unavailable"
        : _runtimeCapabilities.IsThoroughReady
            ? "Advanced installed"
            : _runtimeCapabilities.IsBaseReady
                ? "Base installed"
                : "Repair needed";
    public string RuntimePackStoreText => _runtimeCapabilities?.PackageStoreRoot ??
        "Local tools folder unavailable";
    public string AdvancedAiActionLabel =>
        _runtimeCapabilities?.HasAdvancedCapability == true
            ? "Update Advanced AI"
            : "Add Advanced AI";
    public string RuntimePackNotice => _runtimeNotice;
    public bool HasRuntimePackNotice => !string.IsNullOrWhiteSpace(RuntimePackNotice);

    public bool UseLocalAiForEditorialRerolls
    {
        get => _editorialRerollPreference.UseLocalAi;
        set
        {
            if (value == UseLocalAiForEditorialRerolls)
            {
                return;
            }

            try
            {
                _editorialRerollPreference.SetUseLocalAi(value);
                string lifetime = _editorialRerollPreference.IsPersistent
                    ? "Saved."
                    : "Applied for this app session.";
                _editorialRerollPreferenceNotice = value
                    ? lifetime +
                      " Studio and Publish rerolls now require qualified local AI."
                    : lifetime +
                      " Studio and Publish rerolls now use deterministic grounded wording.";
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                _editorialRerollPreferenceNotice =
                    "Replay Foundry could not save the reroll choice: " +
                    exception.Message;
            }

            NotifyEditorialRerollPreference();
        }
    }

    public string EditorialRerollPreferenceDetail =>
        UseLocalAiForEditorialRerolls
            ? "Reroll reopens the saved grounded scene context and asks " +
              "Qwen for a genuinely different title and description. " +
              "Expect a longer wait and significant GPU memory use. If " +
              "Qwen is unavailable or fails, that reroll stops and reports " +
              "the problem. This does not change the original Generate scan."
            : "Reroll uses the fast deterministic grounded rewriter. It " +
              "keeps the saved scene context and avoids loading Qwen, but " +
              "its alternate wording and structure are more limited. This " +
              "does not change the original Generate scan.";

    public string EditorialRerollPreferencePersistence =>
        _editorialRerollPreference.IsPersistent
            ? "This choice is saved on this PC."
            : "This preview can keep the choice only for this app session.";

    public string EditorialRerollPreferenceNotice =>
        _editorialRerollPreferenceNotice;

    public bool HasEditorialRerollPreferenceNotice =>
        !string.IsNullOrWhiteSpace(EditorialRerollPreferenceNotice);

    public bool IsEditorialMetadataPreferenceLearningEnabled =>
        _editorialMetadataPreferenceLearningConsent.IsEnabled;

    public string EditorialMetadataPreferenceLearningStatus =>
        IsEditorialMetadataPreferenceLearningEnabled
            ? "Local style learning on"
            : "Local style learning off";

    public string EditorialMetadataPreferenceLearningDetail =>
        IsEditorialMetadataPreferenceLearningEnabled
            ? "When you save changed title, description, or tags in " +
              "Studio, Replay Foundry keeps only local numeric structure " +
              "such as lengths, capitalization, punctuation, line count, " +
              "and tag count."
            : "Local learning is off. If you turn it on, Studio Save " +
              "corrections contribute only numeric structure such as " +
              "lengths, capitalization, punctuation, line count, and tag " +
              "count. Any profile already on this PC remains until you " +
              "choose Reset saved app data in Files & storage.";

    public string EditorialMetadataPreferenceLearningPrivacy =>
        "Nothing is uploaded. The profile never retains words, n-grams, " +
        "embeddings, game names, transcripts, file paths, channel IDs, " +
        "or model text. This version does not use the profile to change " +
        "generation or ranking.";

    public string EditorialMetadataPreferenceLearningEnabledAtText =>
        _editorialMetadataPreferenceLearningConsent.EnabledAtUtc is
        { } enabledAt
                ? $"Enabled on this PC {enabledAt.ToLocalTime():g}."
                : "No local style-learning consent is saved.";

    public string EditorialMetadataPreferenceLearningPersistence =>
        _editorialMetadataPreferenceLearningConsent.IsPersistent
            ? "This choice is saved on this PC."
            : "This preview can keep the choice only for this app session.";

    public string EditorialMetadataPreferenceLearningNotice =>
        _editorialMetadataPreferenceLearningNotice;

    public bool HasEditorialMetadataPreferenceLearningNotice =>
        !string.IsNullOrWhiteSpace(
            EditorialMetadataPreferenceLearningNotice);

    public bool IsYouTubeConnectionEnabled => _youtubePermission.IsEnabled;
    public string YouTubeConnectionPermissionStatus =>
        IsYouTubeConnectionEnabled ? "YouTube access allowed" : "YouTube access off";
    public string YouTubeConnectionPermissionDetail =>
        IsYouTubeConnectionEnabled
            ? "Replay Foundry may contact Google only after you choose Connect, check status, or publish."
            : "Replay Foundry will not contact Google or upload to YouTube.";
    public string YouTubeConnectionEnabledAtText =>
        _youtubePermission.EnabledAtUtc is { } enabledAt
            ? $"Allowed on this PC {enabledAt.ToLocalTime():g}."
            : "No YouTube permission is saved.";
    public bool IsResearchParticipationEnabled => _researchParticipation.IsEnabled;
    public int ResearchFeedbackCount => _researchFeedbackStore?.Current.Count ?? 0;
    public string ResearchParticipationStatus =>
        IsResearchParticipationEnabled ? "Optional research sharing on" : "Optional research sharing off";
    public string ResearchParticipationDetail =>
        IsResearchParticipationEnabled
            ? "Replay Foundry may prepare anonymous numeric records about suggestions " +
              "you kept or skipped. Video, audio, transcripts, titles, game names, " +
              "account data, and file paths are excluded."
            : "Local preference learning still works. Nothing is prepared for developer research.";
    public string ResearchDeliveryStatus =>
        $"{ResearchFeedbackCount} anonymous record{(ResearchFeedbackCount == 1 ? string.Empty : "s")} stored locally. This build has no upload service, so nothing is sent.";
    public string OnlineConnectionNotice => _onlineNotice;
    public bool HasOnlineConnectionNotice => !string.IsNullOrWhiteSpace(OnlineConnectionNotice);
    public string PrivacySummary =>
        "Finding clips, editing, rendering, Library records, and local " +
        "preferences stay on this PC. Replay Foundry has no advertising " +
        "or automatic telemetry. Only a report you review and explicitly " +
        "send may use the separately configured support connection.";
    public string YouTubeDataSummary =>
        "Only actions you start in Publish contact Google. A publish action sends the selected video and the YouTube details you reviewed.";
    public string YouTubeStorageSummary =>
        "Replay Foundry never sees your Google password. Windows Credential Manager protects the connection, and local publish history remembers what was uploaded.";
    public string DiagnosticsStatus => IsYouTubeConnectionEnabled
        ? "YouTube allowed · no automatic telemetry · reviewed reports only"
        : "Local by default · no automatic telemetry · reviewed reports only";
    public string VersionText =>
        $"Replay Foundry Desktop · {GetDisplayVersion()}";

    private static string GetDisplayVersion()
    {
        string? informationalVersion = typeof(SettingsViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            int buildMetadata = informationalVersion.IndexOf(
                '+',
                StringComparison.Ordinal);
            return buildMetadata > 0
                ? informationalVersion[..buildMetadata]
                : informationalVersion;
        }

        return typeof(SettingsViewModel).Assembly.GetName().Version?
            .ToString() ?? "development build";
    }

    public ICommand SelectSectionCommand { get; }
    public ICommand EnableYouTubeConnectionsCommand => _enableYouTubeCommand;
    public ICommand DisableYouTubeConnectionsCommand => _disableYouTubeCommand;
    public ICommand EnableResearchParticipationCommand => _enableResearchCommand;
    public ICommand DisableResearchParticipationCommand => _disableResearchCommand;
    public ICommand DeleteResearchFeedbackCommand => _deleteResearchFeedbackCommand;
    public ICommand EnableEditorialMetadataPreferenceLearningCommand =>
        _enableEditorialMetadataPreferenceLearningCommand;
    public ICommand DisableEditorialMetadataPreferenceLearningCommand =>
        _disableEditorialMetadataPreferenceLearningCommand;
    public ICommand AddAdvancedAiCommand => _addAdvancedAiCommand;
    public ICommand RepairRuntimePacksCommand => _repairRuntimePacksCommand;
    public ICommand RemoveAdvancedAiCommand => _removeAdvancedAiCommand;
    public ICommand OpenRuntimePackFolderCommand => _openRuntimePackFolderCommand;
    public ICommand ChooseOutputFolderCommand => _chooseOutputFolderCommand;
    public ICommand UseDefaultOutputFolderCommand => _useDefaultOutputFolderCommand;
    public ICommand OpenOutputFolderCommand => _openOutputFolderCommand;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _youtubePermission.Changed -= YouTubePermission_Changed;
        _researchParticipation.Changed -= ResearchParticipation_Changed;
        _outputLocation.Changed -= OutputLocation_Changed;
        _editorialRerollPreference.Changed -=
            EditorialRerollPreference_Changed;
        _editorialMetadataPreferenceLearningConsent.Changed -=
            EditorialMetadataPreferenceLearningConsent_Changed;
        LocalData.ResetScheduled -= LocalData_ResetScheduled;
        BugReports.Dispose();
    }

    private void ChooseOutputFolder()
    {
        if (_outputFolderPicker is null) return;
        try
        {
            string? selected = _outputFolderPicker.PickOutputFolder(
                _outputLocation.OutputRootDirectory);
            if (selected is null) return;
            _outputLocation.SetCustomRoot(selected);
            SetStorageNotice("Future projects will render here. Existing Library videos were not moved.");
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            SetStorageNotice("Replay Foundry could not use that folder: " + exception.Message);
        }
    }

    private void UseDefaultOutputFolder()
    {
        try
        {
            _outputLocation.UseDefaultRoot();
            SetStorageNotice("Future projects will use the ReplayFoundry folder under Windows Videos.");
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            SetStorageNotice("Replay Foundry could not restore the default folder: " + exception.Message);
        }
    }

    private void OpenOutputFolder()
    {
        if (_folderLauncher is null) return;
        try
        {
            _outputLocation.EnsureCurrentRootIsWritable();
            _folderLauncher.OpenFolder(_outputLocation.OutputRootDirectory);
            SetStorageNotice("Opened the current output folder.");
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            SetStorageNotice("Replay Foundry could not open that folder: " + exception.Message);
        }
    }

    private void EnableYouTubeConnections()
    {
        try
        {
            _youtubePermission.Enable(DateTimeOffset.UtcNow);
            SetOnlineNotice("YouTube access is allowed. Nothing was connected or sent; choose Connect in Publish when ready.");
        }
        catch (Exception exception) when (IsPersistentStateException(exception))
        {
            SetOnlineNotice("Replay Foundry could not save this choice: " + exception.Message);
        }
    }

    private void DisableYouTubeConnections()
    {
        try
        {
            _youtubePermission.Disable();
            SetOnlineNotice("YouTube access is off. Any active upload is cancelled and the local connection is being removed.");
        }
        catch (Exception exception) when (IsPersistentStateException(exception))
        {
            SetOnlineNotice("Replay Foundry could not save local-only mode: " + exception.Message);
        }
    }

    private void EnableResearchParticipation()
    {
        _researchParticipation.Enable(DateTimeOffset.UtcNow);
        SetOnlineNotice("Optional research sharing is on. Nothing can upload because this build has no research upload service.");
    }

    private void DisableResearchParticipation()
    {
        _researchParticipation.Disable();
        SetOnlineNotice("Optional research sharing is off. Existing local records remain until you delete them.");
    }

    private void DeleteResearchFeedback()
    {
        _researchFeedbackStore?.Clear();
        OnPropertyChanged(nameof(ResearchFeedbackCount));
        OnPropertyChanged(nameof(ResearchDeliveryStatus));
        _deleteResearchFeedbackCommand.RaiseCanExecuteChanged();
        SetOnlineNotice("Local research records were deleted.");
    }

    private void EnableEditorialMetadataPreferenceLearning()
    {
        try
        {
            _editorialMetadataPreferenceLearningConsent.Enable(
                DateTimeOffset.UtcNow);
            _editorialMetadataPreferenceLearningNotice =
                "Local structural style learning is on. Nothing was " +
                "uploaded, and saved wording is not retained in the profile.";
        }
        catch (Exception exception) when (
            IsPersistentStateException(exception))
        {
            _editorialMetadataPreferenceLearningNotice =
                "Replay Foundry could not save the local learning choice: " +
                exception.Message;
        }
        NotifyEditorialMetadataPreferenceLearning();
    }

    private void DisableEditorialMetadataPreferenceLearning()
    {
        try
        {
            _editorialMetadataPreferenceLearningConsent.Disable();
            _editorialMetadataPreferenceLearningNotice =
                "Local structural style learning is off. No new correction " +
                "will be recorded. To remove an existing numeric profile, " +
                "choose Reset saved app data in Files & storage.";
        }
        catch (Exception exception) when (
            IsPersistentStateException(exception))
        {
            _editorialMetadataPreferenceLearningNotice =
                "Replay Foundry could not turn off local learning: " +
                exception.Message;
        }
        NotifyEditorialMetadataPreferenceLearning();
    }

    private void AddAdvancedAi() => RunRuntimeMaintenance(
        () => _runtimeMaintenance!.AddAdvanced(),
        "The Advanced AI installer opened. Restart Replay Foundry after it finishes.");

    private void RepairRuntimePacks() => RunRuntimeMaintenance(
        () => _runtimeMaintenance!.Repair(),
        "The repair tool opened. Restart Replay Foundry after it finishes.");

    private void RemoveAdvancedAi() => RunRuntimeMaintenance(
        () => _runtimeMaintenance!.RemoveAdvanced(),
        "Advanced AI removal opened. Core clip discovery and your videos are kept.");

    private void RunRuntimeMaintenance(Action action, string success)
    {
        try
        {
            action();
            _runtimeNotice = success;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _runtimeNotice = "The maintenance tool could not start: " + exception.Message;
        }
        OnPropertyChanged(nameof(RuntimePackNotice));
        OnPropertyChanged(nameof(HasRuntimePackNotice));
    }

    private void YouTubePermission_Changed(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsYouTubeConnectionEnabled));
        OnPropertyChanged(nameof(YouTubeConnectionPermissionStatus));
        OnPropertyChanged(nameof(YouTubeConnectionPermissionDetail));
        OnPropertyChanged(nameof(YouTubeConnectionEnabledAtText));
        OnPropertyChanged(nameof(DiagnosticsStatus));
        _enableYouTubeCommand.RaiseCanExecuteChanged();
        _disableYouTubeCommand.RaiseCanExecuteChanged();
    }

    private void ResearchParticipation_Changed(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsResearchParticipationEnabled));
        OnPropertyChanged(nameof(ResearchParticipationStatus));
        OnPropertyChanged(nameof(ResearchParticipationDetail));
        OnPropertyChanged(nameof(ResearchFeedbackCount));
        OnPropertyChanged(nameof(ResearchDeliveryStatus));
        _enableResearchCommand.RaiseCanExecuteChanged();
        _disableResearchCommand.RaiseCanExecuteChanged();
        _deleteResearchFeedbackCommand.RaiseCanExecuteChanged();
    }

    private void LocalData_ResetScheduled(
        object? sender,
        ReplayFoundryLocalDataResetRequest request)
    {
        if (!request.Includes(
                ReplayFoundryLocalDataKind.PreferencesAndHistory))
        {
            return;
        }
        if (_youtubePermission.IsEnabled) _youtubePermission.Disable();
        if (_researchParticipation.IsEnabled) _researchParticipation.Disable();
        if (_editorialMetadataPreferenceLearningConsent.IsEnabled)
        {
            _editorialMetadataPreferenceLearningConsent.Disable();
        }
        BugReports.DisableForLocalReset();
        SetOnlineNotice(
            "Online permissions were turned off now. Publish is removing the local YouTube connection; the remaining selected data resets on the next start.");
    }

    private void OutputLocation_Changed(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(StorageStatus));
        OnPropertyChanged(nameof(OutputRootDirectory));
        OnPropertyChanged(nameof(OutputRootModeText));
        OnPropertyChanged(nameof(UsesCustomOutputRoot));
        OnPropertyChanged(nameof(PersistenceBannerText));
        OnPropertyChanged(nameof(StatusText));
        _useDefaultOutputFolderCommand.RaiseCanExecuteChanged();
    }

    private void EditorialRerollPreference_Changed(
        object? sender,
        EventArgs args) => NotifyEditorialRerollPreference();

    private void NotifyEditorialRerollPreference()
    {
        OnPropertyChanged(nameof(UseLocalAiForEditorialRerolls));
        OnPropertyChanged(nameof(EditorialRerollPreferenceDetail));
        OnPropertyChanged(nameof(EditorialRerollPreferencePersistence));
        OnPropertyChanged(nameof(EditorialRerollPreferenceNotice));
        OnPropertyChanged(nameof(HasEditorialRerollPreferenceNotice));
    }

    private void EditorialMetadataPreferenceLearningConsent_Changed(
        object? sender,
        EventArgs args) => NotifyEditorialMetadataPreferenceLearning();

    private void NotifyEditorialMetadataPreferenceLearning()
    {
        OnPropertyChanged(
            nameof(IsEditorialMetadataPreferenceLearningEnabled));
        OnPropertyChanged(
            nameof(EditorialMetadataPreferenceLearningStatus));
        OnPropertyChanged(
            nameof(EditorialMetadataPreferenceLearningDetail));
        OnPropertyChanged(
            nameof(EditorialMetadataPreferenceLearningPrivacy));
        OnPropertyChanged(
            nameof(EditorialMetadataPreferenceLearningEnabledAtText));
        OnPropertyChanged(
            nameof(EditorialMetadataPreferenceLearningPersistence));
        OnPropertyChanged(
            nameof(EditorialMetadataPreferenceLearningNotice));
        OnPropertyChanged(
            nameof(HasEditorialMetadataPreferenceLearningNotice));
        _enableEditorialMetadataPreferenceLearningCommand
            .RaiseCanExecuteChanged();
        _disableEditorialMetadataPreferenceLearningCommand
            .RaiseCanExecuteChanged();
    }

    private void SetStorageNotice(string value)
    {
        _storageNotice = value;
        OnPropertyChanged(nameof(StorageNotice));
        OnPropertyChanged(nameof(HasStorageNotice));
    }

    private void SetOnlineNotice(string value)
    {
        _onlineNotice = value;
        OnPropertyChanged(nameof(OnlineConnectionNotice));
        OnPropertyChanged(nameof(HasOnlineConnectionNotice));
    }

    private SettingsSectionItem GetSection(SettingsSection section) =>
        Sections.First(item => item.Key == section);

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or
        System.ComponentModel.Win32Exception;

    private static bool IsPersistentStateException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException;
}
