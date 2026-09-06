using System.Text.Json.Serialization;

namespace ReplayFoundry.Desktop.Features.Publish.YouTube;

public sealed record YouTubeVideoObservation(
    string VideoId, string AssetId, string PublishedTitle, DateOnly From, DateOnly To,
    double? Views, double? EngagedViews, double? AverageViewSeconds, double? AverageViewPercent,
    double? Shares, double? SubscribersGained, DateTimeOffset RetrievedAtUtc,
    YouTubePublishProvenance? Provenance = null, DateTimeOffset? UploadedAtUtc = null,
    DateTimeOffset? ScheduledForUtc = null)
{
    [JsonIgnore]
    public string Summary => $"Views: {Metric(Views, "N0")} · Engaged: {Metric(EngagedViews, "N0")} · " +
        $"Average: {Metric(AverageViewSeconds, "N1", "s")} · Watched: {Metric(AverageViewPercent, "N1", "%")} · " +
        $"Shares: {Metric(Shares, "N0")} · Subscribers gained: {Metric(SubscribersGained, "N0")}";
    [JsonIgnore]
    public string ProvenanceSummary => Provenance is not { } snapshot ? "Source and style were not recorded for this upload." :
        $"{string.Join(" / ", Cuts.Select(cut => cut.GameName ?? "Game unknown").Distinct())} · " +
        $"{snapshot.OutputWidth} × {snapshot.OutputHeight} ({snapshot.Canvas}) · " +
        (snapshot.OutputDuration is { } duration ? $"{duration.TotalSeconds:0.##}s video · " : "") +
        $"{Cuts.Count} source cut(s)";
    [JsonIgnore]
    public string CaptionLookSummary => Provenance is null ? "Caption look unknown" : $"Look {CaptionLookIdentifier} · " + string.Join(" / ", Cuts.Select(cut =>
        cut.CaptionLook is { } look ? $"{look.CaptionStyle}, {look.CaptionTypography.FontFamily}, {look.CaptionTypography.TextColor}, " +
            $"{look.CaptionFontScalePercent:0}% size, {look.CaptionTypography.SafeArea} safe area" : "Captions off").Distinct());
    [JsonIgnore]
    public string CaptionLookIdentifier => YouTubeCaptionLookPresentation.Identifier(Provenance);
    [JsonIgnore]
    public string CaptionLookDetails => YouTubeCaptionLookPresentation.Details(Provenance);
    [JsonIgnore]
    public string SourceCutSummary => string.Join("; ", Cuts.Select(cut =>
        $"{System.IO.Path.GetFileName(cut.SourceFullPath)} {cut.SourceStart:hh\\:mm\\:ss\\.ff}–{cut.SourceEnd:hh\\:mm\\:ss\\.ff} ({cut.CompositionLayout})"));
    [JsonIgnore]
    public string AgeSummary => UploadedAtUtc is not { } uploaded ? "Upload time unknown" :
        $"Uploaded {uploaded:yyyy-MM-dd} UTC · {Math.Max(0, (RetrievedAtUtc - uploaded).TotalDays):0.0} days before refresh" +
        (ScheduledForUtc is { } scheduled ? $" · Scheduled release {scheduled:yyyy-MM-dd HH:mm} UTC" : "");
    [JsonIgnore]
    public string ReportSummary => $"Report {From:yyyy-MM-dd}–{To:yyyy-MM-dd} · Refreshed {RetrievedAtUtc:yyyy-MM-dd HH:mm} UTC · Channel {Provenance?.ChannelId ?? "unknown"}";
    [JsonIgnore]
    public string ComparisonText => PublishedTitle + " " + ProvenanceSummary + " " + CaptionLookSummary + " " + CaptionLookDetails + " " + SourceCutSummary + " " + ReportSummary;
    private IReadOnlyList<YouTubePublishProvenance> Cuts => Provenance is null ? [] :
        Provenance.ContributingCuts.Count > 0 ? Provenance.ContributingCuts : [Provenance];
    private static string Metric(double? value, string format, string unit = "") => value is { } number
        ? number.ToString(format, System.Globalization.CultureInfo.CurrentCulture) + unit : "unavailable";
}

public interface IYouTubeAnalyticsService
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<YouTubeVideoObservation>> ReadAsync(
        IReadOnlyList<YouTubePublishHistoryEntry> history, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface IYouTubeAnalyticsSource
{
    IYouTubeAnalyticsService? Analytics { get; }
}
