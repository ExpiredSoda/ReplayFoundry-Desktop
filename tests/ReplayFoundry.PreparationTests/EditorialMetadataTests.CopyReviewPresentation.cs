using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static Task StudioShowsSafeAiUnavailableReason()
    {
        const string reason = "Update or repair Advanced AI, then restart Replay Foundry.";
        var preference = new ReplayFoundry.Desktop.Features.Settings.EditorialRerollPreferenceState(
            new ReplayFoundry.Desktop.Features.Settings.InMemoryEditorialRerollPreferenceStore());
        var unavailable = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(), aiUnavailableReason: reason);
        using var editor = new StudioEditorialMetadataViewModel(
            null, unavailable, null, preference);
        TestAssert.Equal(reason, editor.RerollProviderText,
            "The safe startup explanation must reach the actual Studio action.");
        preference.SetUseLocalAi(false);
        TestAssert.True(editor.RerollProviderText.Contains("quick local rewrite", StringComparison.Ordinal),
            "AI readiness must not obscure the user's heuristic preference.");
        using var fallback = new StudioEditorialMetadataViewModel(null,
            new ClipEditorialMetadataGenerationService(new HeuristicClipEditorialMetadataGenerator()), null);
        TestAssert.True(fallback.RerollProviderText.Contains("Check Advanced AI", StringComparison.Ordinal),
            "Unknown readiness retains the generic settings guidance.");
        var available = new ClipEditorialMetadataGenerationService(
            new HeuristicClipEditorialMetadataGenerator(), new RichDraftMetadataGenerator(),
            aiUnavailableReason: reason);
        using var ready = new StudioEditorialMetadataViewModel(null, available, null);
        TestAssert.True(available.AiUnavailableReason is null &&
            ready.RerollProviderText.Contains("will write", StringComparison.Ordinal),
            "An available writer must not display a stale startup reason.");
        return Task.CompletedTask;
    }

    private static async Task StudioCopyReviewIsVisibleAndNonBlocking()
    {
        (GenerationOutputAsset asset, ClipEditorialMetadataDraft original) =
            await CreateAssetAsync();
        // A saved older draft can contain a generic provider explanation.
        // Display the current human explanation while retaining its provenance.
        var issues = new[]
        {
            new ClipEditorialMetadataQualityIssue(
                ClipEditorialMetadataQualityIssueCode.AudienceCopyReview,
                "Older generic review message.", "UnsupportedCreatorEmbodiment"),
            new ClipEditorialMetadataQualityIssue(
                ClipEditorialMetadataQualityIssueCode.AudienceCopyReview,
                "Older balance message.", "BalanceNotSatisfied"),
            new ClipEditorialMetadataQualityIssue(
                ClipEditorialMetadataQualityIssueCode.AudienceCopyReview,
                "A provider-specific detail to preserve.", "FutureProviderRule"),
        };
        var reviewable = new ClipEditorialMetadataDraft(
            original.Title, original.Description, original.Tags,
            original.Origin, original.Generator, original.Attempt,
            readiness: ClipEditorialMetadataReadiness.GroundedDraft,
            qualityIssues: issues);
        asset = asset.WithCurrentCutEditorialMetadata(asset.EditorialContext!, reviewable);
        var project = new GenerationOutputProject(
            "project-visible-copy-review", GenerationMode.IndividualClips,
            Path.GetFullPath("studio-visible-copy-review-output"), 1,
            ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget,
            [asset], DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var editor = new StudioEditorialMetadataViewModel(
            session, generator: null, new ClipEditorialProfileSession());
        editor.Bind(project, asset);

        TestAssert.True(editor.HasCopyReview, "Saved copy findings must be visible.");
        TestAssert.Equal("Check copy", editor.DraftState,
            "Semantic findings must not appear as an unqualified Ready badge.");
        TestAssert.True(editor.Status.Contains("does not support", StringComparison.Ordinal) &&
            editor.Status.Contains("unreviewed speech", StringComparison.Ordinal) &&
            editor.Status.Contains("commentary with the visible action", StringComparison.Ordinal) &&
            editor.Status.Contains("A provider-specific detail to preserve.", StringComparison.Ordinal),
            "Known historical rules need actionable explanations and unknown rules retain their own message.");
        TestAssert.True(editor.SaveGuidance.Contains("still add", StringComparison.Ordinal) &&
            reviewable.IsPublishReady,
            "Visible copy review must preserve existing queue/render usability.");
        TestAssert.Equal("Older generic review message.", reviewable.QualityIssues[0].Message,
            "Presentation must not mutate the saved provider finding.");

        editor.MarkReviewedCommand.Execute(null);
        editor.Bind(session.Current!, session.Current!.PrimaryAsset);
        TestAssert.Equal("Reviewed", editor.DraftState,
            "The user's explicit review action must still change the review badge.");
        TestAssert.True(editor.HasCopyReview && editor.SaveGuidance.StartsWith("Reviewed.", StringComparison.Ordinal),
            "Explicit review is respected while automatic findings remain available as provenance.");

        editor.Title = "My corrected clip title";
        TestAssert.Equal("Unsaved", editor.DraftState, "Visible edits retain the unsaved indicator.");
        TestAssert.True(editor.SaveCommand.CanExecute(null), "Review findings must not block saving edits.");
        editor.SaveCommand.Execute(null);
        editor.Bind(session.Current!, session.Current!.PrimaryAsset);
        TestAssert.False(editor.HasCopyReview, "Saved user edits clear stale automatic copy findings.");
        TestAssert.Equal("Saved", editor.DraftState, "Saving edits must not silently mark them reviewed.");
        TestAssert.Equal(ClipEditorialMetadataReadiness.UserEditedDraft,
            session.Current!.PrimaryAsset.EditorialMetadata!.Readiness,
            "The user retains the explicit review action.");
    }
}
