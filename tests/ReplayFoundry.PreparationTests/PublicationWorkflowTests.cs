using System.Net;
using System.Net.Http;
using System.Text;
using System.IO;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Library;
using ReplayFoundry.Desktop.Features.Publish;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Platform.Storage;
using ReplayFoundry.Desktop.Platform.YouTube;

namespace ReplayFoundry.PreparationTests;

internal static class PublicationWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Publication: scheduled uploads become public only after a remote observation", ScheduledToPublic),
        new("Publication: private, processing and inaccessible videos are not public", OtherRemoteStates),
        new("Publication: remote API reads visibility, processing, channel and schedule together", ReadsRemoteState),
        new("Publication: remote observations survive history reopening without rewriting the upload", RetainsObservation),
        new("Publication: status filters preserve older unfinished videos", QueueFilters),
        new("Publication: Library keeps selected videos and reports filtered selections", KeepsHiddenSelections),
        new("Publication: browse choices survive reopening", RemembersBrowsing),
        new("Publication: five thousand Library records project without repeated latest-session scans", LargeLibrary),
        new("Publication: a replaced file cannot inherit an old public badge", ReplacementIsUnpublished),
    ];
    private static YouTubePublishHistoryEntry Entry() => new("history", "asset", "A verified moment", "video", "https://www.youtube.com/watch?v=video",
        YouTubePublishOutcome.Scheduled, YouTubeVideoVisibility.Private, Now.AddDays(-2), Now.AddDays(-1));
    private static Task ScheduledToPublic()
    {
        var entry = Entry();
        TestAssert.Equal(PublicationStage.Unconfirmed, PublicationStatus.FromHistory(entry, Now).Stage, "Elapsed schedule time cannot prove publication.");
        var observed = entry.WithRemoteStatus(YouTubeRemoteVideoStatus.Exists, Now,
            new("channel", YouTubeVideoVisibility.Public, "processed", "succeeded", null));
        TestAssert.Equal(PublicationStage.Public, PublicationStatus.FromHistory(observed, Now).Stage, "A verified public video leaves Scheduled.");
        TestAssert.Equal(YouTubePublishOutcome.Scheduled, observed.Outcome, "Original intent remains in the audit record.");
        return Task.CompletedTask;
    }
    private static Task OtherRemoteStates()
    {
        foreach (var (remote, stage) in new (YouTubeRemoteVideoDetails, PublicationStage)[]
        {
            (new("channel", YouTubeVideoVisibility.Private, "processed", "succeeded", null), PublicationStage.Private),
            (new("channel", YouTubeVideoVisibility.Unlisted, "processed", "succeeded", null), PublicationStage.Unlisted),
            (new("channel", YouTubeVideoVisibility.Public, "uploaded", "processing", null), PublicationStage.Processing),
            (new("channel", YouTubeVideoVisibility.Private, "processed", "succeeded", Now.AddDays(1)), PublicationStage.Scheduled),
            (new("channel", YouTubeVideoVisibility.Public, "failed", "failed", null), PublicationStage.Attention),
            (new("channel", null, null, null, null), PublicationStage.Unconfirmed),
        }) TestAssert.Equal(stage, PublicationStatus.FromHistory(Entry().WithRemoteStatus(YouTubeRemoteVideoStatus.Exists, Now, remote), Now).Stage, "Visibility and processing both determine publication.");
        TestAssert.Equal(PublicationStage.Attention, PublicationStatus.FromHistory(Entry().WithRemoteStatus(YouTubeRemoteVideoStatus.NotFoundOrInaccessible, Now), Now).Stage, "Inaccessible is attention, not assumed deleted or public.");
        return Task.CompletedTask;
    }
    private static async Task ReadsRemoteState()
    {
        using var handler = new StatusHandler();
        using var http = new HttpClient(handler);
        var api = new YouTubeDataApiClient(http);
        var result = await api.GetVideoStatusesAsync("test-token", ["video"], CancellationToken.None);
        TestAssert.Equal(YouTubeVideoVisibility.Private, result["video"].Visibility!.Value, "API privacy is retained.");
        TestAssert.Equal(Now.AddDays(1), result["video"].PublishAtUtc!.Value, "Remote scheduling is retained.");
        TestAssert.Equal("channel", result["video"].ChannelId, "The observation belongs to a channel.");
        TestAssert.Equal(1, result.Count, "Unrequested video IDs cannot contaminate history.");
        TestAssert.True(handler.Query.Contains("processingDetails", StringComparison.Ordinal), "The same request must include processing status.");
    }
    private static Task RetainsObservation()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JsonYouTubePublishHistoryStore(Path.Combine(root, "history.json"));
            store.Append(Entry());
            var entry = store.Current[0].WithRemoteStatus(YouTubeRemoteVideoStatus.Exists, Now, new("channel", YouTubeVideoVisibility.Public, "processed", null, null));
            store.Replace([entry]);
            var reopened = new JsonYouTubePublishHistoryStore(Path.Combine(root, "history.json")).Current.Single();
            TestAssert.Equal(PublicationStage.Public, PublicationStatus.FromHistory(reopened, Now).Stage, "Reopened Library retains verified visibility.");
            TestAssert.Equal(Now.AddDays(-1), reopened.ScheduledForUtc!.Value, "The original request remains unchanged.");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
    private static Task QueueFilters()
    {
        var old = Asset("old", "old-project", Now.AddDays(-20));
        var scheduled = Asset("asset", "latest-project", Now);
        var snapshots = new PublishSnapshotIndex();
        snapshots.Refresh([], [Entry().WithRemoteStatus(YouTubeRemoteVideoStatus.Exists, Now,
            new("channel", YouTubeVideoVisibility.Private, "processed", null, Now.AddDays(1)))], [old, scheduled], observedAt: Now);
        var queue = PublishLibraryProjector.Build([old, scheduled], "", "Any date", null, Now.Date, TimeZoneInfo.Utc,
            snapshots.GetAssetPublishState, snapshots.GetPublicationStatus, "Ready to publish");
        TestAssert.Equal("old", queue.Items.Single().Asset.Id, "Unfinished work stays visible regardless of recording age.");
        return Task.CompletedTask;
    }
    private static Task KeepsHiddenSelections()
    {
        using var library = new LibraryViewModel(new Catalog(Asset("old", "old-project", Now.AddDays(-20)), Asset("new", "new-project", Now)));
        library.BeginSelectionCommand.Execute(null);
        library.SelectAllVisibleCommand.Execute(null);
        library.DateFilter = "Latest session";
        TestAssert.Equal(2, library.MarkedCount, "Changing filters does not silently discard selection.");
        TestAssert.Equal(1, library.HiddenMarkedCount, "Hidden selection is explicitly counted.");
        return Task.CompletedTask;
    }
    private static Task RemembersBrowsing()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-browse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "browse.json");
            using (var library = new LibraryViewModel())
            { library.RestoreBrowsePreferences(new JsonBrowsePreferencesStore(path)); library.DateFilter = "Recent"; }
            using var reopened = new LibraryViewModel();
            reopened.RestoreBrowsePreferences(new JsonBrowsePreferencesStore(path));
            TestAssert.Equal("Recent", reopened.DateFilter, "Date choice persists across app sessions.");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
    private static Task LargeLibrary()
    {
        var assets = Enumerable.Range(0, 5000).Select(i => Asset($"a{i}", $"p{i / 5}", Now.AddMinutes(-i))).ToArray();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var projection = PublishLibraryProjector.Build(assets, "", "Latest session", null, Now.Date, TimeZoneInfo.Utc, _ => "Ready");
        TestAssert.Equal(5, projection.Items.Count, "Latest session groups one generated batch.");
        TestAssert.True(timer.Elapsed < TimeSpan.FromSeconds(3), "A 5,000-item filter must remain interactive without per-row full rescans.");
        Console.WriteLine($"      Publication projection: 5,000 items in {timer.Elapsed.TotalMilliseconds:0} ms");
        return Task.CompletedTask;
    }
    private static Task ReplacementIsUnpublished()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var asset = new LibraryMediaAsset("asset", "project", GenerationMode.IndividualClips, 1, path, null,
                TimeSpan.FromSeconds(25), 1080, 1920, "Title", "Description", [], Now);
            var upload = new YouTubePublishHistoryEntry("h", "asset", "Title", "v", "https://youtu.be/v",
                YouTubePublishOutcome.Published, YouTubeVideoVisibility.Public, Now, null,
                remoteStatus: YouTubeRemoteVideoStatus.Exists, remoteCheckedAtUtc: Now,
                remoteDetails: new("channel", YouTubeVideoVisibility.Public, "processed", "succeeded", null),
                fileRevision: PublishedFileRevision.Capture(path));
            var index = new PublishSnapshotIndex();
            index.Refresh([], [upload], [asset]);
            TestAssert.Equal(PublicationStage.Public, index.GetPublicationStatus(asset).Stage, "The submitted file keeps its status.");
            File.WriteAllBytes(path, [4, 5, 6, 7]);
            TestAssert.Equal(PublicationStage.Ready, index.GetPublicationStatus(asset).Stage, "The replaced file requires a new publishing decision.");
            TestAssert.True(index.GetUpload(asset) is null, "Prepare must not open the earlier uploaded revision.");
            TestAssert.Equal(1, index.History.Count, "Historical publication remains available.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
    private static LibraryMediaAsset Asset(string id, string project, DateTimeOffset date) => new(id, project, GenerationMode.IndividualClips, 1,
        Path.Combine(Path.GetTempPath(), id + ".mp4"), null, TimeSpan.FromSeconds(25), 1080, 1920, id, "Saved description", [], date);
    private sealed class Catalog(params LibraryMediaAsset[] assets) : ILibraryCatalog
    {
        public IReadOnlyList<LibraryMediaAsset> Assets => assets;
        public event EventHandler? Changed { add { } remove { } }
    }
    private sealed class StatusHandler : HttpMessageHandler
    {
        public string Query { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[{"id":"video","snippet":{"channelId":"channel"},"status":{"privacyStatus":"private","uploadStatus":"processed","publishAt":"2026-09-27T12:00:00Z"},"processingDetails":{"processingStatus":"succeeded"}},{"id":"unrequested"}]}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
