using ReplayFoundry.Desktop.Presentation;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task PausedPreviewNavigationStaysMuted()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var view = new ReplayFoundry.Desktop.Features.Studio.Preview.StudioPreviewView();
            TestAssert.True(view.PreviewPlayer.IsMuted, "A newly opened preview must remain silent until playback is requested.");
            void Invoke(string name, params object[] arguments) => view.GetType().GetMethod(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, arguments);
            Invoke("BeginPausedSeekPrime", 0d);
            var priming = view.PreviewPlayer;
            TestAssert.True(priming.IsMuted, "Decoder warm-up is silent.");
            Invoke("CancelSeekPrime");
            TestAssert.True(priming.IsMuted, "Cancellation must not expose queued audio while the decoder is still playing.");
            priming.IsMuted = false; // Simulate a graph that was audibly playing before navigation.
            Invoke("DeactivatePlaybackSurface");
            TestAssert.True(priming.IsMuted, "Navigation mutes the retiring graph before closing it.");
            TestAssert.True(view.PreviewPlayer.IsMuted, "Its replacement also starts muted.");
            TestAssert.True(priming.Source is null, "A retired graph releases its source.");
        });
        return Task.CompletedTask;
    }

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
