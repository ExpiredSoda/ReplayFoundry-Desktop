using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.Desktop.Features.Studio.Rendering;

internal sealed class StudioRenderCaptionReview
{
    private GenerationOutputProject? _project;
    private string _queueKey = "";
    internal StudioCaptionReviewViewModel Panel { get; } = new();
    internal void Refresh(GenerationOutputProject? project, IEnumerable<StudioRenderQueueItem> queue, bool busy)
    {
        Panel.SetBusy(busy);
        var ids = queue.Where(item => !item.IsCompleted).Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
        string key = string.Join('|', ids.Order(StringComparer.Ordinal));
        if (ReferenceEquals(_project, project) && key == _queueKey) return;
        _project = project; _queueKey = key;
        Panel.SetIssues(project?.Assets.Where(asset => ids.Contains(asset.Id) && asset.RenderSettings.BurnCaptions)
            .SelectMany(asset => StudioCaptionTimingReview.Find(asset)).ToArray() ?? []);
    }
}
