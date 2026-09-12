using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Captions;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;
using ReplayFoundry.Desktop.Media.Subtitles;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static async Task BurnedCaptionsDoNotAutoLoadSidecars()
    {
        foreach (GenerationMode mode in new[] { GenerationMode.IndividualClips, GenerationMode.Montage })
        {
            using var fixture = CreateFixture(mode, captionsEnabled: true, scoreSets: [[95, 94]]);
            var tracks = fixture.Moments.SelectedCandidates.Select(candidate => new GenerationCandidateCaptionTrack(candidate,
                fixture.GenerationRequest.SetupOptions.CaptionSettings.FindForSource(candidate.AnalyzedSource.PreparedSource.Media.FullPath)!,
                GenerationCaptionStylePreset.Pop, CreateTranscription(candidate))).ToArray();
            var project = fixture.CreateDraft(new GenerationCaptionPreparationResult(fixture.Moments, tracks, TimeSpan.Zero));
            var appearance = new StudioClipAppearance(GenerationCaptionStylePreset.HighContrast, 37, StudioVideoEffectPreset.None, 0);
            project = project.ReplaceAssets(project.Assets.Select(asset =>
                asset.WithStudioEdits(asset.SourceStart, asset.SourceEnd, appearance)).ToArray());
            using var preview = new StudioPreviewViewModel(mediaService: null);
            preview.Bind(true, project, project.PrimaryAsset);
            TestAssert.Equal(GenerationCaptionStylePreset.HighContrast, preview.LiveCaptionStyle, "Preview uses the edited effect.");
            TestAssert.Equal(37d, preview.LiveCaptionVerticalPercent, "Preview uses the edited placement.");
            var runner = new WritingProcessRunner();
            var rendered = await fixture.CreateStudioRenderer(runner).FinalizeAsync(project,
                new RecordingProgress<StudioProjectRenderProgress>(), CancellationToken.None);
            TestAssert.Equal(project.Assets.Count, runner.CaptionScripts.Count, "Each source cut burns one caption script.");
            var profile = GenerationClipOutputProfile.FromAsset(project.PrimaryAsset);
            string anchor = FormattableString.Invariant($"\\pos({profile.Width / 2d},{Math.Round(profile.Height * .37)})");
            TestAssert.True(runner.CaptionScripts.All(script => script.Contains(anchor) && script.Contains(",HighContrast,")),
                "The actual export script must retain the preview's current effect and placement.");
            foreach (string video in rendered.FinalizedProject.Assets.Select(asset => asset.OutputFullPath!).Distinct())
            {
                TestAssert.False(File.Exists(Path.ChangeExtension(video, ".srt")) || File.Exists(Path.ChangeExtension(video, ".vtt")),
                    "A burned MP4 must not have matching adjacent files for a player to load a second time.");
                using var package = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(video, ".publish.json")));
                foreach (string field in new[] { "subtitlesSrt", "subtitlesVtt" })
                {
                    string relative = package.RootElement.GetProperty("files").GetProperty(field).GetString()!;
                    TestAssert.True(relative.StartsWith("caption-files/", StringComparison.Ordinal), "Upload captions have an explicit relative location.");
                    string sidecar = Path.Combine(Path.GetDirectoryName(video)!, relative);
                    TestAssert.True(File.Exists(sidecar), "Package links must resolve to retained captions.");
                    var cues = SubtitleSidecarSerializer.Parse(await File.ReadAllTextAsync(sidecar),
                        field == "subtitlesSrt" ? SubtitleSidecarFormat.Srt : SubtitleSidecarFormat.WebVtt);
                    TestAssert.True(cues.Count > 0, "Avoiding auto-load cannot discard the upload transcript.");
                }
            }
        }
    }
}
