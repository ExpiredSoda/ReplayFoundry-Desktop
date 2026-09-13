using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio;
using ReplayFoundry.Desktop.Features.Studio.Browser;
using ReplayFoundry.Desktop.Features.Studio.Editing;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static Task ClipSearchUsesSavedCaptionsAndKeepsSelection()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips, scoreSets: [[91, 95]]);
        var project = fixture.CreateDraft(); project = project.ReplaceAsset(PopWithMissingWord(project.PrimaryAsset));
        var session = new GenerationOutputSession(); session.Publish(project);
        using var studio = new StudioViewModel(session, session, fixture.CreateStudioRenderer(new WritingProcessRunner()));
        var selected = studio.SelectedAsset;
        TestAssert.True(project.Assets.Count > 1, "This scenario must compare distinct ranked clips.");
        studio.ClipBrowser.Query = "KNOW now";
        TestAssert.Equal(project.PrimaryAsset.Id, studio.ClipBrowser.Items.Single().AssetId!, "Caption search must be case insensitive and match all terms.");
        studio.ClipBrowser.Query = "no-such-utterance";
        TestAssert.True(studio.ClipBrowser.HasNoMatches, "A failed search needs a truthful empty state.");
        TestAssert.Equal(selected!.Id, studio.SelectedAsset!.Id, "Filtering must not switch the clip or discard its edits.");
        TestAssert.False(studio.FinalRender.HasQueuedItems, "Searching cannot add clips to the render queue.");
        studio.ClipBrowser.ClearCommand.Execute(null);
        studio.ClipBrowser.SelectedOrder = StudioClipBrowserViewModel.Orders[1];
        TestAssert.Equal(string.Join('|', project.Assets.OrderBy(asset => asset.SourceStart).Select(asset => asset.Id)),
            string.Join('|', studio.ClipBrowser.Items.Select(item => item.AssetId)), "Recording order must use source timestamps, not rank labels.");
        studio.ClipBrowser.Query = "know";
        var cut = project.PrimaryAsset.WithStudioEdits(project.PrimaryAsset.SourceStart + TimeSpan.FromSeconds(5.1),
            project.PrimaryAsset.SourceEnd, project.PrimaryAsset.Appearance);
        session.ReplaceAsset(project.Id, cut);
        TestAssert.True(studio.ClipBrowser.HasNoMatches, "A saved cut must invalidate the search index and omit captions outside that cut.");
        return Task.CompletedTask;
    }

    private static Task CaptionFindReplaceIsLiteralAndUndoable()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips);
        var project = fixture.CreateDraft(); var session = new GenerationOutputSession(); session.Publish(project);
        using var editor = new StudioCaptionTrackEditorViewModel(session); editor.Bind(project, project.PrimaryAsset);
        var phrases = new StudioCaptionSegmentEdit[]
        {
            new("one", "marine marine marines.", 0, 2, [new("marine", .1, .4), new("marine", .5, .8), new("marines.", 1, 1.3)]),
            new("two", "Marine wins", 3, 4, [new("Marine", 3.1, 3.4), new("wins", 3.5, 3.8)])
        };
        editor.RestorePendingDraft(new(phrases));
        editor.TextTools.Query = "marine";
        TestAssert.Equal(3, editor.TextTools.Matches.Count, "Whole-word search cannot include marines.");
        editor.TextTools.NextCommand.Execute(null);
        TestAssert.Equal("one", editor.Review.VisibleSegments.Single().Id, "Find next must open the matching phrase.");
        int rebinds = 0;
        editor.Review.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(StudioCaptionDraftReview.VisibleSegments)) rebinds++; };
        editor.Segments[0].Text += "!";
        TestAssert.Equal(0, rebinds, "Typing during a search must not rebuild the focused text editor.");
        editor.TextTools.Replacement = "Astartes";
        editor.TextTools.ReplaceAllCommand.Execute(null);
        TestAssert.Equal("Astartes Astartes marines.!", editor.Segments[0].Text, "Replace all must change only literal matches.");
        TestAssert.Equal("Astartes wins", editor.Segments[1].Text, "The correction must cover every matching phrase in this clip.");
        TestAssert.Equal(.5, editor.Segments[0].Words[1].StartSeconds, "A one-word spelling correction must preserve its measured start.");
        editor.UndoCommand.Execute(null);
        TestAssert.Equal("marine marine marines.!", editor.Segments[0].Text, "One undo must restore the complete batch and retain earlier edits.");
        TestAssert.Equal("Marine wins", editor.Segments[1].Text, "One undo must restore the other affected phrase too.");
        var literal = StudioCaptionTextReplacement.Apply(new("literal", "cost $1 [x]", 0, 2), "[x]", "$2", false);
        TestAssert.Equal("cost $1 $2", literal.Text, "Search and replacement must treat regex metacharacters and dollar substitutions literally.");
        return Task.CompletedTask;
    }

    private static Task CaptionReplacementDoesNotInventNewWordTiming()
    {
        var phrase = new StudioCaptionSegmentEdit("one", "marine wins now", 0, 3,
            [new("marine", .1, .6, AcousticScore: .9), new("wins", 1, 1.3), new("now", 2, 2.3)]);
        var expanded = StudioCaptionTextReplacement.Apply(phrase, "marine", "Space Marine", true);
        var rows = StudioCaptionTimingReview.CreateWordRows(expanded);
        TestAssert.True(double.IsNaN(rows[0].StartSeconds) && double.IsNaN(rows[1].StartSeconds), "Splitting one word into two must not fabricate their boundary.");
        TestAssert.Equal(1d, rows[2].StartSeconds, "The unaffected wins timestamp must survive a longer name.");
        var shortened = StudioCaptionTextReplacement.Apply(phrase, "marine wins", "victory", true);
        rows = StudioCaptionTimingReview.CreateWordRows(shortened);
        TestAssert.True(double.IsNaN(rows[0].EndSeconds), "A merged phrase needs alignment instead of guessed word timing.");
        TestAssert.Equal(2d, rows[1].StartSeconds, "Cross-word replacements must keep later observed clocks.");
        var corrected = StudioCaptionTextReplacement.Apply(phrase, "marine", "Astartes", true);
        TestAssert.Null(corrected.Words![0].AcousticScore, "Acoustic confidence in the old wording must not be attributed to the correction.");
        return Task.CompletedTask;
    }

    private static Task CaptionPacingReviewIsAdvisoryAndCutAware()
    {
        using var fixture = CreateFixture(GenerationMode.IndividualClips);
        var asset = PopWithMissingWord(fixture.CreateDraft().PrimaryAsset);
        var held = new StudioCaptionSegmentEdit("hold", "wait now", 0, 5,
            [new("wait", .1, 3), new("now", 4, 4.3)]);
        var issue = StudioCaptionTimingReview.Find(asset, [held]).Single();
        TestAssert.False(issue.BlocksPop || issue.NeedsWordTiming, "A long but valid word is a listening reminder, not an export failure.");
        TestAssert.True(issue.Reason.Contains("2.9 seconds", StringComparison.Ordinal), "The hold review should explain the measured duration.");
        TestAssert.Equal(3d, held.Words![0].EndSeconds, "A pacing review must never silently shorten a word.");
        var clean = asset.WithStudioEdits(asset.SourceStart, asset.SourceEnd,
            new StudioClipAppearance(GenerationCaptionStylePreset.Clean, 47, StudioVideoEffectPreset.None, 0));
        TestAssert.Equal(0, StudioCaptionTimingReview.Find(clean, [held]).Count, "One-word Pop hold advice must not misdescribe whole-phrase captions.");
        var first = new StudioCaptionSegmentEdit("first", "hello", 0, 1.1, [new("hello", .1, 1)]);
        var second = new StudioCaptionSegmentEdit("second", "world", 1, 2, [new("world", 1.1, 1.8)]);
        var overlaps = StudioCaptionTimingReview.Find(asset, [first, second]);
        TestAssert.Equal(2, overlaps.Count, "Both overlapping caption phrases should be reviewable.");
        TestAssert.True(overlaps.All(value => !value.BlocksPop && value.Reason.Contains("overlaps", StringComparison.Ordinal)),
            "Intentional simultaneous speakers need review, not guessed removal.");
        TestAssert.Equal(0, StudioCaptionTimingReview.Find(asset, [first with { EndSeconds = 1 }, second]).Count,
            "Phrases that only touch at a boundary must not be flagged as overlapping.");
        return Task.CompletedTask;
    }
}
