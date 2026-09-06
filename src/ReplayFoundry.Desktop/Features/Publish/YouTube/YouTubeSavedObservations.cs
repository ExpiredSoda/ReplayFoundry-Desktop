using System.Text.Json;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

internal static class YouTubeSavedObservations
{
    internal sealed record LoadResult(IReadOnlyList<YouTubeVideoObservation> Observations, int DiscardedCount);

    public static LoadResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Saved observations must be an array.");
        var valid = new List<YouTubeVideoObservation>();
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            try
            {
                if (row.Deserialize<YouTubeVideoObservation>() is { } observation && IsValid(observation))
                    valid.Add(observation);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                // A malformed row must not hide other valid saved observations.
            }
        }
        // The service saves one report per video. Keep the freshest saved report;
        // stable ordering keeps the first row when duplicate timestamps are equal.
        YouTubeVideoObservation[] observations = valid.OrderByDescending(item => item.RetrievedAtUtc)
            .DistinctBy(item => item.VideoId, StringComparer.Ordinal).Take(100).ToArray();
        return new(observations, document.RootElement.GetArrayLength() - observations.Length);
    }

    private static bool IsValid(YouTubeVideoObservation item) =>
        Identity(item.VideoId, 128) && item.VideoId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') &&
        Identity(item.AssetId, 256) && !string.IsNullOrWhiteSpace(item.PublishedTitle) && item.PublishedTitle.Length <= 1000 &&
        item.From != default && item.From <= item.To && item.To.DayNumber - item.From.DayNumber <= 366 &&
        item.RetrievedAtUtc != default && item.To <= DateOnly.FromDateTime(item.RetrievedAtUtc.UtcDateTime) &&
        Metric(item.Views) && Metric(item.EngagedViews) && Metric(item.AverageViewSeconds) &&
        Metric(item.AverageViewPercent) && Metric(item.Shares) && Metric(item.SubscribersGained);

    private static bool Identity(string? value, int maximumLength) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Metric(double? value) => value is null || double.IsFinite(value.Value) && value.Value >= 0;
}
