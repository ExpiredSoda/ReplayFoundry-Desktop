using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Features.Generate.Handoff;

public enum MontageStyle { Impact, Banter, Tension }

public sealed record MontageBeat(string AssetId, int Order, string Recording, TimeSpan SourceStart,
    TimeSpan SourceEnd, bool StartsNewScene, string Connection);

/// <summary>An editable source-timed sequence. Similarity alone never establishes continuity.</summary>
public static class MontageSequencePlanner
{
    public static string Describe(MontageStyle style) => style switch
    {
        MontageStyle.Impact => "Lead with a strong encounter; keep actions within each encounter in order. Short cuts, crisp audio joins.",
        MontageStyle.Banter => "Keep exchanges in recording order with room for setup, punchline and response. Gentle audio joins.",
        _ => "Keep discoveries in recording order and preserve the quiet before a reveal. Softer audio joins.",
    };
    public static double SuggestedSeconds(MontageStyle style) => style switch
    { MontageStyle.Impact => 12, MontageStyle.Banter => 30, _ => 40 };
    public static double AudioEdgeSeconds(MontageStyle style) => style switch
    { MontageStyle.Impact => .005, MontageStyle.Banter => .012, _ => .035 };
    public static double VideoEdgeSeconds(MontageStyle style) => style == MontageStyle.Tension ? .12 : 0;

    public static IReadOnlyList<GenerationOutputAsset> Arrange(IReadOnlyList<GenerationOutputAsset> assets, MontageStyle style)
    {
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
        var scenes = new List<List<GenerationOutputAsset>>();
        foreach (var recording in assets.GroupBy(asset => asset.SourceFullPath, StringComparer.OrdinalIgnoreCase))
        {
            List<GenerationOutputAsset>? scene = null;
            GenerationOutputAsset? previous = null;
            foreach (var asset in recording.OrderBy(asset => asset.SourceStart))
            {
                if (previous is null || asset.SourceStart - previous.SourceEnd > TimeSpan.FromSeconds(45))
                { scene = []; scenes.Add(scene); }
                scene!.Add(asset); previous = asset;
            }
        }
        // Never reorder responses within one nearby encounter, even for Impact.
        IEnumerable<List<GenerationOutputAsset>> ordered = style == MontageStyle.Impact
            ? scenes.OrderByDescending(scene => scene.Max(asset => asset.Score)) : scenes;
        return ordered.SelectMany(scene => scene).Select((asset, index) => asset.WithTimelineRank(index + 1)).ToArray();
    }

    public static IReadOnlyList<MontageBeat> DescribeSequence(IReadOnlyList<GenerationOutputAsset> assets)
    {
        var result = new List<MontageBeat>();
        GenerationOutputAsset? previous = null;
        foreach (var asset in assets.Where(asset => asset.IsIncludedInFinalRender))
        {
            bool continuous = previous is not null && previous.SourceFullPath.Equals(asset.SourceFullPath, StringComparison.OrdinalIgnoreCase)
                && asset.SourceStart >= previous.SourceEnd && asset.SourceStart - previous.SourceEnd <= TimeSpan.FromMilliseconds(100);
            result.Add(new(asset.Id, result.Count + 1, Path.GetFileName(asset.SourceFullPath), asset.SourceStart, asset.SourceEnd,
                !continuous, previous is null ? "Opening scene" : continuous ? "Continuous source" : "Separate cut · no continuous encounter implied"));
            previous = asset;
        }
        return result;
    }

    public static string Fingerprint(IEnumerable<GenerationOutputAsset> assets, MontageStyle style)
    {
        string canonical = JsonSerializer.Serialize(new { style, beats = assets.Where(asset => asset.IsIncludedInFinalRender).Select(asset => new
        {
            asset.Id, source = asset.SourceFullPath.ToUpperInvariant(), asset.SourceStart, asset.SourceEnd,
            context = asset.EditorialContext is null ? null : Features.Studio.Editorial.StudioEditorialContextRevision.CreateDurable(asset.CreateCurrentCutEditorialContext()),
            asset.RenderSettings,
        }) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed partial class GenerationOutputProject
{
    public MontageStyle MontageStyle { get; }
    public ClipEditorialMetadataDraft? MontageMetadata { get; }
    public string? MontageMetadataFingerprint { get; }
    public string MontageFingerprint => MontageSequencePlanner.Fingerprint(Assets, MontageStyle);
    public bool IsMontageMetadataCurrent => MontageMetadata is not null && MontageMetadataFingerprint == MontageFingerprint;
    public IReadOnlyList<MontageBeat> MontageBeats => MontageSequencePlanner.DescribeSequence(Assets);
    internal GenerationOutputProject ArrangeMontage(MontageStyle style) =>
        new(Id, GenerationMode.Montage, OutputDirectory, RequestedCount, FulfillmentPreference, FulfillmentOutcome,
            MontageSequencePlanner.Arrange(Assets, style), CreatedAtUtc, resultCountMode: ResultCountMode,
            hiddenMoments: HiddenMoments, candidateSetFingerprint: CandidateSetFingerprint, sourceMedia: SourceMedia,
            montageStyle: style);
    public GenerationOutputProject WithMontageMetadata(ClipEditorialMetadataDraft metadata, string fingerprint)
    {
        if (IsFinalized || fingerprint != MontageFingerprint)
            throw new InvalidOperationException("The montage changed while its wording was being prepared. Your newer sequence was kept.");
        return new(Id, Mode, OutputDirectory, RequestedCount, FulfillmentPreference, FulfillmentOutcome,
            Assets, CreatedAtUtc, resultCountMode: ResultCountMode, hiddenMoments: HiddenMoments,
            candidateSetFingerprint: CandidateSetFingerprint, sourceMedia: SourceMedia, montageStyle: MontageStyle,
            montageMetadata: metadata, montageMetadataFingerprint: fingerprint);
    }
}
