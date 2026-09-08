using System.Text.Json;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.ModeSelection;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.HiddenMoments;
using ReplayFoundry.Desktop.Features.Studio.Projects;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Editorial;
using ReplayFoundry.Desktop.Media.Intelligence.Preferences;
using ReplayFoundry.Desktop.Media.Moments;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Presentation.Commands;
using ReplayFoundry.Desktop.Presentation.Controls;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static GenerationOutputProject TaggedMomentsProject(string path)
    {
        var media = TestMediaFactory.Create(path, TimeSpan.FromMinutes(20));
        var selected = new GenerationOutputAsset("selected", 1, media, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40),
            80, 70, GenerationCandidateSelectionReason.QualityQualified, "Existing clip.");
        MomentContentProfile[] profiles = [new(Gameplay: true, RecordingIndexCompleted: true),
            new(Commentary: true, Funny: true, VisualReviewCompleted: true, Lore: true), new()];
        var hidden = profiles.Select((profile, index) => GenerationHiddenMoment.RestoreStudioHandoff(
            "hidden-" + index, index + 1, 0, media, TimeSpan.FromSeconds(200 + index * 100), TimeSpan.FromSeconds(230 + index * 100),
            80, 70, GenerationHiddenMomentReason.RequestedCountReached, "More from this recording.",
            new ClipPreferenceFeatureVector([new(ClipPreferenceFeatureCode.DeterministicScore, .8)], detectedContent: profile),
            ClipEditorialGenerationPreference.HeuristicOnly, null, null, null, null)).ToArray();
        return new("tagged-moments", GenerationMode.IndividualClips, Path.GetTempPath(), 1, ClipFulfillmentPreference.QualityFirst,
            GenerationClipFulfillmentOutcome.RequestedCountMetAtQualityTarget, [selected], DateTimeOffset.UtcNow, hiddenMoments: hidden, sourceMedia: [media]);
    }
    private static Task MomentTagsSurviveProjects()
    {
        string path = Path.GetTempFileName();
        try
        {
            GenerationOutputProject project = TaggedMomentsProject(path);
            var doc = StudioProjectDocumentMapper.Capture(project, 1, DateTimeOffset.UtcNow);
            var reopened = StudioProjectDocumentMapper.Restore(JsonSerializer.Deserialize<StudioProjectDocument>(JsonSerializer.Serialize(doc))!);
            TestAssert.True(reopened.HiddenMoments[1].PreferenceFeatures.DetectedContent is { Commentary: true, Funny: true, Lore: true },
                "Detected categories must survive serialization, including moments beyond the requested clip count.");
            TestAssert.True(reopened.HiddenMoments[0].PreferenceFeatures.DetectedContent is { RecordingIndexCompleted: true, VisualReviewCompleted: false },
                "A saved recording label must not pretend a close picture check has completed.");
            var filters = new StudioMomentFilters(() => { });
            filters.Commentary = true; filters.Funny = true;
            filters.Lore = true;
            TestAssert.Equal(1, reopened.HiddenMoments.Count(moment => filters.Matches(moment.PreferenceFeatures)), "Combined filters require both categories.");
            filters.Unclassified = true;
            TestAssert.Equal(1, reopened.HiddenMoments.Count(moment => filters.Matches(moment.PreferenceFeatures)), "Unreviewed moments remain discoverable.");
            filters.Reset();
            TestAssert.Equal(3, reopened.HiddenMoments.Count(moment => filters.Matches(moment.PreferenceFeatures)), "All restores the complete candidate pool.");
            var legacy = new ClipPreferenceFeatureVector([new(ClipPreferenceFeatureCode.ContinuousActivity, 1),
                new(ClipPreferenceFeatureCode.CreatorSpeech, .5)], new("game", "IndividualClips", "Gameplay", "Humor"));
            MomentContentProfile legacyProfile = StudioMomentFilters.Profile(legacy);
            TestAssert.False(legacyProfile.HasLabels,
                "Older track routing, motion and requested Humor are not evidence of semantic content categories.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
    private static Task SuggestedMomentsDoNotChangeTrim()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var project = TaggedMomentsProject(path);
                using var model = new StudioManualClipViewModel(null, null, () => true);
                model.Bind(project); model.IsOpen = true;
                TestAssert.Equal(1200d, model.ViewportDurationSeconds, "The separate moment workspace starts with the full recording.");
                TestAssert.Equal(4, model.Suggestions.Items.Count, "The timeline includes chosen and extra candidate moments.");
                model.Suggestions.Filters.Commentary = true;
                model.Suggestions.NextCommand.Execute(null);
                TestAssert.Equal(300d, model.SourcePositionSeconds, "Selecting a suggestion seeks to its absolute recording time.");
                TestAssert.Equal(0d, model.SelectionStartSeconds, "Previewing cannot silently replace the trim start.");
                TestAssert.Equal(30d, model.SelectionEndSeconds, "Previewing cannot silently replace the trim end.");
                model.Suggestions.UseCommand.Execute(null);
                TestAssert.Equal(300d, model.SelectionStartSeconds, "Use this range explicitly sets the trim.");
                TestAssert.Equal(330d, model.SelectionEndSeconds, "Use this range preserves the suggested end.");
                model.Suggestions.Filters.Gameplay = true;
                TestAssert.Equal(0, model.Suggestions.Items.Count, "Empty combined filters return no invented matches.");
                TestAssert.False(model.Suggestions.UseCommand.CanExecute(null), "A filtered-out selection must not remain actionable.");
                TestAssert.Equal(300d, model.SelectionStartSeconds, "Changing filters preserves the user's chosen range.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }
    private static Task HiddenMomentFiltersPreserveReviewState()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var session = new GenerationOutputSession(); session.Publish(TaggedMomentsProject(path));
                using var model = new StudioHiddenMomentsViewModel(session, null, null);
                model.Bind(session.Current); model.OpenCommand.Execute(null);
                model.Filters.Funny = true; model.Filters.Commentary = true;
                TestAssert.Equal("hidden-1", model.Current!.Id, "The review browser uses the same combined categories as the timeline.");
                TestAssert.False(model.NextMomentCommand.CanExecute(null), "Next cannot navigate to a nonmatching candidate.");
                model.SkipCommand.Execute(null);
                TestAssert.True(model.Current is null && model.RemainingCount == 2, "Skipping the matching moment must preserve the two other candidates.");
                model.Filters.Reset();
                TestAssert.Equal("hidden-0", model.Current!.Id, "Clearing a filter restores undiscarded candidates.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }
    private static Task TimelineMarkerGroupsPreserveCandidates()
    {
        var items = Enumerable.Range(0, 100).Select(index => new StudioMomentSuggestion(index.ToString(), index * 10,
            index * 10 + 30, 80, new(), "Candidate")).ToArray();
        var groups = StudioMomentMarkerProjection.Project(items, 0, 1200, 600);
        TestAssert.Equal(1, groups.Count, "Dense overlapping windows occupy one uncluttered range.");
        TestAssert.Equal(100, groups.Sum(group => group.Moments.Count), "Grouping must preserve every candidate for exploration.");
        StudioMomentSuggestion local = StudioMomentMarkerProjection.AtTime(groups[0], 715, null);
        TestAssert.True(local.Start <= 715 && local.End >= 715,
            "Clicking within a dense region must explore that point in the recording, not a distant top-scoring moment.");
        TestAssert.True(StudioMomentMarkerProjection.AtTime(groups[0], 715, local.Id).Id != local.Id,
            "Repeated clicks must cycle the overlapping candidates at that point.");
        var zoomed = StudioMomentMarkerProjection.Project(items, 500, 20, 600);
        TestAssert.True(zoomed.All(group => group.Left >= 0 && group.Right <= 600), "Zoomed markers must remain inside the visible recording range.");
        TestAssert.True(zoomed.SelectMany(group => group.Moments).All(item => item.End > 500 && item.Start < 520), "Offscreen candidates cannot draw false markers.");
        TestAssert.Equal(0, StudioMomentMarkerProjection.Project(items, 0, double.NaN, 600).Count, "Invalid viewport values cannot produce invalid drawing coordinates.");
        return Task.CompletedTask;
    }
    private static Task WaveformSeekingSupportsAccessibility()
    {
        RunOnSta(() =>
        {
            double selected = -1;
            var waveform = new AudioSignalWaveform { Peaks = new double[] { 0, .5, 1 }, SeekCommand = new DelegateCommand<double>(value => selected = value) };
            waveform.SeekTo(1.5); TestAssert.Equal(1d, selected, "Pointer seeking clamps at the end of the real sample.");
            waveform.SeekTo(-.5); TestAssert.Equal(0d, selected, "Pointer seeking clamps at the sample start.");
            waveform.SeekTo(double.NaN); TestAssert.Equal(0d, selected, "Invalid input cannot reach the audio service.");
            var peer = UIElementAutomationPeer.CreatePeerForElement(waveform)!;
            var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!;
            range.SetValue(75); TestAssert.Equal(.75d, selected, "Assistive technology seeks the same waveform by percentage.");
            waveform.Peaks = [];
            TestAssert.True(range.IsReadOnly, "A waveform without prepared audio cannot pretend to be seekable.");
        });
        return Task.CompletedTask;
    }
    private static async Task FaceSuggestionsAreExplicitAndExpire()
    {
        string path = Path.GetTempFileName();
        CompositionPreviewViewModel? preview = null;
        CompositionLayoutAssistViewModel? assist = null;
        Task? operation = null;
        var service = new DeferredLayoutSuggestion();
        try
        {
            RunOnSta(() =>
            {
                var prepared = new PreparedGenerationSource(new SelectedVideoSource(path, true), TestMediaFactory.Create(path, TimeSpan.FromMinutes(20)),
                    TestMediaFactory.CreateSnapshot(path, new FileInfo(path).Length));
                preview = new(prepared, new TimelineFrameProvider()); preview.LoadAsync().GetAwaiter().GetResult();
                var regions = new CompositionRegionCollectionViewModel(() => { }); regions.InitializeFullFrameGameplay();
                assist = new(preview, regions, service);
                operation = assist.SuggestAsync();
                preview.RequestedTimestampSeconds += 1;
                service.Complete();
            });
            await operation!;
            RunOnSta(() =>
            {
                TestAssert.False(assist!.HasSuggestion, "A result for an older frame cannot become an actionable suggestion.");
                TestAssert.False(assist.ApplyCommand.CanExecute(null), "Stale suggestions cannot alter manual regions.");
            });
            TestAssert.True(CompositionLayoutSuggestionPolicy.FromFaces([], true) is null, "No clear face must not invent a camera.");
            TestAssert.True(CompositionLayoutSuggestionPolicy.FromFaces([new(.1, .1, .1, .1), new(.6, .1, .1, .1)], false) is null,
                "Multiple faces must not silently choose a game character or another person.");
            var suggested = CompositionLayoutSuggestionPolicy.FromFaces([new(.4, .84, .12, .1)], true)!;
            TestAssert.True(suggested.Gameplay is not null && suggested.Presenter.Y == suggested.Gameplay.Height,
                "A portrait camera near the bottom can suggest an editable split with gameplay above.");
            RunOnSta(() =>
            {
                preview!.LoadAsync().GetAwaiter().GetResult();
                int manualChanges = 0;
                var regions = new CompositionRegionCollectionViewModel(() => manualChanges++);
                regions.InitializeFullFrameGameplay();
                regions.Regions[0].SetGeometry(.1, .2, .8, .7);
                int before = manualChanges;
                using var currentAssist = new CompositionLayoutAssistViewModel(preview, regions, new CompletedLayoutSuggestion(suggested));
                currentAssist.SuggestAsync().GetAwaiter().GetResult();
                TestAssert.True(currentAssist.HasSuggestion, "A result for the current frame is available for explicit review.");
                TestAssert.Equal(1, regions.Regions.Count, "Finding a face alone must not add any regions.");
                TestAssert.Equal(before, manualChanges, "An unapplied suggestion must not mark the layout as changed.");
                currentAssist.ApplyCommand.Execute(null);
                TestAssert.Equal(2, regions.Regions.Count, "Applying the suggestion adds an editable presenter region.");
                TestAssert.Equal(.1, regions.Regions[0].X, "An existing manually adjusted gameplay region must be preserved.");
                TestAssert.True(manualChanges > before && !currentAssist.HasSuggestion,
                    "Applied regions require a fresh confirmation and cannot be added twice.");
            });
        }
        finally { RunOnSta(() => { assist?.Dispose(); preview?.Dispose(); }); File.Delete(path); }
    }
    private sealed class DeferredLayoutSuggestion : ICompositionLayoutSuggestionService
    {
        private readonly TaskCompletionSource<CompositionLayoutSuggestion?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CompositionLayoutSuggestion?> SuggestAsync(VideoPreviewFrame frame, CancellationToken token) => _completion.Task;
        public void Complete() => _completion.SetResult(new(new(.1, .1, .3, .3), null, "Possible face."));
    }
    private sealed class CompletedLayoutSuggestion(CompositionLayoutSuggestion result) : ICompositionLayoutSuggestionService
    {
        public Task<CompositionLayoutSuggestion?> SuggestAsync(VideoPreviewFrame frame, CancellationToken token) => Task.FromResult<CompositionLayoutSuggestion?>(result);
    }
}
