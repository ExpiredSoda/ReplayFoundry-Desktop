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
    private DateTimeOffset _observedAt = DateTimeOffset.UtcNow;

    public IReadOnlyList<YouTubePublishDraft> Drafts { get; private set; } = [];
    public IReadOnlyList<YouTubePublishHistoryEntry> History { get; private set; } = [];
    public IReadOnlyList<PublishPlanningItem> PlanningBacklog { get; private set; } = [];

    public void Refresh(
        IReadOnlyList<YouTubePublishDraft> drafts,
        IReadOnlyList<YouTubePublishHistoryEntry> history,
        IReadOnlyList<LibraryMediaAsset> assets,
        string? channelId = null,
        DateTimeOffset? observedAt = null)
    {
        SetDrafts(drafts);
        _observedAt = observedAt ?? DateTimeOffset.UtcNow;
        History = history;
        _uploadedHistoryByAsset = new Dictionary<
            string,
            YouTubePublishHistoryEntry>(StringComparer.Ordinal);
        _failedHistoryAssetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (YouTubePublishHistoryEntry entry in History.OrderByDescending(static entry => entry.AttemptedAtUtc))
        {
            string? owner = entry.Provenance?.ChannelId ?? entry.RemoteDetails?.ChannelId;
            if (channelId is not null && owner is not null && owner != channelId) continue;
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

    public YouTubePublishHistoryEntry? GetUpload(LibraryMediaAsset asset)
    {
        var upload = _uploadedHistoryByAsset.GetValueOrDefault(asset.Id);
        return upload is not null && !HasReplacement(asset, upload) ? upload : null;
    }

    private static bool HasReplacement(LibraryMediaAsset asset, YouTubePublishHistoryEntry upload)
    {
        var current = PublishedFileRevision.Capture(asset.OutputFullPath);
        if (current is null) return false; // Missing local files do not erase remote history.
        return upload.FileRevision is { } submitted ? submitted != current
            : current.LastWriteUtcTicks > upload.AttemptedAtUtc.UtcTicks;
    }

    public string GetAssetPublishState(LibraryMediaAsset asset) => GetPublicationStatus(asset).Label;

    public PublicationStatus GetPublicationStatus(LibraryMediaAsset asset)
    {
        if (_uploadedHistoryByAsset.TryGetValue(
                asset.Id,
                out YouTubePublishHistoryEntry? uploaded))
        {
            if (HasReplacement(asset, uploaded))
                return new(PublicationStage.Ready, "Changed since upload", "Prepare", "Icon.Media",
                    "The local file changed after upload. The previous video remains in publishing history.");
            return PublicationStatus.FromHistory(uploaded, _observedAt);
        }

        return _failedHistoryAssetIds.Contains(asset.Id)
            ? new(PublicationStage.Attention, "Last upload failed", "Review and retry", "Icon.Warning", "Review the failure before trying again.")
            : _draftsByAsset.ContainsKey(asset.Id) ? PublicationStatus.Draft : PublicationStatus.Ready;
    }
}
