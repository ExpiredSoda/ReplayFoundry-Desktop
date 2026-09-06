using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Features.Studio.Editing;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.PreparationTests;

internal static partial class GenerationClipRenderingTests
{
    private static async Task AuditionViewsPreserveSessionLifetime()
    {
        var mixSession = new ControlledAuditionSession();
        using var mix = new StudioMixAudioAuditionViewModel(mixSession);
        object? playbackSender = null;
        mix.PlaybackStarting += (sender, _) => playbackSender = sender;
        Task before = ((AsyncDelegateCommand)mix.BeforeCommand).ExecuteAsync();
        TestAssert.Equal(false, mixSession.Processed,
            "The Before command must request the original mix.");
        TestAssert.Same(mix, playbackSender!,
            "Playback coordination must retain the ViewModel as its event sender.");
        TestAssert.False(mix.AfterCommand.CanExecute(null),
            "The alternate mix cannot start while the current session is preparing.");
        Task stop = mix.StopAsync(CancellationToken.None);
        TestAssert.False(stop.IsCompleted,
            "Stopping the view must await the backend's pending cleanup.");
        mixSession.Complete();
        await Task.WhenAll(before, stop);
        Task after = ((AsyncDelegateCommand)mix.AfterCommand).ExecuteAsync();
        TestAssert.Equal(true, mixSession.Processed,
            "The After command must request the saved processing settings.");
        mixSession.Complete();
        await after;
        mix.SetHostBusy(true);
        TestAssert.False(mix.BeforeCommand.CanExecute(null),
            "Host work must prevent new playback requests.");

        var captionSession = new ControlledAuditionSession();
        using var caption = new StudioCaptionAudioAuditionViewModel(captionSession);
        caption.Bind(null, 7);
        TestAssert.Equal(7, captionSession.StreamIndex,
            "The selected absolute stream index must reach the caption session unchanged.");
        Task listen = caption.ListenAsync();
        caption.Dispose();
        Task drain = caption.StopAsync(CancellationToken.None);
        TestAssert.False(drain.IsCompleted,
            "Dispose followed by Stop must still drain the active backend operation.");
        captionSession.Complete();
        await Task.WhenAll(listen, drain);
        TestAssert.True(captionSession.Disposed,
            "The view owns and disposes its playback session.");
        TestAssert.False(caption.ListenCommand.CanExecute(null),
            "Disposed views must not restart playback even after backend work finishes.");
        int lateNotifications = 0;
        caption.PropertyChanged += (_, _) => lateNotifications++;
        captionSession.SetHostBusy(false);
        TestAssert.Equal(0, lateNotifications,
            "Disposal must detach backend events before late completion notifications.");
    }

    private sealed class ControlledAuditionSession : IStudioMixAudioAuditionSession, IStudioCaptionAudioAuditionSession
    {
        private TaskCompletionSource? _pending;
        private bool _busy;
        public event EventHandler? Changed;
        public event EventHandler? PlaybackStarting;
        public string Status => IsActive ? "Preparing" : "Stopped";
        public bool CanListen => !_busy && !IsActive && !Disposed;
        public bool IsActive => _pending?.Task.IsCompleted == false;
        public bool Disposed { get; private set; }
        public bool Processed { get; private set; }
        public int StreamIndex { get; private set; }
        public void Bind(GenerationOutputAsset? asset) { }
        public void Bind(GenerationOutputAsset? asset, int streamIndex) => StreamIndex = streamIndex;
        public Task StartAsync(bool processed)
        {
            Processed = processed;
            _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            PlaybackStarting?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
            return _pending.Task;
        }
        public Task ListenAsync() => StartAsync(false);
        public void SetHostBusy(bool busy) { _busy = busy; Changed?.Invoke(this, EventArgs.Empty); }
        public void Stop() => Changed?.Invoke(this, EventArgs.Empty);
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Stop();
            if (_pending is not null) await _pending.Task.WaitAsync(cancellationToken);
        }
        public void Complete() { _pending?.TrySetResult(); Changed?.Invoke(this, EventArgs.Empty); }
        public void Dispose() { Disposed = true; Stop(); }
    }
}
