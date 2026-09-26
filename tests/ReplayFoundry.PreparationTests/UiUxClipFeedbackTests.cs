using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Generate.Moments;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Preview;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task PreviewFeedbackTracksCurrentClip()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var service = new PreviewFeedbackTestService();
            using var preference = new StudioClipPreferenceViewModel(service);
            GenerationOutputAsset Asset(string id) => new(id, 1,
                TestMediaFactory.Create(TestMediaFactory.CreateSourcePath("feedback-preview.mkv"), TimeSpan.FromMinutes(2)),
                null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40), 90, 70,
                GenerationCandidateSelectionReason.QualityQualified, "Test moment");
            preference.Bind(null, Asset("first"));
            var preview = new StudioPreviewView { DataContext = new LayoutOnlyStudioPreview(), ClipPreference = preference, Width = 450, Height = 650 };
            var alternatePreview = new StudioPreviewView { DataContext = new LayoutOnlyStudioPreview(), ClipPreference = preference, Width = 450, Height = 650 };
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(preview); host.Children.Add(alternatePreview);
            host.Measure(new Size(900, 650)); host.Arrange(new Rect(0, 0, 900, 650)); host.UpdateLayout();
            var choices = EnumerateVisualDescendants<RadioButton>(preview).Where(button => button.Command == preference.SetPreferenceCommand).ToArray();
            TestAssert.Equal(3, choices.Length, "All three rating commands must be present beside playback.");
            TestAssert.True(choices.All(button => button.IsChecked != true) && service.Updates.Count == 0,
                "Viewing a clip must not record an implicit Neutral rating.");
            var like = choices.Single(button => Equals(button.CommandParameter, StudioClipPreferenceRating.Like));
            like.Command.Execute(like.CommandParameter);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            TestAssert.Equal("first", service.Updates.Single(), "The playback rating must address the visible clip.");
            TestAssert.True(like.IsChecked == true, "The saved rating must be reflected in the preview.");
            TestAssert.True(EnumerateVisualDescendants<RadioButton>(alternatePreview)
                .Single(button => Equals(button.CommandParameter, StudioClipPreferenceRating.Like)).IsChecked == true,
                "Wide and compact previews must both reflect the same saved rating without unchecking each other.");
            preference.Bind(null, Asset("second"));
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            TestAssert.True(choices.All(button => button.IsChecked != true), "Changing clips must clear the previous clip's selected rating.");
            preference.SetHostBusy(true);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            TestAssert.True(choices.All(button => !button.IsEnabled), "Busy editing must disable ratings consistently with the shared command.");
        });
        return Task.CompletedTask;
    }

    private sealed class PreviewFeedbackTestService : IStudioClipPreferenceService
    {
        public StudioClipPreferenceStatus Current => new(0, 0, 0, 60, false);
        public List<string> Updates { get; } = [];
        public bool CanRate(GenerationOutputAsset asset) => true;
        public void Update(GenerationOutputAsset asset, StudioClipPreferenceRating? previous, StudioClipPreferenceRating current) => Updates.Add(asset.Id);
    }
}
