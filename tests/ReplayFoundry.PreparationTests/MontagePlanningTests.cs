using System.Text.Json;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Rendering;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task MontagePlanPersistence()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.Montage, scoreSets: [[95, 90, 85]]);
        var draft = fixture.CreateDraft().ArrangeMontage(MontageStyle.Banter);
        foreach (string source in draft.SourceMedia.Select(media => media.FullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            if (!File.Exists(source)) File.WriteAllText(source, "test-owned source for persistence");
        }
        TestAssert.True(draft.Assets.Select(asset => asset.SourceStart).SequenceEqual(draft.Assets.Select(asset => asset.SourceStart).Order()), "Banter retains source chronology.");
        var metadata = draft.PrimaryAsset.EditorialMetadata!;
        draft = draft.WithMontageMetadata(metadata, draft.MontageFingerprint);
        var document = StudioProjectDocumentMapper.Capture(draft, 1, draft.CreatedAtUtc.AddSeconds(1));
        var restored = StudioProjectDocumentMapper.Restore(JsonSerializer.Deserialize<StudioProjectDocument>(JsonSerializer.Serialize(document))!);
        TestAssert.Equal(MontageStyle.Banter, restored.MontageStyle, "Reopening must preserve style.");
        TestAssert.True(restored.IsMontageMetadataCurrent, "Unchanged sequence retains its whole-montage copy.");
        TestAssert.Equal(metadata.Description, restored.MontageMetadata!.Description, "Whole-montage description must survive serialization.");
        var moved = restored.MoveAsset(restored.Assets[1].Id, -1);
        TestAssert.False(moved.IsMontageMetadataCurrent, "Rearranging invalidates the complete sequence's wording.");
        TestAssert.Throws<InvalidOperationException>(() => moved.WithMontageMetadata(metadata, draft.MontageFingerprint), "An in-flight writer must not overwrite a newer sequence.");
        TestAssert.True(moved.Assets.All(asset => restored.Assets.Any(previous => previous.Id == asset.Id && previous.SourceStart == asset.SourceStart && previous.SourceEnd == asset.SourceEnd)), "Arrangement never trims away speech or outcomes.");
        TestAssert.Equal(draft.IncludedCount, draft.MontageBeats.Count, "The plan describes every kept cut.");
        return Task.CompletedTask;
    }
    private static Task MontageStyleAudioClocks()
    {
        using PipelineFixture fixture = CreateFixture(GenerationMode.Montage);
        var asset = fixture.CreateDraft().PrimaryAsset;
        foreach (MontageStyle style in Enum.GetValues<MontageStyle>())
        {
            var command = FfmpegClipRenderCommandBuilder.BuildSegment(asset.SourceMedia, asset.SourceStart,
                asset.SourceStart + TimeSpan.FromSeconds(2.1373), new GenerationClipOutputProfile(720, 1280, 30),
                Path.Combine(fixture.Root, style + ".mp4"), renderSettings: asset.RenderSettings,
                audioEdgeSeconds: MontageSequencePlanner.AudioEdgeSeconds(style),
                videoEdgeSeconds: MontageSequencePlanner.VideoEdgeSeconds(style));
            TestAssert.True(ContainsPair(command.Arguments, "-t", "2.1373"), "Join treatment cannot shift duration or subtitle offsets.");
            TestAssert.True(command.Arguments.Any(value => value.Contains("afade=t=in:st=0:d=", StringComparison.Ordinal)), "Audio edges are de-clicked without overlapping speech from separate cuts.");
            TestAssert.True(ContainsPair(command.Arguments, "-map", "[ajoin]"), "The treated audio must actually be used in output.");
            TestAssert.Equal(style == MontageStyle.Tension, command.Arguments.Any(value => value.Contains(",fade=t=in", StringComparison.Ordinal)),
                "Tension adds a restrained image fade while Impact and Banter keep picture cuts; no transition overlaps subtitle clocks.");
        }
        return Task.CompletedTask;
    }
}
