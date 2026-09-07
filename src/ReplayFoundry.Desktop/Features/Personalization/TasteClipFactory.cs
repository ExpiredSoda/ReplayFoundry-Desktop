using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Learning;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Transcription;

namespace ReplayFoundry.Desktop.Features.Personalization;

public static class TasteClipFactory
{
    private static readonly ConcurrentDictionary<string, string> SourceHashes = new(StringComparer.OrdinalIgnoreCase);
    public static TasteClip FromAsset(GenerationOutputAsset asset) => Build(asset.SourceMedia, asset.SourceStart, asset.SourceEnd,
        asset.PreferenceFeatures, asset.Score, AssetSpeech(asset), VisualText(asset.EditorialContext, asset.SourceStart, asset.SourceEnd),
        asset.PreferenceFeatures?.Context?.Game ?? asset.EditorialContext?.GameContext.GameName ?? "");
    public static TasteClip FromHidden(GenerationHiddenMoment moment) => Build(moment.SourceMedia, moment.SourceStart, moment.SourceEnd,
        moment.PreferenceFeatures, moment.FinalScore, ContextSpeech(moment.EditorialContext, moment.SourceStart, moment.SourceEnd),
        VisualText(moment.EditorialContext, moment.SourceStart, moment.SourceEnd), moment.PreferenceFeatures.Context?.Game ?? "");
    internal static TasteClip FromCandidate(GenerationSourceMomentResult source, MomentCandidate candidate,
        GenerationCandidateRefinement? refinement, GenerationMomentFindingResult moments, GenerationCandidateIntelligenceResult? intelligence)
    {
        var context = GenerationClipPreferenceFeatureExtractor.CreateContext(moments.Request.Setup, source.AnalyzedSource.PreparedSource.Media.FullPath);
        var vector = GenerationClipPreferenceFeatureExtractor.Create(candidate, refinement, context);
        var transcript = intelligence?.Transcripts?.Sources.Where(s => s.SourceFullPath.Equals(source.AnalyzedSource.PreparedSource.Media.FullPath,
            StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Segments).ToArray() ?? [];
        var speech = transcript.Where(s => s.AbsoluteSourceStart >= candidate.Window.Start && s.AbsoluteSourceEnd <= candidate.Window.End)
            .Select(s => new Speech(s.AbsoluteSourceStart, s.AbsoluteSourceEnd, s.Text)).ToArray();
        string visual = string.Join(" ", intelligence?.VisualSemantic?.Observations.Where(x => ReferenceEquals(x.Candidate, candidate))
            .SelectMany(x => x.Observation.ObservedChanges).Select(x => x.Description) ?? []);
        return Build(source.AnalyzedSource.PreparedSource.Media, candidate.Window.Start, candidate.Window.End,
            vector, candidate.HeuristicScore, speech, visual, context.Game);
    }
    private static TasteClip Build(MediaProbeResult media, TimeSpan start, TimeSpan end, ClipPreferenceFeatureVector? features,
        double baseline, Speech[] speech, string visual, string game)
    {
        double duration = (end - start).TotalSeconds;
        var numbers = new double[TasteClip.MeasurementCount];
        void Set(int index, double value) { numbers[index] = Math.Clamp(value, 0, 1); numbers[index + 24] = 1; }
        Set(0, Math.Log(1 + duration) / Math.Log(181));
        if (media.PrimaryVideoStream.PreferredFrameRate is double fps && fps > 0) Set(1, fps / 120);
        if (media.PrimaryVideoStream.Height > 0) Set(2, media.PrimaryVideoStream.Width / (double)media.PrimaryVideoStream.Height / 4);
        Set(3, start.TotalSeconds / Math.Max(1, media.Duration.TotalSeconds));
        if (speech.Length > 0)
        {
            var ordered = speech.OrderBy(x => x.Start).ToArray();
            Set(4, ordered.Sum(x => (x.End - x.Start).TotalSeconds) / duration);
            Set(5, ordered.Sum(x => x.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length) / duration / 6);
            Set(6, ordered.Zip(ordered.Skip(1), (a, b) => Math.Max(0, (b.Start - a.End).TotalSeconds)).Sum() / duration);
        }
        foreach (var feature in features?.Features ?? [])
        {
            // Measurements may describe the moment, but detector scores/rejections cannot become training labels or content shortcuts.
            if (feature.Code is ClipPreferenceFeatureCode.DeterministicScore or ClipPreferenceFeatureCode.VisualSemanticSupport or
                ClipPreferenceFeatureCode.VisualSemanticRejection) continue;
            Set(8 + (int)feature.Code, feature.NormalizedValue);
        }
        string group = SourceGroup(media);
        string id = Hash(group + "|" + start.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + end.Ticks.ToString(CultureInfo.InvariantCulture));
        string content = Bound(string.Join(" ", speech.Select(x => x.Text).Append(visual)), 8192);
        string context = Bound(string.Join(". ", new[] { game, features?.Context?.OutputKind, features?.Context?.Emphasis, features?.Context?.Intent }
            .Where(x => !string.IsNullOrWhiteSpace(x))), 2048);
        var clip = new TasteClip(id, group, content, context, numbers, Math.Clamp(baseline, 0, 100)); clip.Validate(); return clip;
    }
    private static Speech[] AssetSpeech(GenerationOutputAsset asset) => asset.Captions is { } captions
        ? captions.Segments.Where(s => s.AbsoluteSourceStart >= asset.SourceStart && s.AbsoluteSourceEnd <= asset.SourceEnd)
            .Select(s => new Speech(s.AbsoluteSourceStart, s.AbsoluteSourceEnd, s.Text)).ToArray()
        : ContextSpeech(asset.EditorialContext, asset.SourceStart, asset.SourceEnd);
    private static Speech[] ContextSpeech(ClipEditorialContext? context, TimeSpan start, TimeSpan end) => context?.Transcripts.SelectMany(t => t.Spans)
        .Where(s => s.SourceStart >= start && s.SourceEnd <= end).Select(s => new Speech(s.SourceStart, s.SourceEnd, s.Text)).ToArray() ?? [];
    private static string VisualText(ClipEditorialContext? context, TimeSpan start, TimeSpan end) =>
        context?.SourceStart == start && context.SourceEnd == end
            ? string.Join(" ", context.Evidence.Where(e => e.Kind == ClipEditorialEvidenceKind.VisualObservation).Select(e => e.Description)) : "";
    private static string SourceGroup(MediaProbeResult media)
    {
        var file = new FileInfo(media.FullPath);
        if (!file.Exists) return Hash(media.FullPath.ToUpperInvariant() + "|" + media.Duration.Ticks.ToString(CultureInfo.InvariantCulture));
        string cacheKey = media.FullPath + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
        if (SourceHashes.TryGetValue(cacheKey, out var group)) return group;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(file.Length.ToString(CultureInfo.InvariantCulture)));
        using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var buffer = new byte[65536]; int count = stream.Read(buffer); hash.AppendData(buffer, 0, count);
            stream.Position = Math.Max(0, stream.Length - buffer.Length); count = stream.Read(buffer); hash.AppendData(buffer, 0, count);
        }
        group = Convert.ToHexString(hash.GetHashAndReset());
        if (SourceHashes.Count > 512) SourceHashes.Clear();
        SourceHashes[cacheKey] = group; return group;
    }
    private static string Bound(string value, int limit) => value.Length <= limit ? value : value[..limit];
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private sealed record Speech(TimeSpan Start, TimeSpan End, string Text);
}
