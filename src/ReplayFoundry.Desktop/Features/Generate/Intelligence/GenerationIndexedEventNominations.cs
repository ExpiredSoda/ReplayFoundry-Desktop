using System.Text.Json;

namespace ReplayFoundry.Desktop.Features.Generate.Intelligence;

internal static class GenerationIndexedEventNominations
{
    internal static IReadOnlyList<GenerationTimedExplorationSeed> Create(IReadOnlyList<JsonElement> windows)
    {
        var seeds = new List<(double Value, GenerationTimedExplorationSeed Seed)>();
        foreach (var window in windows)
        {
            var prediction = window.GetProperty("prediction");
            if (!prediction.TryGetProperty("eventStartFrame", out var first) || !prediction.TryGetProperty("eventEndFrame", out var last) ||
                !first.TryGetInt32(out int startIndex) || !last.TryGetInt32(out int endIndex)) continue;
            double left = window.GetProperty("start").GetDouble(), right = window.GetProperty("end").GetDouble();
            if (!double.IsFinite(left) || !double.IsFinite(right) || left < 0 || right <= left ||
                startIndex < 0 || endIndex < startIndex || endIndex >= Math.Ceiling((right-left)/5)) continue;
            var seed = new GenerationTimedExplorationSeed(TimeSpan.FromSeconds(left+startIndex*5),
                TimeSpan.FromSeconds(Math.Min(right,left+(endIndex+1)*5)),
                "The recording map nominated a bounded visual event with its surrounding context for close review; this is not a verified highlight.");
            double value = prediction.GetProperty("editorialValue").GetDouble();
            if (double.IsFinite(value)) seeds.Add((value, seed));
        }
        return seeds.OrderByDescending(item => item.Value).Take(16).Select(item => item.Seed).ToArray();
    }
}
