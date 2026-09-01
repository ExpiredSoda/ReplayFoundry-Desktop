using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task VideoPreviewsRestartFromZeroAfterEnd()
    {
        TestAssert.False(
            MediaPlaybackBoundary.HasReachedEnd(9.9, 10),
            "Playback before the shared end tolerance must not rewind unexpectedly.");
        TestAssert.True(
            MediaPlaybackBoundary.HasReachedEnd(9.96, 10),
            "Playback inside the shared end tolerance must be treated as complete.");
        TestAssert.False(
            MediaPlaybackBoundary.HasReachedEnd(0, 0),
            "An unopened player without a real duration must not look complete.");

        string libraryPlayback = ReadDesktopSource(
            "Features", "Library", "LibraryPlaybackViewModel.cs");
        string studioPlayback = ReadDesktopSource(
            "Features", "Studio", "Preview", "StudioPreviewViewModel.cs");
        string publishPlayback = ReadDesktopSource(
            "Features", "Publish", "PublishPreparationWindow.xaml.cs");
        string guidancePlayback = ReadDesktopSource(
            "Features", "Generate", "GenerationSetup", "Steps",
            "MomentGuidance", "MomentGuidanceStepView.xaml.cs");
        string hiddenMoments = ReadDesktopSource(
            "Features", "Studio", "HiddenMoments",
            "StudioHiddenMomentsView.xaml");

        foreach ((string name, string code) in new[]
                 {
                     ("Library", libraryPlayback),
                     ("Studio", studioPlayback),
                     ("Publish", publishPlayback),
                     ("priority-moment guidance", guidancePlayback),
                 })
        {
            TestAssert.True(
                code.Contains(
                    "MediaPlaybackBoundary.HasReachedEnd(",
                    StringComparison.Ordinal),
                $"{name} playback must use the shared end boundary before starting a replay.");
        }

        TestAssert.True(
            libraryPlayback.Contains(
                "SetPosition(0, requestSeek: true);",
                StringComparison.Ordinal) &&
            studioPlayback.Contains(
                "PreviewPositionSeconds = PreviewPositionMinimumSeconds;",
                StringComparison.Ordinal) &&
            publishPlayback.Contains("SeekTo(0);", StringComparison.Ordinal) &&
            guidancePlayback.Contains(
                "PriorityPlayer.Position = TimeSpan.Zero;",
                StringComparison.Ordinal),
            "Every replay-capable video surface must issue its zero-time native seek before Play.");
        TestAssert.True(
            hiddenMoments.Contains(
                "<preview:StudioPreviewView",
                StringComparison.Ordinal),
            "Alternate-moment review must keep sharing Studio's corrected transport instead of forking another player.");

        return Task.CompletedTask;
    }

    private static string ReadDesktopSource(params string[] segments) =>
        File.ReadAllText(Path.Combine(
            RepositoryLayout.Root,
            "src",
            "ReplayFoundry.Desktop",
            Path.Combine(segments)));
}
