using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static async Task FailedStudioRewriteKeepsEdits()
    {
        var (asset, _) = await CreateAssetAsync();
        var project = new GenerationOutputProject("rewrite-failure", GenerationMode.IndividualClips,
            Path.GetFullPath("rewrite-failure-output"), 1, ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [asset], DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        var generator = new ClipEditorialMetadataGenerationService(new HeuristicClipEditorialMetadataGenerator(),
            new FailingBatchMetadataGenerator());
        using var studio = new StudioViewModel(session, session, new UnusedProjectRenderer(), generator, new ClipEditorialProfileSession());
        var editor = studio.Inspector.Editorial;
        editor.Title = "My edited title must survive a failed rewrite";
        editor.Description = "My description must survive too.";
        await ((AsyncDelegateCommand)editor.RerollCommand).ExecuteAsync();
        TestAssert.Equal("My edited title must survive a failed rewrite", session.Current!.PrimaryAsset.EditorialMetadata!.Title,
            "A failed rewrite must retain the edit it just saved.");
        TestAssert.Equal("My description must survive too.", editor.Description, "The visible draft must retain the user's wording.");
        TestAssert.False(editor.HasUnsavedChanges, "The saved edit stays clean even if generation fails.");
        TestAssert.False(editor.IsGenerating, "The editor becomes usable after the failure.");
    }
}
