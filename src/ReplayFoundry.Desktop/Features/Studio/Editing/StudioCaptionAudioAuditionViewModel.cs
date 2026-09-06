using System.ComponentModel;
using System.Windows.Input;
using ReplayFoundry.Desktop.Features.Generate.Handoff;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

public sealed class StudioCaptionAudioAuditionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStudioCaptionAudioAuditionSession _session;
    private readonly AsyncDelegateCommand _listen;
    private readonly DelegateCommand _stop;
    private bool _disposed;

    public StudioCaptionAudioAuditionViewModel(IStudioCaptionAudioAuditionSession? session = null)
    {
        _session = session ?? StudioAudioAuditionFactory.CreateCaption();
        _listen = new(ListenAsync, () => !_disposed && _session.CanListen);
        _stop = new(Stop, () => !_disposed && _session.IsActive);
        _session.Changed += SessionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand ListenCommand => _listen;
    public ICommand StopCommand => _stop;
    public string Status => _session.Status;
    public void Bind(GenerationOutputAsset? asset, int streamIndex) => _session.Bind(asset, streamIndex);
    public void SetHostBusy(bool busy) => _session.SetHostBusy(busy);
    public Task ListenAsync() => _session.ListenAsync();
    public void Stop() => _session.Stop();
    public Task StopAsync(CancellationToken cancellationToken) => _session.StopAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Changed -= SessionChanged;
        _session.Dispose();
        Notify();
    }

    private void SessionChanged(object? sender, EventArgs args) => Notify();
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new(nameof(Status)));
        _listen.RaiseCanExecuteChanged();
        _stop.RaiseCanExecuteChanged();
    }
}
