using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.Desktop.Platform.VisualSemantic;

internal static class QwenSceneCopyAlternatives
{
    internal static IReadOnlyList<ClipEditorialAlternative> Read(JsonElement row, ClipEditorialMetadataRequest request)
    {
        if (!row.TryGetProperty("alternatives", out var alternatives)) return [];
        if (alternatives.ValueKind != JsonValueKind.Array || alternatives.GetArrayLength() > 3)
            throw new InvalidDataException("Invalid writing alternatives.");
        List<ClipEditorialAlternative> result = [];
        foreach (var choice in alternatives.EnumerateArray())
        {
            foreach (string key in request.RequiresNoveltyReview
                ? new[] { "neuralGrounding", "neuralQuality", "neuralNovelty" }
                : new[] { "neuralGrounding", "neuralQuality" })
            {
                var judgment = choice.GetProperty(key);
                double value = judgment.GetProperty("value").GetDouble();
                Qwen3VlSceneReviewProvider.ValidateNeuralValue(judgment, value * 100, "copy-judgment-2");
                if (value <= .5 || judgment.GetProperty("margins").EnumerateArray().Any(item => item.GetDouble() <= 0))
                    throw new InvalidDataException("An alternative failed its source or writing checks.");
            }
            string body = choice.GetProperty("copy").GetProperty("titleBody").GetString()!.Trim();
            string description = choice.GetProperty("copy").GetProperty("description").GetString()!.Trim();
            Qwen3VlSceneCopyGenerator.ValidateLocks(body, description, request);
            string title = request.Writing?.KeepTitle == true ? request.Writing.CurrentTitle : body + " " + request.Context.GameContext.AudienceGameHashtag;
            if (body.Length == 0 || request.Writing?.KeepTitle != true && body.Contains('#') || title.Length > 100 || description.Length < 1 ||
                description.Length > (request.Writing?.KeepDescription == true ? 5000 : 420) ||
                request.RequiresNewAngle && request.PriorAcceptedTitleExclusions.Any(prior => prior.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("An alternative is repetitive or out of bounds.");
            description = Qwen3VlSceneCopyGenerator.FinishDescription(description, request);
            string[] tags = new[] { request.Context.GameContext.AudienceGameHashtag.TrimStart('#') }
                .Concat(request.Profile.DefaultTags).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
            result.Add(new(title, description, tags, choice.GetProperty("angle").GetString()!,
                ClipEditorialAlternative.ProfileKey(request.Profile, request.Tone), choice.GetProperty("factHash").GetString()!));
        }
        return result;
    }
}
