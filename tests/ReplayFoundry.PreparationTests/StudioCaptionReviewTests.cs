using System.ComponentModel;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task CaptionReviewDistinguishesMissingAndWeakTiming()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        var asset = PopWithMissingWord(fixture.CreateDraft().PrimaryAsset);
        var missing = StudioCaptionTimingReview.Find(asset).Single();
        TestAssert.True(missing.BlocksPop && missing.Reason.Contains("“I”", StringComparison.Ordinal),
            "The review must identify the omitted word and the effect it prevents.");
        var edits = StudioCaptionTrackEditing.CreateDrafts(asset);
        var rows = StudioCaptionTimingReview.CreateWordRows(edits[0]);
        TestAssert.True(double.IsNaN(rows[0].StartSeconds), "Review must not invent timing for I.");
        TestAssert.Equal(4d, rows[1].StartSeconds, "The measured know clock must survive the inserted missing row.");
        var weakWords = rows.ToArray(); weakWords[0] = new("I", .05, .25, AcousticScore: .1);
        var advisory = StudioCaptionTimingReview.Find(asset, [edits[0] with { Words = weakWords }]).Single();
        TestAssert.False(advisory.BlocksPop || advisory.NeedsWordTiming, "Weak acoustic fit is advisory when valid timing exists.");
        TestAssert.Equal("Listen to check", advisory.Label, "An acoustic score must not label the transcription as incorrect.");
        TestAssert.Equal(0, StudioCaptionTimingReview.Find(asset, [edits[0] with { StartSeconds = -8, EndSeconds = -3 }]).Count,
            "A phrase wholly outside this cut must not request review.");
        var duplicate = StudioCaptionTimingReview.CreateWordRows(new("ambiguous", "go go now", 0, 2,
            [new("go", .2, .4), new("now", 1, 1.2)]));
        TestAssert.True(double.IsNaN(duplicate[0].StartSeconds) && double.IsNaN(duplicate[1].StartSeconds),
            "Repeated text cannot borrow an ambiguously matched provider clock.");
        return Task.CompletedTask;
    }

    private static Task CaptionReviewOpensThePhraseWithoutLosingDraftEdits()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        var project = fixture.CreateDraft(); project = project.ReplaceAsset(PopWithMissingWord(project.PrimaryAsset));
        var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioCaptionTrackEditorViewModel(session); editor.Bind(project, project.PrimaryAsset);
        var issue = editor.Review.Panel.Issues.Single();
        StudioCaptionTimingIssue? selected = null; editor.Review.PreviewRequested += value => selected = value;
        editor.Review.Panel.ReviewCommand.Execute(issue);
        TestAssert.Equal(issue.SegmentId, selected!.SegmentId, "Review must target the exact phrase, not search for a text occurrence.");
        TestAssert.True(editor.Review.IsReviewingTiming && editor.Review.VisibleSegments.Single().IsTimingReviewOpen,
            "Review must expose the word controls without another menu click.");
        TestAssert.False(editor.HasUnsavedChanges, "Opening review is navigation, not an edit.");
        var changed = new List<string?>(); editor.Review.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        editor.Segments[0].Text = "I know now!";
        TestAssert.False(changed.Contains(nameof(StudioCaptionDraftReview.VisibleSegments)),
            "Typing must not rebind the editor's item source and lose its caret.");
        editor.RebuildWordRowsCommand.Execute(editor.Segments[0]);
        TestAssert.True(double.IsNaN(editor.Segments[0].Words[0].StartSeconds), "Missing rows stay blank for audio alignment or manual timing.");
        TestAssert.Equal(4d, editor.Segments[0].Words[1].StartSeconds, "Rebuilding rows must keep surrounding measured timings.");
        editor.Segments[0].Words[0].StartSeconds = .05; editor.Segments[0].Words[0].EndSeconds = .25;
        TestAssert.False(editor.Review.Panel.HasIssues, "Completing the actual missing time must clear its flag without dismissing anything.");
        editor.Review.ShowAllPhrasesCommand.Execute(null);
        TestAssert.Equal("I know now!", editor.Segments[0].Text, "Changing review views must preserve unsaved corrections.");
        editor.UndoCommand.Execute(null);
        TestAssert.True(editor.Review.Panel.HasIssues, "Undoing a timing correction must restore its review flag.");
        return Task.CompletedTask;
    }

    private static async Task CaptionReviewKeepsFailedRendersActionable()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, hasAudio: true);
        var project = fixture.CreateDraft(); project = project.ReplaceAsset(PopWithMissingWord(project.PrimaryAsset));
        var session = new GenerationOutputSession(); session.Publish(project);
        using var studio = new StudioViewModel(session, session, fixture.CreateStudioRenderer(new WritingProcessRunner()));
        studio.FinalRender.AddToQueueCommand.Execute(null);
        TestAssert.True(studio.FinalRender.CaptionReview.HasBlockingIssues, "The queue must flag the phrase before the render is attempted.");
        var issue = studio.FinalRender.CaptionReview.Issues.Single();
        studio.FinalRender.CaptionReview.ReviewFirstCommand.Execute(null);
        TestAssert.Equal(StudioInspectorSection.Captions, studio.Inspector.SelectedInspector, "Queue review must open the caption inspector.");
        TestAssert.True(studio.Inspector.Caption.Review.IsReviewingTiming, "Queue review must focus the specific phrase's controls.");
        TestAssert.Equal(project.PrimaryAsset.SourceStart.TotalSeconds + issue.StartSeconds, studio.Preview.PreviewPositionSeconds,
            "Queue review must seek to the issue's clock in the source recording.");
        studio.Inspector.Caption.Segments[0].Text = "Changed draft";
        TestAssert.True(studio.FinalRender.NeedsCaptionSave && !studio.FinalRender.IsReadyToRender,
            "Render must not bypass an unsaved caption correction and use the old transcript.");
        int queueRefreshes = 0;
        studio.FinalRender.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(studio.FinalRender.QueueItems)) queueRefreshes++; };
        studio.Inspector.Caption.Segments[0].Text = "Changed draft again";
        TestAssert.Equal(0, queueRefreshes, "Continued typing must not refresh the queue or Browser cards for each character.");
        studio.Inspector.Caption.UndoCommand.Execute(null);
        studio.Inspector.Caption.UndoCommand.Execute(null);
        await ((AsyncDelegateCommand)studio.FinalRender.RenderQueueCommand).ExecuteAsync();
        TestAssert.False(studio.FinalRender.HasError, "Expected timing review should not appear as a generic red encoder failure.");
        TestAssert.True(studio.FinalRender.Status.Contains("timing review", StringComparison.OrdinalIgnoreCase), "The queue must explain the actionable state.");
        TestAssert.True(studio.FinalRender.HasQueuedItems && studio.FinalRender.IsReadyToRender, "A caption review stop must keep the queue available for retry.");
        TestAssert.True(studio.FinalRender.CaptionReview.HasBlockingIssues, "Failed automatic repair must not clear unresolved flags.");
        TestAssert.True(studio.FinalRender.CaptionReview.HasMessage, "The compact queue must expose the repair failure's actionable guidance.");
        studio.FinalRender.RemoveQueuedItemCommand.Execute(issue.AssetId);
        TestAssert.False(studio.FinalRender.CaptionReview.HasIssues, "Removing an item from the queue must remove its queue-only reminders.");
        TestAssert.False(studio.FinalRender.CaptionReview.HasMessage, "A removed issue must not leave a stale repair message.");
    }
}
