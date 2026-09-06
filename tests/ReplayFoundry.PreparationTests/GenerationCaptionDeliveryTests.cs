using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup.Steps.Audio;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Workflow;
using ReplayFoundry.Desktop.Features.Publish.YouTube;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Media.Subtitles;
using ReplayFoundry.Desktop.Platform.Storage;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static async Task GenerateCapturesNamedCaptionLook()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips, captionsEnabled: true, createPhysicalSource: true);
        var look = new StudioCaptionLook(GenerationCaptionStylePreset.HighContrast, 37,
            StudioCaptionWordLimitPreset.Streamlined, 65, 120,
            new StudioCaptionTypography("Arial", textColor: "#12AAEF", alignment: StudioCaptionAlignment.Left,
                casing: StudioCaptionCasing.Uppercase, lineSpacingPercent: 145));
        var request = new GenerationSetupRequest(fixture.GenerationRequest.Mode, fixture.GenerationRequest.Preparation);
        var draft = new GenerationSetupDraft(request, fixture.GenerationRequest.SetupOptions);
        using var audio = new AudioStepViewModel(draft, captionLooks: [new StudioNamedCaptionLook("Tournament", look)]);
        audio.SelectedCaptionLook = audio.CaptionLooks.Single(option => option.Name == "Tournament");
        GenerationSetupOptions captured = draft.CreateOptions();
        TestAssert.Equal("Tournament", captured.CaptionSettings.SavedLookName!, "Generate must retain the selected look's reviewable name.");
        TestAssert.Equal(look, captured.CaptionSettings.SavedLook!, "Generate must capture the full immutable typography, not a mutable store lookup.");
        TestAssert.Equal(look.CaptionStyle, audio.SelectedCaptionStyle.Value, "A saved look also owns its caption effect.");
        TestAssert.False(audio.IsCaptionStyleSelectable, "The effect selector cannot conflict with a complete named look.");
        TestAssert.True(captured.Summary.Contains("Tournament captions", StringComparison.Ordinal), "The setup summary must identify the chosen look.");

        // The catalog may later change or disappear; reopening this draft must keep its captured settings.
        using var reopened = new AudioStepViewModel(new GenerationSetupDraft(request, captured), captionLooks: []);
        TestAssert.Equal(look, reopened.SelectedCaptionLook.Look!, "A deleted catalog entry must not silently reset an already selected look.");
        using var sameStyles = new AudioStepViewModel(new GenerationSetupDraft(request, captured),
            captionLooks: [new StudioNamedCaptionLook("Practice", look), new StudioNamedCaptionLook("Tournament", look)]);
        TestAssert.Equal("Tournament", sameStyles.SelectedCaptionLook.Name, "Two identically styled presets must retain the deliberately selected name.");
        audio.SelectedCaptionLook = audio.CaptionLooks[0];
        TestAssert.True(audio.IsCaptionStyleSelectable && draft.CaptionSettings.SavedLook is null,
            "Choosing effect defaults must explicitly release the named look while leaving the captured run unchanged.");
        TestAssert.Equal(look, captured.CaptionSettings.SavedLook!, "Later setup choices cannot mutate a captured run.");

        var generation = new GenerationRequest(fixture.GenerationRequest.Preparation, captured,
            fixture.GenerationRequest.CompositionReview, fixture.GenerationRequest.EvidenceAnalysis);
        GenerationMomentFindingResult moments = fixture.MomentService.Find(new GenerationMomentFindingRequest(generation.EvidenceAnalysis, captured));
        var tracks = moments.SelectedCandidates.Select(candidate => new GenerationCandidateCaptionTrack(candidate,
            captured.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
            captured.CaptionSettings.Style, CreateTranscription(candidate))).ToArray();
        var captions = new GenerationCaptionPreparationResult(moments, tracks, TimeSpan.Zero);
        var editorial = await fixture.EditorialMetadata.GenerateAsync(moments, captions, CancellationToken.None);
        GenerationOutputProject project = GenerationOutputProject.FromResult(new GenerationResult(generation, moments, editorial, captions), fixture.FinalDirectory);
        TestAssert.True(project.Assets.All(asset => StudioCaptionLook.FromAppearance(asset.Appearance) == look),
            "Every generated clip must begin with the complete selected look, including its intentional placement.");
        var document = StudioProjectDocumentMapper.Capture(project, 1, project.CreatedAtUtc.AddSeconds(1));
        GenerationOutputProject restored = StudioProjectDocumentMapper.Restore(document);
        TestAssert.Equal(look, StudioCaptionLook.FromAppearance(restored.PrimaryAsset.Appearance), "The generated look must survive Studio save/reopen.");
    }

    private static async Task CleanCaptionDeliveryPreservesCutClocks()
    {
        foreach (GenerationMode mode in new[] { GenerationMode.IndividualClips, GenerationMode.Montage })
        {
            using PipelineFixture fixture = CreateFixture(mode, captionsEnabled: true, scoreSets: [[95, 94]]);
            var tracks = fixture.Moments.SelectedCandidates.Select(candidate => new GenerationCandidateCaptionTrack(candidate,
                fixture.GenerationRequest.SetupOptions.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
                GenerationCaptionStylePreset.KaraokeSweep, CreateTranscription(candidate))).ToArray();
            var captions = new GenerationCaptionPreparationResult(fixture.Moments, tracks, TimeSpan.Zero);
            GenerationOutputProject original = fixture.CreateDraft(captions);
            var clean = new StudioRenderSettings(StudioOutputCanvas.Portrait, burnCaptions: false);
            var restoredSettings = JsonSerializer.Deserialize<StudioRenderSettings>(JsonSerializer.Serialize(clean))!;
            TestAssert.False(restoredSettings.BurnCaptions, "Clean video is an explicit persisted output decision.");
            TestAssert.True(JsonSerializer.Deserialize<StudioRenderSettings>("{}")!.BurnCaptions,
                "Existing projects must keep their original burned-caption default.");
            GenerationOutputProject project = original.ReplaceAssets(original.Assets.Select(asset => asset.WithStudioEdits(
                asset.SourceStart + TimeSpan.FromSeconds(1.6), asset.SourceStart + TimeSpan.FromSeconds(2.6), asset.Appearance)
                .WithRenderSettings(restoredSettings)).ToArray());
            TestAssert.Equal(2, project.Assets.Count, "The delivery fixture must exercise more than one source cut.");
            var cues = new List<SubtitleCue>();
            TimeSpan cursor = TimeSpan.Zero;
            foreach (GenerationOutputAsset asset in project.Assets)
            {
                TestAssert.Equal("hello world", asset.Captions!.Segments[0].Text, "Clean mode cannot rewrite the retained source transcript.");
                TestAssert.False(YouTubePublishProvenance.Capture(asset).CaptionsEnabled,
                    "Publish analytics must not label clean video as burned captions merely because a sidecar exists.");
                cues.AddRange(SubtitleSidecarSerializer.Project(asset.Captions, asset.SourceStart, asset.Duration)
                    .Select(cue => cue with { Start = cue.Start + cursor, End = cue.End + cursor }));
                cursor += asset.Duration;
            }
            using var preview = new StudioPreviewViewModel(mediaService: null);
            preview.Bind(true, project, project.PrimaryAsset);
            preview.PreviewPositionSeconds = project.PrimaryAsset.SourceStart.TotalSeconds + .1;
            TestAssert.False(preview.HasLiveCaption || preview.HasLiveSecondaryCaption || preview.CanShowCaptionControls,
                "Clean video preview must agree with export while retaining captions for authoring and sidecars.");
            TestAssert.False(preview.ToggleCaptionVisibilityCommand.CanExecute(null), "CC cannot suggest burned captions are enabled in clean mode.");

            var runner = new WritingProcessRunner();
            StudioProjectRenderResult result = await fixture.CreateStudioRenderer(runner).FinalizeAsync(project,
                new RecordingProgress<StudioProjectRenderProgress>(), CancellationToken.None);
            TestAssert.False(runner.Requests.Any(request => request.Arguments.Any(argument => argument.Contains("ass=filename=", StringComparison.Ordinal))),
                "Clean caption delivery must not ask FFmpeg to burn a caption script.");
            foreach (GenerationOutputAsset output in result.FinalizedProject.Assets)
            {
                if (output.OutputFullPath is null) continue;
                foreach (SubtitleSidecarFormat format in Enum.GetValues<SubtitleSidecarFormat>())
                {
                    string path = Path.ChangeExtension(output.OutputFullPath, format == SubtitleSidecarFormat.Srt ? ".srt" : ".vtt");
                    var actual = SubtitleSidecarSerializer.Parse(await File.ReadAllTextAsync(path), format);
                    var expected = mode == GenerationMode.Montage ? cues : SubtitleSidecarSerializer.Project(output.Captions!, output.SourceStart, output.Duration);
                    TestAssert.Equal(expected.Count, actual.Count, "Clean outputs must retain all cut-aware subtitle cues.");
                    TestAssert.Equal(mode == GenerationMode.Montage ? 2 : 1, actual.Count,
                        "Each one-second cut retains one measured word, and the montage includes both cuts.");
                    for (int index = 0; index < actual.Count; index++)
                    {
                        TestAssert.Equal("world", actual[index].Text, "A trim crossing hello must expose only the fully contained measured word.");
                        TestAssert.Equal(expected[index].Start, actual[index].Start, "Subtitle clocks must follow the actual output timeline.");
                        TestAssert.Equal(expected[index].End, actual[index].End, "Subtitle ends must retain the measured speech interval.");
                        TestAssert.Equal(TimeSpan.FromSeconds(index), actual[index].Start,
                            "The first retained word starts at zero and later montage words follow exact one-second cuts.");
                        TestAssert.Equal(TimeSpan.FromSeconds(index + .7), actual[index].End,
                            "Only the measured 700ms of the retained word may enter the exported sidecar.");
                    }
                }
            }
        }
    }
}
