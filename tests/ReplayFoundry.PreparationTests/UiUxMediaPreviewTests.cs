using System.Windows;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Features.Studio.Inspector;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Preview;
using ReplayFoundry.Desktop.Platform.Media;

namespace ReplayFoundry.PreparationTests;

internal static partial class UiUxApplicationSurfaceTests
{
    private static Task TimelineFilmstripCachesRealFrames()
    {
        RunOnSta(() =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var provider = new TimelineFrameProvider();
                var media = TestMediaFactory.Create(path, TimeSpan.FromMinutes(20));
                using var strip = new StudioTimelineFilmstrip(provider);
                strip.Request(media, 300, 120);
                strip.LoadAsync().GetAwaiter().GetResult();
                TestAssert.Equal(20, strip.Frames.Count, "The filmstrip must contain actual provider frames across the visible range.");
                TestAssert.Equal(300d, strip.Frames.First().StartSeconds, "First cell starts at the visible recording time.");
                TestAssert.Equal(420d, strip.Frames.Last().EndSeconds, "Last cell ends at the visible recording time.");
                TestAssert.True(strip.Frames.All(frame => frame.Frame.RequestedTimestamp.TotalSeconds >= frame.StartSeconds &&
                    frame.Frame.RequestedTimestamp.TotalSeconds < frame.EndSeconds), "Every thumbnail is sampled from the interval it represents.");
                TestAssert.True(provider.Requests.All(request => request.WorkIntent == VideoPreviewWorkIntent.BackgroundFilmstrip),
                    "Thumbnails must yield to playback and export in the media scheduler.");
                var command = FfmpegPreviewCommandBuilder.Build(provider.Requests[0], Path.Combine(Path.GetTempPath(), "thumbnail.png"));
                TestAssert.Equal(2, command.Arguments.Count(argument => argument == "-threads"), "Both the decoder and PNG encoder must have bounded thread counts.");
                strip.Request(media, 300, 120); strip.LoadAsync().GetAwaiter().GetResult();
                TestAssert.Equal(20, provider.Requests.Count, "Returning to the same timeline must reuse cached frames.");
                strip.Request(media, 900, 60); strip.LoadAsync().GetAwaiter().GetResult();
                TestAssert.Equal(900d, strip.Frames.First().StartSeconds, "Navigation cannot display old frames as if they belonged to the new time range.");
                File.AppendAllText(path, "source changed");
                strip.Request(media, 300, 120); strip.LoadAsync().GetAwaiter().GetResult();
                TestAssert.Equal(60, provider.Requests.Count, "Changed source bytes cannot reuse stale cached scene images.");
                strip.Request(null, 0, 0);
                TestAssert.Equal(0, strip.Frames.Count, "Closing the editor clears visible frames and cancels queued work.");
            }
            finally { File.Delete(path); }
        });
        return Task.CompletedTask;
    }
    private static Task CaptionEffectSamplesFollowRealEffects()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var sample = new StudioCaptionEffectPreview { Preset = GenerationCaptionStylePreset.WordFocus };
            sample.RenderSampleAt(.4);
            int firstWord = sample.SampleText.AccentStartIndex;
            sample.RenderSampleAt(1.5);
            TestAssert.True(sample.SampleText.AccentStartIndex > firstWord, "Word focus follows each sample word.");
            sample.Preset = GenerationCaptionStylePreset.KaraokeSweep;
            sample.RenderSampleAt(1.4);
            double early = sample.SampleText.AccentProgress;
            sample.RenderSampleAt(1.8);
            TestAssert.True(sample.SampleText.AccentProgress > early, "Karaoke must show a genuine progressive sweep.");
            sample.Preset = GenerationCaptionStylePreset.Pop;
            sample.RenderSampleAt(1.27);
            double firstScale = sample.SampleText.CaptionScale;
            TestAssert.Equal("close!", sample.SampleText.CaptionText, "Pop displays one spoken word.");
            sample.RenderSampleAt(1.37);
            TestAssert.True(sample.SampleText.CaptionScale > firstScale, "Pop uses the real scale envelope.");
            sample.RenderSampleAt(2.6);
            TestAssert.Equal(string.Empty, sample.SampleText.CaptionText, "The sample clears during its speech pause.");
            var window = new Window { Width = 220, Height = 140, Content = sample, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                sample.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                TestAssert.False(sample.IsMotionRunning, "Unhovered tiles do not run timers.");
                sample.IsPreviewActive = true;
                TestAssert.Equal(SystemParameters.ClientAreaAnimation, sample.IsMotionRunning, "Hover or focus begins motion only when Windows permits it.");
                sample.IsPreviewActive = false;
                TestAssert.False(sample.IsMotionRunning, "Leaving a tile stops animation without changing the selected clip effect.");
                sample.IsPreviewActive = true; sample.Visibility = Visibility.Collapsed;
                TestAssert.False(sample.IsMotionRunning, "Hidden caption inspectors cannot keep animating.");
            }
            finally { window.Close(); }
        });
        return Task.CompletedTask;
    }
    private static async Task TimelineFilmstripDrainsCancelledWork()
    {
        string path = Path.GetTempFileName();
        StudioTimelineFilmstrip? strip = null;
        Task? stopped = null;
        var provider = new TimelineFrameProvider { HoldFrame = true };
        try
        {
            RunOnSta(() =>
            {
                strip = new StudioTimelineFilmstrip(provider);
                strip.Request(TestMediaFactory.Create(path, TimeSpan.FromMinutes(20)), 0, 120);
                _ = strip.LoadAsync();
                stopped = strip.StopAsync(CancellationToken.None);
                TestAssert.False(stopped.IsCompleted, "Stopping must wait for an active extraction to acknowledge cancellation.");
                provider.ReleaseFrame();
            });
            await stopped!;
            RunOnSta(() => TestAssert.Equal(0, strip!.Frames.Count, "Late provider results cannot repopulate a closed clip maker."));
        }
        finally
        {
            RunOnSta(() => strip?.Dispose());
            File.Delete(path);
        }
    }
    private sealed class TimelineFrameProvider : IVideoPreviewFrameProvider
    {
        public List<VideoPreviewFrameRequest> Requests { get; } = [];
        public bool HoldFrame { get; init; }
        private readonly TaskCompletionSource<VideoPreviewFrame> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private VideoPreviewFrame? _frame;
        public void ReleaseFrame() => _pending.SetResult(_frame!);
        public Task<VideoPreviewFrame> GetFrameAsync(VideoPreviewFrameRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request);
            _frame = new VideoPreviewFrame(request.Media.FullPath, request.Media.Duration,
                request.Media.PrimaryVideoStream.Index, request.Timestamp, null, 256, 144,
                CompositionCoordinateSpace.EffectiveDisplayNormalizedBeforeCrop, TestMediaFactory.CreatePngHeader(256, 144),
                new VideoPreviewFrameManifest("test", "1", "test", "1", Path.Combine(Path.GetTempPath(), "ffmpeg.exe"), DateTimeOffset.UtcNow, TimeSpan.Zero));
            return HoldFrame ? _pending.Task : Task.FromResult(_frame);
        }
    }
}
