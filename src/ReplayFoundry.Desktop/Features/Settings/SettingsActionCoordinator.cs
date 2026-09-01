using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Research;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Settings;

internal sealed record SettingsActionServices(
    YouTubeConnectionPermissionState YouTubePermission,
    ResearchParticipationState ResearchParticipation,
    IResearchFeedbackStore? ResearchFeedbackStore,
    SettingsRuntimeCapabilitySnapshot? RuntimeCapabilities,
    IRuntimePackMaintenanceActions? RuntimeMaintenance,
    GenerationOutputLocationState OutputLocation,
    IOutputFolderPicker? OutputFolderPicker,
    ILocalFolderLauncher? FolderLauncher,
    EditorialMetadataPreferenceLearningConsentState EditorialLearningConsent);

internal sealed record SettingsActionNotifications(
    Action<string> SetStorageNotice,
    Action<string> SetOnlineNotice,
    Action<string> SetRuntimeNotice,
    Action<string> SetEditorialLearningNotice,
    Action ResearchFeedbackChanged,
    Action EditorialLearningChanged);

internal sealed class SettingsActionCoordinator
{
    private readonly SettingsActionServices _services;
    private readonly SettingsActionNotifications _notifications;

    public SettingsActionCoordinator(
        SettingsActionServices services,
        SettingsActionNotifications notifications)
    {
        _services = services;
        _notifications = notifications;
        EnableYouTubeConnectionsCommand = new(
            EnableYouTubeConnections,
            () => !services.YouTubePermission.IsEnabled);
        DisableYouTubeConnectionsCommand = new(
            DisableYouTubeConnections,
            () => services.YouTubePermission.IsEnabled);
        EnableResearchParticipationCommand = new(
            EnableResearchParticipation,
            () => !services.ResearchParticipation.IsEnabled);
        DisableResearchParticipationCommand = new(
            DisableResearchParticipation,
            () => services.ResearchParticipation.IsEnabled);
        DeleteResearchFeedbackCommand = new(
            DeleteResearchFeedback,
            () => ResearchFeedbackCount > 0);
        EnableEditorialLearningCommand = new(
            EnableEditorialLearning,
            () => !services.EditorialLearningConsent.IsEnabled);
        DisableEditorialLearningCommand = new(
            DisableEditorialLearning,
            () => services.EditorialLearningConsent.IsEnabled);
        AddAdvancedAiCommand = new(
            AddAdvancedAi,
            () => services.RuntimeMaintenance?.CanAddAdvanced == true);
        RepairRuntimePacksCommand = new(
            RepairRuntimePacks,
            () => services.RuntimeMaintenance?.CanRepair == true);
        RemoveAdvancedAiCommand = new(
            RemoveAdvancedAi,
            () => services.RuntimeMaintenance?.CanRemoveAdvanced == true &&
                  services.RuntimeCapabilities?.HasAdvancedCapability == true);
        OpenRuntimePackFolderCommand = new(
            () => services.RuntimeMaintenance?.OpenPackageFolder(),
            () => services.RuntimeMaintenance is not null);
        ChooseOutputFolderCommand = new(
            ChooseOutputFolder,
            () => services.OutputFolderPicker is not null);
        UseDefaultOutputFolderCommand = new(
            UseDefaultOutputFolder,
            () => services.OutputLocation.UsesCustomRoot);
        OpenOutputFolderCommand = new(
            OpenOutputFolder,
            () => services.FolderLauncher is not null);
    }

    public int ResearchFeedbackCount =>
        _services.ResearchFeedbackStore?.Current.Count ?? 0;

    public DelegateCommand EnableYouTubeConnectionsCommand { get; }
    public DelegateCommand DisableYouTubeConnectionsCommand { get; }
    public DelegateCommand EnableResearchParticipationCommand { get; }
    public DelegateCommand DisableResearchParticipationCommand { get; }
    public DelegateCommand DeleteResearchFeedbackCommand { get; }
    public DelegateCommand EnableEditorialLearningCommand { get; }
    public DelegateCommand DisableEditorialLearningCommand { get; }
    public DelegateCommand AddAdvancedAiCommand { get; }
    public DelegateCommand RepairRuntimePacksCommand { get; }
    public DelegateCommand RemoveAdvancedAiCommand { get; }
    public DelegateCommand OpenRuntimePackFolderCommand { get; }
    public DelegateCommand ChooseOutputFolderCommand { get; }
    public DelegateCommand UseDefaultOutputFolderCommand { get; }
    public DelegateCommand OpenOutputFolderCommand { get; }

    public void RefreshYouTubeCommands()
    {
        EnableYouTubeConnectionsCommand.RaiseCanExecuteChanged();
        DisableYouTubeConnectionsCommand.RaiseCanExecuteChanged();
    }

    public void RefreshResearchCommands()
    {
        EnableResearchParticipationCommand.RaiseCanExecuteChanged();
        DisableResearchParticipationCommand.RaiseCanExecuteChanged();
        DeleteResearchFeedbackCommand.RaiseCanExecuteChanged();
    }

    public void RefreshEditorialLearningCommands()
    {
        EnableEditorialLearningCommand.RaiseCanExecuteChanged();
        DisableEditorialLearningCommand.RaiseCanExecuteChanged();
    }

    public void RefreshOutputFolderCommands() =>
        UseDefaultOutputFolderCommand.RaiseCanExecuteChanged();

    private void ChooseOutputFolder() => RunStorageAction(() =>
    {
        string? selected = _services.OutputFolderPicker?.PickOutputFolder(
            _services.OutputLocation.OutputRootDirectory);
        if (selected is null) return;
        _services.OutputLocation.SetCustomRoot(selected);
        _notifications.SetStorageNotice(
            "Future projects will render here. Existing Library videos were not moved.");
    }, "Replay Foundry could not use that folder: ");

    private void UseDefaultOutputFolder() => RunStorageAction(() =>
    {
        _services.OutputLocation.UseDefaultRoot();
        _notifications.SetStorageNotice(
            "Future projects will use the ReplayFoundry folder under Windows Videos.");
    }, "Replay Foundry could not restore the default folder: ");

    private void OpenOutputFolder()
    {
        if (_services.FolderLauncher is null) return;
        RunStorageAction(() =>
        {
            _services.OutputLocation.EnsureCurrentRootIsWritable();
            _services.FolderLauncher.OpenFolder(
                _services.OutputLocation.OutputRootDirectory);
            _notifications.SetStorageNotice("Opened the current output folder.");
        }, "Replay Foundry could not open that folder: ");
    }

    private void RunStorageAction(Action action, string failurePrefix)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            _notifications.SetStorageNotice(failurePrefix + exception.Message);
        }
    }

    private void EnableYouTubeConnections() => RunPersistentAction(
        () => _services.YouTubePermission.Enable(DateTimeOffset.UtcNow),
        "YouTube access is allowed. Nothing was connected or sent; choose Connect in Publish when ready.",
        "Replay Foundry could not save this choice: ",
        _notifications.SetOnlineNotice);

    private void DisableYouTubeConnections() => RunPersistentAction(
        _services.YouTubePermission.Disable,
        "YouTube access is off. Any active upload is cancelled and the local connection is being removed.",
        "Replay Foundry could not save local-only mode: ",
        _notifications.SetOnlineNotice);

    private void EnableResearchParticipation()
    {
        _services.ResearchParticipation.Enable(DateTimeOffset.UtcNow);
        _notifications.SetOnlineNotice(
            "Optional research sharing is on. Nothing can upload because this build has no research upload service.");
    }

    private void DisableResearchParticipation()
    {
        _services.ResearchParticipation.Disable();
        _notifications.SetOnlineNotice(
            "Optional research sharing is off. Existing local records remain until you delete them.");
    }

    private void DeleteResearchFeedback()
    {
        _services.ResearchFeedbackStore?.Clear();
        _notifications.ResearchFeedbackChanged();
        DeleteResearchFeedbackCommand.RaiseCanExecuteChanged();
        _notifications.SetOnlineNotice("Local research records were deleted.");
    }

    private void EnableEditorialLearning() => SetEditorialLearning(
        () => _services.EditorialLearningConsent.Enable(DateTimeOffset.UtcNow),
        "Local structural style learning is on. Nothing was uploaded, " +
        "and saved wording is not retained in the profile.",
        "Replay Foundry could not save the local learning choice: ");

    private void DisableEditorialLearning() => SetEditorialLearning(
        _services.EditorialLearningConsent.Disable,
        "Local structural style learning is off. No new correction will be " +
        "recorded. To remove an existing numeric profile, choose Reset saved " +
        "app data in Files & storage.",
        "Replay Foundry could not turn off local learning: ");

    private void SetEditorialLearning(
        Action action,
        string success,
        string failurePrefix)
    {
        RunPersistentAction(
            action,
            success,
            failurePrefix,
            _notifications.SetEditorialLearningNotice);
        _notifications.EditorialLearningChanged();
    }

    private void RunPersistentAction(
        Action action,
        string success,
        string failurePrefix,
        Action<string> setNotice)
    {
        try
        {
            action();
            setNotice(success);
        }
        catch (Exception exception) when (IsPersistentStateException(exception))
        {
            setNotice(failurePrefix + exception.Message);
        }
    }

    private void AddAdvancedAi() => RunRuntimeMaintenance(
        () => _services.RuntimeMaintenance!.AddAdvanced(),
        "The Advanced AI installer opened. Restart Replay Foundry after it finishes.");

    private void RepairRuntimePacks() => RunRuntimeMaintenance(
        () => _services.RuntimeMaintenance!.Repair(),
        "The repair tool opened. Restart Replay Foundry after it finishes.");

    private void RemoveAdvancedAi() => RunRuntimeMaintenance(
        () => _services.RuntimeMaintenance!.RemoveAdvanced(),
        "Replay Foundry is closing so Advanced AI can be removed safely. Core clip discovery and your videos are kept.");

    private void RunRuntimeMaintenance(Action action, string success)
    {
        string notice;
        try
        {
            action();
            notice = success;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            notice = "The maintenance tool could not start: " + exception.Message;
        }
        _notifications.SetRuntimeNotice(notice);
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or
        System.ComponentModel.Win32Exception;

    private static bool IsPersistentStateException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException;
}
