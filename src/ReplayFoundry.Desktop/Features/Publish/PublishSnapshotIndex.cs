using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish.YouTube;

namespace ReplayFoundry.Desktop.Features.Publish;

internal sealed class PublishSnapshotIndex
{
    private Dictionary<string, YouTubePublishDraft> _draftsByAsset =
        new Dictionary<string, YouTubePublishDraft>(StringComparer.Ordinal);
    private Dictionary<string, YouTubePublishHistoryEntry>
        _uploadedHistoryByAsset =
            new Dictionary<string, YouTubePublishHistoryEntry>(
                StringComparer.Ordinal);
    private HashSet<string> _failedHistoryAssetIds =
        new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<YouTubePublishDraft> Drafts { get; private set; } = [];
    public IReadOnlyList<YouTubePublishHistoryEntry> History { get; private set; } = [];
    public IReadOnlyList<PublishPlanningItem> PlanningBacklog { get; private set; } = [];

    public void Refresh(
        IReadOnlyList<YouTubePublishDraft> drafts,
        IReadOnlyList<YouTubePublishHistoryEntry> history,
        IReadOnlyList<LibraryMediaAsset> assets)
    {
        SetDrafts(drafts);
        History = history;
        _uploadedHistoryByAsset = new Dictionary<
            string,
            YouTubePublishHistoryEntry>(StringComparer.Ordinal);
        _failedHistoryAssetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (YouTubePublishHistoryEntry entry in History)
        {
            if (entry.VideoId is not null)
            {
                _uploadedHistoryByAsset.TryAdd(entry.AssetId, entry);
            }
            if (entry.Outcome == YouTubePublishOutcome.Failed)
            {
                _failedHistoryAssetIds.Add(entry.AssetId);
            }
        }
        RebuildPlanningBacklog(assets);
    }

    public void RefreshDrafts(
        IReadOnlyList<YouTubePublishDraft> drafts,
        IReadOnlyList<LibraryMediaAsset> assets)
    {
        SetDrafts(drafts);
        RebuildPlanningBacklog(assets);
    }

    private void SetDrafts(IReadOnlyList<YouTubePublishDraft> drafts)
    {
        Drafts = drafts;
        _draftsByAsset = drafts.ToDictionary(
            static draft => draft.AssetId,
            StringComparer.Ordinal);
    }

    public void RebuildPlanningBacklog(
        IReadOnlyList<LibraryMediaAsset> assets)
    {
        PlanningBacklog = assets
            .Select(asset => new PublishPlanningItem(
                asset.Title,
                $"{PublishPresentationRules.FormatDuration(asset.Duration)} · finalized in Studio",
                _draftsByAsset.ContainsKey(asset.Id)
                    ? "DRAFT SAVED"
                    : GetAssetPublishState(asset).ToUpperInvariant(),
                "Icon.Media",
                asset))
            .ToArray();
    }

    public YouTubePublishDraft? GetDraft(string assetId) =>
        _draftsByAsset.GetValueOrDefault(assetId);

    public string GetAssetPublishState(LibraryMediaAsset asset)
    {
        if (_uploadedHistoryByAsset.TryGetValue(
                asset.Id,
                out YouTubePublishHistoryEntry? uploaded))
        {
            if (uploaded.RemoteStatus ==
                YouTubeRemoteVideoStatus.NotFoundOrInaccessible)
            {
                return "YouTube copy needs attention";
            }
            return uploaded.Outcome == YouTubePublishOutcome.Scheduled
                ? "Scheduled on YouTube"
                : "Uploaded to YouTube";
        }

        return _failedHistoryAssetIds.Contains(asset.Id)
            ? "Last upload failed"
            : "Not uploaded";
    }
}
