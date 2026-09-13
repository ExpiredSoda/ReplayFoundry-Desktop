using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class QwenSceneMomentEvidenceParser
{
    internal const string PolicyHash = "4cee9d7eff93babd20f2ef0c4ee6e3dd66f61e535630cb304859041c546a1773";
    internal static SceneMomentEvidence Parse(JsonElement row, VisualSemanticRequest request, JsonElement? preparedAudio = null)
    {
        var evidence = row.GetProperty("momentEvidence");
        if (evidence.GetProperty("version").GetString() != SceneMomentEvidence.Version ||
            evidence.GetProperty("policyHash").GetString() != PolicyHash ||
            !evidence.GetProperty("grounded").GetBoolean() && evidence.GetProperty("categories").EnumerateArray()
                .Any(item => item.GetProperty("verdict").GetString() != "Uncertain"))
            throw new InvalidDataException("Timed moment evidence did not pass its grounding check.");
        double duration = (request.CandidateEndRelative - request.CandidateStartRelative).TotalSeconds;
        double Number(JsonElement value, string key, double maximum = double.MaxValue, double minimum = 0)
        {
            double number = value.GetProperty(key).GetDouble();
            return double.IsFinite(number) && number >= minimum && number <= maximum ? number
                : throw new InvalidDataException("Audio or category timing is outside this review.");
        }
        var ids = new Dictionary<string, (double Start, double End)>(StringComparer.Ordinal);
        int frameIndex = 0;
        foreach (var frame in row.GetProperty("frameTimes").EnumerateArray())
        {
            double time = frame.GetDouble() - request.CandidateStartRelative.TotalSeconds;
            if (!double.IsFinite(time) || time < 0 || time >= duration) throw new InvalidDataException("Invalid frame citation time.");
            ids.Add("frame-" + frameIndex++, (time, time));
        }
        if (frameIndex != Qwen3VlSceneReviewProvider.FrameCount) throw new InvalidDataException("Missing review frame citations.");
        var audio = row.GetProperty("audioEvidence");
        string status = audio.GetProperty("status").GetString()!;
        if (audio.GetProperty("version").GetString() != "audio-evidence-1" || audio.GetProperty("similaritiesAreProbabilities").GetBoolean())
            throw new InvalidDataException("Unknown acoustic evidence contract.");
        var seen = new HashSet<int>();
        var creators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in audio.GetProperty("tracks").EnumerateArray())
        {
            int index = track.GetProperty("streamIndex").GetInt32();
            var owner = request.SceneContext?.AudioTracks.SingleOrDefault(track => track.StreamIndex == index);
            if (!seen.Add(index) || owner is null || track.GetProperty("role").GetString() != owner.Role.Role.ToString() ||
                track.GetProperty("roleSource").GetString() != owner.Role.Source.ToString())
                throw new InvalidDataException("Audio speaker provenance changed during review.");
            if (preparedAudio is { } prepared)
            {
                var expected = prepared.GetProperty("tracks").EnumerateArray().Single(item => item.GetProperty("streamIndex").GetInt32() == index);
                if (track.GetProperty("audioSha256").GetString() != expected.GetProperty("sha256").GetString())
                    throw new InvalidDataException("Reviewed audio bytes changed.");
            }
            int windowIndex = 0;
            foreach (var sound in track.GetProperty("windows").EnumerateArray())
            {
                string id = sound.GetProperty("id").GetString()!;
                double start = Number(sound, "start", duration), end = Number(sound, "end", duration);
                if (id != $"audio-{index}-{windowIndex++}" || windowIndex > 12 || end <= start)
                    throw new InvalidDataException("Invalid acoustic observation interval.");
                Number(sound, "rmsDb", 0, -160); Number(sound, "peakDb", 0, -160);
                foreach (var similarity in sound.GetProperty("similarities").EnumerateObject())
                    Number(sound.GetProperty("similarities"), similarity.Name, 1, -1);
                ids.Add(id, (start, end));
            }
            var returnedSpeech = track.GetProperty("speech").EnumerateArray().ToArray();
            if (returnedSpeech.Length != owner.Speech.Count) throw new InvalidDataException("Reviewed speech changed.");
            for (int speechIndex = 0; speechIndex < owner.Speech.Count; speechIndex++)
            {
                var speech = owner.Speech[speechIndex]; var returned = returnedSpeech[speechIndex];
                string id = $"speech-{index}-{speech.Id}";
                double start = Number(returned, "start", duration), end = Number(returned, "end", duration);
                if (returned.GetProperty("id").GetString() != id || returned.GetProperty("text").GetString() != speech.Text ||
                    Math.Abs(start - speech.ReviewRelativeStart.TotalSeconds) > .0001 ||
                    Math.Abs(end - speech.ReviewRelativeEnd.TotalSeconds) > .0001 || end <= start)
                    throw new InvalidDataException("Reviewed speech no longer matches the supplied transcript.");
                ids.Add(id, (start, end));
                if (owner.Role.Role == AudioContentRole.CreatorSpeech && owner.Role.Source == AudioContentRoleSource.UserConfirmed)
                    creators.Add(id);
            }
        }
        if (seen.Count != (request.SceneContext?.AudioTracks.Count ?? 0) ||
            seen.Count > 0 && status is not ("Analyzed" or "AcousticOnly") ||
            seen.Count == 0 && status != (request.SceneContext is null ? "Unavailable" : "NoAudio"))
            throw new InvalidDataException("Audio review omitted a selected stream or changed availability.");
        var categories = evidence.GetProperty("categories").EnumerateArray().Select(item =>
        {
            T Parse<T>(string key) where T : struct, Enum => Enum.TryParse<T>(item.GetProperty(key).GetString(), out var value) && Enum.IsDefined(value)
                ? value : throw new InvalidDataException("Unknown moment evidence label.");
            var references = item.GetProperty("evidenceIds").EnumerateArray().Select(value => value.GetString()!).ToArray();
            if (references.Any(id => !ids.ContainsKey(id))) throw new InvalidDataException("Moment evidence cites an unavailable observation.");
            var category = Parse<SceneMomentCategory>("category"); var verdict = Parse<SceneEvidenceVerdict>("verdict");
            double setup = Number(item, "setupStart", duration), payoff = Number(item, "payoffEnd", duration);
            if (verdict == SceneEvidenceVerdict.Supported && (references.Any(id => ids[id].Start < setup-.001 || ids[id].End > payoff+.001) ||
                category == SceneMomentCategory.Commentary && !references.Any(creators.Contains)))
                throw new InvalidDataException("Category citations do not establish its timing or speaker.");
            return new SceneCategoryEvidence(category, verdict,
                TimeSpan.FromSeconds(Number(item, "start", duration)), TimeSpan.FromSeconds(Number(item, "end", duration)),
                TimeSpan.FromSeconds(setup), TimeSpan.FromSeconds(payoff),
                item.GetProperty("explanation").GetString()!, references);
        }).ToArray();
        return new(categories, audio.GetProperty("status").GetString()!, audio.GetRawText(),
            request.CandidateEndRelative - request.CandidateStartRelative);
    }
}
