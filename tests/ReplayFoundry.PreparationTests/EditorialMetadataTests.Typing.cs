using ReplayFoundry.Desktop.Features.Generate.Editorial;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editorial;

namespace ReplayFoundry.PreparationTests;

internal static partial class EditorialMetadataTests
{
    private static async Task StudioTypingKeepsSavedSurfacesStable()
    {
        var (asset, _) = await CreateAssetAsync();
        var project = new GenerationOutputProject("typing-project", GenerationMode.IndividualClips,
            Path.GetFullPath("typing-output"), 1, ClipFulfillmentPreference.FillRequestedCount,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [asset], DateTimeOffset.UtcNow);
        var session = new GenerationOutputSession();
        session.Publish(project);
        using var editor = new StudioEditorialMetadataViewModel(session, null, new ClipEditorialProfileSession());
        editor.Bind(project, asset);
        string saved = editor.Description;
        var notifications = new List<string>();
        editor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName!);
        string[] allowed = [nameof(editor.Description), nameof(editor.DescriptionCharacterCount),
            nameof(editor.PackagingGuidance), nameof(editor.HasUnsavedChanges), nameof(editor.DraftState),
            nameof(editor.SaveGuidance), nameof(editor.RerollProviderText),
            nameof(editor.RerollButtonText), nameof(editor.RerollAutomationName)];
        for (int i = 0; i < 100; i++) editor.Description += "x";
        TestAssert.True(notifications.All(allowed.Contains),
            "Typing must not rebuild copy history, game evidence, or unrelated text fields.");
        TestAssert.True(notifications.Count <= 702, "Only the first edit changes the action label; later keys keep a bounded draft-only cost.");
        TestAssert.True(editor.HasUnsavedChanges && editor.DraftState == "Unsaved", "Typing still marks the draft dirty.");
        TestAssert.True(editor.SaveCommand.CanExecute(null), "Valid typed copy remains saveable.");
        TestAssert.True(ReferenceEquals(project, session.Current), "Typing cannot mutate the saved project.");
        editor.Description = saved;
        TestAssert.False(editor.HasUnsavedChanges, "Returning to saved wording restores the clean state.");
        notifications.Clear();
        editor.AudienceAddress += "!";
        TestAssert.True(notifications.SequenceEqual([nameof(editor.AudienceAddress), nameof(editor.HasUnsavedProfileChanges)]),
            "Profile typing must not touch the clip's draft, evidence, or AI state.");
    }
}
