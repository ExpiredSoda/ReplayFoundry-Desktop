using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Platform.Diagnostics;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.Desktop.Composition;

internal sealed record GenerationWorkspaceServices(
    GenerationOutputSession OutputSession,
    JsonStudioProjectStore StudioProjectStore,
    StudioProjectPersistenceCoordinator StudioProjectPersistence,
    RecentGenerationProjectCatalog RecentProjects,
    GenerationLibraryCatalog LibraryCatalog);

internal static class GenerationWorkspaceComposition
{
    public static GenerationWorkspaceServices Create()
    {
        var outputSession = new GenerationOutputSession();
        var studioProjectStore = new JsonStudioProjectStore();
        var studioProjectPersistence =
            new StudioProjectPersistenceCoordinator(
                outputSession,
                studioProjectStore);
        var recentProjects = new RecentGenerationProjectCatalog(
            outputSession,
            studioProjectStore: studioProjectStore);
        var libraryCatalog = new GenerationLibraryCatalog(
            outputSession,
            CreateLibraryCatalogStore());
        return new(
            outputSession,
            studioProjectStore,
            studioProjectPersistence,
            recentProjects,
            libraryCatalog);
    }

    private static ILibraryCatalogStore CreateLibraryCatalogStore()
    {
        try
        {
            return new JsonLibraryCatalogStore();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SafeDiagnosticTrace.Write(
                "Library catalog storage is unavailable",
                exception);
            return new InMemoryLibraryCatalogStore();
        }
    }
}
