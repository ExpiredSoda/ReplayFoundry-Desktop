using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task CaptionEditingUsesCurrentCutAndPreservesMeasuredWords()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.KaraokeSweep, CreateTranscription(candidate));
        var project = fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, [track], TimeSpan.Zero));
        var asset = project.PrimaryAsset;
        var cut = asset.WithStudioEdits(asset.SourceStart + TimeSpan.FromSeconds(0.5), asset.SourceEnd, asset.Appearance);
        project = project.ReplaceAsset(cut);
        var session = new GenerationOutputSession(); session.Publish(project);
        var edits = StudioCaptionTrackEditing.CreateDrafts(cut).ToArray();
        TestAssert.Equal(track.Segments[0].RelativeStart.TotalSeconds - 0.5, edits[0].StartSeconds,
            "Caption fields must use the current playback origin after trimming.");
        edits[0] = edits[0] with { Text = edits[0].Text.ToUpperInvariant() + "!" };
        var saved = StudioCaptionTrackEditing.Apply(session, project, cut, edits);
        TestAssert.Equal(track.Segments[0].Words[0].AbsoluteSourceStart, saved.Captions!.Segments[0].Words[0].AbsoluteSourceStart,
            "Punctuation and case corrections must preserve measured source timestamps.");
        var corrected = StudioCaptionTrackEditing.CreateDrafts(saved).ToArray();
        corrected[0] = corrected[0] with { Text = "a completely different transcript" };
        var replaced = StudioCaptionTrackEditing.Apply(session, session.Current!, saved, corrected);
        TestAssert.Equal(0, replaced.Captions!.Segments[0].Words.Count,
            "Arbitrary text corrections must not be assigned invented word timing.");
        return Task.CompletedTask;
    }

    private static Task CaptionEditorSupportsStructureUndoAndWordTiming()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean, CreateTranscription(candidate));
        var project = fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, [track], TimeSpan.Zero));
        var session = new GenerationOutputSession(); session.Publish(project);
        var editor = new StudioCaptionTrackEditorViewModel(session); editor.Bind(project, project.PrimaryAsset);
        int initialCount = editor.Segments.Count;
        editor.SplitCommand.Execute(editor.Segments[0]);
        TestAssert.Equal(initialCount + 1, editor.Segments.Count, "Split must add one phrase.");
        editor.UndoCommand.Execute(null);
        TestAssert.Equal(initialCount, editor.Segments.Count, "Undo must restore the original phrase structure.");
        editor.RedoCommand.Execute(null);
        editor.MergeCommand.Execute(editor.Segments[0]);
        TestAssert.Equal(initialCount, editor.Segments.Count, "Merge must restore one phrase from its pair.");
        var word = editor.Segments[0].Words[0];
        word.EndSeconds -= 0.01; word.IsEmphasized = true;
        editor.Segments[0].Speaker = "Creator";
        editor.Segments[0].SecondaryText = "Hola mundo";
        editor.SaveCommand.Execute(null);
        var saved = session.Current!.PrimaryAsset.Captions!.Segments[0];
        TestAssert.Equal(word.EndSeconds, (saved.Words[0].AbsoluteSourceEnd - project.PrimaryAsset.SourceStart).TotalSeconds,
            "The user's explicit word timing correction must persist.");
        TestAssert.True(saved.Words[0].IsEmphasized, "Per-word emphasis must persist.");
        TestAssert.Equal("Creator", saved.Speaker!, "Manual speaker attribution must persist.");
        TestAssert.Equal("Hola mundo", saved.SecondaryText!, "The optional second language must persist with the phrase.");
        TestAssert.True(AssSubtitleDocumentBuilder.Build(session.Current.PrimaryAsset.Captions!, 1080, 1920).Script.Contains("\\u1", StringComparison.Ordinal),
            "Saved word emphasis must reach final subtitles.");
        TestAssert.True(AssSubtitleDocumentBuilder.Build(session.Current.PrimaryAsset.Captions!, 1080, 1920).Script.Contains("Hola mundo", StringComparison.Ordinal),
            "Secondary captions must be included in final rendered subtitles.");
        return Task.CompletedTask;
    }

    private static Task SubtitleSidecarsRoundTripAndClipToTheCut()
    {
        var cues = new[] { new SubtitleCue(TimeSpan.FromMilliseconds(1234), TimeSpan.FromMilliseconds(3456), "I'm sure: don't say \"One & two < three\".", "O'Neil & Friends") };
        string vtt = SubtitleSidecarSerializer.Build(cues, SubtitleSidecarFormat.WebVtt);
        var parsed = SubtitleSidecarSerializer.Parse(vtt, SubtitleSidecarFormat.WebVtt);
        TestAssert.Equal(cues[0], parsed[0], "WebVTT must round-trip millisecond timing, text escaping and speaker labels.");
        string srt = SubtitleSidecarSerializer.Build(cues.Select(c => c with { Speaker = null }), SubtitleSidecarFormat.Srt);
        TestAssert.True(vtt.Contains("I'm sure: don't say \"", StringComparison.Ordinal) && srt.Contains("I'm sure: don't say \"", StringComparison.Ordinal),
            "Real speech apostrophes and quotes must remain literal in plain subtitle players.");
        TestAssert.Equal(cues[0] with { Speaker = null }, SubtitleSidecarSerializer.Parse(srt, SubtitleSidecarFormat.Srt)[0], "SRT must round-trip text and timing.");
        TestAssert.Throws<FormatException>(() => SubtitleSidecarSerializer.Parse("1\n00:00:02,000 --> 00:00:01,000\nbad\n", SubtitleSidecarFormat.Srt), "Reversed subtitle intervals must be rejected.");
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean, CreateTranscription(candidate));
        TimeSpan cutStart = track.Segments[0].AbsoluteSourceStart + TimeSpan.FromMilliseconds(10);
        var clipped = SubtitleSidecarSerializer.Project(track, cutStart, TimeSpan.FromMilliseconds(100));
        TestAssert.Equal(0, clipped.Count, "A 100ms cut entirely inside a measured word cannot claim that whole word or phrase was spoken.");
        var word = track.Segments[0].Words[1];
        var complete = SubtitleSidecarSerializer.Project(track, word.AbsoluteSourceStart,
            word.AbsoluteSourceEnd - word.AbsoluteSourceStart);
        TestAssert.Equal(word.Text, complete.Single().Text, "A completely retained measured word remains in the sidecar.");
        TestAssert.Equal(TimeSpan.Zero, complete.Single().Start, "A retained word is rebased to the current cut.");
        TestAssert.Equal(word.AbsoluteSourceEnd - word.AbsoluteSourceStart, complete.Single().End,
            "Sidecar captions cannot run beyond the exact exported cut.");
        return Task.CompletedTask;
    }

    private static Task NamedCaptionTypographyPersistsAndRenders()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var typography = new StudioCaptionTypography("Arial", "#11AAEE", "#FF4422", "#000000", false, 4, 1, StudioCaptionSafeArea.TikTok);
        var look = new StudioCaptionLook(GenerationCaptionStylePreset.Clean, 90, StudioCaptionWordLimitPreset.Streamlined, 100, 110, typography);
        var store = new JsonStudioCaptionLookStore(System.IO.Path.Combine(fixture.Root, "caption-looks.json"));
        store.Save("Creator", look);
        TestAssert.Equal(typography, store.Load().Single().Look.CaptionTypography, "Named looks must preserve complete typography across reload.");
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.Clean, CreateTranscription(candidate));
        string script = AssSubtitleDocumentBuilder.Build(track, 1080, 1920, verticalPositionPercent: 90, captionTypography: typography).Script;
        TestAssert.True(script.Contains("Style: Clean,Arial,", StringComparison.Ordinal) && script.Contains("&H00EEAA11", StringComparison.Ordinal), "Export must use the chosen font and RGB text color.");
        TestAssert.True(script.Contains("\\pos(540,1382)", StringComparison.Ordinal), "TikTok safe placement must clamp the caption center to 72 percent.");
        var rtl = new StudioCaptionTypography(rightToLeft: true, background: StudioCaptionBackground.Panel);
        string rtlScript = AssSubtitleDocumentBuilder.Build(track, 1080, 1920, captionTypography: rtl).Script;
        TestAssert.True(rtlScript.Split(Environment.NewLine).Where(line => line.StartsWith("Style: ", StringComparison.Ordinal))
            .All(line => line.Split(',')[22] == "-1"),
            "Every RTL glyph and panel style must enable whole-line libass bidi so trailing punctuation and color overrides keep logical order.");
        TestAssert.True(rtlScript.Contains('\u200F'), "RTL lines must keep an explicit direction mark before mixed-script text.");
        TestAssert.True(script.Split(Environment.NewLine).Where(line => line.StartsWith("Style: ", StringComparison.Ordinal))
            .All(line => line.Split(',')[22] == "1"), "Ordinary caption scripts must retain their existing encoding policy.");
        return Task.CompletedTask;
    }

    private static Task CaptionReadabilityAndVocabularyRemainExplicit()
    {
        var report = StudioCaptionReadability.Assess("This caption is much too long for its short visible interval.", 0.5);
        TestAssert.True(report.CharactersPerSecond > 20 && report.Warning is not null, "Dense speech needs a measurable reading-speed warning.");
        var emoji = StudioCaptionReadability.Assess("👨‍👩‍👦 hello", 3);
        TestAssert.Equal(7, emoji.Characters, "Character counts should treat a composed emoji as one visible character.");
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        var store = new JsonCaptionVocabularyStore(System.IO.Path.Combine(fixture.Root, "vocabulary.json"));
        store.Save(["Ranni", "Elden Ring", "Ranni"]);
        TestAssert.Equal(2, store.Load().Count, "Remembered terms should be deduplicated.");
        TestAssert.True(store.ReadPrompt()!.Contains("Ranni, Elden Ring", StringComparison.Ordinal), "Only explicitly supplied terms should enter the spelling prompt.");
        return Task.CompletedTask;
    }

    private static Task IndependentCaptionStyleControlsRender()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true, captionsEnabled: true);
        var typography = new StudioCaptionTypography(textColor: "#112233", accentColor: "#FFFFFF", outlineColor: "#445566",
            background: StudioCaptionBackground.Panel, backgroundColor: "#FF0000", backgroundOpacityPercent: 50,
            alignment: StudioCaptionAlignment.Left, casing: StudioCaptionCasing.Uppercase, animationIntensityPercent: 0,
            lineSpacingPercent: 150);
        var look = new StudioCaptionLook(GenerationCaptionStylePreset.WordFocus, 75, StudioCaptionWordLimitPreset.FullSegment, 45, 110, typography);
        var store = new JsonStudioCaptionLookStore(System.IO.Path.Combine(fixture.Root, "complete-caption-look.json"));
        store.Save("Readable", look);
        TestAssert.Equal(typography, store.Load().Single().Look.CaptionTypography, "Every independent style field must survive a reusable look round trip.");
        var candidate = fixture.Moments.SelectedCandidates[0];
        var track = new GenerationCandidateCaptionTrack(candidate,
            fixture.Moments.Request.Setup.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            GenerationCaptionStylePreset.WordFocus, CreateTranscription(candidate));
        string script = AssSubtitleDocumentBuilder.Build(track, 1080, 1920, captionMaximumWidthPercent: 45, captionTypography: typography).Script;
        int left = StudioCaptionPresentationPolicy.CalculateFrameLayout(1080, 1920, GenerationCaptionStylePreset.WordFocus, 45, 100).HorizontalMarginPixels;
        TestAssert.True(script.Contains($"\\an4\\pos({left},", StringComparison.Ordinal), "Left alignment must anchor to the selected width's left margin.");
        TestAssert.True(script.Contains("&H800000FF", StringComparison.Ordinal), "A half-transparent red panel must keep its independent background alpha and BGR channels.");
        TestAssert.True(script.Contains("&H00665544", StringComparison.Ordinal), "The background cannot consume the independent glyph outline color.");
        TestAssert.True(script.Contains("{\\c&H00FFFFFF&\\fscx100\\fscy100", StringComparison.Ordinal), "A white accent must not be recolored by the nonwhite text mapping; zero motion must remove zoom.");
        TestAssert.True(!script.Contains("\\fscx108", StringComparison.Ordinal) && !script.Contains("\\fsp", StringComparison.Ordinal), "Line spacing must never stretch glyphs or masquerade as letter spacing.");
        TestAssert.True(script.Contains("Dialogue: -1", StringComparison.Ordinal), "Custom background panels must render behind the caption text.");
        var old = System.Text.Json.JsonSerializer.Deserialize<StudioCaptionTypography>("{\"fontFamily\":\"Arial\"}",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        TestAssert.Equal(100d, old.LineSpacingPercent, "Existing projects without new typography properties must retain default line spacing.");
        TestAssert.Equal(StudioCaptionBackground.StyleDefault, old.Background, "Existing projects must retain their style's original background behavior.");
        return Task.CompletedTask;
    }

    private static Task CaptionStyleProjectionPreservesSpeech()
    {
        var word = new ReplayFoundry.Desktop.Media.Transcription.AudioTranscriptionWord("voice", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(12), 0.9, true);
        var cue = new StudioCaptionCue("A voice speaks", TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(13),
            [new StudioCaptionWordSpan(word, 2, 5)]);
        var typography = new StudioCaptionTypography(casing: StudioCaptionCasing.Uppercase, lineSpacingPercent: 160);
        var displayed = StudioCaptionDisplayText.Transform(cue, typography);
        TestAssert.Equal("A VOICE SPEAKS", displayed.Text, "Casing changes only what is displayed.");
        TestAssert.Equal(word.AbsoluteSourceStart, displayed.Words[0].AbsoluteSourceStart, "Casing must retain the exact observed source word start.");
        var sliced = StudioCaptionDisplayText.Slice(displayed, 2, 5);
        TestAssert.Equal("VOICE", sliced.Text, "Explicit lines must preserve their exact text slice.");
        TestAssert.Equal(word.RelativeEnd, sliced.Words[0].RelativeEnd, "Explicit line positions cannot invent a different word interval.");
        TestAssert.True(sliced.Words[0].IsEmphasized, "Per-word emphasis must survive casing and line layout.");
        TestAssert.Equal("voice", word.Text, "Presentation cannot rewrite the source transcript.");
        return Task.CompletedTask;
    }
}
