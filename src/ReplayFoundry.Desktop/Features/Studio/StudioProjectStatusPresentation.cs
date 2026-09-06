using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.Desktop.Features.Studio;

internal static class StudioProjectStatusPresentation
{
    internal static string ProjectName(GenerationOutputProject? project, bool hasProject) =>
        project is null
        ? hasProject
            ? "Open Studio project"
            : "No project open"
        : project.Assets
            .Select(static asset => asset.SourceFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray() is { Length: 1 } sources
                ? Path.GetFileNameWithoutExtension(sources[0])
                : $"{project.Assets.Select(static asset => asset.SourceFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()} source project";

    internal static string SaveStateText(GenerationOutputProject? project, bool hasProject, string? lastError) =>
        lastError is { } error
        ? "Studio could not save your latest changes: " + error
        : project is null
        ? hasProject
            ? "Studio is ready"
            : "No Studio project is open"
        : project.IsFinalized
            ? "Final files saved in Library"
            : "Changes and the queue are saved on this device";

    internal static string StatusText(GenerationOutputProject? project, WorkspaceSurfaceState surfaceState, string? lastError) =>
        lastError is not null
        ? "Studio could not save your latest changes"
        : surfaceState switch
        {
            WorkspaceSurfaceState.ContentReady => project?.IsFinalized == true
                ? "Finished files in Library"
                : project is null
                    ? "Studio project ready"
                    : $"{project.SelectedCount} " +
                      (project.SelectedCount == 1
                          ? "clip ready to edit"
                          : "clips ready to edit"),
            WorkspaceSurfaceState.Loading => "Opening your Studio project…",
            WorkspaceSurfaceState.Error => "Studio needs your attention",
            WorkspaceSurfaceState.Unavailable => "Studio is unavailable right now",
            _ => "Waiting for a project",
        };
}
