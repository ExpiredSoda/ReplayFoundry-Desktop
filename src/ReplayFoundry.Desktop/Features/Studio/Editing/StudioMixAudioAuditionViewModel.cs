using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioMixAudioAuditionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStudioMixAudioAuditionSession _session;
    private readonly AsyncDelegateCommand _before, _after;
    private readonly DelegateCommand _stop;
    private bool _disposed;

    public StudioMixAudioAuditionViewModel(IStudioMixAudioAuditionSession? session = null)
    {
        _session = session ?? StudioAudioAuditionFactory.CreateMix();
        _before = new(() => _session.StartAsync(false), () => !_disposed && _session.CanListen);
        _after = new(() => _session.StartAsync(true), () => !_disposed && _session.CanListen);
        _stop = new(Stop, () => !_disposed && _session.IsActive);
        _session.Changed += SessionChanged;
        _session.PlaybackStarting += SessionPlaybackStarting;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? PlaybackStarting;
    public ICommand BeforeCommand => _before;
    public ICommand AfterCommand => _after;
    public ICommand StopCommand => _stop;
    public string Status => _session.Status;
    public void Bind(GenerationOutputAsset? asset) => _session.Bind(asset);
    public void SetHostBusy(bool busy) => _session.SetHostBusy(busy);
    public void Stop() => _session.Stop();
    public Task StopAsync(CancellationToken cancellationToken) => _session.StopAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Changed -= SessionChanged;
        _session.PlaybackStarting -= SessionPlaybackStarting;
        _session.Dispose();
        Notify();
    }

    private void SessionChanged(object? sender, EventArgs args) => Notify();
    private void SessionPlaybackStarting(object? sender, EventArgs args) => PlaybackStarting?.Invoke(this, args);
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new(nameof(Status)));
        _before.RaiseCanExecuteChanged();
        _after.RaiseCanExecuteChanged();
        _stop.RaiseCanExecuteChanged();
    }
}
