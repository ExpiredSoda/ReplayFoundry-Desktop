using System.Text.Json;
using System.Text.Json.Nodes;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Projects;

namespace ReplayFoundry.PreparationTests;

internal static partial class StudioProjectPersistenceTests
{
    private static Task AuthoredContextFreshnessSurvivesStorage()
    {
        using var fixture = new PersistenceFixture();
        GenerationOutputProject project = fixture.CreateProject();
        var legacy = project.PrimaryAsset;
        TestAssert.False(legacy.IsEditorialMetadataCurrentForCut, "Restoring older unaudited wording must not invent an authored caption identity.");
        var authored = legacy.WithCurrentCutEditorialMetadata(legacy.CreateCurrentCutEditorialContext(), legacy.EditorialMetadata!);
        project = project.ReplaceAsset(authored);
        fixture.Store.Save(project, revision: 1);
        var restored = fixture.Store.Load(project.Id).Project!.PrimaryAsset;
        TestAssert.True(restored.IsEditorialMetadataCurrentForCut, "A known current authored context must survive the real storage contract.");
        TestAssert.Equal(authored.EditorialAuthoredContextRevision, restored.EditorialAuthoredContextRevision, "Authored identity must be persisted exactly.");
        var changedContext = restored.EditorialContext!.WithTranscripts([]);
        var stale = restored.WithCurrentCutEditorialMetadata(changedContext, restored.EditorialMetadata!, markAuthoredForCurrentContext: false);
        TestAssert.False(stale.IsEditorialMetadataCurrentForCut, "Changing saved transcript evidence cannot silently re-author unchanged wording.");
        project = project.ReplaceAsset(stale);
        fixture.Store.Save(project, revision: 2);
        var restoredStale = fixture.Store.Load(project.Id).Project!.PrimaryAsset;
        TestAssert.False(restoredStale.IsEditorialMetadataCurrentForCut, "The stale state must survive restart.");
        TestAssert.Equal(authored.EditorialAuthoredContextRevision, restoredStale.EditorialAuthoredContextRevision, "Old wording keeps its original context after restart.");

        var document = StudioProjectDocumentMapper.Capture(project, 3, DateTimeOffset.UtcNow);
        var node = JsonNode.Parse(JsonSerializer.Serialize(document))!;
        node["Assets"]![0]!.AsObject().Remove("EditorialAuthoredContextRevision");
        var oldDocument = JsonSerializer.Deserialize<StudioProjectDocument>(node.ToJsonString())!;
        var restoredUnknown = StudioProjectDocumentMapper.Restore(oldDocument).PrimaryAsset;
        TestAssert.True(restoredUnknown.EditorialAuthoredContextRevision is null, "Absent legacy identity must remain unknown.");
        TestAssert.False(restoredUnknown.WithRenderSettings(restoredUnknown.RenderSettings).IsEditorialMetadataCurrentForCut,
            "An unrelated render edit must not stamp unknown legacy wording as current.");
        return Task.CompletedTask;
    }
}
