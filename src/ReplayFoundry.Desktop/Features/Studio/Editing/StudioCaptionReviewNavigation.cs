using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Features.Studio.Rendering;
using System.ComponentModel;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal sealed class StudioCaptionReviewNavigation : IDisposable
{
    private readonly StudioInspectorViewModel _inspector;
    private readonly StudioPreviewViewModel _preview;
    private readonly StudioFinalRenderViewModel _render;
    private readonly Action<string> _select;
    private bool _hasUnsavedCaptions, _isAligning;
    internal StudioCaptionReviewNavigation(StudioInspectorViewModel inspector, StudioPreviewViewModel preview,
        StudioFinalRenderViewModel render, Action<string> select)
    {
        _inspector = inspector; _preview = preview; _render = render; _select = select;
        _hasUnsavedCaptions = inspector.Caption.HasUnsavedChanges; _isAligning = inspector.Caption.IsAligning;
        render.CaptionReview.ReviewRequested += ReviewQueued;
        inspector.Caption.Review.PreviewRequested += Seek;
        inspector.Caption.PropertyChanged += CaptionChanged;
    }
    private void ReviewQueued(StudioCaptionTimingIssue issue)
    {
        if (_inspector.SelectedAsset?.Id != issue.AssetId &&
            (_inspector.Caption.HasUnsavedChanges || _inspector.Graphics.HasUnsavedChanges))
        {
            _render.CaptionReview.ShowMessage("Save your current edits before reviewing another clip's captions.");
            return;
        }
        _select(issue.AssetId);
        if (_inspector.SelectedAsset?.Id != issue.AssetId) return;
        _inspector.SelectedInspector = StudioInspectorSection.Captions;
        _inspector.Caption.Review.Open(issue);
    }
    private void Seek(StudioCaptionTimingIssue issue)
    {
        if (_inspector.SelectedAsset is not { } asset || asset.Id != issue.AssetId) return;
        _inspector.Caption.AudioAudition.Stop();
        if (_preview.IsPreviewPlaying) _preview.PlayCommand.Execute(null);
        _preview.PreviewPositionSeconds = asset.SourceStart.TotalSeconds + issue.StartSeconds;
    }
    private void CaptionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(StudioCaptionTrackEditorViewModel.HasUnsavedChanges) or nameof(StudioCaptionTrackEditorViewModel.IsAligning))) return;
        bool dirty = _inspector.Caption.HasUnsavedChanges, aligning = _inspector.Caption.IsAligning;
        if (dirty == _hasUnsavedCaptions && aligning == _isAligning) return;
        _hasUnsavedCaptions = dirty; _isAligning = aligning;
        _render.RefreshReadiness();
    }
    public void Dispose()
    {
        _render.CaptionReview.ReviewRequested -= ReviewQueued;
        _inspector.Caption.Review.PreviewRequested -= Seek;
        _inspector.Caption.PropertyChanged -= CaptionChanged;
    }
}
