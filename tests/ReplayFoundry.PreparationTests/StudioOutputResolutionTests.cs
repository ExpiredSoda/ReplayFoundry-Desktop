using System.Globalization;
using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Media.Inspection;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task ResolutionProfilesPreserveGeometryAndBoundPreviews()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        GenerationOutputAsset original = fixture.CreateDraft().PrimaryAsset;
        foreach (var (resolution, shortEdge, longEdge) in new[]
        {
            (StudioOutputResolution.Hd720, 720, 1280),
            (StudioOutputResolution.FullHd1080, 1080, 1920),
            (StudioOutputResolution.Qhd1440, 1440, 2560),
            (StudioOutputResolution.Uhd2160, 2160, 3840),
        })
        {
            foreach (StudioOutputCanvas canvas in new[] { StudioOutputCanvas.Portrait, StudioOutputCanvas.Landscape, StudioOutputCanvas.Square })
            {
                GenerationClipOutputProfile profile = GenerationClipOutputProfile.FromAsset(original.WithRenderSettings(new(canvas, resolution: resolution)));
                TestAssert.Equal(canvas == StudioOutputCanvas.Landscape ? longEdge : shortEdge, profile.Width, "Saved output width must follow the selected canvas and resolution.");
                TestAssert.Equal(canvas == StudioOutputCanvas.Portrait ? longEdge : shortEdge, profile.Height, "Saved output height must follow the selected canvas and resolution.");
                GenerationClipOutputProfile preview = FfmpegStudioPreviewMediaService.FitPreview(profile);
                TestAssert.True(Math.Max(preview.Width, preview.Height) <= 720 && preview.FramesPerSecond <= 30,
                    "Choosing 4K delivery must not multiply interactive proxy work.");
                TestAssert.NearlyEqual(profile.Width / (double)profile.Height, preview.Width / (double)preview.Height, .006,
                    "Even-sized proxies must preserve the delivery aspect ratio.");
            }
        }
        MediaProbeResult rotated = TestMediaFactory.Create(original.SourceFullPath, width: 3840, height: 2160,
            rotationDegrees: 90, frameRate: new MediaRational(60000, 1001));
        GenerationClipOutputProfile native = GenerationClipOutputProfile.FromReference(rotated.PrimaryVideoStream, StudioOutputResolution.Uhd2160);
        TestAssert.Equal(2160, native.Width, "Source delivery must apply rotation before resolution limits.");
        TestAssert.Equal(3840, native.Height, "Native 4K detail should survive the 4K Source profile.");
        TestAssert.NearlyEqual(60000d / 1001, native.FramesPerSecond, 1e-10, "Resolution changes cannot alter fractional cadence.");
        MediaProbeResult small = TestMediaFactory.Create(original.SourceFullPath, width: 640, height: 360);
        TestAssert.Equal(640, GenerationClipOutputProfile.FromReference(small.PrimaryVideoStream, StudioOutputResolution.Uhd2160).Width,
            "Source canvas must never upscale a low-resolution recording merely because the delivery cap is higher.");
        TestAssert.Throws<ArgumentOutOfRangeException>(() => new StudioRenderSettings(resolution: (StudioOutputResolution)99),
            "Invalid persisted resolution values must not enter a render graph.");
        return Task.CompletedTask;
    }

    private static Task ResolutionSurvivesReloadAndCutListEdits()
    {
        StudioRenderSettings legacy = JsonSerializer.Deserialize<StudioRenderSettings>("{\"Canvas\":1}")!;
        TestAssert.Equal(StudioOutputResolution.FullHd1080, legacy.Resolution, "Older projects must retain their 1080p default.");
        var settings = new StudioRenderSettings(StudioOutputCanvas.Portrait, audioTracks: [new(1, -4)],
            audioMastering: new(true, -18, true, -2), burnCaptions: false, resolution: StudioOutputResolution.Uhd2160);
        StudioRenderSettings restored = JsonSerializer.Deserialize<StudioRenderSettings>(settings.CanonicalIdentity())!;
        TestAssert.Equal(settings.CanonicalIdentity(), restored.CanonicalIdentity(), "All delivery decisions must survive reload.");
        StudioRenderSettings styled = StudioLayoutPreset.Capture("A visual layout", "Test game", new()).Apply(restored);
        styled = StudioPlatformExportPresets.Apply(StudioPlatformExportPreset.YouTubeShorts, styled.WithFrameEdits([], []));
        TestAssert.Equal(settings.Resolution, styled.Resolution, "Layout, platform and motion edits must preserve delivery resolution.");

        using PipelineFixture fixture = CreateFixture(GenerationMode.Montage);
        GenerationOutputProject project = fixture.CreateDraft();
        GenerationOutputAsset first = project.PrimaryAsset.WithRenderSettings(settings);
        project = project.ReplaceAsset(first).SplitAsset(first.Id, first.SourceStart + first.Duration / 2);
        TestAssert.True(project.Assets.All(asset => asset.RenderSettings.Resolution == settings.Resolution), "Splitting a cut must preserve delivery resolution.");
        GenerationOutputAsset manual = project.CreateManualSourceAsset(first.SourceFullPath, TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(205));
        TestAssert.Equal(settings.Resolution, manual.RenderSettings.Resolution, "Manual source cuts must inherit the project's delivery resolution.");
        var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioOutputEditorViewModel(session);
        editor.Bind(project, project.PrimaryAsset);
        editor.Resolution = StudioOutputResolution.Qhd1440;
        TestAssert.True(session.Current!.Assets.All(asset => asset.RenderSettings.Resolution == StudioOutputResolution.Qhd1440),
            "A montage resolution edit must keep every cut compatible for concatenation.");
        TestAssert.True(session.Current.Assets.All(asset => !asset.RenderSettings.BurnCaptions && asset.RenderSettings.AudioTracks.Single().GainDecibels == -4),
            "Changing montage delivery size cannot reset clean-video or audio decisions.");
        TestAssert.Equal("4K UHD", new StudioResolutionChoice(StudioOutputResolution.Uhd2160, "4K UHD").ToString(),
            "Resolution controls must expose friendly labels under custom ComboBox templates.");
        return Task.CompletedTask;
    }

    private static Task HighResolutionReceivesEncodingBudget()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.IndividualClips);
        GenerationOutputAsset asset = fixture.CreateDraft().PrimaryAsset;
        int BitRate(GenerationClipOutputProfile profile)
        {
            var command = FfmpegClipRenderCommandBuilder.BuildSegment(asset.SourceMedia, asset.SourceStart, asset.SourceEnd,
                profile, Path.Combine(fixture.Root, "delivery.mp4"));
            int index = command.Arguments.ToList().IndexOf("-b:v");
            TestAssert.True(index >= 0, "Final encoding must declare a video bitrate budget.");
            return int.Parse(command.Arguments[index + 1], CultureInfo.InvariantCulture);
        }
        int hd = BitRate(new(1080, 1920, 60));
        int uhd = BitRate(new(2160, 3840, 60));
        TestAssert.True(uhd > 30_000_000 && uhd > hd * 3, "4K60 must not inherit the old 1080p bitrate ceiling.");
        TestAssert.True(uhd <= 80_000_000 && hd <= 30_000_000, "Each standard profile must remain within its bounded encoder budget.");
        return Task.CompletedTask;
    }
}
