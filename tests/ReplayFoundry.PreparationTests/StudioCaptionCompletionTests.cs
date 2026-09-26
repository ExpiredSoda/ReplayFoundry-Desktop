using System.IO;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.RecentProjects;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Media.Transcription;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task RegeneratedCaptionsFollowMovedStudioCut()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        var original = fixture.CreateDraft().PrimaryAsset;
        var start = original.SourceEnd + TimeSpan.FromSeconds(10);
        var end = start + TimeSpan.FromSeconds(5);
        var moved = original.WithStudioEdits(start, end, original.Appearance);
        var selection = new GenerationCaptionSourceSelection(original.SourceFullPath,
            original.SourceMedia.AudioStreams[0].Index, CaptionAudioContentRole.CreatorCommentary);
        var track = GenerationCandidateCaptionTrack.RestoreStudioHandoff(original.Id, "moved-speech", selection,
            GenerationCaptionStylePreset.Clean, start, end - start, original.SourceDuration,
            [new AudioTranscriptionSegment("new-phrase", "moved-speech", "Speech in the new cut.",
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3),
                start + TimeSpan.FromSeconds(1), start + TimeSpan.FromSeconds(3))],
            isUserEdited: false, GenerationCaptionSuppressionReason.None);

        // Studio regeneration, subtitle import, and saved word edits all use
        // this boundary after a trim; the old editorial cut remains retained.
        var updated = moved.WithCaptionTrack(track);
        TestAssert.Equal(start, updated.EditorialContext!.SourceStart,
            "New speech must be validated against the edited cut, not the Generate-time window.");
        TestAssert.Equal(end, updated.EditorialContext.SourceEnd,
            "Caption updates must refresh both editorial boundaries.");
        TestAssert.Equal("Speech in the new cut.", updated.EditorialContext.Transcripts.Single().Text,
            "Newly reached speech must remain available for titles and descriptions.");
        TestAssert.Equal(start + TimeSpan.FromSeconds(1), updated.Captions!.Segments.Single().AbsoluteSourceStart,
            "Refreshing editorial context must not move measured speech timestamps.");
        TestAssert.Equal(original.SourceStart, original.EditorialContext!.SourceStart,
            "Updating captions must not mutate the retained original asset.");
        var repeated = updated.WithCaptionTrack(track);
        TestAssert.Equal(1, repeated.EditorialContext!.Transcripts.Count,
            "Repeated caption saves must replace the stream rather than duplicate it.");
        return Task.CompletedTask;
    }

    private static Task ClearCaptionedRecentProjectWithMissingSource()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true,
            sourceName: "clear-missing-" + Guid.NewGuid().ToString("N") + ".mkv");
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep, CreateTranscription(candidate));
        var project = fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, [track], TimeSpan.Zero));
        Directory.CreateDirectory(Path.GetDirectoryName(project.PrimaryAsset.SourceMedia.FullPath)!);
        File.WriteAllText(project.PrimaryAsset.SourceMedia.FullPath, "isolated source fixture");
        var session = new GenerationOutputSession();
        var store = new JsonStudioProjectStore(Path.Combine(fixture.Root, "saved-projects"));
        store.Save(project, revision: 1);
        var recentStore = new JsonRecentGenerationProjectStore(Path.Combine(fixture.Root, "recent.json"));
        using var catalog = new RecentGenerationProjectCatalog(session, recentStore, store);
        session.Publish(project);
        using var studio = new ReplayFoundry.Desktop.Features.Studio.StudioViewModel(session);

        // Both transient empty ranges and a range from a different recording
        // can arrive before the previous caption track has been unbound.
        studio.Preview.UpdateRange(TimeSpan.Zero, TimeSpan.Zero);
        TestAssert.Null(studio.Preview.LiveCaptionText, "An empty selection must clear the previous caption.");
        studio.Preview.UpdateRange(TimeSpan.Zero, project.PrimaryAsset.SourceDuration + TimeSpan.FromSeconds(1));
        TestAssert.Null(studio.Preview.LiveCaptionText, "A new source range must not be projected onto the old transcript.");
        studio.Preview.UpdateRange(project.PrimaryAsset.SourceStart, project.PrimaryAsset.SourceEnd);
        File.Delete(project.PrimaryAsset.SourceMedia.FullPath);

        TestAssert.Equal(1, catalog.ClearAll(), "Clear all should remove the unavailable recent project.");
        TestAssert.Null(session.Current, "The output session must release the cleared project.");
        TestAssert.Null(studio.Preview.LiveCaptionText, "Clearing the project must clear its caption frame.");
        TestAssert.False(studio.HasProject, "Studio must show its empty state.");
        TestAssert.Equal(0, recentStore.Read().Count, "Cleared projects must stay cleared after restart.");
        TestAssert.False(store.Exists(project.Id), "The saved draft must be removed even when its video is missing.");
        return Task.CompletedTask;
    }

    private static Task CaptionCutRoundTripsKeepAbsoluteWordClocks()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep, CreateTranscription(candidate));
        var initial = fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, [track], TimeSpan.Zero));
        var original = initial.PrimaryAsset;
        var ranges = new[]
        {
            (original.SourceStart, original.SourceEnd),
            (original.SourceStart + TimeSpan.FromSeconds(.5), original.SourceEnd),
            (original.SourceStart + TimeSpan.FromSeconds(1.2), original.SourceStart + TimeSpan.FromSeconds(2)),
            (original.SourceStart + TimeSpan.FromSeconds(2.5), original.SourceEnd),
            (original.SourceStart, original.SourceStart + TimeSpan.FromSeconds(.5)),
            (TimeSpan.FromTicks(Math.Max(0, original.SourceStart.Ticks - TimeSpan.TicksPerSecond)),
                TimeSpan.FromTicks(Math.Min(original.SourceDuration.Ticks, original.SourceEnd.Ticks + TimeSpan.TicksPerSecond))),
        };
        foreach (var (start, end) in ranges)
        {
            var asset = original.WithStudioEdits(start, end, original.Appearance);
            var project = initial.ReplaceAsset(asset);
            var session = new GenerationOutputSession(); session.Publish(project);
            var drafts = StudioCaptionTrackEditing.CreateDrafts(asset);
            TestAssert.Equal((track.Segments[0].AbsoluteSourceStart - start).TotalSeconds, drafts[0].StartSeconds,
                "Every unchanged draft, including a retained off-cut phrase, must use the visible cut's origin.");
            var saved = StudioCaptionTrackEditing.Apply(session, project, asset, drafts);
            TestAssert.Equal(track.Segments.Count, saved.Captions!.Segments.Count, "Saving an unchanged trim cannot delete retained source phrases.");
            var expected = track.Segments[0]; var actual = saved.Captions.Segments[0];
            TestAssert.Equal(expected.Text, actual.Text, "An unchanged save must retain every word and punctuation mark.");
            TestAssert.Equal(expected.AbsoluteSourceStart, actual.AbsoluteSourceStart, "Phrase starts cannot shift after unchanged, trimmed or extended saves.");
            TestAssert.Equal(expected.AbsoluteSourceEnd, actual.AbsoluteSourceEnd, "Phrase ends cannot shift after unchanged, trimmed or extended saves.");
            TestAssert.Equal(expected.Words.Count, actual.Words.Count, "Unchanged speech must keep word animation after an editor save.");
            for (int i = 0; i < expected.Words.Count; i++)
            {
                TestAssert.Equal(expected.Words[i].Text, actual.Words[i].Text, "Word text must round-trip exactly.");
                TestAssert.Equal(expected.Words[i].AbsoluteSourceStart, actual.Words[i].AbsoluteSourceStart, "Observed word starts cannot move.");
                TestAssert.Equal(expected.Words[i].AbsoluteSourceEnd, actual.Words[i].AbsoluteSourceEnd, "Observed word ends cannot move.");
                TestAssert.Equal(actual.Words[i].AbsoluteSourceStart - saved.Captions.SourceWindowStart, actual.Words[i].RelativeStart,
                    "Extended source windows must recompute their relative offset without moving the observed word.");
            }
            TestAssert.Equal(SubtitleSidecarSerializer.Build(track, start, end - start, SubtitleSidecarFormat.WebVtt),
                SubtitleSidecarSerializer.Build(saved.Captions, start, end - start, SubtitleSidecarFormat.WebVtt),
                "An unchanged editor round trip must produce byte-identical cut-relative subtitles.");
            var editor = new StudioCaptionTrackEditorViewModel(session); editor.Bind(project, asset);
            string hint = editor.Segments[0].TimingHint;
            if (expected.AbsoluteSourceEnd <= start) TestAssert.True(hint.StartsWith("Before this cut", StringComparison.Ordinal), "A wholly earlier phrase needs a before-cut marker.");
            if (expected.AbsoluteSourceStart >= end) TestAssert.True(hint.StartsWith("After this cut", StringComparison.Ordinal), "A wholly later phrase needs an after-cut marker.");
            if (expected.AbsoluteSourceStart < start && expected.AbsoluteSourceEnd > end) TestAssert.True(hint.Contains("both cut boundaries", StringComparison.Ordinal), "A crossing phrase must identify both boundaries.");
            editor.Dispose();
        }
        return Task.CompletedTask;
    }

    private static Task NamedCaptionLookCatalogMigratesExplicitly()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        string path = Path.Combine(fixture.Root, "legacy-caption-looks.json");
        var look = new StudioCaptionLook(GenerationCaptionStylePreset.Clean, 75, StudioCaptionWordLimitPreset.FullSegment, 80, 100,
            new(safeAreaInsets: new(10, 15, 20, 25)));
        string legacy = JsonSerializer.Serialize(new[] { new StudioNamedCaptionLook("Original", look) });
        File.WriteAllText(path, legacy);
        var store = new JsonStudioCaptionLookStore(path);
        TestAssert.Equal(look, store.Load().Single().Look, "Legacy arrays must load without losing complete styles.");
        TestAssert.Equal(legacy, File.ReadAllText(path), "Reading a legacy catalog must not rewrite user settings during startup.");
        store.Save("Second", look);
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            TestAssert.Equal(JsonStudioCaptionLookStore.CurrentSchemaVersion, document.RootElement.GetProperty("SchemaVersion").GetInt32(), "The first explicit save must migrate to the versioned envelope.");
            TestAssert.Equal(2, document.RootElement.GetProperty("Looks").GetArrayLength(), "Migration must retain the complete existing catalog.");
        }
        TestAssert.True(store.Load().All(item => item.Look == look), "Versioned styles must round-trip custom safe-area margins.");
        string future = JsonSerializer.Serialize(new StudioCaptionLookDocument(999, [new("Future", look)]));
        File.WriteAllText(path, future);
        TestAssert.Throws<InvalidDataException>(() => store.Save("Replacement", look), "An unknown future schema must not be silently overwritten.");
        TestAssert.Equal(future, File.ReadAllText(path), "Rejected schema migration must leave the original catalog intact.");
        return Task.CompletedTask;
    }

    private static Task MissingCaptionFontsResolveConsistently()
    {
        var exact = StudioCaptionFontResolver.Resolve("arial", ["Segoe UI", "Arial"]);
        TestAssert.Equal("Arial", exact.Family, "Case-insensitive matching must resolve the installed canonical family.");
        TestAssert.False(exact.IsFallback, "A case-only difference is not a missing font.");
        TestAssert.Equal("Segoe UI", StudioCaptionFontResolver.Resolve("Missing", ["Arial", "Segoe UI"]).Family, "Missing font fallback must be deterministic.");
        const string missing = "Replay Foundry Test Missing Font 95AFDB";
        var resolved = StudioCaptionFontResolver.Resolve(missing);
        var typography = new StudioCaptionTypography(missing);
        UiUxApplicationSurfaceTests.RunOnSta(() =>
        {
            var preview = new StudioCaptionPreviewText { CaptionTypography = typography };
            TestAssert.Equal(resolved.Family, preview.ResolvedCaptionFontFamily, "WPF preview must use the shared resolved family.");
        });
        var fallback = new StudioCaptionTypography(resolved.Family);
        var missingLines = StudioCaptionLineLayout.Create("Measured caption words", typography, 32, 180);
        var fallbackLines = StudioCaptionLineLayout.Create("Measured caption words", fallback, 32, 180);
        TestAssert.Equal(JsonSerializer.Serialize(fallbackLines), JsonSerializer.Serialize(missingLines), "Missing-font measurement must match the exact family passed to preview and ASS.");
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean, CreateTranscription(candidate));
        var ass = AssSubtitleDocumentBuilder.Build(track, 1080, 1920, captionTypography: typography);
        TestAssert.True(ass.Script.Contains("Style: Clean," + resolved.Family + ",", StringComparison.Ordinal), "Final ASS must receive the same explicit fallback family as WPF.");
        TestAssert.True(ass.Warnings.Contains(resolved.Warning!), "Export must report that the saved family was unavailable.");
        TestAssert.Equal(missing, typography.FontFamily, "Font resolution cannot overwrite the saved user's intent.");
        return Task.CompletedTask;
    }

    private static Task CustomCaptionSafeAreasReportMeasuredOverflow()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(() => new StudioCaptionSafeAreaInsets(leftPercent: 46), "Custom safe areas must reject unbounded margins.");
        TestAssert.Equal(72d, new StudioCaptionTypography(safeArea: StudioCaptionSafeArea.TikTok).ConstrainVerticalPosition(90), "Legacy presets must retain their existing position policy.");
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean, CreateTranscription(candidate));
        var project = fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, [track], TimeSpan.Zero));
        var typography = new StudioCaptionTypography(safeArea: StudioCaptionSafeArea.TikTok, safeAreaInsets: new(45, 12, 45, 18));
        var look = new StudioCaptionLook(GenerationCaptionStylePreset.Clean, 50, StudioCaptionWordLimitPreset.FullSegment, 80, 150, typography);
        var asset = project.PrimaryAsset.WithStudioEdits(project.PrimaryAsset.SourceStart, project.PrimaryAsset.SourceEnd, look.ApplyTo(project.PrimaryAsset.Appearance))
            .WithRenderSettings(new StudioRenderSettings(StudioOutputCanvas.Portrait));
        project = project.ReplaceAsset(asset);
        using var preview = new StudioPreviewViewModel(mediaService: null); preview.Bind(true, project, asset);
        var ass = AssSubtitleDocumentBuilder.Build(track, 1080, 1920, verticalPositionPercent: 50,
            captionMaximumWidthPercent: 80, captionFontScalePercent: 150, captionTypography: typography);
        TestAssert.True(ass.Warnings.Any(warning => warning.Contains("safe-area margins", StringComparison.Ordinal)), "Measured text wider than the custom area must produce an export warning.");
        TestAssert.True(preview.LiveCaptionPresentationWarning?.Contains("safe-area margins", StringComparison.Ordinal) == true, "Studio must expose the same measured overflow warning before export.");
        TestAssert.True(ass.Script.Contains("\\q2", StringComparison.Ordinal), "Custom safe areas must use explicit shared line layout in ASS.");
        var changedPlatform = StudioPlatformExportPresets.Apply(StudioPlatformExportPreset.YouTubeShorts, asset.Appearance);
        TestAssert.Equal(typography.SafeAreaInsets, changedPlatform.CaptionTypography.SafeAreaInsets, "Platform changes must preserve explicitly authored safe margins.");
        var normal = StudioCaptionPresentationPolicy.CalculateFrameLayout(1080, 1920, GenerationCaptionStylePreset.Clean, 80, 100);
        var tight = StudioCaptionBoundsReview.Measure("hello world", typography, GenerationCaptionStylePreset.Clean, normal, 1080, 960);
        var small = StudioCaptionBoundsReview.Measure("hi", typography, GenerationCaptionStylePreset.Clean, normal, 1080, 960);
        TestAssert.True(tight.Width > small.Width, "Bounds must follow measured words rather than only the configured maximum width.");
        return Task.CompletedTask;
    }
}
