using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.PreparationTests;

internal static partial class YouTubeAnalyticsTests
{
    private static YouTubeVideoObservation SavedObservation() => new("video-a", "asset-a", "Saved title",
        new(2026, 8, 1), new(2026, 8, 31), 0, null, null, 125, null, null,
        new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));

    private static Task SavedObservationsAreValidated()
    {
        YouTubeVideoObservation valid = SavedObservation();
        YouTubeVideoObservation newer = valid with { PublishedTitle = "Fresh title", RetrievedAtUtc = valid.RetrievedAtUtc.AddHours(1) };
        JsonObject malformedProvenance = JsonSerializer.SerializeToNode(valid with
            { Provenance = Snapshot(TestMediaFactory.CreateSourcePath("saved-analytics.mkv")) })!.AsObject();
        malformedProvenance["Provenance"]!["SourceEnd"] = "00:00:00";
        JsonNode?[] rows =
        [
            null, new JsonObject(), malformedProvenance,
            JsonSerializer.SerializeToNode(valid with { VideoId = "video a" }),
            JsonSerializer.SerializeToNode(valid with { AssetId = " " }),
            JsonSerializer.SerializeToNode(valid with { PublishedTitle = null! }),
            JsonSerializer.SerializeToNode(valid with { From = default }),
            JsonSerializer.SerializeToNode(valid with { From = new(2026, 9, 1) }),
            JsonSerializer.SerializeToNode(valid with { From = new(2025, 1, 1) }),
            JsonSerializer.SerializeToNode(valid with { To = new(2026, 9, 5) }),
            JsonSerializer.SerializeToNode(valid with { Views = -1 }),
            JsonSerializer.SerializeToNode(valid),
            JsonSerializer.SerializeToNode(newer),
            JsonSerializer.SerializeToNode(newer with { PublishedTitle = "Later row with equal freshness" }),
            JsonSerializer.SerializeToNode(valid with { VideoId = "Video-a", AssetId = "another-asset" }),
        ];
        YouTubeSavedObservations.LoadResult loaded = YouTubeSavedObservations.Parse(new JsonArray(rows).ToJsonString());
        TestAssert.Equal(2, loaded.Observations.Count, "Malformed rows must be excluded without losing valid legacy rows with unknown provenance.");
        TestAssert.Equal(rows.Length - 2, loaded.DiscardedCount, "Recovery must account for invalid and duplicate records.");
        TestAssert.Equal("Fresh title", loaded.Observations[0].PublishedTitle,
            "Newest retrieval wins; equal timestamps preserve the first row deterministically.");
        TestAssert.Equal("Video-a", loaded.Observations[1].VideoId, "YouTube video identities are case sensitive.");
        TestAssert.True(loaded.Observations[0].Provenance is null && loaded.Observations[0].Views == 0 &&
            loaded.Observations[0].AverageViewPercent == 125,
            "Unknown provenance, measured zero, and repeat-view retention above 100 percent remain valid.");
        string sparse = "[" + string.Join(',', Enumerable.Repeat("null", 100)) + "," + JsonSerializer.Serialize(valid) + "]";
        TestAssert.Equal(1, YouTubeSavedObservations.Parse(sparse).Observations.Count,
            "Invalid rows must not consume the retained-observation limit.");
        return Task.CompletedTask;
    }

    private static Task NullSavedObservationsRemainRecoverable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ReplayFoundry-AnalyticsRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "observations.json");
        try
        {
            File.WriteAllText(path, "[null,{}]");
            using var model = new YouTubeAnalyticsViewModel(new UnusedSavedAnalyticsService(), () => [], path);
            TestAssert.Equal(0, model.Observations.Count, "Null saved rows must not reach the bound observation getter.");
            TestAssert.Equal(1, model.ReferenceChoices.Count, "The reference selector must remain usable with only its all-observations choice.");
            TestAssert.True(model.Status.Contains("Skipped 2", StringComparison.Ordinal), "The UI must explain recovery rather than silently hiding corruption.");
            TestAssert.True(model.RefreshCommand.CanExecute(null), "A malformed saved cache must not prevent a new refresh.");
            model.ComparisonFilter = "anything";
            TestAssert.Equal(0, model.Observations.Count, "Filtering recovered observations must remain safe.");
            model.ClearCommand.Execute(null);
            TestAssert.True(!File.Exists(path), "The user must still be able to clear the malformed local cache.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
        return Task.CompletedTask;
    }

    private static Task CaptionLookGroupsRemainIdentifiable()
    {
        YouTubePublishProvenance first = Snapshot(TestMediaFactory.CreateSourcePath("caption-cohort.mkv"));
        var look = first.CaptionLook!;
        var changed = new StudioCaptionLook(look.CaptionStyle, look.CaptionVerticalPositionPercent,
            look.CaptionWordLimit, look.CaptionMaximumWidthPercent, look.CaptionFontScalePercent,
            new StudioCaptionTypography("Arial", "#FFFF00", accentColor: "#00FF00", safeArea: StudioCaptionSafeArea.TikTok));
        var second = new YouTubePublishProvenance(first.SourceFullPath, first.SourceStart, first.SourceEnd,
            first.GameName, first.Canvas, first.OutputWidth, first.OutputHeight, first.CompositionLayout, true, changed);
        YouTubeVideoObservation reference = SavedObservation() with { Provenance = first };
        YouTubeVideoObservation peer = reference with { VideoId = "video-b", Provenance = second, Views = 100 };
        TestAssert.True(reference.CaptionLookIdentifier != peer.CaptionLookIdentifier && reference.CaptionLookSummary != peer.CaptionLookSummary,
            "An accent-only change formerly produced identical labels; full look groups must now remain distinguishable.");
        string summary = YouTubeObservationCohorts.Describe([reference, peer]);
        TestAssert.True(summary.Contains(reference.CaptionLookIdentifier, StringComparison.Ordinal) &&
            summary.Contains(peer.CaptionLookIdentifier, StringComparison.Ordinal),
            "Each cohort's median must carry the same look identifier shown on its observations.");
        TestAssert.True(peer.CaptionLookDetails.Contains("accent #00FF00", StringComparison.Ordinal) &&
            peer.ComparisonText.Contains("accent #00FF00", StringComparison.Ordinal),
            "The distinguishing setting must be readable in the tooltip and searchable in the comparison filter.");
        YouTubeVideoObservation restored = JsonSerializer.Deserialize<YouTubeVideoObservation>(JsonSerializer.Serialize(peer))!;
        TestAssert.Equal(peer.CaptionLookIdentifier, restored.CaptionLookIdentifier, "The same saved look must retain its identifier after reload.");
        TestAssert.Equal(peer.CaptionLookDetails, restored.CaptionLookDetails, "The complete readable look must survive reload.");
        return Task.CompletedTask;
    }

    private sealed class UnusedSavedAnalyticsService : IYouTubeAnalyticsService
    {
        public Task ConnectAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected analytics connection.");
        public Task DisconnectAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected analytics disconnection.");
        public Task<IReadOnlyList<YouTubeVideoObservation>> ReadAsync(IReadOnlyList<YouTubePublishHistoryEntry> history,
            DateOnly from, DateOnly to, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected analytics network request.");
    }
}
