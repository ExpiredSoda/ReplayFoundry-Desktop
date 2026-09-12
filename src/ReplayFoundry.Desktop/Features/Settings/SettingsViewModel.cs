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
    private readonly SettingsRuntimeCapabilitySnapshot? _runtimeCapabilities;
    private readonly GenerationOutputLocationState _outputLocation;
    private readonly EditorialRerollPreferenceState
        _editorialRerollPreference;
    private readonly EditorialMetadataPreferenceLearningConsentState
        _editorialMetadataPreferenceLearningConsent;
    private readonly WorkspaceSurfaceState _surfaceState;
    private readonly SettingsActionCoordinator _actions;
    private SettingsSection _selectedSection = SettingsSection.Storage;
    private string _onlineNotice = string.Empty;
    private string _storageNotice = string.Empty;
    private string _runtimeNotice = string.Empty;
    private string _editorialRerollPreferenceNotice = string.Empty;
    private string _editorialMetadataPreferenceLearningNotice = string.Empty;
    private bool _isDisposed;

    public ApplicationUpdateViewModel Updates { get; } = new();

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
        _researchParticipation = researchParticipation ??
            new ResearchParticipationState(
                new InMemoryResearchParticipationStore());
        _outputLocation = outputLocation ??
            new GenerationOutputLocationState(
                new InMemoryGenerationOutputLocationStore());
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
        _actions = new SettingsActionCoordinator(
            new SettingsActionServices(
                _youtubePermission,
                _researchParticipation,
                researchFeedbackStore,
                runtimeCapabilities,
                runtimeMaintenance,
                _outputLocation,
                outputFolderPicker,
                folderLauncher,
                _editorialMetadataPreferenceLearningConsent),
            new SettingsActionNotifications(
                SetStorageNotice,
                SetOnlineNotice,
                SetRuntimeNotice,
                SetEditorialMetadataPreferenceLearningNotice,
                NotifyResearchFeedback,
                NotifyEditorialMetadataPreferenceLearning));

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
                "Clip learning, writing and installed tools"),
            new SettingsSectionItem(
                SettingsSection.PrivacyDiagnostics,
                "Privacy & connections",
                "Icon.Lock",
                "YouTube and optional research sharing"),
            new SettingsSectionItem(
                SettingsSection.About,
                "About & updates",
                "Icon.Info",
                "Version and local-first promise"),
        });
        AiCapabilities = Array.AsReadOnly(
            runtimeCapabilities?.Capabilities.ToArray() ?? []);

        SelectSectionCommand = new DelegateCommand<SettingsSectionItem>(
            selected => SelectedSection = selected.Key);

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
    public SettingsSectionItem AiModelsSection => GetSection(SettingsSection.AiModels);
    public CreatorVoiceSettingsViewModel CreatorVoice { get; }
    public BugReportSettingsViewModel BugReports { get; }
    public LocalDataSettingsViewModel LocalData { get; }
    public Personalization.TasteLearningSettingsViewModel Learning { get; private set; } = new(null);
    internal void AttachTasteLearning(Media.Intelligence.Learning.ITasteLearningService? learning)
    {
        Learning.Dispose(); Learning = new(learning); OnPropertyChanged(nameof(Learning));
    }
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
                ? "The title-rewrite choice lasts only until you close this preview."
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
        : !_runtimeCapabilities.IsBaseReady
            ? "Choose Repair installed tools to restore video processing."
        : _runtimeCapabilities.IsThoroughReady
            ? "Advanced local analysis is installed."
            : _runtimeCapabilities.IsBalancedReady
                ? "Speech tools are installed; the picture tool is not installed."
                : "Clip finding is ready. Add Advanced AI for speech tools and AI title writing.";
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
                      " Studio and Publish rewrites now use local AI."
                    : lifetime +
                      " Studio and Publish rewrites now use the faster built-in writer.";
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                _editorialRerollPreferenceNotice =
                    "Replay Foundry could not save the title-writing choice: " +
                    exception.Message;
            }

            NotifyEditorialRerollPreference();
        }
    }

    public string EditorialRerollPreferenceDetail =>
        UseLocalAiForEditorialRerolls
            ? "Uses Advanced AI on this PC for Studio and Publish rewrites. Takes longer; keeps your wording if it cannot finish."
            : "Uses the faster built-in writer for Studio and Publish rewrites, with fewer variations.";

    public string EditorialRerollPreferencePersistence =>
        _editorialRerollPreference.IsPersistent
            ? "This choice is saved on this PC."
            : "This preview can keep the choice only for this app session.";

    public string EditorialRerollPreferenceNotice =>
        _editorialRerollPreferenceNotice;

    public bool HasEditorialRerollPreferenceNotice =>
        !string.IsNullOrWhiteSpace(EditorialRerollPreferenceNotice);

    public bool IsEditorialMetadataPreferenceLearningEnabled
    {
        get => _editorialMetadataPreferenceLearningConsent.IsEnabled;
        set
        {
            if (value == IsEditorialMetadataPreferenceLearningEnabled) return;
            if (value) _actions.EnableEditorialLearningCommand.Execute(null);
            else _actions.DisableEditorialLearningCommand.Execute(null);
        }
    }

    public string EditorialMetadataPreferenceLearningStatus =>
        IsEditorialMetadataPreferenceLearningEnabled
            ? "Local style learning on"
            : "Local style learning off";

    public string EditorialMetadataPreferenceLearningDetail =>
        IsEditorialMetadataPreferenceLearningEnabled
            ? "Saved edits become training examples for the title or description you changed. Mark reviewed wording when it matches the video. " +
              "You can explain a correction in Studio. Clip likes and exports stay separate from wording approval."
            : "Turn this on to learn from titles and descriptions you correct or mark as reviewed. " +
              "Saved learning stays on this PC until you reset app data in Files & storage.";

    public string EditorialMetadataPreferenceLearningPrivacy =>
        "Wording, clip facts, correction notes, original recording references and montage order stay on this PC. Nothing is uploaded. " +
        "Keep your recordings available for later video checks. Separate recordings are reserved for testing each new model. " +
        "The existing writer stays available until a personal update passes review.";

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
    public int ResearchFeedbackCount => _actions.ResearchFeedbackCount;
    public string ResearchParticipationStatus =>
        IsResearchParticipationEnabled ? "Optional research sharing on" : "Optional research sharing off";
    public string ResearchParticipationDetail =>
        IsResearchParticipationEnabled
            ? "Replay Foundry may prepare anonymous counts and ratings about suggestions " +
              "you kept or skipped. Video, audio, transcripts, titles, game names, " +
              "account data, and file paths are excluded."
            : "Local preference learning still works. Nothing is prepared for optional product research.";
    public string ResearchDeliveryStatus =>
        $"{ResearchFeedbackCount} anonymous feedback item{(ResearchFeedbackCount == 1 ? string.Empty : "s")} saved on this PC. Nothing is sent automatically.";
    public string OnlineConnectionNotice => _onlineNotice;
    public bool HasOnlineConnectionNotice => !string.IsNullOrWhiteSpace(OnlineConnectionNotice);
    public string PrivacySummary =>
        "Finding clips, editing, rendering, Library records, and local " +
        "preferences stay on this PC. Replay Foundry has no advertising " +
        "or background data sharing. Only a report you review and explicitly " +
        "send may use the separately configured support connection.";
    public string YouTubeDataSummary =>
        "Only actions you start in Publish contact Google. A publish action sends the selected video and the YouTube details you reviewed.";
    public string YouTubeStorageSummary =>
        "Replay Foundry never sees your Google password. Windows Credential Manager protects the connection, and local publish history remembers what was uploaded.";
    public string DiagnosticsStatus => IsYouTubeConnectionEnabled
        ? "YouTube allowed · no background data sharing · reviewed reports only"
        : "Local by default · no background data sharing · reviewed reports only";
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
    public ICommand EnableYouTubeConnectionsCommand =>
        _actions.EnableYouTubeConnectionsCommand;
    public ICommand DisableYouTubeConnectionsCommand =>
        _actions.DisableYouTubeConnectionsCommand;
    public ICommand EnableResearchParticipationCommand =>
        _actions.EnableResearchParticipationCommand;
    public ICommand DisableResearchParticipationCommand =>
        _actions.DisableResearchParticipationCommand;
    public ICommand DeleteResearchFeedbackCommand =>
        _actions.DeleteResearchFeedbackCommand;
    public ICommand EnableEditorialMetadataPreferenceLearningCommand =>
        _actions.EnableEditorialLearningCommand;
    public ICommand DisableEditorialMetadataPreferenceLearningCommand =>
        _actions.DisableEditorialLearningCommand;
    public ICommand AddAdvancedAiCommand => _actions.AddAdvancedAiCommand;
    public ICommand RepairRuntimePacksCommand => _actions.RepairRuntimePacksCommand;
    public ICommand RemoveAdvancedAiCommand => _actions.RemoveAdvancedAiCommand;
    public ICommand OpenRuntimePackFolderCommand =>
        _actions.OpenRuntimePackFolderCommand;
    public ICommand ChooseOutputFolderCommand => _actions.ChooseOutputFolderCommand;
    public ICommand UseDefaultOutputFolderCommand =>
        _actions.UseDefaultOutputFolderCommand;
    public ICommand OpenOutputFolderCommand => _actions.OpenOutputFolderCommand;

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
        Updates.Dispose();
        Learning.Dispose();
    }

    private void YouTubePermission_Changed(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsYouTubeConnectionEnabled));
        OnPropertyChanged(nameof(YouTubeConnectionPermissionStatus));
        OnPropertyChanged(nameof(YouTubeConnectionPermissionDetail));
        OnPropertyChanged(nameof(YouTubeConnectionEnabledAtText));
        OnPropertyChanged(nameof(DiagnosticsStatus));
        _actions.RefreshYouTubeCommands();
    }

    private void ResearchParticipation_Changed(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsResearchParticipationEnabled));
        OnPropertyChanged(nameof(ResearchParticipationStatus));
        OnPropertyChanged(nameof(ResearchParticipationDetail));
        OnPropertyChanged(nameof(ResearchFeedbackCount));
        OnPropertyChanged(nameof(ResearchDeliveryStatus));
        _actions.RefreshResearchCommands();
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
        _actions.RefreshOutputFolderCommands();
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
        _actions.RefreshEditorialLearningCommands();
    }

    private void NotifyResearchFeedback()
    {
        OnPropertyChanged(nameof(ResearchFeedbackCount));
        OnPropertyChanged(nameof(ResearchDeliveryStatus));
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

    private void SetRuntimeNotice(string value)
    {
        _runtimeNotice = value;
        OnPropertyChanged(nameof(RuntimePackNotice));
        OnPropertyChanged(nameof(HasRuntimePackNotice));
    }

    private void SetEditorialMetadataPreferenceLearningNotice(string value) =>
        _editorialMetadataPreferenceLearningNotice = value;

    private SettingsSectionItem GetSection(SettingsSection section) =>
        Sections.First(item => item.Key == section);
}
