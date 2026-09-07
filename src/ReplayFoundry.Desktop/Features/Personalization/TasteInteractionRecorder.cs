using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.Desktop.Features.Personalization;

public sealed class TasteInteractionRecorder : IDisposable
{
    private readonly ITasteLearningService _learning;
    private readonly IGenerationOutputSession _session;
    private readonly IGenerationRenderedOutputSession? _rendered;
    private readonly ILibraryCatalog _library;
    private readonly StudioViewModel _studio;
    private readonly IStudioCandidateDecisionStore? _decisions;
    private readonly ConcurrentDictionary<string, GenerationOutputAsset> _known = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GenerationOutputAsset[]> _renderSnapshots = new(StringComparer.Ordinal);
    private readonly HashSet<string> _imported = new(StringComparer.Ordinal);
    public TasteInteractionRecorder(ITasteLearningService learning, IGenerationOutputSession session, ILibraryCatalog library,
        StudioViewModel studio, IStudioCandidateDecisionStore? decisions = null)
    {
        _learning = learning; _session = session; _library = library; _studio = studio; _decisions = decisions;
        _rendered = session as IGenerationRenderedOutputSession;
        session.CurrentChanged += ProjectChanged;
        if (_rendered is not null) _rendered.RenderedOutputCommitted += Rendered;
        studio.ManualClips.ClipAdded += ManualAdded;
        studio.HiddenMoments.MomentAccepted += HiddenAccepted;
        studio.HiddenMoments.PropertyChanged += HiddenChanged;
        CaptureProject();
    }
    private void ProjectChanged(object? sender, GenerationOutputChangedEventArgs e) => CaptureProject();
    private void CaptureProject()
    {
        foreach (var asset in _session.Current?.Assets ?? [])
        {
            _known[asset.Id] = asset;
            Record(asset, null);
            string key = asset.Id + "|" + asset.SourceStart.Ticks + "|" + asset.SourceEnd.Ticks;
            if (!_imported.Add(key)) continue;
            var decision = _decisions?.Find(asset.Id);
            if (decision is { Rating: { } rating } && decision.SourceStart == asset.SourceStart && decision.SourceEnd == asset.SourceEnd &&
                decision.RecordedAtUtc >= _learning.HistoryStartUtc)
                Record(asset, rating switch { StudioClipPreferenceRating.Like => TasteSignal.Like,
                    StudioClipPreferenceRating.Dislike => TasteSignal.Dislike, _ => TasteSignal.Neutral });
            if (asset.SelectionReason == Features.Generate.Moments.GenerationCandidateSelectionReason.ManualSourceCut &&
                _session.Current!.CreatedAtUtc >= _learning.HistoryStartUtc) Record(asset, TasteSignal.ManualCreated);
            if (asset.SelectionReason == Features.Generate.Moments.GenerationCandidateSelectionReason.HiddenMomentRecovery &&
                _session.Current!.CreatedAtUtc >= _learning.HistoryStartUtc) Record(asset, TasteSignal.HiddenAccepted);
            if (_library.Assets.Any(item => item.AddedAtUtc >= _learning.HistoryStartUtc && item.SourceCandidateIds.Contains(asset.Id) &&
                Matches(item.SourceProvenance, asset)))
                Record(asset, TasteSignal.Rendered);
        }
    }
    private void ManualAdded(object? sender, EventArgs e)
    { if (_session.Current?.Assets.LastOrDefault() is { } asset) Record(asset, TasteSignal.ManualCreated); }
    private void HiddenAccepted(object? sender, StudioHiddenMomentAcceptedEventArgs e)
    { if (_session.Current?.Assets.FirstOrDefault(a => a.Id == e.CandidateId) is { } asset) Record(asset, TasteSignal.HiddenAccepted); }
    private void HiddenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioHiddenMomentsViewModel.Current) && _studio.HiddenMoments.Current is { } moment)
            TryRecord(() => _learning.Observe(TasteClipFactory.FromHidden(moment), null));
    }
    private void Rendered(object? sender, GenerationRenderedOutputEventArgs e)
    {
        foreach (var item in _library.Assets.Where(item => item.SourceCandidateIds.Count > 0 &&
            item.SourceCandidateIds.All(id => e.RenderedProject.IncludedAssets.Any(a => a.Id == id && Matches(item.SourceProvenance, a)))))
            _renderSnapshots.TryAdd(SnapshotKey(item), e.RenderedProject.IncludedAssets.Where(a => item.SourceCandidateIds.Contains(a.Id)).ToArray());
        foreach (var asset in e.RenderedProject.IncludedAssets)
        {
            // The Library commit must exist. Failed or rolled-back render attempts are not learning outcomes.
            if (!_library.Assets.Any(item => item.SourceCandidateIds.Contains(asset.Id) && Matches(item.SourceProvenance, asset))) continue;
            _known[asset.Id] = asset; Record(asset, TasteSignal.Rendered);
        }
    }
    public void Published(LibraryMediaAsset asset, YouTubePublishOutcome outcome)
    {
        if (outcome is not (YouTubePublishOutcome.Published or YouTubePublishOutcome.UploadedUnlisted)) return;
        if (_renderSnapshots.TryGetValue(SnapshotKey(asset), out var snapshots))
        { foreach (var snapshot in snapshots) Record(snapshot, TasteSignal.Published); return; }
        if (asset.SourceProvenance is not { } provenance) return;
        PublishedCuts(provenance, asset.SourceCandidateIds);
    }
    private void PublishedCuts(YouTubePublishProvenance provenance, IReadOnlyList<string> candidateIds)
    {
        var cuts = provenance.ContributingCuts.Count > 0 ? provenance.ContributingCuts : [provenance];
        foreach (var cut in cuts)
        {
            var source = candidateIds.Select(id => _known.GetValueOrDefault(id)).FirstOrDefault(a => a is not null &&
                a.SourceFullPath.Equals(cut.SourceFullPath, StringComparison.OrdinalIgnoreCase) && a.SourceDuration >= cut.SourceEnd);
            if (source is not null) TryRecord(() => Record(source.WithStudioEdits(cut.SourceStart, cut.SourceEnd, source.Appearance), TasteSignal.Published));
        }
    }
    private static string SnapshotKey(LibraryMediaAsset asset)
    {
        var provenance = asset.SourceProvenance;
        var cuts = provenance is null ? [] : provenance.ContributingCuts.Count > 0 ? provenance.ContributingCuts : [provenance];
        return asset.Id + "|" + asset.OutputFullPath + "|" + string.Join('|', cuts.Select(c => $"{c.SourceFullPath}:{c.SourceStart.Ticks}:{c.SourceEnd.Ticks}"));
    }
    private static bool Matches(YouTubePublishProvenance? provenance, GenerationOutputAsset asset) => provenance is not null &&
        (provenance.ContributingCuts.Count > 0 ? provenance.ContributingCuts : [provenance]).Any(c =>
            c.SourceFullPath.Equals(asset.SourceFullPath, StringComparison.OrdinalIgnoreCase) && c.SourceStart == asset.SourceStart && c.SourceEnd == asset.SourceEnd);
    public void ImportPublishHistory(IReadOnlyList<YouTubePublishHistoryEntry> history)
    {
        foreach (var entry in history.Where(e => e.AttemptedAtUtc >= _learning.HistoryStartUtc && e.Provenance is not null &&
            e.Outcome is YouTubePublishOutcome.Published or YouTubePublishOutcome.UploadedUnlisted))
        {
            var asset = _library.Assets.FirstOrDefault(a => a.Id == entry.AssetId);
            // A Library ID can be reused by a later render. History must use the submitted cut, never the current Library range.
            PublishedCuts(entry.Provenance!, asset?.SourceCandidateIds ?? _known.Keys.ToArray());
        }
    }
    private void Record(GenerationOutputAsset asset, TasteSignal? signal) => TryRecord(() => _learning.Observe(TasteClipFactory.FromAsset(asset), signal));
    private static void TryRecord(Action record)
    {
        try { record(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { SafeDiagnosticTrace.Write("A clip learning observation could not be recorded", e); }
    }
    public void Dispose()
    {
        _session.CurrentChanged -= ProjectChanged;
        if (_rendered is not null) _rendered.RenderedOutputCommitted -= Rendered;
        _studio.ManualClips.ClipAdded -= ManualAdded;
        _studio.HiddenMoments.MomentAccepted -= HiddenAccepted;
        _studio.HiddenMoments.PropertyChanged -= HiddenChanged;
    }
}
