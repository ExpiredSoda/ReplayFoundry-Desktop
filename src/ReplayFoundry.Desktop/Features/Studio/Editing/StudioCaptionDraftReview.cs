using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

/// <summary>Owns focused phrase navigation without replacing the text editor's collection while typing.</summary>
public sealed class StudioCaptionDraftReview : ObservableObject
{
    private readonly Func<bool> _canReview;
    private readonly DelegateCommand _showAll;
    private readonly DelegateCommand _next;
    private string? _reviewedId;
    private GenerationOutputAsset? _asset;
    private IEnumerable<StudioCaptionSegmentDraft> _segments = [];
    public StudioCaptionDraftReview(Func<bool> canReview)
    {
        _canReview = canReview;
        Panel.ReviewRequested += Open;
        _showAll = new(() => { _reviewedId = null; UpdateSelection(); });
        _next = new(() =>
        {
            int index = Panel.Issues.ToList().FindIndex(issue => issue.SegmentId == _reviewedId);
            Open(Panel.Issues[(index + 1) % Panel.Issues.Count]);
        }, () => _canReview() && Panel.HasIssues);
    }
    public StudioCaptionReviewViewModel Panel { get; } = new();
    public event Action<StudioCaptionTimingIssue>? PreviewRequested;
    public bool IsReviewingTiming => _reviewedId is not null;
    public IEnumerable<StudioCaptionSegmentDraft> VisibleSegments => IsReviewingTiming
        ? _segments.Where(segment => segment.Id == _reviewedId) : _segments;
    public ICommand ShowAllPhrasesCommand => _showAll;
    public ICommand NextIssueCommand => _next;
    public void OpenPhrase(string segmentId)
    {
        var segment = _segments.FirstOrDefault(segment => segment.Id == segmentId);
        if (_asset is null || segment is null) return;
        Open(new(_asset.Id, segment.Id, _asset.DisplayName, Math.Max(0, segment.StartSeconds), segment.Text, "", false, false));
    }
    public void Open(StudioCaptionTimingIssue issue)
    {
        if (!_canReview() || _asset?.Id != issue.AssetId || !_segments.Any(segment => segment.Id == issue.SegmentId)) return;
        _reviewedId = issue.SegmentId;
        UpdateSelection();
        PreviewRequested?.Invoke(issue);
    }
    internal void Bind(GenerationOutputAsset? asset, bool sameClip)
    { _asset = asset; if (!sameClip) _reviewedId = null; }
    internal void Refresh(IEnumerable<StudioCaptionSegmentDraft> segments, bool replaced = false)
    {
        _segments = segments;
        Panel.SetIssues(_asset is null ? [] : StudioCaptionTimingReview.Find(_asset, segments.Select(segment => segment.Snapshot()).ToArray()));
        _next.RaiseCanExecuteChanged();
        if (_reviewedId is not null && !segments.Any(segment => segment.Id == _reviewedId)) { _reviewedId = null; replaced = true; }
        if (replaced) UpdateSelection();
    }
    internal void SetBusy(bool busy) { Panel.SetBusy(busy); _showAll.RaiseCanExecuteChanged(); _next.RaiseCanExecuteChanged(); }
    private void UpdateSelection()
    {
        foreach (var segment in _segments) segment.IsTimingReviewOpen = segment.Id == _reviewedId;
        OnPropertyChanged(nameof(IsReviewingTiming)); OnPropertyChanged(nameof(VisibleSegments));
    }
}
