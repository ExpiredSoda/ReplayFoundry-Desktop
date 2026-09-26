using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.YouTube;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static partial class YouTubeAnalyticsTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Analytics maps named columns and keeps missing reports distinct from zero", MapsMetricsByName),
        new("Analytics does not access credentials or network while connection is disabled", HonorsPermission),
        new("Analytics authorization uses a separate read-only scope", UsesReadOnlyScope),
        new("Disconnecting analytics locally does not revoke publishing access", AnalyticsDisconnectIsLocal),
        new("Publish provenance survives Library and history reloads while legacy uploads remain unknown", ProvenancePersists),
        new("Analytics links the submitted cut and style with explicit missing metrics and upload age", ObservationsKeepPublishSnapshot),
        new("Analytics cohorts require known matching context and report metric sample sizes", MatchesComparableObservations),
        new("Saved analytics rejects malformed rows and deterministically keeps the freshest video observation", SavedObservationsAreValidated),
        new("An analytics cache containing null rows remains recoverable in the view model", NullSavedObservationsRemainRecoverable),
        new("Full caption looks remain distinguishable and stable across analytics persistence", CaptionLookGroupsRemainIdentifiable),
    ];

    private static YouTubePublishHistoryEntry Entry(string id) => new("history-" + id, "asset-" + id,
        "Published " + id, id, "https://youtu.be/" + id, YouTubePublishOutcome.Published,
        YouTubeVideoVisibility.Public, DateTimeOffset.UnixEpoch, null);

    private static Task MatchesComparableObservations()
    {
        var provenance = Snapshot(TestMediaFactory.CreateSourcePath("cohort.mkv")).WithPublishedContext(TimeSpan.FromSeconds(12), "channel-1");
        var reference = new YouTubeVideoObservation("a", "a", "Reference", new(2026, 8, 1), new(2026, 8, 31),
            0, null, null, null, null, null, new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero), provenance,
            new(2026, 8, 28, 0, 0, 0, TimeSpan.Zero));
        var peer = reference with { VideoId = "b", Views = 100, AverageViewPercent = 125 };
        var unknown = reference with { VideoId = "c", Provenance = null };
        var otherPeriod = reference with { VideoId = "d", To = new(2026, 9, 1) };
        var older = reference with { VideoId = "e", UploadedAtUtc = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) };
        var otherChannel = reference with { VideoId = "f", Provenance = provenance.WithPublishedContext(TimeSpan.FromSeconds(12), "channel-2") };
        var cohort = YouTubeObservationCohorts.Match([reference, peer, unknown, otherPeriod, older, otherChannel], reference, true, true, true, true, true);
        TestAssert.Equal(2, cohort.Count, "Only same-period known peers in the same upload-age band are comparable.");
        string description = YouTubeObservationCohorts.Describe(cohort);
        TestAssert.True(description.Contains("n=2", StringComparison.Ordinal) && description.Contains("n=1", StringComparison.Ordinal) &&
            description.Contains("125", StringComparison.Ordinal), "Zero is measured, missing retention is excluded, and replay above 100% is retained.");
        TestAssert.Equal(0, YouTubeObservationCohorts.Match([unknown], unknown, true, true, true, true, true).Count,
            "Two unknown identities cannot masquerade as a matched cohort.");
        TestAssert.True(reference.ReportSummary.Contains("2026-08-31", StringComparison.Ordinal), "Saved results carry their own period.");
        return Task.CompletedTask;
    }

    private static Task MapsMetricsByName()
    {
        using var json = JsonDocument.Parse("""
            {"columnHeaders":[{"name":"averageViewPercentage"},{"name":"video"},{"name":"views"}],"rows":[[125.5,"video-a",0]]}
            """);
        var rows = YouTubeAnalyticsService.Parse(json.RootElement, [Entry("video-a"), Entry("video-b")],
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), DateTimeOffset.UnixEpoch);
        TestAssert.Equal("asset-video-a", rows[0].AssetId, "Observations must retain the exact published artifact identity.");
        TestAssert.Equal(125.5, rows[0].AverageViewPercent!.Value, "Repeat viewing may produce retention above 100 percent.");
        TestAssert.Equal(0d, rows[0].Views!.Value, "Measured zero is valid.");
        TestAssert.True(rows[1].Views is null, "Missing data must not be invented as zero views.");
        TestAssert.True(rows[0].EngagedViews is null, "An absent metric must stay unknown.");
        TestAssert.True(rows[0].Summary.Contains("Engaged: unavailable", StringComparison.Ordinal),
            "The display must distinguish missing engagement data from a blank or measured zero.");
        return Task.CompletedTask;
    }

    private static YouTubePublishProvenance Snapshot(string source) => new(source, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(24),
        "REANIMAL", StudioOutputCanvas.Portrait, 1080, 1920, StudioCompositionLayout.FacecamTopFit, true,
        new StudioCaptionLook(GenerationCaptionStylePreset.Pop, 72, StudioCaptionWordLimitPreset.Punchy, 82, 110,
            new StudioCaptionTypography("Arial", "#FFFF00", safeArea: StudioCaptionSafeArea.TikTok)));

    private static Task ProvenancePersists()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ReplayFoundry-Provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            YouTubePublishProvenance first = Snapshot(Path.Combine(directory, "original.mkv"));
            var second = new YouTubePublishProvenance(Path.Combine(directory, "other.mkv"), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(98),
                "Other game", StudioOutputCanvas.Portrait, 1080, 1920, StudioCompositionLayout.Fit, false, null);
            var callerCuts = new List<YouTubePublishProvenance> { first, second };
            var sequence = new YouTubePublishProvenance(first.SourceFullPath, first.SourceStart, first.SourceEnd, first.GameName,
                first.Canvas, first.OutputWidth, first.OutputHeight, first.CompositionLayout, true, first.CaptionLook,
                contributingCuts: callerCuts, outputDuration: TimeSpan.FromSeconds(20));
            callerCuts.Clear();
            var asset = new LibraryMediaAsset("asset-1", "project-1", GenerationMode.Montage, 1,
                Path.Combine(directory, "rendered.mp4"), null, TimeSpan.FromSeconds(20), 1080, 1920,
                "Library title", "", [], DateTimeOffset.UnixEpoch, 2, ["cut-1", "cut-2"], sourceProvenance: sequence);
            string libraryPath = Path.Combine(directory, "library.json");
            new JsonLibraryCatalogStore(libraryPath).Replace([asset]);
            LibraryMediaAsset restoredAsset = new JsonLibraryCatalogStore(libraryPath).Current.Single();
            YouTubePublishProvenance submitted = YouTubePublishProvenance.Capture(restoredAsset.Relink(
                Path.Combine(directory, "relocated.mp4"), null), "channel-exact")!;
            var entry = new YouTubePublishHistoryEntry("history-1", asset.Id, "Exact submitted title", "video-1",
                "https://youtu.be/video-1", YouTubePublishOutcome.Published, YouTubeVideoVisibility.Public,
                DateTimeOffset.UnixEpoch.AddDays(10), null, provenance: submitted);
            string historyPath = Path.Combine(directory, "history.json");
            new JsonYouTubePublishHistoryStore(historyPath).Append(entry.WithRemoteStatus(
                YouTubeRemoteVideoStatus.Exists, DateTimeOffset.UnixEpoch.AddDays(11)));
            YouTubePublishHistoryEntry restored = new JsonYouTubePublishHistoryStore(historyPath).Current.Single();
            TestAssert.Equal("Exact submitted title", restored.Title, "History stores the submitted title, independent of Library metadata.");
            TestAssert.Equal("channel-exact", restored.Provenance!.ChannelId!, "Known channel identity must survive reload.");
            TestAssert.Equal(2, restored.Provenance.ContributingCuts.Count, "Every montage cut survives caller mutation and persistence.");
            TestAssert.Equal(TimeSpan.FromSeconds(20), restored.Provenance.OutputDuration!.Value, "Montage duration is the total, not its first cut.");
            TestAssert.Equal(first.SourceFullPath, restored.Provenance.ContributingCuts[0].SourceFullPath, "Relinking output cannot rewrite source provenance.");
            TestAssert.Equal(first.CaptionLook!, restored.Provenance.ContributingCuts[0].CaptionLook!, "Exact portable typography and caption layout survive both stores.");
            TestAssert.Equal(TimeSpan.FromSeconds(90), restored.Provenance.ContributingCuts[1].SourceStart, "Each cut keeps its source clock.");
            TestAssert.Throws<ArgumentException>(() => new YouTubePublishHistoryEntry("failed", "asset", "title", null, null,
                YouTubePublishOutcome.Failed, YouTubeVideoVisibility.Private, DateTimeOffset.UnixEpoch, null,
                "failed", "Upload did not finish", provenance: submitted), "Only successful submissions can have outcome-linked provenance.");
            foreach (string schema in new[] { "1.0", "1.1" })
            {
                JsonNode legacy = JsonNode.Parse(File.ReadAllText(historyPath))!;
                legacy["schemaVersion"] = "replayfoundry-youtube-publish-history-" + schema;
                legacy["entries"]![0]!.AsObject().Remove("provenance");
                File.WriteAllText(historyPath, legacy.ToJsonString());
                TestAssert.True(new JsonYouTubePublishHistoryStore(historyPath).Current.Single().Provenance is null,
                    "Old uploads must remain unknown instead of being reconstructed from later assets.");
            }
            JsonNode legacyLibrary = JsonNode.Parse(File.ReadAllText(libraryPath))!;
            legacyLibrary["assets"]![0]!.AsObject().Remove("sourceProvenance");
            File.WriteAllText(libraryPath, legacyLibrary.ToJsonString());
            TestAssert.True(YouTubePublishProvenance.Capture(new JsonLibraryCatalogStore(libraryPath).Current.Single()) is null,
                "Old Library renders do not gain invented Studio provenance at upload time.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        return Task.CompletedTask;
    }

    private static Task ObservationsKeepPublishSnapshot()
    {
        YouTubePublishProvenance snapshot = Snapshot(TestMediaFactory.CreateSourcePath("analytics-source.mkv"))
            .WithPublishedContext(TimeSpan.FromSeconds(12), "channel-1");
        DateTimeOffset uploaded = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = new YouTubePublishHistoryEntry("history-1", "asset-1", "Title at upload", "video-a", "https://youtu.be/video-a",
            YouTubePublishOutcome.Scheduled, YouTubeVideoVisibility.Private, uploaded, uploaded.AddDays(2), provenance: snapshot);
        using var json = JsonDocument.Parse("""
            {"columnHeaders":[{"name":"subscribersGained"},{"name":"video"},{"name":"views"}],"rows":[[3,"video-a",0]]}
            """);
        YouTubeVideoObservation observation = YouTubeAnalyticsService.Parse(json.RootElement, [entry],
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), uploaded.AddDays(10)).Single();
        TestAssert.True(ReferenceEquals(snapshot, observation.Provenance), "Analytics must use the successful upload snapshot directly.");
        TestAssert.Equal(uploaded, observation.UploadedAtUtc!.Value, "Observation age starts at the recorded upload, without claiming publication age.");
        TestAssert.True(observation.Summary.Contains("Subscribers gained: 3", StringComparison.Ordinal) &&
            observation.Summary.Contains("Average: unavailable", StringComparison.Ordinal), "Fetched subscription gains and missing retention are explicit.");
        TestAssert.True(observation.ComparisonText.Contains("REANIMAL", StringComparison.Ordinal) &&
            observation.ComparisonText.Contains("TikTok", StringComparison.Ordinal) &&
            observation.ComparisonText.Contains("analytics-source.mkv", StringComparison.Ordinal),
            "The comparison filter must identify the actual game, style, and source cut.");
        TestAssert.True(observation.AgeSummary.Contains("Scheduled release", StringComparison.Ordinal), "Scheduled release must not be conflated with upload time.");
        YouTubeVideoObservation restored = JsonSerializer.Deserialize<YouTubeVideoObservation>(JsonSerializer.Serialize(observation))!;
        TestAssert.Equal(snapshot.CaptionLook!, restored.Provenance!.CaptionLook!, "Saved observations retain the exact immutable style snapshot.");
        return Task.CompletedTask;
    }

    private static async Task HonorsPermission()
    {
        var authorization = new UnusedAuthorization();
        using var http = new HttpClient();
        using var service = new YouTubeAnalyticsService(authorization, http,
            new YouTubeConnectionPermissionState(new InMemoryYouTubeConnectionPermissionStore()));
        bool rejected = false;
        try { await service.ReadAsync([Entry("video-a")], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), CancellationToken.None); }
        catch (InvalidOperationException) { rejected = true; }
        TestAssert.True(rejected, "Disabled YouTube permission must reject analytics access.");
        TestAssert.Equal(0, authorization.Calls, "Permission rejection must happen before credential access.");
    }

    private static Task UsesReadOnlyScope()
    {
        var configuration = new YouTubeOAuthClientConfiguration("fixture.apps.googleusercontent.com", "fixture", analyticsOnly: true);
        TestAssert.Equal(YouTubeOAuthClientConfiguration.AnalyticsReadOnlyScope, configuration.Scopes.Single(),
            "Connecting analytics must not request publishing access.");
        return Task.CompletedTask;
    }

    private sealed class UnusedAuthorization : IYouTubeAuthorizationService
    {
        public int Calls { get; private set; }
        public Task<YouTubeAccessCredential?> GetAccessCredentialAsync(bool forceRefresh, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected credential access."); }
        public Task<YouTubeAccessCredential> ConnectAsync(CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected connection."); }
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
