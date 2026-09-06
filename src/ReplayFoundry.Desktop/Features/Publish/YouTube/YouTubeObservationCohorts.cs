using System.Globalization;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

/// <summary>Descriptive comparisons only. Unknown matching attributes never count as a match.</summary>
internal static class YouTubeObservationCohorts
{
    public static IReadOnlyList<YouTubeVideoObservation> Match(IEnumerable<YouTubeVideoObservation> observations,
        YouTubeVideoObservation reference, bool game, bool format, bool channel, bool age, bool duration)
    {
        return observations.Where(item => item.From == reference.From && item.To == reference.To &&
            (!game || KnownEqual(Game(item), Game(reference))) &&
            (!format || item.Provenance is { } frame && reference.Provenance is { } target &&
                frame.OutputWidth == target.OutputWidth && frame.OutputHeight == target.OutputHeight) &&
            (!channel || KnownEqual(item.Provenance?.ChannelId, reference.Provenance?.ChannelId)) &&
            (!age || AgeBucket(item) is { } bucket && bucket == AgeBucket(reference)) &&
            (!duration || item.Provenance?.OutputDuration is { } length && reference.Provenance?.OutputDuration is { } targetLength &&
                Math.Abs(length.TotalSeconds - targetLength.TotalSeconds) <= Math.Max(3, targetLength.TotalSeconds * .2))).ToArray();
    }

    public static string Describe(IReadOnlyList<YouTubeVideoObservation> observations)
    {
        if (observations.Count == 0) return "No clips match these controls. Unknown matching attributes are excluded; loosen a control to inspect them.";
        string[] groups = observations.GroupBy(item => YouTubeCaptionLookPresentation.Key(item.Provenance))
            .Select(group => $"{group.First().CaptionLookSummary}: {group.Count()} clip(s); " +
                $"median views {Median(group.Select(item => item.Views), "N0")}; " +
                $"median watched {Median(group.Select(item => item.AverageViewPercent), "N1", "%")}").ToArray();
        return $"{observations.Count} comparable observation(s). Medians give each video equal weight; unavailable metrics are excluded. " +
            "Upload age is not public release age. These comparisons do not establish a cause.\n" + string.Join("\n", groups);
    }

    internal static int? AgeBucket(YouTubeVideoObservation item)
    {
        if (item.UploadedAtUtc is not { } uploaded) return null;
        double days = (item.To.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc) - uploaded.UtcDateTime).TotalDays;
        return days < 0 ? null : days < 8 ? 0 : days < 31 ? 1 : days < 91 ? 2 : 3;
    }
    private static string? Game(YouTubeVideoObservation item)
    {
        if (item.Provenance is not { } snapshot) return null;
        var cuts = snapshot.ContributingCuts.Count > 0 ? snapshot.ContributingCuts : [snapshot];
        if (cuts.Any(cut => string.IsNullOrWhiteSpace(cut.GameName))) return null;
        return string.Join("|", cuts.Select(cut => cut.GameName!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase));
    }
    private static bool KnownEqual(string? left, string? right) => !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Median(IEnumerable<double?> values, string format, string unit = "")
    {
        double[] known = values.Where(value => value is { } number && double.IsFinite(number)).Select(value => value!.Value).Order().ToArray();
        if (known.Length == 0) return "unavailable (n=0)";
        double median = known.Length % 2 == 0 ? (known[known.Length / 2 - 1] + known[known.Length / 2]) / 2 : known[known.Length / 2];
        return median.ToString(format, CultureInfo.CurrentCulture) + unit + $" (n={known.Length})";
    }
}
