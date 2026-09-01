using System.IO;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.Editorial;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Dialogs;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.YouTube;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record PublishFeatureDependencies(
    ApplicationPreferenceServices Preferences,
    GenerationWorkspaceServices Workspace,
    EditorialServices Editorial,
    GenerationExperienceServices Experience);

internal sealed record PublishFeatureServices(
    YouTubeConnectionPermissionState ConnectionPermission,
    IYouTubePublishingService? Publishing,
    PublishViewModel ViewModel);

internal static class PublishFeatureComposition
{
    public static PublishFeatureServices Create(
        PublishFeatureDependencies dependencies)
    {
        YouTubeConnectionPermissionState connectionPermission =
            CreateConnectionPermission();
        IYouTubePublishingService? publishing =
            YouTubePublishingFactory.CreateDefault(connectionPermission);
        var viewModel = new PublishViewModel(
            dependencies.Workspace.LibraryCatalog,
            publishing,
            new JsonYouTubePublishPreferencesStore(),
            new WindowsThumbnailFilePicker(),
            connectionPermission,
            new JsonYouTubePublishDraftStore(),
            new WindowsPublishPreparationDialogService(),
            new WindowsPublishBulkConfirmation(),
            new PublishEditorialMetadataService(
                dependencies.Workspace.OutputSession,
                dependencies.Editorial.MetadataGenerator,
                dependencies.Editorial.ProfileSession,
                dependencies.Workspace.StudioProjectStore,
                dependencies.Experience.GameKnowledge),
            dependencies.Preferences.EditorialReroll,
            new WindowsPublishHistoryDialogService(),
            new WindowsPublishHistoryLinkLauncher());
        return new(connectionPermission, publishing, viewModel);
    }

    private static YouTubeConnectionPermissionState
        CreateConnectionPermission()
    {
        try
        {
            return new YouTubeConnectionPermissionState(
                new JsonYouTubeConnectionPermissionStore());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "YouTube connection-permission storage is unavailable",
                exception);
            return new YouTubeConnectionPermissionState(
                new InMemoryYouTubeConnectionPermissionStore());
        }
    }
}
