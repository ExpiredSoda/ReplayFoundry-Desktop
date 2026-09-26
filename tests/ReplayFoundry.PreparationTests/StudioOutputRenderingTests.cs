using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Platform.Media;
using ReplayFoundry.Desktop.Platform.Processes;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio.Projects;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task AudioMasteringAndAuditionUseSavedCut()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips, audioStreamCount: 2);
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset.WithRenderSettings(new StudioRenderSettings(
            audioTracks: [new(1, -6), new(2, muted: true)], audioMastering: new(true, -17.5, true, -2.5), burnCaptions: false));
        StudioRenderSettings restored = JsonSerializer.Deserialize<StudioRenderSettings>(asset.RenderSettings.CanonicalIdentity())!;
        TestAssert.Equal(asset.RenderSettings.CanonicalIdentity(), restored.CanonicalIdentity(), "Mastering targets and clean-video choice must survive persistence.");
        var audio = new List<string>();
        FfmpegStudioCompositionGraph.AppendAudio(audio, asset.SourceMedia, restored, asset.Duration);
        string graph = string.Join(';', audio);
        TestAssert.True(graph.Contains("loudnorm=I=-17.5:TP=-2.5:LRA=11:linear=false,aresample=48000", StringComparison.Ordinal),
            "The full mixed signal must be normalized with the requested finite loudness and peak targets, then return to 48 kHz.");
        TestAssert.False(graph.Contains("[0:2]", StringComparison.Ordinal), "Mastering cannot reintroduce a muted duplicate track.");
        TestAssert.True(graph.Contains(",asetpts=PTS-STARTPTS,loudnorm=", StringComparison.Ordinal),
            "Mastering must see an exact trimmed audio window so future source audio cannot affect the end of the cut.");
        var masteringOnly = FfmpegClipRenderCommandBuilder.BuildSegment(asset.SourceMedia, asset.SourceStart, asset.SourceEnd,
            GenerationClipOutputProfile.FromAsset(asset), Path.Combine(fixture.Root, "mastering-only.mp4"),
            renderSettings: new(audioMastering: new(normalizeLoudness: true)));
        TestAssert.True(masteringOnly.Arguments.Any(argument => argument.Contains("loudnorm=", StringComparison.Ordinal)),
            "Mastering must run even when no individual track overrides were saved.");
        string executable = Path.Combine(fixture.Root, "ffmpeg.exe");
        ProcessRunRequest after = WpfStudioMixAudioAuditionSession.CreateRequest(asset, true, Path.Combine(fixture.Root, "after.wav"), executable);
        ProcessRunRequest before = WpfStudioMixAudioAuditionSession.CreateRequest(asset, false, Path.Combine(fixture.Root, "before.wav"), executable);
        TestAssert.True(ContainsPair(after.Arguments, "-filter_complex", graph), "The after audition must use the final renderer's exact mix graph.");
        TestAssert.False(before.Arguments.Any(argument => argument.Contains("loudnorm", StringComparison.Ordinal) || argument.Contains("volume=-6dB", StringComparison.Ordinal)),
            "The before audition must exclude the saved gain and mastering changes.");
        TestAssert.True(ContainsPair(after.Arguments, "-ss", asset.SourceStart.TotalSeconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)) &&
            ContainsPair(after.Arguments, "-t", asset.Duration.TotalSeconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)),
            "Audition processes the current cut so dynamic normalization has the same source window as export.");
        var preview = new ReplayFoundry.Desktop.Features.Studio.Preview.StudioPreviewMediaRequest(asset);
        TestAssert.Equal(asset.SourceStart, preview.SourceStart, "Normalized video previews must not use different material from the trim envelope.");
        TestAssert.Equal(asset.SourceEnd, preview.SourceEnd, "Normalized video previews must stop at the exact saved cut.");
        var peakOnly = new List<string>();
        FfmpegStudioCompositionGraph.AppendAudio(peakOnly, asset.SourceMedia, new(audioMastering: new(limitTruePeak: true, truePeakDecibels: -6)));
        TestAssert.True(string.Join(';', peakOnly).Contains("aresample=192000,alimiter=limit=0.501187", StringComparison.Ordinal),
            "Peak-only limiting must oversample and convert the requested dB ceiling to amplitude.");
        TestAssert.Throws<ArgumentException>(() => new StudioAudioMastering(integratedLufs: double.NaN), "Invalid target values cannot enter filter graphs.");
        TestAssert.Equal("Track 2 · Commentary", new StudioAudioTrackEditor(2, "Track 2 · Commentary", null, () => { }).ToString(),
            "Custom ComboBox templates must receive a human-readable audio label.");
        return Task.CompletedTask;
    }

    private static Task ReusableLayoutsPreserveAudioAndComposeHud()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips, audioStreamCount: 2);
        var decoration = new StudioCanvasDecoration("#123456", new(.1, .8, .8, .2), new(.1, .75, .8, .2));
        var layout = new StudioRenderSettings(StudioOutputCanvas.Portrait, decoration: decoration);
        var preset = StudioLayoutPreset.Capture("Full HUD", "REANIMAL", layout);
        string path = Path.Combine(fixture.Root, "layouts.json");
        new ReplayFoundry.Desktop.Platform.Storage.JsonStudioLayoutPresetStore(path).Save(preset);
        StudioLayoutPreset restored = new ReplayFoundry.Desktop.Platform.Storage.JsonStudioLayoutPresetStore(path).Load().Single();
        var current = new StudioRenderSettings(audioTracks: [new(1, -4), new(2, muted: true)],
            audioMastering: new(true, -18, true, -2), burnCaptions: false);
        StudioRenderSettings applied = restored.Apply(current);
        TestAssert.Equal("REANIMAL · Full HUD", restored.ToString(), "A reusable layout must carry its game/series label across reload.");
        TestAssert.Equal(current.AudioMastering, applied.AudioMastering, "Applying a visual layout cannot silently reset audio mastering.");
        TestAssert.True(applied.AudioTracks[1].Muted && !applied.BurnCaptions, "Layout changes must preserve audio routing and clean-video delivery.");
        TestAssert.Equal(JsonSerializer.Serialize(decoration), JsonSerializer.Serialize(applied.Decoration),
            "The manual HUD crop, canvas placement, and background must round-trip.");
        StudioRenderSettings copied = StudioPlatformExportPresets.Apply(StudioPlatformExportPreset.TikTok, applied.WithFrameEdits([], []));
        TestAssert.Equal(applied.Decoration, copied.Decoration, "Platform presets and frame edits must retain manual HUD/background choices.");
        TestAssert.Equal(applied.AudioMastering, copied.AudioMastering, "Platform presets must retain mastering choices.");
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset.WithRenderSettings(applied);
        var graph = new List<string>();
        string output = FfmpegStudioCompositionGraph.AppendVideo(graph, asset.SourceMedia, GenerationClipOutputProfile.FromAsset(asset), applied);
        string filters = string.Join(';', graph);
        TestAssert.Equal("[withhud]", output, "The final video graph must include the separately positioned HUD layer.");
        TestAssert.True(filters.Contains("[normalized]split=2[layoutinput][hudinput]", StringComparison.Ordinal) &&
            filters.Contains("[composed][hudcanvas]overlay=x=108:y=1440", StringComparison.Ordinal),
            "HUD pixels must come from the source and map to the requested portrait canvas coordinates.");
        TestAssert.True(filters.Contains("color=0x123456", StringComparison.Ordinal), "Fit padding must use the saved background color.");
        TestAssert.Throws<ArgumentException>(() => new StudioCanvasDecoration("black;movie=bad"), "Background input must never interpolate arbitrary filter syntax.");
        return Task.CompletedTask;
    }

    private static Task RenderValidationChecksColorContract()
    {
        string path = TestMediaFactory.CreateSourcePath("color-validation.mp4");
        TimeSpan duration = TimeSpan.FromSeconds(10);
        MediaProbeResult valid = TestMediaFactory.Create(path, duration, hasAudio: true);
        GenerationClipOutputProfile profile = GenerationClipOutputProfile.FromReference(valid.PrimaryVideoStream);
        StudioRenderedMediaValidator.ValidateProbe(valid, duration, profile, requiresBt709ToneMap: true);
        MediaProbeResult[] invalidToneMaps =
        [
            TestMediaFactory.Create(path, duration, hasAudio: true, colorPrimaries: null),
            TestMediaFactory.Create(path, duration, hasAudio: true, colorTransfer: null),
            TestMediaFactory.Create(path, duration, hasAudio: true, colorMatrix: null),
            TestMediaFactory.Create(path, duration, hasAudio: true, colorRange: null),
            TestMediaFactory.Create(path, duration, hasAudio: true, colorPrimaries: "bt2020"),
            TestMediaFactory.Create(path, duration, hasAudio: true, colorTransfer: "smpte2084"),
            TestMediaFactory.Create(path, duration, hasAudio: true, colorMatrix: "bt2020nc"),
            TestMediaFactory.Create(path, duration, hasAudio: true, colorRange: "pc"),
        ];
        foreach (MediaProbeResult invalid in invalidToneMaps)
            TestAssert.Throws<InvalidOperationException>(() =>
                StudioRenderedMediaValidator.ValidateProbe(invalid, duration, profile, requiresBt709ToneMap: true),
                "Missing or conflicting HDR conversion tags must not become reusable final outputs.");
        StudioRenderedMediaValidator.ValidateProbe(TestMediaFactory.Create(path, duration, hasAudio: true,
            colorPrimaries: "smpte170m", colorTransfer: "smpte170m", colorMatrix: "smpte170m"),
            duration, profile, requiresBt709ToneMap: false);
        TestAssert.Throws<InvalidOperationException>(() => StudioRenderedMediaValidator.ValidateProbe(
            TestMediaFactory.Create(path, duration, hasAudio: true, pixelFormat: "yuv420p10le"),
            duration, profile, requiresBt709ToneMap: false), "Every output must remain compatible 8-bit 4:2:0 video.");
        return Task.CompletedTask;
    }

    private static Task OutputSettingsPersistAndSurviveEdits()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips, audioStreamCount: 2);
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset;
        var settings = new StudioRenderSettings(StudioOutputCanvas.Square, StudioCompositionLayout.FacecamTop,
            new(.2, 0, .8, 1), new(0, 0, .2, .25), 30,
            [new(1, -6), new(2, 3, true)], 1, true, StudioExportQuality.High);
        StudioRenderSettings restored = JsonSerializer.Deserialize<StudioRenderSettings>(JsonSerializer.Serialize(settings))!;
        TestAssert.Equal(settings.CanonicalIdentity(), restored.CanonicalIdentity(), "Every output decision must round trip.");
        asset = asset.WithRenderSettings(restored);
        TestAssert.Equal(settings.CanonicalIdentity(),
            asset.WithStudioEdits(asset.SourceStart, asset.SourceEnd, asset.Appearance).RenderSettings.CanonicalIdentity(),
            "A trim or caption style edit must not reset composition or audio.");
        TestAssert.Equal(settings.CanonicalIdentity(),
            asset.WithDisposition(GenerationOutputAssetDisposition.ExcludeFromFinalRender).RenderSettings.CanonicalIdentity(),
            "Inclusion changes must preserve the output settings.");
        TestAssert.Equal(1080, GenerationClipOutputProfile.FromAsset(asset).Width, "Square canvas width.");
        TestAssert.Equal(1080, GenerationClipOutputProfile.FromAsset(asset).Height, "Square canvas height.");
        return Task.CompletedTask;
    }

    private static Task OutputCompositionCropsAndStacks()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips, audioStreamCount: 2);
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset.WithRenderSettings(
            new StudioRenderSettings(StudioOutputCanvas.Portrait, StudioCompositionLayout.FacecamTop,
                new(.25, 0, .75, 1), new(0, 0, .25, .25), 25));
        var command = FfmpegClipRenderCommandBuilder.BuildSegment(asset.SourceMedia, asset.SourceStart, asset.SourceEnd,
            GenerationClipOutputProfile.FromAsset(asset), Path.Combine(fixture.Root, "portrait.mp4"), renderSettings: asset.RenderSettings);
        string graph = command.Arguments[command.Arguments.ToList().IndexOf("-filter_complex") + 1];
        TestAssert.True(graph.Contains("split=2[gameinput][faceinput]", StringComparison.Ordinal), "The two source regions require independent crops.");
        TestAssert.True(graph.Contains("scale=1080:480", StringComparison.Ordinal) && graph.Contains("scale=1080:1440", StringComparison.Ordinal),
            "Facecam and gameplay must exactly fill the requested portrait canvas.");
        TestAssert.True(graph.Contains("vstack=inputs=2[composed]", StringComparison.Ordinal), "The composed video must reach the final graph.");
        TestAssert.True(ContainsPair(command.Arguments, "-map", "[vout]"), "Export must map the composed frame rather than the original source.");
        return Task.CompletedTask;
    }

    private static Task OutputAudioAppliesMuteGainAndDucking()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips, audioStreamCount: 2);
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset;
        var graph = new List<string>();
        FfmpegStudioCompositionGraph.AppendAudio(graph, asset.SourceMedia,
            new StudioRenderSettings(audioTracks: [new(1, -6), new(2, 3)], voiceAudioStreamIndex: 2, duckGameplay: true));
        string filters = string.Join(';', graph);
        TestAssert.True(filters.Contains("[0:1]volume=-6dB", StringComparison.Ordinal), "Track gain must be applied independently.");
        TestAssert.True(filters.Contains("[gameaudio][voicekey]sidechaincompress", StringComparison.Ordinal), "Only nonvoice tracks should be ducked.");
        graph.Clear();
        FfmpegStudioCompositionGraph.AppendAudio(graph, asset.SourceMedia,
            new StudioRenderSettings(audioTracks: [new(1, muted: true), new(2, 3)]));
        TestAssert.False(string.Join(';', graph).Contains("[0:1]", StringComparison.Ordinal), "Muted tracks must not enter the mix.");
        return Task.CompletedTask;
    }

    private static async Task RenderCheckpointReusesOnlyIntactOutput()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        string root = Path.Combine(fixture.Root, "checkpoints");
        var cache = new StudioRenderCheckpointCache(root);
        string key = new('A', 64);
        string original = Path.Combine(fixture.Root, "encoded.mp4");
        string restored = Path.Combine(fixture.Root, "restored.mp4");
        await File.WriteAllBytesAsync(original, [1, 2, 3, 4]);
        await cache.StoreAsync(key, original, CancellationToken.None);
        TestAssert.True(await cache.TryRestoreAsync(key, restored, CancellationToken.None),
            "A validated completed segment must survive a later batch retry.");
        TestAssert.True(File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(restored)),
            "A reused segment must be byte-identical.");
        await File.WriteAllBytesAsync(Path.Combine(root, key, "segment.mp4"), [9, 9]);
        TestAssert.False(await cache.TryRestoreAsync(key, Path.Combine(fixture.Root, "corrupt.mp4"), CancellationToken.None),
            "A corrupt cache entry must never be reused.");
        await cache.StoreAsync(key, original, CancellationToken.None);
        TestAssert.True(await cache.TryRestoreAsync(key, Path.Combine(fixture.Root, "repaired.mp4"), CancellationToken.None),
            "A rejected corrupt entry must be replaceable by a newly validated output.");
        TestAssert.False(await cache.TryRestoreAsync(key, restored, CancellationToken.None),
            "Checkpoint recovery cannot overwrite an existing destination.");
        TestAssert.False(Directory.EnumerateFiles(fixture.Root, "*.restoring-*").Any(),
            "A failed restore must not leave partial media beside the destination.");
    }

    private static Task OutputProfilePreservesSourceCadence()
    {
        foreach (MediaRational cadence in new[] { new MediaRational(24, 1), new MediaRational(25, 1),
            new MediaRational(30000, 1001), new MediaRational(60000, 1001) })
        {
            var media = TestMediaFactory.Create(TestMediaFactory.CreateSourcePath("cadence.mkv"), frameRate: cadence);
            GenerationClipOutputProfile profile = GenerationClipOutputProfile.FromReference(media.PrimaryVideoStream);
            TestAssert.NearlyEqual(cadence.ToDouble(), profile.FramesPerSecond, 1e-10,
                "Export must not repeat source frames merely to force a 30/60fps preset.");
            var command = FfmpegClipRenderCommandBuilder.BuildSegment(media, TimeSpan.Zero, TimeSpan.FromSeconds(5),
                profile, Path.Combine(Path.GetDirectoryName(media.FullPath)!, "cadence.mp4"));
            TestAssert.True(command.Arguments.Any(argument => argument.Contains("fps=" + profile.FfmpegFrameRate, StringComparison.Ordinal)),
                "FFmpeg must receive the actual source cadence.");
        }
        return Task.CompletedTask;
    }

    private static async Task HardwareEncodingQualifiesAndFallsBack()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        string executable = Path.Combine(fixture.Root, "qualified-ffmpeg.exe");
        File.WriteAllBytes(executable, [1]);
        string output = Path.Combine(fixture.Root, "hardware-fallback.mp4");
        var runner = new EncoderScenarioRunner(output, rejectHardware: true);
        var executor = new FfmpegEncodingExecutor(runner);
        string[] arguments = new[] { "-n" }.Concat(FfmpegH264EncodingPolicy.CreateArguments(4_000_000)).Append(output).ToArray();
        ProcessRunResult result = await executor.RunAsync(new ProcessRunRequest(executable, arguments, TimeSpan.FromSeconds(5)), CancellationToken.None);
        TestAssert.True(result.Succeeded, "A rejected real hardware encode must fall back to the baseline software encoder.");
        TestAssert.Equal(4, runner.Requests.Count, "Qualification encode+decode must precede actual encode+software retry.");
        TestAssert.True(ContainsPair(runner.Requests[2].Arguments, "-c:v", "h264_nvenc"), "The qualified encoder must be selected explicitly.");
        TestAssert.False(runner.Requests[2].Arguments.Contains("-hw_encoding"), "Media Foundation-only options cannot leak to NVENC.");
        TestAssert.True(ContainsPair(runner.Requests[3].Arguments, "-hw_encoding", "0"), "Fallback must retain the tested software policy.");
        await executor.RunAsync(new ProcessRunRequest(executable, arguments, TimeSpan.FromSeconds(5)), CancellationToken.None);
        TestAssert.Equal(5, runner.Requests.Count, "Failed hardware must remain disabled for subsequent clips in the session.");
    }

    private static async Task HardwareValidationFailureRetriesSoftware()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        foreach (bool probeFailure in new[] { false, true })
        {
            string executable = Path.Combine(fixture.Root, $"validation-{probeFailure}-ffmpeg.exe");
            File.WriteAllBytes(executable, [2]);
            string output = Path.Combine(fixture.Root, $"validation-{probeFailure}-fallback.mp4");
            var runner = new EncoderScenarioRunner(output, rejectHardware: false);
            var executor = new FfmpegEncodingExecutor(runner);
            string[] arguments = new[] { "-n" }.Concat(FfmpegH264EncodingPolicy.CreateArguments(4_000_000)).Append(output).ToArray();
            int validationCalls = 0;
            ProcessRunResult result = await executor.RunAsync(new ProcessRunRequest(executable, arguments, TimeSpan.FromSeconds(5)), CancellationToken.None,
                _ =>
                {
                    if (++validationCalls == 1)
                    {
                        if (probeFailure) throw new MediaProbeException("ffprobe rejected the hardware container");
                        throw new InvalidOperationException("hardware bitstream does not decode");
                    }
                    return Task.CompletedTask;
                });
            TestAssert.True(result.Succeeded, "Undecodable hardware containers or streams must be replaced by valid software output.");
            TestAssert.Equal(2, validationCalls, "The software fallback must pass the same output validator before acceptance.");
            TestAssert.Equal(4, runner.Requests.Count, "Only one bounded software retry is allowed.");
        }
    }

    private static Task FrameEditsRetainSourceTiming()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset;
        var settings = asset.RenderSettings.WithFrameEdits(
            [new(asset.SourceStart, 1), new(asset.SourceStart + TimeSpan.FromSeconds(4), 1.5, 70, 40)],
            [new("Heads up\nWatch here", asset.SourceStart, asset.SourceStart + TimeSpan.FromSeconds(3))]);
        StudioRenderSettings restored = JsonSerializer.Deserialize<StudioRenderSettings>(JsonSerializer.Serialize(settings))!;
        TestAssert.Equal(settings.CanonicalIdentity(), restored.CanonicalIdentity(), "Source framing and timed text must persist with the project.");
        asset = asset.WithRenderSettings(settings);
        var profile = GenerationClipOutputProfile.FromAsset(asset);
        var full = new List<string>();
        var trimmed = new List<string>();
        FfmpegStudioCompositionGraph.AppendVideo(full, asset.SourceMedia, profile, settings, asset.SourceStart);
        FfmpegStudioCompositionGraph.AppendVideo(trimmed, asset.SourceMedia, profile, settings, asset.SourceStart + TimeSpan.FromSeconds(1));
        TestAssert.True(string.Join(';', full).Contains("lt(in_time,4)", StringComparison.Ordinal), "The full clip reaches its second waypoint at four seconds.");
        TestAssert.True(string.Join(';', trimmed).Contains("lt(in_time,3)", StringComparison.Ordinal), "Trimming a second must retain the original waypoint's source timing.");
        string script = StudioTimedTextScript.Build(settings.TimedTextOverlays, profile,
            asset.SourceStart + TimeSpan.FromSeconds(1), asset.SourceStart + TimeSpan.FromSeconds(2));
        TestAssert.True(script.Contains("Dialogue: 0,0:00:00.00,0:00:01.00", StringComparison.Ordinal), "A text overlay crossing the trim must clip to the output's exact interval.");
        TestAssert.True(script.Contains("Heads up\\NWatch here", StringComparison.Ordinal), "Newlines must use owned subtitle escaping, not filter interpolation.");
        GenerationOutputAsset edited = asset.WithStudioEdits(asset.SourceStart + TimeSpan.FromSeconds(1), asset.SourceEnd, asset.Appearance);
        TestAssert.Equal(settings.CanonicalIdentity(), edited.RenderSettings.CanonicalIdentity(), "A trim must never rewrite source-anchored motion/text edits.");
        return Task.CompletedTask;
    }

    private static async Task HardwareCancellationDoesNotRetry()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        using var cancellation = new CancellationTokenSource();
        string executable = Path.Combine(fixture.Root, "cancel-ffmpeg.exe");
        File.WriteAllBytes(executable, [3]);
        string output = Path.Combine(fixture.Root, "canceled-encode.mp4");
        var runner = new EncoderScenarioRunner(output, rejectHardware: false, cancellation);
        var executor = new FfmpegEncodingExecutor(runner);
        string[] arguments = new[] { "-n" }.Concat(FfmpegH264EncodingPolicy.CreateArguments(4_000_000)).Append(output).ToArray();
        await TestAssert.ThrowsAsync<OperationCanceledException>(() => executor.RunAsync(
            new ProcessRunRequest(executable, arguments, TimeSpan.FromSeconds(5)), cancellation.Token),
            "Cancel must stop the render rather than launch another encoder.");
        TestAssert.Equal(3, runner.Requests.Count, "Only qualification encode/decode and the canceled actual encode may run.");
        TestAssert.False(File.Exists(output), "Cancellation before output must leave no completed segment.");
    }

    private static Task PortraitCompositionPreservesFullRecording()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        var portrait = TestMediaFactory.Create(TestMediaFactory.CreateSourcePath("precomposed-portrait.mkv"), width: 1080, height: 1920);
        var settings = StudioRenderSettings.FromComposition(fixture.Moments.Sources[0].AnalyzedSource.CompositionPlan.Plan,
            fixture.CreateDraft().PrimaryAsset.SourceStart, portrait.PrimaryVideoStream);
        TestAssert.Equal(StudioOutputCanvas.Portrait, settings.Canvas, "The export canvas must stay explicit.");
        TestAssert.Equal(StudioCompositionLayout.Fit, settings.Layout, "An already-portrait recording must not have its panes cropped and stacked again.");
        TestAssert.Equal(1d, settings.GameplayRegion.Width, "Existing gameplay HUD and facecam must remain within the full source frame.");
        return Task.CompletedTask;
    }

    private static Task CutListsPreserveSourceTiming()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.Montage, hasAudio: true, captionsEnabled: true,
            sourceName: "cut-list-" + Guid.NewGuid().ToString("N") + ".mkv");
        var tracks = fixture.Moments.SelectedCandidates.Select(candidate => new GenerationCandidateCaptionTrack(candidate,
            fixture.GenerationRequest.SetupOptions.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean, CreateTranscription(candidate))).ToArray();
        GenerationOutputProject draft = fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, tracks, TimeSpan.Zero));
        GenerationOutputAsset before = draft.PrimaryAsset;
        TimeSpan splitTime = before.SourceStart + before.Duration / 2;
        double duration = draft.Assets.Sum(asset => asset.Duration.TotalSeconds);
        GenerationOutputProject split = draft.SplitAsset(before.Id, splitTime);
        TestAssert.Equal(draft.Assets.Count + 1, split.Assets.Count, "Splitting must create two independently editable source sections.");
        TestAssert.Equal(splitTime, split.Assets[0].SourceEnd, "The first section ends at the exact split.");
        TestAssert.Equal(splitTime, split.Assets[1].SourceStart, "The second section starts at the exact split, without a gap or repeat.");
        TestAssert.NearlyEqual(duration, split.Assets.Sum(asset => asset.Duration.TotalSeconds), 1e-8, "Splitting alone must preserve total content duration.");
        TestAssert.Equal(split.Assets[1].Id, split.Assets[1].Captions!.CandidateId, "The new section must own its caption identity.");
        TestAssert.Equal(before.Captions!.Segments[0].AbsoluteSourceStart, split.Assets[1].Captions!.Segments[0].AbsoluteSourceStart,
            "Measured caption times must remain anchored to the source, even where a later section clips the cue.");
        TestAssert.Same(before.EditorialContext!.GameContext, split.Assets[1].EditorialContext!.GameContext,
            "A split must retain confirmed game identity while rebuilding claims about the new section.");
        TestAssert.Equal(0d, split.Assets[1].Score, "A new manually selected section has not received its own automatic score.");
        TestAssert.True(split.Assets.Select(asset => asset.Rank).SequenceEqual(Enumerable.Range(1, split.Assets.Count)), "Ranks must remain contiguous after insertion.");
        string movedId = split.Assets[1].Id;
        GenerationOutputProject reordered = split.MoveAsset(movedId, -1);
        TestAssert.Equal(movedId, reordered.PrimaryAsset.Id, "Moving a section must change its rendered ordering.");
        GenerationOutputProject omitted = reordered.ReplaceAsset(reordered.PrimaryAsset.WithDisposition(GenerationOutputAssetDisposition.ExcludeFromFinalRender));
        TestAssert.Equal(split.IncludedCount - 1, omitted.IncludedCount, "Removed sections remain recoverable but must leave the montage assembly.");
        // Renderer fixtures ordinarily use probe-only source metadata. Persistence additionally
        // snapshots the real source's size/write time, so this test owns a unique physical input.
        File.WriteAllBytes(before.SourceFullPath, [1, 2, 3, 4]);
        try
        {
            GenerationOutputProject restored = StudioProjectDocumentMapper.Restore(StudioProjectDocumentMapper.Capture(omitted, 1, DateTimeOffset.UtcNow));
            TestAssert.Equal(movedId, restored.PrimaryAsset.Id, "Project persistence must retain cut-list ordering.");
            TestAssert.Equal(draft.SourceMedia.Count, restored.SourceMedia.Count, "All original source recordings must remain available after timeline edits.");
            TestAssert.False(restored.PrimaryAsset.IsIncludedInFinalRender, "Project persistence must retain omitted sections.");
        }
        finally { File.Delete(before.SourceFullPath); }
        return Task.CompletedTask;
    }

    private static Task FrameEditInputsRebaseAfterTrim()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        GenerationOutputProject draft = fixture.CreateDraft();
        GenerationOutputAsset asset = draft.PrimaryAsset;
        TimeSpan waypointSource = asset.SourceStart + TimeSpan.FromSeconds(1);
        asset = asset.WithRenderSettings(asset.RenderSettings.WithFrameEdits([new(waypointSource, 1.5)],
            [new("Retained text", asset.SourceStart, asset.SourceStart + TimeSpan.FromSeconds(4), centerXPercent: 27)]));
        draft = draft.ReplaceAsset(asset);
        var session = new GenerationOutputSession(); session.Publish(draft);
        var editor = new StudioFrameEditsViewModel(session);
        editor.Bind(draft, asset); editor.SelectedFrame = editor.Frames[0];
        GenerationOutputAsset trimmed = asset.WithStudioEdits(asset.SourceStart + TimeSpan.FromSeconds(1), asset.SourceEnd, asset.Appearance);
        GenerationOutputProject trimmedProject = draft.ReplaceAsset(trimmed);
        session.Publish(trimmedProject); editor.Bind(trimmedProject, trimmed);
        TestAssert.Equal(0d, editor.FrameOffsetSeconds, "A source waypoint at the new trim start must display zero seconds.");
        editor.SaveFrameCommand.Execute(null);
        TestAssert.Equal(waypointSource, session.Current!.PrimaryAsset.RenderSettings.FrameKeyframes[0].SourcePosition,
            "Saving a rebased waypoint must not move it later in the recording.");
        editor.Bind(session.Current, session.Current.PrimaryAsset); editor.SelectedText = editor.Texts[0];
        editor.Text = "Changed content"; editor.SaveTextCommand.Execute(null);
        TestAssert.Equal(27d, session.Current!.PrimaryAsset.RenderSettings.TimedTextOverlays[0].CenterXPercent,
            "Editing text must preserve its existing horizontal position.");
        TestAssert.Equal(asset.SourceStart, session.Current.PrimaryAsset.RenderSettings.TimedTextOverlays[0].SourceStart,
            "A saved overlay crossing the new trim must retain its original source timing.");
        return Task.CompletedTask;
    }

    private sealed class EncoderScenarioRunner(string destination, bool rejectHardware, CancellationTokenSource? cancellation = null) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            string output = request.Arguments[^1];
            if (output != "-")
            {
                if (output == destination && request.Arguments.Contains("h264_nvenc") && cancellation is not null)
                {
                    cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested();
                }
                if (File.Exists(output)) return Task.FromResult(new ProcessRunResult(1, "", "output already exists", TimeSpan.Zero));
                if (output == destination && request.Arguments.Contains("h264_nvenc") && rejectHardware)
                    return Task.FromResult(new ProcessRunResult(1, "", "encoder rejected real input", TimeSpan.Zero));
                File.WriteAllBytes(output, [1, 2, 3]);
            }
            return Task.FromResult(new ProcessRunResult(0, "", "", TimeSpan.Zero));
        }
    }
}
