using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Platform.Storage;

/// <summary>Actual sequence outcomes; an export is not a factual or wording approval.</summary>
public sealed class JsonMontageLearningStore(string? root = null, Func<bool>? enabled = null)
{
    private static readonly object Gate = new();
    private readonly string _root = ReplayFoundryLocalDataPaths.Resolve(root, "Personalization/Writer/sequences");
    private bool IsEnabled => enabled?.Invoke() ?? new JsonEditorialMetadataPreferenceLearningConsentStore().Current is
        { IsEnabled: true, NoticeVersion: EditorialMetadataPreferenceLearningConsentSnapshot.CurrentNoticeVersion };

    public bool Record(string outputId, string action, YouTubePublishProvenance? provenance)
    {
        if (!IsEnabled || provenance?.ContributingCuts.Count is not (>= 2 and <= 300)) return false;
        if (action is not ("Rendered" or "Published") || outputId.Length is < 1 or > 200)
            throw new ArgumentException("Unknown montage learning outcome.");
        string json = JsonSerializer.Serialize(new
        {
            schema = "foundry-montage-sequence-1", outputId, action,
            wordingApproved = false, sequenceQualityReviewed = false,
            cuts = provenance.ContributingCuts.Select((cut, index) => new
            {
                order = index, sourcePath = cut.SourceFullPath, startSeconds = cut.SourceStart.TotalSeconds,
                endSeconds = cut.SourceEnd.TotalSeconds, game = cut.GameName,
                sourceLength = File.Exists(cut.SourceFullPath) ? new FileInfo(cut.SourceFullPath).Length : (long?)null,
                sourceModifiedUtcTicks = File.Exists(cut.SourceFullPath) ? File.GetLastWriteTimeUtc(cut.SourceFullPath).Ticks : (long?)null,
            }),
        });
        if (Encoding.UTF8.GetByteCount(json) > 262144) return false;
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        lock (Gate)
        {
            if (!IsEnabled) return false;
            Directory.CreateDirectory(_root);
            string path = Path.Combine(_root, id + ".json");
            if (File.Exists(path)) return false;
            string temporary = path + ".tmp";
            try { File.WriteAllText(temporary, json, new UTF8Encoding(false)); File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            foreach (var file in new DirectoryInfo(_root).EnumerateFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(2048))
                file.Delete();
            return true;
        }
    }
}
