using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Geometry;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static class StudioSourceCropTrackingIntegrationTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Reviewed source crops persist with source clocks and backward-compatible defaults", TrackingPersists),
        new("Source tracking preserves compatible edits and rejects changed layout or recording", TrackingInvalidatesPrecisely),
        new("Source crop graphs keep gaps manual and HUD canvas independent", TrackingGraphKeepsSourceClocks),
        new("Source tracking decode is CPU bounded and cleans owned frames after cancellation", TrackingDecodeCancellationCleansUp),
        new("Source tracking rejects a recording changed during frame extraction", TrackingRejectsChangedSource),
        new("Tracking review projection retains source clocks bounds and frame pixels", TrackingReviewProjectionPreservesEvidence),
    ];
    private static Task TrackingReviewProjectionPreservesEvidence()
    {
        var feature = new NormalizedRectangle(.2, .3, .1, .1);
        var viewport = new NormalizedRectangle(.1, .2, .5, .5);
        var accepted = new StudioTrackingSample(TimeSpan.FromSeconds(12.5), StudioCropTrackingState.Tracked,
            .9, .15, feature, viewport);
        var lost = new StudioTrackingSample(TimeSpan.FromSeconds(12.625), StudioCropTrackingState.Lost,
            .2, .01, null, null);
        byte[] pixels = [0, 64, 128, 255];
        var draft = new StudioSourceTrackingDraft(null, new([accepted, lost], [], "Retained review"),
            [pixels, new byte[4]], 2, 2);
        StudioSourceTrackingReview review = WpfStudioSourceTrackingReviewService.Project(draft);
        TestAssert.Equal(accepted.ToString(), review.Samples[0].Label,
            "Projection must preserve measured source clocks and score labels.");
        TestAssert.Equal(feature.X, review.Samples[0].Feature!.Value.X,
            "Feature overlay coordinates must retain the normalized measurement.");
        TestAssert.Equal(viewport.Height, review.Samples[0].Viewport!.Value.Height,
            "Viewport geometry must not change at the presentation boundary.");
        TestAssert.Null(review.Samples[1].Viewport,
            "A lost tracking frame must not gain an inferred viewport.");
        var image = (System.Windows.Media.Imaging.BitmapSource)review.CreateFrameImage(0);
        byte[] copied = new byte[4];
        image.CopyPixels(copied, 2, 0);
        TestAssert.True(image.IsFrozen && copied.SequenceEqual(pixels),
            "Selected-frame rendering must retain exact grayscale pixels in a frozen image.");
        return Task.CompletedTask;
    }
    private static Task TrackingPersists()
    {
        using var fixture = new Fixture();
        StudioRenderSettings settings = fixture.Asset.RenderSettings.WithSourceCropTracks([fixture.Track()]);
        var asset = fixture.Asset.WithRenderSettings(settings);
        TestAssert.False(StudioPreviewCacheKey.CreateMediaIdentity(new(fixture.Asset)) ==
            StudioPreviewCacheKey.CreateMediaIdentity(new(asset)), "Applying motion must invalidate any proxy created for the earlier manual composition.");
        var project = new GenerationOutputProject("tracking-persistence", GenerationMode.IndividualClips,
            Path.Combine(fixture.Root, "rendered"), 1, ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [asset], DateTimeOffset.UtcNow);
        var store = new JsonStudioProjectStore(Path.Combine(fixture.Root, "projects"));
        store.Save(project, 1, null);
        var loaded = store.Load(project.Id);
        TestAssert.Equal(StudioProjectLoadOutcome.Loaded, loaded.Outcome, "The real project store must reload the reviewed immutable track.");
        var restored = loaded.Project!.PrimaryAsset.RenderSettings;
        TestAssert.Equal(settings.CanonicalIdentity(), restored.CanonicalIdentity(), "All source identity, layout, accepted interval and motion decisions must survive saving.");
        TestAssert.Equal(TimeSpan.FromSeconds(4), restored.SourceCropTracks[0].Runs[1].Knots[0].SourcePosition,
            "Separate runs retain absolute source clocks through persistence.");
        TestAssert.Equal(0, JsonSerializer.Deserialize<StudioRenderSettings>("{}")!.SourceCropTracks.Count,
            "Earlier projects must continue using their manual layout without inferred tracking.");
        TestAssert.Equal(settings.CanonicalIdentity(), asset.WithStudioEdits(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(6), asset.Appearance)
            .RenderSettings.CanonicalIdentity(), "Trimming must retain source motion clocks, so extension can restore the accepted run.");
        return Task.CompletedTask;
    }
    private static Task TrackingInvalidatesPrecisely()
    {
        using var fixture = new Fixture();
        var tracked = fixture.Asset.RenderSettings.WithSourceCropTracks([fixture.Track()]);
        var compatible = new StudioRenderSettings(StudioOutputCanvas.Portrait, quality: StudioExportQuality.High,
            burnCaptions: false, resolution: StudioOutputResolution.Uhd2160).WithCompatibleSourceCropTracksFrom(tracked);
        TestAssert.Equal(1, compatible.SourceCropTracks.Count, "Quality, captions and resolution preserve source geometry.");
        TestAssert.Equal(1, tracked.WithFrameEdits([new(TimeSpan.FromSeconds(2), 1.5)], []).SourceCropTracks.Count,
            "Independent canvas waypoints must not erase source tracking.");
        TestAssert.Equal(1, StudioPlatformExportPresets.Apply(StudioPlatformExportPreset.YouTubeShorts, tracked).SourceCropTracks.Count,
            "An already portrait platform preset can retain compatible motion.");
        foreach (var changed in new[] { new StudioRenderSettings(StudioOutputCanvas.Square),
            new StudioRenderSettings(StudioOutputCanvas.Portrait, StudioCompositionLayout.Fill),
            new StudioRenderSettings(StudioOutputCanvas.Portrait, gameplayRegion: new(.1, 0, .9, 1)) })
            TestAssert.Equal(0, changed.WithCompatibleSourceCropTracksFrom(tracked).SourceCropTracks.Count,
                "Changed gameplay region or pane layout must require a new review, not reuse a stale viewport.");
        TestAssert.Throws<ArgumentException>(() => new StudioRenderSettings(StudioOutputCanvas.Square, sourceCropTracks: tracked.SourceCropTracks),
            "Direct persisted construction cannot bypass the saved layout guard.");
        string other = Path.Combine(fixture.Root, "other.mkv"); File.WriteAllBytes(other, [1, 2, 3]);
        TestAssert.Throws<InvalidOperationException>(() => tracked.SourceCropTracks[0].RequireSource(TestMediaFactory.Create(other), false),
            "Source motion cannot be attached to another recording.");
        File.AppendAllText(fixture.Asset.SourceFullPath, "changed");
        TestAssert.Throws<InvalidOperationException>(() => tracked.SourceCropTracks[0].RequireSource(fixture.Asset.SourceMedia, true),
            "Source modification must be checked again before applying or rendering.");
        return Task.CompletedTask;
    }
    private static Task TrackingGraphKeepsSourceClocks()
    {
        using var fixture = new Fixture();
        var hud = new NormalizedRectangle(.1, .1, .2, .1);
        var settings = new StudioRenderSettings(StudioOutputCanvas.Portrait, StudioCompositionLayout.FacecamTopFit,
            facecamRegion: new(0, 0, .2, .2), decoration: new("#202030", hud, new(.1, .75, .8, .1)));
        var gameplayTrack = fixture.Track(settings: settings);
        var hudTrack = fixture.Track(StudioCropTrackingTarget.Hud, settings);
        settings = settings.WithSourceCropTracks([gameplayTrack, hudTrack]);
        var asset = fixture.Asset.WithRenderSettings(settings);
        var graph = new List<string>();
        FfmpegStudioCompositionGraph.AppendVideo(graph, asset.SourceMedia, GenerationClipOutputProfile.FromAsset(asset), settings, TimeSpan.FromSeconds(1.5));
        TestAssert.Contains("between(t,-0.5,0.5)+between(t,2.5,3.5)", graph,
            "Accepted run enable expressions must rebase to the selected source start without spanning the missing interval.");
        TestAssert.Contains("[manualgame][trackedgame]overlay", graph, "Gameplay requires a manual fallback branch, not extrapolated crop motion across gaps.");
        TestAssert.Contains("[faceinput]crop=", graph, "Facecam uses an independent fixed source crop.");
        TestAssert.Contains("[composed][hudcanvas]overlay=x=108:y=1440", graph, "HUD canvas position must remain fixed while its source crop moves.");
        TestAssert.False(graph.Any(static filter => filter.Contains("zoompan", StringComparison.Ordinal)), "Source tracking cannot move the completed composition as a substitute for source motion.");
        string hudCrop = FfmpegTrackedSourceCropGraph.Crop(hudTrack,
            EffectiveDisplayGeometryCalculator.Calculate(asset.SourceMedia.PrimaryVideoStream), TimeSpan.FromSeconds(1.5), true);
        TestAssert.True(hudCrop.Contains("if(between(t", StringComparison.Ordinal) && hudCrop.Contains(",32)", StringComparison.Ordinal),
            "HUD crop expressions must restore the exact saved manual X outside accepted intervals.");
        return Task.CompletedTask;
    }
    private static async Task TrackingDecodeCancellationCleansUp()
    {
        using var fixture = new Fixture(); using var cancel = new CancellationTokenSource();
        var runner = new TrackingRunner(block: true); var service = new FfmpegStudioSourceTrackingService(runner, new Locator(fixture.Root));
        var task = service.AnalyzeAsync(fixture.Asset, StudioCropTrackingTarget.Gameplay, new(.3, .3, .1, .1),
            TimeSpan.Zero, TimeSpan.FromSeconds(1), 1, cancel.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancel.Cancel();
        await TestAssert.ThrowsAsync<OperationCanceledException>(async () => await task, "Cancellation must propagate instead of manufacturing a successful track.");
        TestAssert.False(File.Exists(runner.Output), "Owned grayscale frames must be removed when extraction is canceled.");
        TestAssert.Contains("-hwaccel", runner.Request!.Arguments, "Source extraction must choose CPU decoding explicitly.");
        TestAssert.Contains("none", runner.Request.Arguments, "No GPU decoder may be requested by source tracking.");
        TestAssert.True(runner.Request.Arguments.Contains("480") && runner.Request.Arguments.Contains("1") && runner.Request.Timeout <= TimeSpan.FromSeconds(90),
            "Decoder frame/thread/wall limits must be carried into the actual process request.");
    }
    private static async Task TrackingRejectsChangedSource()
    {
        using var fixture = new Fixture();
        var runner = new TrackingRunner(mutateSource: fixture.Asset.SourceFullPath);
        var service = new FfmpegStudioSourceTrackingService(runner, new Locator(fixture.Root));
        await TestAssert.ThrowsAsync<InvalidOperationException>(async () => await service.AnalyzeAsync(fixture.Asset,
            StudioCropTrackingTarget.Gameplay, new(.3, .3, .1, .1), TimeSpan.Zero, TimeSpan.FromSeconds(.5), 1, CancellationToken.None),
            "A source modified while its frames were extracted must never produce an applicable result.");
        TestAssert.False(File.Exists(runner.Output), "Failure after frame extraction must clean the owned raw frame file.");
    }
    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "ReplayFoundryTracking-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root);
            string path = Path.Combine(Root, "source.mkv"); File.WriteAllBytes(path, [1, 2, 3]);
            Asset = new("tracking", 1, TestMediaFactory.Create(path, TimeSpan.FromSeconds(10), width: 320, height: 180), null,
                TimeSpan.Zero, TimeSpan.FromSeconds(8), 0, 0, GenerationCandidateSelectionReason.ManualSourceCut, "Tracking test source.",
                renderSettings: new(StudioOutputCanvas.Portrait));
        }
        internal string Root { get; }
        internal GenerationOutputAsset Asset { get; }
        internal StudioSourceCropTrack Track(StudioCropTrackingTarget target = StudioCropTrackingTarget.Gameplay, StudioRenderSettings? settings = null)
        {
            settings ??= Asset.RenderSettings;
            var info = new FileInfo(Asset.SourceFullPath);
            var manual = target == StudioCropTrackingTarget.Gameplay ? settings.GameplayRegion : settings.Decoration.HudSourceRegion!;
            return new(target, Asset.SourceFullPath, info.Length, info.LastWriteTimeUtc, 320, 180,
                TimeSpan.Zero, TimeSpan.FromSeconds(5.5), new(.3, .3, .1, .1), NormalizedRectangle.FullFrame, manual,
                target == StudioCropTrackingTarget.Gameplay ? .4 : manual.Width,
                target == StudioCropTrackingTarget.Gameplay ? .5 : manual.Height,
                [new([new(TimeSpan.FromSeconds(1), .1, .1), new(TimeSpan.FromSeconds(2), .2, .2)]),
                    new([new(TimeSpan.FromSeconds(4), .3, .2), new(TimeSpan.FromSeconds(5), .4, .3)])],
                44, 18, StudioCropTrackingState.Lost, canvas: settings.Canvas, layout: settings.Layout,
                facecamHeightPercent: settings.FacecamHeightPercent, hasFacecam: settings.FacecamRegion is not null);
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class Locator(string root) : IFfmpegToolLocator
    {
        public string LocateFfmpeg() => Path.Combine(root, "ffmpeg.exe");
        public string LocateFfprobe() => Path.Combine(root, "ffprobe.exe");
    }
    private sealed class TrackingRunner(bool block = false, string? mutateSource = null) : IProcessRunner
    {
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Output { get; private set; } = "";
        internal ProcessRunRequest? Request { get; private set; }
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Request = request; Output = request.Arguments[^1];
            byte[] frame = new byte[320 * 180]; new Random(731).NextBytes(frame);
            await File.WriteAllBytesAsync(Output, frame.Concat(frame).Concat(frame).Concat(frame).ToArray(), cancellationToken);
            Started.TrySetResult(true);
            if (block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (mutateSource is not null) await File.AppendAllTextAsync(mutateSource, "changed", cancellationToken);
            return new(0, "", "", TimeSpan.Zero);
        }
    }
}
