using System.Collections.Generic;
using System.Windows.Input;
using ReplayFoundry.Desktop.Presentation.Workspaces;

namespace ReplayFoundry.Desktop.Features.Studio.DesignTime;

public sealed class StudioDesignViewModel
{
    public StudioDesignViewModel()
    {
        ToolSections = StudioSurfaceCatalog.ToolSections;
        InspectorSections = StudioSurfaceCatalog.InspectorSections;
        BrowserPreviewItems = new[]
        {
            new StudioBrowserPreviewItem("Clip 01", "0:45 · creator-session.mkv", "QUALITY TARGET", "Icon.Media", "clip-01", true),
            new StudioBrowserPreviewItem("Clip 02", "0:32 · creator-session.mkv", "BEST AVAILABLE", "Icon.Media", "clip-02")
        };
    }

    public IReadOnlyList<StudioToolItem> ToolSections { get; }
    public IReadOnlyList<StudioInspectorItem> InspectorSections { get; }
    public IReadOnlyList<StudioBrowserPreviewItem> BrowserPreviewItems { get; }
    public StudioDesignViewModel Preview => this;
    public StudioDesignViewModel Inspector => this;
    public StudioDesignViewModel Clip => this;
    public StudioDesignViewModel Preference => this;
    public StudioDesignViewModel FinalRender => this;
    public StudioToolSection SelectedTool => StudioToolSection.MomentsClips;
    public StudioInspectorSection SelectedInspector => StudioInspectorSection.Clip;
    public bool IsEmpty => false;
    public WorkspaceSurfaceState SurfaceState => WorkspaceSurfaceState.ContentReady;
    public bool IsContentReady => true;
    public bool IsLoading => false;
    public bool IsError => false;
    public bool IsUnavailable => false;
    public bool HasProject => true;
    public bool IsProjectMissing => false;
    public bool IsCaptionContentVisible => true;
    public string ProjectName => "Creator reel / Q2 highlight";
    public string SaveStateText => "Edits stay in this Studio session until final render.";
    public string ReadinessText => "Ready when your edits are complete";
    public string ButtonText => "Add selected clip";
    public string RenderQueueButtonText => "Render queue";
    public string QueueSummary => "The queue is empty. Select a kept Browser clip, then add it from the project bar above.";
    public bool HasQueuedItems => false;
    public int QueuedClipCount => 0;
    public IReadOnlyList<object> QueueItems => [];
    public bool IsProgressVisible => false;
    public string Status => "No clips are queued.";
    public string? Error => null;
    public bool HasError => false;
    public bool IsRendering => false;
    public double Percent => 0;
    public string ProjectPromptTitle => "Preview ready";
    public string ProjectPromptDescription => "Choose a clip, adjust it, then render the clips you want.";
    public string ModeBadge => "STUDIO / EDIT";
    public string StatusText => "Design preview · no project loaded at runtime";
    public string ErrorSummary => "Studio could not load a project.";
    public string SelectedToolTitle => "Clips";
    public string SelectedToolDescription => "Choose the generated clip to preview and edit.";
    public string SelectedInspectorTitle => "Clip controls";
    public string SelectedInspectorDescription => "Changes stay nondestructive until render.";
    public string PreviewTimecode => "0:12";
    public string PreviewDurationText => "0:45";
    public string PreviewFormatText => "1080 × 1920 · 30 FPS";
    public string PreviewScaleText => "PORTRAIT · FIT";
    public string SequenceSummary => "Selected clip layers";
    public string SelectedClipDurationText => "0:45";
    public string CaptionVisibilityText => "Hide captions";
    public ICommand? SelectToolCommand => null;
    public ICommand? SelectBrowserAssetCommand => null;
    public ICommand? SelectInspectorCommand => null;
    public ICommand? PlayCommand => null;
    public ICommand? PreviousCommand => null;
    public ICommand? NextCommand => null;
    public ICommand? VolumeCommand => null;
    public ICommand? ToggleCaptionVisibilityCommand => null;
    public ICommand? AddToQueueCommand => null;
    public ICommand? RenderQueueCommand => null;
    public ICommand? RemoveQueuedItemCommand => null;
    public ICommand? RerenderQueuedItemCommand => null;
    public ICommand? CancelCommand => null;
    public ICommand? ExportHandoffCommand => null;
}
