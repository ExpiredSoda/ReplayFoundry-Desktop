using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Intelligence;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Features.Generate.Editorial;

internal static class GenerationSceneEditorialEvidence
{
    internal const string EvidenceId = "scene-moment-context-1";
    internal static ClipEditorialEvidenceReference? Create(GenerationVisualSemanticCandidateObservation review)
        => Create(review.MomentEvidence);

    internal static ClipEditorialEvidenceReference? Create(SceneMomentEvidence? evidence)
    {
        if (evidence is null) return null;
        var supported = evidence.Categories.Where(row => row.Verdict == SceneEvidenceVerdict.Supported).ToArray();
        var cited = supported.SelectMany(row => row.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        using var audio = JsonDocument.Parse(evidence.AudioEvidenceJson);
        var speech = new List<object>();
        int remaining = 3000;
        foreach (var track in audio.RootElement.GetProperty("tracks").EnumerateArray())
            foreach (var span in track.GetProperty("speech").EnumerateArray())
            {
                string text = span.GetProperty("text").GetString()!;
                if (!cited.Contains(span.GetProperty("id").GetString()!) || text.Length > remaining || speech.Count >= 8) continue;
                speech.Add(new { id = span.GetProperty("id").GetString(), streamIndex = track.GetProperty("streamIndex").GetInt32(),
                    role = track.GetProperty("role").GetString(), roleSource = track.GetProperty("roleSource").GetString(),
                    start = span.GetProperty("start").GetDouble(), end = span.GetProperty("end").GetDouble(), text });
                remaining -= text.Length;
            }
        return new(EvidenceId, ClipEditorialEvidenceKind.ReviewedMomentContext,
            JsonSerializer.Serialize(new { schema = SceneMomentEvidence.Version, audioStatus = evidence.AudioStatus,
                categories = supported.Select(row => new { category = row.Category.ToString(), explanation = row.Explanation,
                    start = row.Start.TotalSeconds, end = row.End.TotalSeconds,
                    setupStart = row.SetupStart.TotalSeconds, payoffEnd = row.PayoffEnd.TotalSeconds }),
                sourceText = evidence.SourceText.Select(item => new { claim = item.Claim, text = item.Text, evidenceIds = item.EvidenceIds }),
                speech, attribution = "Source text retains model-checked frame/speech citations, not human confirmation. " +
                    "Names addressed in dialogue identify the recipient, not the speaker. " +
                    "Only UserConfirmed CreatorSpeech identifies creator routing. GameDialogue is game speech; " +
                    "MixedSpeech/Unknown do not identify the speaker. Speech recognition remains unreviewed text; " +
                    "do not invent quotations or physical events." }));
    }
}
