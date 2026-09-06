using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Guidance;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static async Task FolderImportRejectsActiveAndChangingRecordings()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        string folder = Path.Combine(fixture.Root, "recordings"); Directory.CreateDirectory(folder);
        string stable = Path.Combine(folder, "stable.MKV"), active = Path.Combine(folder, "active.mp4"), changing = Path.Combine(folder, "changing.mov");
        File.WriteAllBytes(stable, [1, 2]); File.WriteAllBytes(active, [1]); File.WriteAllBytes(changing, [1]);
        File.WriteAllBytes(Path.Combine(folder, "empty.mkv"), []);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a recording");
        using var writer = new FileStream(active, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        Task<StableVideoFolderImport> scan = new StableVideoFolderImporter().ScanAsync(folder, CancellationToken.None, TimeSpan.FromMilliseconds(100));
        File.AppendAllText(changing, "another packet");
        StableVideoFolderImport result = await scan;
        TestAssert.Equal(1, result.ReadyFiles.Count, "Only a complete stable closed recording should be admitted.");
        TestAssert.Equal(stable, result.ReadyFiles[0], "Import must retain the source path.");
        TestAssert.Equal(3, result.SkippedFiles.Count, "Empty, actively written, and changing recordings should be reported.");
    }

    private static Task MarkerImportPreservesBoundedSourceGuidance()
    {
        string source = TestMediaFactory.CreateSourcePath("marked-recording.mkv");
        TimeSpan duration = TimeSpan.FromMinutes(2);
        var csv = MomentMarkerImporter.ParseCsv("timestamp,end,label\r\n00:10.500,,\"A, moment\"\r\n20,25,range\r\n10.5,,duplicate\r\n", source, duration);
        TestAssert.Equal(2, csv.Count, "Duplicate timestamp markers should collapse by stable guidance identity.");
        TestAssert.Equal(TimeSpan.FromSeconds(10.5), csv[0].Start, "CSV timecodes should preserve subsecond markers.");
        TestAssert.Equal(UserMomentGuidanceKind.PriorityRange, csv[1].Kind, "CSV start and end should import a prioritized range.");
        TestAssert.Equal(TimeSpan.FromSeconds(25), csv[1].End, "The imported range must preserve its source end.");
        var chapters = MomentMarkerImporter.ParseChapterJson("{\"chapters\":[{\"start_time\":\"10.500000\",\"end_time\":\"30\"},{\"start_time\":\"30\"}]}", source, duration);
        TestAssert.Equal(csv[0].Id, chapters[0].Id, "OBS recording chapters and CSV points should share the same source-bound identities.");
        TestAssert.True(chapters.All(static marker => marker.Kind == UserMomentGuidanceKind.PriorityPoint), "A chapter start marks a moment without selecting the whole chapter as a highlight.");
        TestAssert.Throws<ArgumentOutOfRangeException>(() => MomentMarkerImporter.ParseCsv("start,end\n20,121\n", source, duration), "Markers outside the recording should be rejected before mutation.");
        TestAssert.Throws<FormatException>(() => MomentMarkerImporter.ParseCsv("NaN", source, duration), "Nonfinite marker times should be rejected.");
        TestAssert.Throws<FormatException>(() => MomentMarkerImporter.ParseChapterJson("{}", source, duration), "Malformed chapter documents should report an import error.");
        return Task.CompletedTask;
    }

    private static async Task PlatformPresetAndPublishingPackagePreserveEdits()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        var project = fixture.CreateDraft();
        var asset = project.PrimaryAsset;
        var current = new StudioRenderSettings(StudioOutputCanvas.Landscape,
            audioTracks: [new(1, -6)], quality: StudioExportQuality.High,
            frameKeyframes: [new(asset.SourceStart, 1.2)],
            timedTextOverlays: [new("Watch this", asset.SourceStart, asset.SourceEnd)]);
        var settings = StudioPlatformExportPresets.Apply(StudioPlatformExportPreset.TikTok, current);
        TestAssert.Equal(StudioOutputCanvas.Portrait, settings.Canvas, "Social presets should choose the portrait output contract.");
        TestAssert.Equal(StudioExportQuality.High, settings.Quality, "Applying a platform preset must retain the user's quality choice.");
        TestAssert.Equal(-6d, settings.AudioTracks[0].GainDecibels, "Applying a preset must retain the source audio mix.");
        TestAssert.Equal(1, settings.FrameKeyframes.Count, "A preset must not discard manual framing.");
        TestAssert.Equal("Watch this", settings.TimedTextOverlays[0].Text, "A preset must not discard hook cards.");
        var appearance = StudioPlatformExportPresets.Apply(StudioPlatformExportPreset.TikTok, asset.Appearance);
        TestAssert.Equal(StudioCaptionSafeArea.TikTok, appearance.CaptionTypography.SafeArea, "The preset must align caption placement with its preview guide.");
        string folder = Path.Combine(fixture.Root, "publishing"); Directory.CreateDirectory(folder);
        string output = Path.Combine(folder, "clip.mp4"); File.WriteAllBytes(output, [1]);
        File.WriteAllText(Path.Combine(folder, "clip.srt"), "captions");
        var exported = asset.WithStudioEdits(asset.SourceStart, asset.SourceEnd, appearance).WithRenderSettings(settings).WithRenderedOutput(output);
        await StudioPlatformExportPackageWriter.WriteAsync(project, [exported], folder, CancellationToken.None);
        using var package = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "clip.publish.json")));
        TestAssert.Equal("clip.mp4", package.RootElement.GetProperty("files").GetProperty("video").GetString()!, "Publishing bundles should use portable relative filenames.");
        TestAssert.Equal("clip.srt", package.RootElement.GetProperty("files").GetProperty("subtitlesSrt").GetString()!, "The package should reference the actual exported sidecar.");
        TestAssert.True(package.RootElement.GetProperty("files").GetProperty("subtitlesVtt").ValueKind == JsonValueKind.Null, "The package must not promise files that were not emitted.");
        TestAssert.True(File.Exists(Path.Combine(folder, "Publishing Guide.md")) && File.Exists(Path.Combine(folder, "clip.publish.md")), "Each output should contain human-readable publishing text and a guide.");
        using var sourceTimeline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "source-cuts.otio")));
        JsonElement sourceClip = sourceTimeline.RootElement.GetProperty("tracks").GetProperty("children")[0].GetProperty("children")[0];
        JsonElement clock = sourceClip.GetProperty("source_range").GetProperty("start_time");
        TestAssert.Equal(exported.SourceStart.TotalSeconds, clock.GetProperty("value").GetDouble() / clock.GetProperty("rate").GetDouble(),
            "OTIO source timelines must retain the actual source clock.");
        TestAssert.Equal(new Uri(exported.SourceFullPath).AbsoluteUri, sourceClip.GetProperty("media_reference").GetProperty("target_url").GetString()!,
            "Original-cut handoff must reference the original source, not a staging render.");
        using var renderedTimeline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "rendered.otio")));
        JsonElement renderedClip = renderedTimeline.RootElement.GetProperty("tracks").GetProperty("children")[0].GetProperty("children")[0];
        TestAssert.Equal("clip.mp4", Uri.UnescapeDataString(renderedClip.GetProperty("media_reference").GetProperty("target_url").GetString()!),
            "Rendered handoff must survive the staging directory's atomic move.");
    }

    private static Task CaptionAuditionUsesChosenStreamAndCurrentCut()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        var asset = fixture.CreateDraft().PrimaryAsset;
        var trimmed = asset.WithStudioEdits(asset.SourceStart + TimeSpan.FromSeconds(2), asset.SourceEnd, asset.Appearance);
        int streamIndex = asset.SourceMedia.AudioStreams[^1].Index;
        var request = WpfStudioCaptionAudioAuditionSession.CreateRequest(trimmed, streamIndex);
        TestAssert.Equal(streamIndex, request.AbsoluteAudioStreamIndex, "Audition must isolate exactly the user's chosen source track.");
        TestAssert.Equal(trimmed.SourceStart, request.Start, "The audition must use the current cut rather than a representative point elsewhere in the recording.");
        TestAssert.True(request.Duration <= TimeSpan.FromSeconds(15) && request.End <= trimmed.SourceEnd, "Auditions should remain inside a bounded part of the actual cut.");
        TestAssert.Equal("peak -20.0 dBFS", WpfStudioCaptionAudioAuditionSession.DescribePeak(0.1), "The level label must show measured PCM amplitude without automatic normalization.");
        TestAssert.Equal("digital silence", WpfStudioCaptionAudioAuditionSession.DescribePeak(0), "Zero samples should be reported honestly.");
        using var editor = new StudioCaptionTrackEditorViewModel(new GenerationOutputSession());
        editor.Bind(fixture.CreateDraft(), asset);
        TestAssert.Equal(asset.SourceMedia.AudioStreams[0].Index, editor.SelectedAudioStreamIndex,
            "A clip without saved captions should start with its first actual audio stream selected.");
        TestAssert.True(editor.AudioStreams.All(choice => choice.ToString() == choice.Label && choice.Label.StartsWith("Track ", StringComparison.Ordinal)),
            "Collapsed themed ComboBox selections must display a friendly track label even when its template uses ToString.");
        return Task.CompletedTask;
    }
}
